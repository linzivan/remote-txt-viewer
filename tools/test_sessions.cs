using System;
using System.IO;
using System.Text;
using RemoteTxtViewer;

internal static class SessionStoreTest
{
    private static int _pass = 0;
    private static int _fail = 0;

    private static void Check(string name, bool ok, string detail)
    {
        if (ok) { _pass++; Console.WriteLine("  PASS  " + name); }
        else { _fail++; Console.WriteLine("  FAIL  " + name + "   -> " + detail); }
    }

    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        string root = Path.Combine(Path.GetTempPath(), "rtv_sess_" + DateTime.Now.Ticks.ToString());
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("RTV_CONFIG_DIR", root);
        Console.WriteLine("config dir: " + root);

        // ---------- 1. 造一个旧的单文件 config.ini ----------
        string legacy = Path.Combine(root, "config.ini");
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("host=10.0.0.5");
        sb.AppendLine("port=22");
        sb.AppendLine("user=root");
        sb.AppendLine("password=" + AppConfig.ProtectSecret("p@ss中文"));
        sb.AppendLine("use_key=0");
        sb.AppendLine("key_file=");
        sb.AppendLine("key_pass=");
        sb.AppendLine("remote_path=/var/log/app.log");
        sb.AppendLine("interval=5");
        sb.AppendLine("encoding=gbk");
        sb.AppendLine("follow=1");
        sb.AppendLine("word_wrap=1");
        sb.AppendLine("font_size=12");
        sb.AppendLine("host_key_fp=abcdef1234");
        File.WriteAllText(legacy, sb.ToString(), new UTF8Encoding(false));

        // 造旧的时间线数据（应在迁移时被搬走）
        string oldHist = Path.Combine(root, "history");
        Directory.CreateDirectory(oldHist);
        File.WriteAllText(Path.Combine(oldHist, "index.tsv"), "#id\tlocalTicks\n1\t2\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(oldHist, "c_111.txt"), "old snapshot", new UTF8Encoding(false));

        // ---------- 2. 首次 Load 应自动迁移 ----------
        SessionStore store = SessionStore.Load();
        Console.WriteLine();
        Console.WriteLine("[迁移旧配置]");
        Check("迁移出 1 个会话", store.Sessions.Count == 1, "count=" + store.Sessions.Count);
        if (store.Sessions.Count != 1) return 1;

        AppConfig s0 = store.Sessions[0];
        Check("host 正确", s0.Host == "10.0.0.5", s0.Host);
        Check("user 正确", s0.User == "root", s0.User);
        Check("path 正确", s0.RemotePath == "/var/log/app.log", s0.RemotePath);
        Check("密码解密正确", s0.Password == "p@ss中文", "len=" + s0.Password.Length);
        Check("interval 正确", s0.IntervalSec == 5, s0.IntervalSec.ToString());
        Check("encoding 正确", s0.EncodingPref == "gbk", s0.EncodingPref);
        Check("指纹保留", s0.HostKeyFp == "abcdef1234", s0.HostKeyFp);
        Check("UI 设置迁移(fontSize)", Math.Abs(store.Ui.FontSize - 12f) < 0.01, store.Ui.FontSize.ToString());
        Check("UI 设置迁移(wordWrap)", store.Ui.WordWrap, store.Ui.WordWrap.ToString());

        Check("sessions.ini 已生成", File.Exists(SessionStore.StorePath), SessionStore.StorePath);

        string id0 = SessionStore.MakeId(s0);
        Check("时间线已迁移到会话目录",
            File.Exists(Path.Combine(root, "history", id0, "index.tsv")) &&
            File.Exists(Path.Combine(root, "history", id0, "c_111.txt")),
            "id=" + id0);
        Check("旧目录已清空", !File.Exists(Path.Combine(oldHist, "index.tsv")), "still there");

        // ---------- 3. 加第二个会话并保存/重载 ----------
        Console.WriteLine();
        Console.WriteLine("[多会话往返]");
        AppConfig s1 = new AppConfig();
        s1.Host = "192.168.1.9";
        s1.Port = 2222;
        s1.User = "test";
        s1.Password = "另一个密码";
        s1.RemotePath = "/home/test/notes.txt";
        s1.IntervalSec = 2;
        s1.EncodingPref = "auto";
        s1.HostKeyFp = "deadbeef";
        store.Sessions.Add(s1);
        store.ActiveIndex = 1;
        store.Save();

        SessionStore re = SessionStore.Load();
        Check("重载后 2 个会话", re.Sessions.Count == 2, "count=" + re.Sessions.Count);
        Check("active 索引保留", re.ActiveIndex == 1, re.ActiveIndex.ToString());
        Check("第二个 host 正确", re.Sessions[1].Host == "192.168.1.9", re.Sessions[1].Host);
        Check("第二个端口正确", re.Sessions[1].Port == 2222, re.Sessions[1].Port.ToString());
        Check("第二个密码解密正确", re.Sessions[1].Password == "另一个密码", re.Sessions[1].Password.Length.ToString());
        Check("第一个密码仍正确", re.Sessions[0].Password == "p@ss中文", "len=" + re.Sessions[0].Password.Length);
        Check("两个时间线目录互不干扰",
            SessionStore.MakeId(re.Sessions[0]) != SessionStore.MakeId(re.Sessions[1]),
            "ids equal");

        // ---------- 4. MakeId 稳定性 ----------
        Console.WriteLine();
        Console.WriteLine("[会话 id]");
        AppConfig a = new AppConfig(); a.Host = "h"; a.User = "u"; a.RemotePath = "/p/a.txt";
        AppConfig b = new AppConfig(); b.Host = "h"; b.User = "u"; b.RemotePath = "/p/a.txt";
        AppConfig c = new AppConfig(); c.Host = "h"; c.User = "u"; c.RemotePath = "/p/b.txt";
        Check("同配置 id 一致", SessionStore.MakeId(a) == SessionStore.MakeId(b), "diff");
        Check("不同路径 id 不同", SessionStore.MakeId(a) != SessionStore.MakeId(c), "same");
        Check("id 长度 16", SessionStore.MakeId(a).Length == 16, SessionStore.MakeId(a));

        // ---------- 5. sessions.ini 不含明文密码 ----------
        Console.WriteLine();
        Console.WriteLine("[安全]");
        string ini = File.ReadAllText(SessionStore.StorePath, Encoding.UTF8);
        Check("ini 里没有明文密码", ini.IndexOf("p@ss中文") < 0 && ini.IndexOf("另一个密码") < 0, "plaintext found");
        Check("ini 里有 password= 字段", ini.IndexOf("password=AQ") >= 0, "no encrypted field");

        try { Directory.Delete(root, true); } catch { }

        Console.WriteLine();
        Console.WriteLine("==================================");
        Console.WriteLine("PASS " + _pass + "   FAIL " + _fail);
        return _fail == 0 ? 0 : 1;
    }
}
