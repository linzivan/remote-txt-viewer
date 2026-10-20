using System;
using System.Text;
using RemoteTxtViewer;

internal static class HostKeyTest
{
    private static int _pass = 0;
    private static int _fail = 0;

    private static void Check(string name, bool ok, string detail)
    {
        if (ok) { _pass++; Console.WriteLine("  PASS  " + name); }
        else { _fail++; Console.WriteLine("  FAIL  " + name + "   -> " + detail); }
    }

    public static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        AppConfig cfg = new AppConfig();
        cfg.Host = "127.0.0.1";
        cfg.Port = 2222;
        cfg.User = "test";
        cfg.Password = "123456";
        cfg.RemotePath = "/var/log/app.log";
        cfg.HostKeyFp = "";     // 首次：还没有记录指纹

        string learned = "";

        Console.WriteLine("[1] 首次连接（未记录指纹，应信任并学习）");
        try
        {
            using (SftpFetcher f = new SftpFetcher(cfg))
            {
                RemoteStat st = f.Stat();
                learned = f.LearnedHostKeyFp;
                Check("连接成功", true, "");
                Check("读到文件大小 > 0", st.Size > 0, "size=" + st.Size);
                Check("学到了指纹", learned.Length == 64, "len=" + learned.Length + " fp=" + learned);
                Check("CurrentHostKeyFp 与 learned 一致", f.CurrentHostKeyFp == learned, f.CurrentHostKeyFp);

                Snapshot snap = f.Read();
                Check("读到内容", snap.Text != null && snap.Text.Length > 0, "len=" + (snap.Text == null ? -1 : snap.Text.Length));
                Console.WriteLine("        指纹 = " + learned);
            }
        }
        catch (Exception ex)
        {
            Check("首次连接成功", false, ex.GetType().Name + ": " + ex.Message);
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("[2] 已记录指纹 + 本次一致（应放行）");
        AppConfig c2 = cfg.Clone();
        c2.HostKeyFp = learned;
        try
        {
            using (SftpFetcher f = new SftpFetcher(c2))
            {
                RemoteStat st = f.Stat();
                Check("连接成功", st.Size > 0, "size=" + st.Size);
                Check("没有误报不匹配", !f.HostKeyMismatch, "mismatch=true");
            }
        }
        catch (Exception ex)
        {
            Check("一致时应放行", false, ex.GetType().Name + ": " + ex.Message);
        }

        Console.WriteLine();
        Console.WriteLine("[3] 已记录指纹 + 本次不一致（必须拦截）");
        AppConfig c3 = cfg.Clone();
        c3.HostKeyFp = new string('a', 64);   // 假的指纹
        bool blocked = false;
        try
        {
            using (SftpFetcher f = new SftpFetcher(c3))
            {
                f.Stat();
                Check("不匹配必须拦截", false, "居然连上了，没有拦截");
            }
        }
        catch (HostKeyMismatchException ex)
        {
            blocked = true;
            Check("抛出了 HostKeyMismatchException", true, "");
            Check("异常带期望/实际指纹", ex.Expected.Length == 64 && ex.Actual.Length == 64,
                "exp=" + ex.Expected.Length + " act=" + ex.Actual.Length);
            string msg = SftpFetcher.DescribeError(ex);
            Console.WriteLine("        提示文案：" + msg);
            Check("提示里出现'指纹不符'", msg.Contains("指纹不符"), msg);
        }
        catch (Exception ex)
        {
            Check("拦截方式正确", false, "抛的是 " + ex.GetType().Name + ": " + ex.Message);
        }
        Check("确实被拦住了", blocked, "没有触发拦截");

        Console.WriteLine();
        Console.WriteLine("==================================");
        Console.WriteLine("PASS " + _pass + "   FAIL " + _fail);
        return _fail == 0 ? 0 : 1;
    }
}
