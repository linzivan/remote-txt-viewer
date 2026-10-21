using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RemoteTxtViewer
{
    /// <summary>
    /// 配置模型：本软件一次只连一台服务器。
    ///   [server]  全局服务器连接（主机/端口/账号/密码/密钥/指纹）
    ///   [file.N]  每个监控文件只记录：路径 + 刷新间隔 + 编码
    /// 旧版本（每会话各自带服务器配置）会自动迁移成这个结构。
    /// </summary>
    public class SessionStore
    {
        public AppConfig Server = new AppConfig();                 // 只有连接字段有效
        public List<AppConfig> Items = new List<AppConfig>();      // 只有 path/interval/encoding 有效
        public AppConfig Ui = new AppConfig();                     // 界面字段
        public int ActiveIndex = 0;

        public static string StorePath
        {
            get { return Path.Combine(AppConfig.ConfigDir, "sessions.ini"); }
        }

        /// <summary>把「全局服务器」和「某个文件的设置」合成一份完整连接配置，供 SftpFetcher 使用。</summary>
        public AppConfig BuildConn(AppConfig item)
        {
            AppConfig c = item == null ? new AppConfig() : item.Clone();
            c.Host = Server.Host;
            c.Port = Server.Port;
            c.User = Server.User;
            c.Password = Server.Password;
            c.UseKey = Server.UseKey;
            c.KeyFile = Server.KeyFile;
            c.KeyPass = Server.KeyPass;
            c.HostKeyFp = Server.HostKeyFp;
            return c;
        }

        public AppConfig BuildConn(int index)
        {
            if (index < 0 || index >= Items.Count) return null;
            return BuildConn(Items[index]);
        }

        public bool HasServer
        {
            get { return !string.IsNullOrEmpty(Server.Host) && !string.IsNullOrEmpty(Server.User); }
        }

        /// <summary>由 服务器+路径 派生稳定 id，用于分时间线目录。</summary>
        public static string MakeId(AppConfig c)
        {
            string key = (c.Host == null ? "" : c.Host) + "|" +
                         (c.User == null ? "" : c.User) + "|" +
                         (c.RemotePath == null ? "" : c.RemotePath);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                StringBuilder sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // ---------------- 读取 ----------------

        public static SessionStore Load()
        {
            SessionStore st = new SessionStore();

            if (File.Exists(StorePath))
            {
                try { st.Parse(StorePath); }
                catch { }
                if (st.Items.Count > 0 || st.HasServer) return st;
            }

            // 没有新配置：尝试从旧的单文件 config.ini 迁移
            try
            {
                AppConfig old = AppConfig.Load();
                if (old != null && old.IsReady)
                {
                    st.Server = old.Clone();
                    AppConfig item = new AppConfig();
                    item.RemotePath = old.RemotePath;
                    item.IntervalSec = old.IntervalSec;
                    item.EncodingPref = old.EncodingPref;
                    st.Items.Add(item);
                    CopyUi(old, st.Ui);
                    st.ActiveIndex = 0;
                    st.MigrateLegacyHistory(MakeId(st.BuildConn(item)));
                    st.Save();
                }
            }
            catch { }

            return st;
        }

        private static void CopyUi(AppConfig from, AppConfig to)
        {
            to.FontSize = from.FontSize;
            to.Follow = from.Follow;
            to.WordWrap = from.WordWrap;
            to.ShowTimeline = from.ShowTimeline;
            to.WinX = from.WinX;
            to.WinY = from.WinY;
            to.WinW = from.WinW;
            to.WinH = from.WinH;
            to.WinMax = from.WinMax;
        }

        private void Parse(string path)
        {
            string section = "";
            AppConfig cur = null;
            Dictionary<string, string> g = new Dictionary<string, string>();

            // 旧格式（每会话都带服务器配置）先全部读进这个列表，最后统一迁移
            List<AppConfig> legacy = new List<AppConfig>();

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    cur = null;
                    if (section == "server") cur = Server;
                    else if (section.StartsWith("file."))
                    {
                        cur = new AppConfig();
                        Items.Add(cur);
                    }
                    else if (section.StartsWith("session."))
                    {
                        cur = new AppConfig();
                        legacy.Add(cur);
                    }
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1);

                if (cur != null) ApplyField(cur, k, v);
                else if (section == "global") g[k] = v;
            }

            ApplyGlobal(g);

            // 旧格式：取第一个会话的服务器信息作为全局服务器
            if (legacy.Count > 0 && Items.Count == 0)
            {
                Server = legacy[0].Clone();
                for (int i = 0; i < legacy.Count; i++)
                {
                    AppConfig item = new AppConfig();
                    item.RemotePath = legacy[i].RemotePath;
                    item.IntervalSec = legacy[i].IntervalSec <= 0 ? 3 : legacy[i].IntervalSec;
                    item.EncodingPref = legacy[i].EncodingPref;
                    Items.Add(item);
                }
                if (Ui.FontSize <= 0) CopyUi(legacy[0], Ui);
                Save();   // 升级成新格式
            }

            if (ActiveIndex < 0) ActiveIndex = 0;
            if (ActiveIndex >= Items.Count) ActiveIndex = Items.Count == 0 ? 0 : Items.Count - 1;
        }

        /// <summary>服务器段和文件段共用一套字段名，统一按名字落到对应属性上。</summary>
        private static void ApplyField(AppConfig c, string k, string v)
        {
            switch (k)
            {
                case "host": c.Host = v.Trim(); break;
                case "port": c.Port = ParseInt(v, 22); break;
                case "user": c.User = v.Trim(); break;
                case "password": c.Password = AppConfig.UnprotectSecret(v.Trim()); break;
                case "use_key": c.UseKey = (v.Trim() == "1"); break;
                case "key_file": c.KeyFile = v.Trim(); break;
                case "key_pass": c.KeyPass = AppConfig.UnprotectSecret(v.Trim()); break;
                case "host_key_fp": c.HostKeyFp = v.Trim(); break;
                case "remote_path": c.RemotePath = v.Trim(); break;
                case "interval": c.IntervalSec = ParseInt(v, 3); break;
                case "encoding": c.EncodingPref = v.Trim(); break;
            }
        }

        private void ApplyGlobal(Dictionary<string, string> g)
        {
            string v;
            if (g.TryGetValue("active", out v)) ActiveIndex = ParseInt(v, 0);
            if (g.TryGetValue("font_size", out v)) Ui.FontSize = ParseFloat(v, 10f);
            if (g.TryGetValue("follow", out v)) Ui.Follow = (v.Trim() == "1");
            if (g.TryGetValue("word_wrap", out v)) Ui.WordWrap = (v.Trim() == "1");
            if (g.TryGetValue("show_timeline", out v)) Ui.ShowTimeline = (v.Trim() == "1");
            if (g.TryGetValue("win_x", out v)) Ui.WinX = ParseInt(v, -1);
            if (g.TryGetValue("win_y", out v)) Ui.WinY = ParseInt(v, -1);
            if (g.TryGetValue("win_w", out v)) Ui.WinW = ParseInt(v, 0);
            if (g.TryGetValue("win_h", out v)) Ui.WinH = ParseInt(v, 0);
            if (g.TryGetValue("win_max", out v)) Ui.WinMax = (v.Trim() == "1");

            if (Ui.FontSize < 7f) Ui.FontSize = 7f;
            if (Ui.FontSize > 30f) Ui.FontSize = 30f;
        }

        // ---------------- 保存 ----------------

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppConfig.ConfigDir);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# RemoteTxtViewer 配置（一次只连一台服务器）");
                sb.AppendLine("# 密码与私钥口令用 Windows DPAPI 加密，换用户/换电脑需重填");
                sb.AppendLine();
                sb.AppendLine("[global]");
                sb.AppendLine("active=" + ActiveIndex.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("font_size=" + Ui.FontSize.ToString("0.#", CultureInfo.InvariantCulture));
                sb.AppendLine("follow=" + (Ui.Follow ? "1" : "0"));
                sb.AppendLine("word_wrap=" + (Ui.WordWrap ? "1" : "0"));
                sb.AppendLine("show_timeline=" + (Ui.ShowTimeline ? "1" : "0"));
                sb.AppendLine("win_x=" + Ui.WinX.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("win_y=" + Ui.WinY.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("win_w=" + Ui.WinW.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("win_h=" + Ui.WinH.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("win_max=" + (Ui.WinMax ? "1" : "0"));

                sb.AppendLine();
                sb.AppendLine("[server]");
                sb.AppendLine("host=" + Server.Host);
                sb.AppendLine("port=" + Server.Port.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("user=" + Server.User);
                sb.AppendLine("password=" + AppConfig.ProtectSecret(Server.Password));
                sb.AppendLine("use_key=" + (Server.UseKey ? "1" : "0"));
                sb.AppendLine("key_file=" + Server.KeyFile);
                sb.AppendLine("key_pass=" + AppConfig.ProtectSecret(Server.KeyPass));
                sb.AppendLine("host_key_fp=" + Server.HostKeyFp);

                for (int i = 0; i < Items.Count; i++)
                {
                    AppConfig f = Items[i];
                    sb.AppendLine();
                    sb.AppendLine("[file." + i.ToString() + "]");
                    sb.AppendLine("remote_path=" + f.RemotePath);
                    sb.AppendLine("interval=" + (f.IntervalSec <= 0 ? 3 : f.IntervalSec).ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("encoding=" + (string.IsNullOrEmpty(f.EncodingPref) ? "auto" : f.EncodingPref));
                }

                File.WriteAllText(StorePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        // ---------------- 旧数据迁移 ----------------

        private void MigrateLegacyHistory(string firstId)
        {
            if (string.IsNullOrEmpty(firstId)) return;
            try
            {
                string root = Path.Combine(AppConfig.ConfigDir, "history");
                string idx = Path.Combine(root, "index.tsv");
                if (!File.Exists(idx)) return;

                string target = Path.Combine(root, firstId);
                if (Directory.Exists(target) && File.Exists(Path.Combine(target, "index.tsv"))) return;

                Directory.CreateDirectory(target);

                string[] files = Directory.GetFiles(root, "c_*.txt");
                for (int i = 0; i < files.Length; i++)
                {
                    string dst = Path.Combine(target, Path.GetFileName(files[i]));
                    if (!File.Exists(dst)) File.Move(files[i], dst);
                }

                string idxDst = Path.Combine(target, "index.tsv");
                if (!File.Exists(idxDst)) File.Move(idx, idxDst);
            }
            catch { }
        }

        private static int ParseInt(string s, int dflt)
        {
            int v;
            if (int.TryParse(s == null ? "" : s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return dflt;
        }

        private static float ParseFloat(string s, float dflt)
        {
            float v;
            if (float.TryParse(s == null ? "" : s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return dflt;
        }
    }
}
