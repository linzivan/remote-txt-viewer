using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RemoteTxtViewer
{
    /// <summary>
    /// 连接与界面配置。保存在 %APPDATA%\RemoteTxtViewer\config.ini，
    /// 密码/私钥口令用 Windows DPAPI 加密（只有当前 Windows 用户能解开）。
    /// </summary>
    public class AppConfig
    {
        public string Host = "";
        public int Port = 22;
        public string User = "";
        public string Password = "";
        public bool UseKey = false;
        public string KeyFile = "";
        public string KeyPass = "";
        public string RemotePath = "";
        public int IntervalSec = 3;
        public string EncodingPref = "auto";   // auto | utf-8 | gbk
        public bool Follow = true;
        public bool WordWrap = false;
        public float FontSize = 10f;
        public string HostKeyFp = "";      // 服务器主机密钥 SHA256 指纹（防中间人）
        public bool ShowTimeline = true;   // 右侧变更时间线是否展开

        public int WinX = -1;
        public int WinY = -1;
        public int WinW = 0;
        public int WinH = 0;
        public bool WinMax = false;

        public static string ConfigDir
        {
            get
            {
                // 允许用环境变量重定向配置目录（用于隔离测试，不影响正常使用）
                string over = Environment.GetEnvironmentVariable("RTV_CONFIG_DIR");
                if (!string.IsNullOrEmpty(over)) return over;

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RemoteTxtViewer");
            }
        }

        public static string ConfigFile
        {
            get { return Path.Combine(ConfigDir, "config.ini"); }
        }

        public AppConfig Clone()
        {
            return (AppConfig)this.MemberwiseClone();
        }

        public bool IsReady
        {
            get { return !string.IsNullOrEmpty(Host) && !string.IsNullOrEmpty(RemotePath); }
        }

        public string DisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(Host)) return "未配置";
                string tail = RemotePath;
                int i = tail.LastIndexOf('/');
                if (i >= 0 && i < tail.Length - 1) tail = tail.Substring(i + 1);
                return User + "@" + Host + " · " + tail;
            }
        }

        // ---------------- DPAPI ----------------

        private static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                byte[] raw = Encoding.UTF8.GetBytes(plain);
                byte[] enc = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(enc);
            }
            catch { return ""; }
        }

        private static string Unprotect(string cipher)
        {
            if (string.IsNullOrEmpty(cipher)) return "";
            try
            {
                byte[] raw = Convert.FromBase64String(cipher);
                byte[] dec = ProtectedData.Unprotect(raw, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(dec);
            }
            catch { return ""; }
        }

        // ---------------- load / save ----------------

        public static AppConfig Load()
        {
            AppConfig c = new AppConfig();
            try
            {
                if (!File.Exists(ConfigFile)) return c;
                string[] lines = File.ReadAllLines(ConfigFile, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1);

                    switch (k)
                    {
                        case "host": c.Host = v.Trim(); break;
                        case "port": c.Port = ParseInt(v, 22); break;
                        case "user": c.User = v.Trim(); break;
                        case "password": c.Password = Unprotect(v.Trim()); break;
                        case "use_key": c.UseKey = (v.Trim() == "1"); break;
                        case "key_file": c.KeyFile = v.Trim(); break;
                        case "key_pass": c.KeyPass = Unprotect(v.Trim()); break;
                        case "remote_path": c.RemotePath = v.Trim(); break;
                        case "interval": c.IntervalSec = ParseInt(v, 3); break;
                        case "encoding": c.EncodingPref = v.Trim(); break;
                        case "follow": c.Follow = (v.Trim() == "1"); break;
                        case "word_wrap": c.WordWrap = (v.Trim() == "1"); break;
                        case "font_size": c.FontSize = ParseFloat(v, 10f); break;
                        case "host_key_fp": c.HostKeyFp = v.Trim(); break;
                        case "show_timeline": c.ShowTimeline = (v.Trim() == "1"); break;
                        case "win_x": c.WinX = ParseInt(v, -1); break;
                        case "win_y": c.WinY = ParseInt(v, -1); break;
                        case "win_w": c.WinW = ParseInt(v, 1020); break;
                        case "win_h": c.WinH = ParseInt(v, 680); break;
                        case "win_max": c.WinMax = (v.Trim() == "1"); break;
                    }
                }
            }
            catch { }
            if (c.IntervalSec < 1) c.IntervalSec = 1;
            if (c.IntervalSec > 3600) c.IntervalSec = 3600;
            if (c.FontSize < 7f) c.FontSize = 7f;
            if (c.FontSize > 30f) c.FontSize = 30f;
            return c;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# RemoteTxtViewer 配置文件");
                sb.AppendLine("# 密码与私钥口令已用 Windows DPAPI 加密，换用户/换电脑需重填");
                sb.AppendLine("host=" + Host);
                sb.AppendLine("port=" + Port.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("user=" + User);
                sb.AppendLine("password=" + Protect(Password));
                sb.AppendLine("use_key=" + (UseKey ? "1" : "0"));
                sb.AppendLine("key_file=" + KeyFile);
                sb.AppendLine("key_pass=" + Protect(KeyPass));
                sb.AppendLine("remote_path=" + RemotePath);
                sb.AppendLine("interval=" + IntervalSec.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("encoding=" + EncodingPref);
                sb.AppendLine("follow=" + (Follow ? "1" : "0"));
                sb.AppendLine("word_wrap=" + (WordWrap ? "1" : "0"));
                sb.AppendLine("font_size=" + FontSize.ToString("0.#", CultureInfo.InvariantCulture));
                sb.AppendLine("host_key_fp=" + HostKeyFp);
                sb.AppendLine("show_timeline=" + (ShowTimeline ? "1" : "0"));
                sb.AppendLine("win_x=" + WinX.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("win_y=" + WinY.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("win_w=" + WinW.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("win_h=" + WinH.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("win_max=" + (WinMax ? "1" : "0"));
                File.WriteAllText(ConfigFile, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        private static int ParseInt(string s, int dflt)
        {
            int v;
            if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return dflt;
        }

        private static float ParseFloat(string s, float dflt)
        {
            float v;
            if (float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return dflt;
        }
    }
}
