using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RemoteTxtViewer
{
    /// <summary>
    /// 多会话配置：sessions.ini 用分节存放每个监控项 + 一份全局界面设置。
    /// 首次运行时会自动把旧的单文件 config.ini 迁移成一个会话。
    /// </summary>
    public class SessionStore
    {
        public List<AppConfig> Sessions = new List<AppConfig>();
        public AppConfig Ui = new AppConfig();     // 只用到界面字段(fontSize/follow/wordWrap/showTimeline/窗口)
        public int ActiveIndex = 0;

        public static string StorePath
        {
            get { return Path.Combine(AppConfig.ConfigDir, "sessions.ini"); }
        }

        /// <summary>由 host|user|path 派生稳定 id，用来分时间线目录（路径不变则 id 不变）。</summary>
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
                return st;
            }

            // 首次升级：把旧的单文件 config.ini 迁移成一个会话
            try
            {
                AppConfig old = AppConfig.Load();
                if (old != null && old.IsReady)
                {
                    st.Sessions.Add(old);
                    CopyUi(old, st.Ui);
                    st.ActiveIndex = 0;
                    st.MigrateLegacyHistory(MakeId(old));
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

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    cur = null;
                    if (section.StartsWith("session."))
                    {
                        cur = new AppConfig();
                        Sessions.Add(cur);
                    }
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1);

                if (cur != null) ApplyConn(cur, k, v);
                else if (section == "global") g[k] = v;
            }

            ApplyGlobal(g);

            if (ActiveIndex < 0) ActiveIndex = 0;
            if (ActiveIndex >= Sessions.Count) ActiveIndex = Sessions.Count == 0 ? 0 : Sessions.Count - 1;
        }

        private static void ApplyConn(AppConfig c, string k, string v)
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
                case "remote_path": c.RemotePath = v.Trim(); break;
                case "interval": c.IntervalSec = ParseInt(v, 3); break;
                case "encoding": c.EncodingPref = v.Trim(); break;
                case "host_key_fp": c.HostKeyFp = v.Trim(); break;
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
                sb.AppendLine("# RemoteTxtViewer 多会话配置");
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

                for (int i = 0; i < Sessions.Count; i++)
                {
                    AppConfig c = Sessions[i];
                    sb.AppendLine();
                    sb.AppendLine("[session." + i.ToString() + "]");
                    sb.AppendLine("host=" + c.Host);
                    sb.AppendLine("port=" + c.Port.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("user=" + c.User);
                    sb.AppendLine("password=" + AppConfig.ProtectSecret(c.Password));
                    sb.AppendLine("use_key=" + (c.UseKey ? "1" : "0"));
                    sb.AppendLine("key_file=" + c.KeyFile);
                    sb.AppendLine("key_pass=" + AppConfig.ProtectSecret(c.KeyPass));
                    sb.AppendLine("remote_path=" + c.RemotePath);
                    sb.AppendLine("interval=" + c.IntervalSec.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("encoding=" + c.EncodingPref);
                    sb.AppendLine("host_key_fp=" + c.HostKeyFp);
                }

                File.WriteAllText(StorePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        // ---------------- 旧数据迁移 ----------------

        /// <summary>把旧的 history/index.tsv + c_*.txt 搬到第一个会话的目录里。</summary>
        private void MigrateLegacyHistory(string firstId)
        {
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

        // ---------------- 小工具 ----------------

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
