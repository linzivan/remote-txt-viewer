using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace RemoteTxtViewer
{
    public class RemoteStat
    {
        public long Size;
        public DateTime ModifiedUtc;
    }

    public class Snapshot
    {
        public string Text = "";
        public long Size;
        public DateTime ModifiedUtc;
        public string EncodingName = "";
    }

    public class DirEntry
    {
        public string Name = "";
        public bool IsDir;
        public long Size;
        public DateTime ModifiedUtc = DateTime.MinValue;
    }

    /// <summary>服务器主机密钥指纹与首次记录的不一致 —— 可能是中间人，也可能是服务器重装过系统。</summary>
    public class HostKeyMismatchException : Exception
    {
        public readonly string Expected;
        public readonly string Actual;

        public HostKeyMismatchException(string expected, string actual)
            : base("服务器主机密钥指纹与首次记录的不一致")
        {
            Expected = expected;
            Actual = actual;
        }
    }

    /// <summary>
    /// 封装 SFTP 读取。所有 SSH 类型都只出现在本类中，方便主窗口保持"不引用 SSH 库"。
    /// </summary>
    public class SftpFetcher : IDisposable
    {
        private SftpClient _client;
        private readonly AppConfig _cfg;

        /// <summary>首次连接时学到的服务器指纹；上层应把它存进配置。</summary>
        public string LearnedHostKeyFp = "";

        public bool HostKeyMismatch;
        public string ExpectedFp = "";
        public string ActualFp = "";
        public string CurrentHostKeyFp = "";   // 本次连接实际拿到的指纹

        public SftpFetcher(AppConfig cfg)
        {
            _cfg = cfg.Clone();
        }

        public static string Fingerprint(byte[] key)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(key);
                StringBuilder sb = new StringBuilder(h.Length * 2);
                for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private void OnHostKeyReceived(object sender, HostKeyEventArgs e)
        {
            string fp = Fingerprint(e.HostKey);
            CurrentHostKeyFp = fp;

            if (string.IsNullOrEmpty(_cfg.HostKeyFp))
            {
                // 首次连接：信任并记录指纹（TOFU，类似 ssh 的 known_hosts）
                LearnedHostKeyFp = fp;
                e.CanTrust = true;
                return;
            }

            if (string.Equals(fp, _cfg.HostKeyFp, StringComparison.OrdinalIgnoreCase))
            {
                e.CanTrust = true;
                return;
            }

            HostKeyMismatch = true;
            ExpectedFp = _cfg.HostKeyFp;
            ActualFp = fp;
            e.CanTrust = false;
        }

        // ---------------- 连接 ----------------

        private void Connect()
        {
            Disconnect();

            List<AuthenticationMethod> methods = new List<AuthenticationMethod>();
            if (_cfg.UseKey && !string.IsNullOrEmpty(_cfg.KeyFile))
            {
                PrivateKeyFile pk;
                if (string.IsNullOrEmpty(_cfg.KeyPass))
                    pk = new PrivateKeyFile(_cfg.KeyFile);
                else
                    pk = new PrivateKeyFile(_cfg.KeyFile, _cfg.KeyPass);
                methods.Add(new PrivateKeyAuthenticationMethod(_cfg.User, new PrivateKeyFile[] { pk }));
            }
            else
            {
                methods.Add(new PasswordAuthenticationMethod(_cfg.User, _cfg.Password ?? ""));
            }

            int port = _cfg.Port > 0 ? _cfg.Port : 22;
            ConnectionInfo ci = new ConnectionInfo(_cfg.Host, port, _cfg.User, methods.ToArray());
            ci.Timeout = TimeSpan.FromSeconds(12);

            HostKeyMismatch = false;

            SftpClient c = new SftpClient(ci);
            c.OperationTimeout = TimeSpan.FromSeconds(30);
            c.HostKeyReceived += OnHostKeyReceived;

            try
            {
                c.Connect();
            }
            catch (Exception)
            {
                try { c.Dispose(); } catch { }
                if (HostKeyMismatch)
                    throw new HostKeyMismatchException(ExpectedFp, ActualFp);
                throw;
            }
            _client = c;
        }

        private void EnsureConnected()
        {
            if (_client == null || !_client.IsConnected) Connect();
        }

        public void Disconnect()
        {
            if (_client != null)
            {
                try { if (_client.IsConnected) _client.Disconnect(); } catch { }
                try { _client.Dispose(); } catch { }
                _client = null;
            }
        }

        public void Dispose()
        {
            Disconnect();
        }

        // ---------------- 读取 ----------------

        /// <summary>只取文件属性（大小 + 修改时间），比下载全文便宜得多。</summary>
        public RemoteStat Stat()
        {
            EnsureConnected();
            SftpFileAttributes a = _client.GetAttributes(_cfg.RemotePath);
            RemoteStat s = new RemoteStat();
            s.Size = a.Size;
            s.ModifiedUtc = a.LastWriteTimeUtc;
            return s;
        }

        /// <summary>下载并解码整个文件。</summary>
        public Snapshot Read()
        {
            EnsureConnected();

            RemoteStat st = Stat();

            byte[] data;
            using (MemoryStream ms = new MemoryStream())
            {
                _client.DownloadFile(_cfg.RemotePath, ms);
                data = ms.ToArray();
            }

            Snapshot snap = new Snapshot();
            snap.Size = data.Length;
            snap.ModifiedUtc = st.ModifiedUtc;
            string encName;
            snap.Text = Decode(data, _cfg.EncodingPref, out encName);
            snap.EncodingName = encName;
            return snap;
        }

        public List<DirEntry> ListDir(string path)
        {
            EnsureConnected();
            List<DirEntry> list = new List<DirEntry>();
            if (string.IsNullOrEmpty(path)) path = ".";
            foreach (SftpFile f in _client.ListDirectory(path))
            {
                if (f.Name == "." || f.Name == "..") continue;
                DirEntry e = new DirEntry();
                e.Name = f.Name;
                e.IsDir = f.IsDirectory;
                e.Size = f.Length;
                try { e.ModifiedUtc = f.LastWriteTimeUtc; } catch { e.ModifiedUtc = DateTime.MinValue; }
                list.Add(e);
            }
            list.Sort(delegate(DirEntry a, DirEntry b)
            {
                if (a.IsDir != b.IsDir) return a.IsDir ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        // ---------------- 写入 ----------------

        /// <summary>根据 Decode() 报出的编码名返回对应 Encoding（保留 BOM 行为，避免写回后中文变乱码）。</summary>
        public static Encoding EncodingFor(string encodingName)
        {
            if (encodingName == "UTF-8 BOM") return new UTF8Encoding(true);
            if (encodingName == "UTF-16 LE") return new UnicodeEncoding(false, true);
            if (encodingName == "UTF-16 BE") return new UnicodeEncoding(true, true);
            if (encodingName == "GBK") return Encoding.GetEncoding(936);
            return new UTF8Encoding(false);
        }

        /// <summary>内容指纹，用于冲突检测（只比文本内容，不受编码/换行差异干扰）。</summary>
        public static string HashText(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(text == null ? "" : text));
                StringBuilder sb = new StringBuilder(h.Length * 2);
                for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static string PosixDir(string path)
        {
            if (string.IsNullOrEmpty(path)) return "/";
            int i = path.LastIndexOf('/');
            if (i < 0) return ".";
            if (i == 0) return "/";
            return path.Substring(0, i);
        }

        private static string PosixName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int i = path.LastIndexOf('/');
            return i < 0 ? path : path.Substring(i + 1);
        }

        private static string PosixJoin(string dir, string name)
        {
            if (string.IsNullOrEmpty(dir)) return name;
            if (dir.EndsWith("/")) return dir + name;
            return dir + "/" + name;
        }

        /// <summary>
        /// 原子写入：先上传临时文件 → 原文件改名备份 → 临时文件改名成目标 → 删备份。
        /// 任何一步失败都回滚，服务器上任何时刻都存在一份完整文件（不会写出半个文件）。
        /// </summary>
        public void WriteAtomic(string text, string encodingName)
        {
            EnsureConnected();

            string target = _cfg.RemotePath;
            string dir = PosixDir(target);
            string name = PosixName(target);
            string stamp = DateTime.Now.Ticks.ToString();

            string tmp = PosixJoin(dir, "." + name + ".rtv-tmp-" + stamp);
            string bak = PosixJoin(dir, "." + name + ".rtv-bak-" + stamp);

            Encoding enc = EncodingFor(encodingName);
            byte[] data = enc.GetBytes(text == null ? "" : text);

            // GetBytes() 不会写 BOM，必须手动把 preamble 放回最前面，
            // 否则原来是 UTF-8 BOM / UTF-16 的文件写回后会变成无 BOM，再次读会解析错。
            byte[] pre = enc.GetPreamble();
            if (pre != null && pre.Length > 0)
            {
                byte[] merged = new byte[pre.Length + data.Length];
                Buffer.BlockCopy(pre, 0, merged, 0, pre.Length);
                Buffer.BlockCopy(data, 0, merged, pre.Length, data.Length);
                data = merged;
            }

            using (MemoryStream ms = new MemoryStream(data))
            {
                _client.UploadFile(ms, tmp, true);
            }

            bool backedUp = false;
            try
            {
                if (_client.Exists(target))
                {
                    _client.RenameFile(target, bak);
                    backedUp = true;
                }
                _client.RenameFile(tmp, target);
            }
            catch (Exception)
            {
                if (backedUp)
                {
                    try { _client.RenameFile(bak, target); } catch { }
                }
                try { _client.DeleteFile(tmp); } catch { }
                throw;
            }

            if (backedUp)
            {
                try { _client.DeleteFile(bak); } catch { }
            }
        }

        /// <summary>只取大小，用于保存后校验。</summary>
        public long SizeOf()
        {
            return Stat().Size;
        }

        // ---------------- 编码 ----------------

        public static string Decode(byte[] data, string pref, out string usedName)
        {
            if (data == null) data = new byte[0];

            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                usedName = "UTF-8 BOM";
                return new UTF8Encoding(false).GetString(data, 3, data.Length - 3);
            }
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            {
                usedName = "UTF-16 LE";
                return Encoding.Unicode.GetString(data, 2, data.Length - 2);
            }
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            {
                usedName = "UTF-16 BE";
                return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
            }

            if (pref == "utf-8")
            {
                usedName = "UTF-8";
                return new UTF8Encoding(false, false).GetString(data);
            }
            if (pref == "gbk")
            {
                usedName = "GBK";
                return Encoding.GetEncoding(936).GetString(data);
            }

            // auto：先按严格 UTF-8 解码，失败就按 GBK（中文日志文件的常态）
            try
            {
                UTF8Encoding strict = new UTF8Encoding(false, true);
                string s = strict.GetString(data);
                usedName = "UTF-8";
                return s;
            }
            catch (DecoderFallbackException)
            {
                usedName = "GBK";
                return Encoding.GetEncoding(936).GetString(data);
            }
        }

        // ---------------- 错误信息 ----------------

        public static string DescribeError(Exception ex)
        {
            if (ex == null) return "未知错误";

            Exception root = ex;
            while (root.InnerException != null) root = root.InnerException;

            HostKeyMismatchException hkm = ex as HostKeyMismatchException;
            if (hkm == null) hkm = root as HostKeyMismatchException;
            if (hkm != null)
            {
                return "服务器指纹不符！已记录 " + Short(hkm.Expected) + "，本次 " + Short(hkm.Actual)
                     + " —— 可能是中间人攻击；若服务器重装过系统，请在设置里点「重置主机指纹」";
            }

            if (ex is SshAuthenticationException || root is SshAuthenticationException)
                return "认证失败：用户名、密码或密钥不正确";

            if (ex is SftpPathNotFoundException || root is SftpPathNotFoundException)
                return "远程路径不存在（检查文件路径是否正确）";

            if (ex is SftpPermissionDeniedException || root is SftpPermissionDeniedException)
                return "权限不足：该账号无权读取这个文件";

            if (root is System.Net.Sockets.SocketException)
            {
                System.Net.Sockets.SocketException se = (System.Net.Sockets.SocketException)root;
                switch (se.SocketErrorCode)
                {
                    case System.Net.Sockets.SocketError.TimedOut:
                        return "连接超时：主机无响应（检查 IP、网络、防火墙）";
                    case System.Net.Sockets.SocketError.ConnectionRefused:
                        return "端口被拒绝：SSH 服务未启动或端口不对";
                    case System.Net.Sockets.SocketError.HostNotFound:
                        return "找不到主机：IP 或域名错误";
                    case System.Net.Sockets.SocketError.NetworkUnreachable:
                    case System.Net.Sockets.SocketError.HostUnreachable:
                        return "网络不可达：检查是否连上服务器所在网络（VPN？）";
                }
                return "网络错误：" + se.Message;
            }

            if (ex is SshConnectionException || root is SshConnectionException)
                return "连接中断或握手失败（检查端口 / 服务器 SSH 配置）";

            if (root is System.Security.Cryptography.CryptographicException)
                return "私钥无法读取（格式不支持，或私钥口令错误）";

            if (root is FileNotFoundException && !ReferenceEquals(root, ex))
                return "本机文件不存在（私钥路径？）";
            if (ex is FileNotFoundException)
                return "本机文件不存在（私钥路径？）";

            if (root is UnauthorizedAccessException || ex is UnauthorizedAccessException)
                return "权限不足（本机私钥文件或 Windows 账户权限）";

            return ex.GetType().Name + "：" + ex.Message;
        }

        /// <summary>判断异常是否表示"远程路径不存在"（用于自动询问是否创建文件）。</summary>
        public static bool IsNotFound(Exception ex)
        {
            if (ex == null) return false;
            if (ex is SftpPathNotFoundException) return true;

            Exception root = ex;
            while (root.InnerException != null) root = root.InnerException;
            return root is SftpPathNotFoundException;
        }

        private static string Short(string fp)
        {
            if (string.IsNullOrEmpty(fp)) return "(空)";
            if (fp.Length <= 16) return fp;
            return fp.Substring(0, 8) + "…" + fp.Substring(fp.Length - 8);
        }
    }
}
