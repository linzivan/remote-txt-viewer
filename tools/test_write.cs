using System;
using System.Text;
using RemoteTxtViewer;

internal static class WriteTest
{
    private static int _pass = 0;
    private static int _fail = 0;

    private static void Check(string name, bool ok, string detail)
    {
        if (ok) { _pass++; Console.WriteLine("  PASS  " + name); }
        else { _fail++; Console.WriteLine("  FAIL  " + name + "   -> " + detail); }
    }

    private static AppConfig Cfg(string remote)
    {
        AppConfig c = new AppConfig();
        c.Host = "127.0.0.1";
        c.Port = 2222;
        c.User = "test";
        c.Password = "123456";
        c.RemotePath = remote;
        c.HostKeyFp = "";
        return c;
    }

    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        string path = "/var/log/app.log";
        string body = "第一行 中文内容\n第二行 ABC def\n第三行\n";

        // ---- 1. UTF-8 无 BOM ----
        using (SftpFetcher f = new SftpFetcher(Cfg(path)))
        {
            f.WriteAtomic(body, "UTF-8");
            Snapshot s = f.Read();
            Check("UTF-8 写后读回一致", s.Text == body, "len=" + s.Text.Length);
            Check("UTF-8 未引入 BOM", s.EncodingName == "UTF-8", s.EncodingName);
        }

        // ---- 2. UTF-8 BOM ----
        using (SftpFetcher f = new SftpFetcher(Cfg(path)))
        {
            f.WriteAtomic(body, "UTF-8 BOM");
            Snapshot s = f.Read();
            Check("UTF-8 BOM 写后读回一致", s.Text == body, "len=" + s.Text.Length);
            Check("BOM 被保留", s.EncodingName == "UTF-8 BOM", s.EncodingName);
        }

        // ---- 3. GBK ----
        using (SftpFetcher f = new SftpFetcher(Cfg(path)))
        {
            f.WriteAtomic(body, "GBK");
            Snapshot s = f.Read();
            Check("GBK 写后读回一致（中文不乱码）", s.Text == body, "len=" + s.Text.Length);
            Check("编码被识别为 GBK", s.EncodingName == "GBK", s.EncodingName);
        }

        // ---- 4. UTF-16 LE ----
        using (SftpFetcher f = new SftpFetcher(Cfg(path)))
        {
            f.WriteAtomic(body, "UTF-16 LE");
            Snapshot s = f.Read();
            Check("UTF-16 LE 写后读回一致", s.Text == body, "len=" + s.Text.Length);
        }

        // ---- 5. 空内容 ----
        using (SftpFetcher f = new SftpFetcher(Cfg(path)))
        {
            f.WriteAtomic("", "UTF-8");
            Snapshot s = f.Read();
            Check("写入空内容", s.Text == "" && s.Size == 0, "size=" + s.Size);
        }

        // ---- 6. 大文件 ----
        StringBuilder big = new StringBuilder();
        for (int i = 0; i < 2000; i++) big.Append("2026-10-20 10:00:00 [INFO] 第 " + i + " 行 some payload abcdef\n");
        string bigText = big.ToString();
        using (SftpFetcher f = new SftpFetcher(Cfg(path)))
        {
            f.WriteAtomic(bigText, "UTF-8");
            Snapshot s = f.Read();
            Check("2000 行写后读回一致", s.Text == bigText,
                "写 " + bigText.Length + " 读 " + s.Text.Length);
        }

        // ---- 7. 无残留临时/备份文件 ----
        using (SftpFetcher f = new SftpFetcher(Cfg(path)))
        {
            var entries = f.ListDir("/var/log");
            int junk = 0;
            StringBuilder names = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                string n = entries[i].Name;
                if (n.IndexOf(".rtv-tmp-") >= 0 || n.IndexOf(".rtv-bak-") >= 0)
                {
                    junk++;
                    names.Append(n).Append(" ");
                }
            }
            Check("目录里没有临时/备份残留", junk == 0, "残留: " + names.ToString());
            Check("只剩目标文件", entries.Count == 1 && entries[0].Name == "app.log",
                "entries=" + entries.Count);
        }

        // ---- 8. 冲突检测用 hash ----
        string h1 = SftpFetcher.HashText("abc");
        string h2 = SftpFetcher.HashText("abc");
        string h3 = SftpFetcher.HashText("abd");
        Check("相同内容 hash 相同", h1 == h2, h1);
        Check("不同内容 hash 不同", h1 != h3, h3);
        Check("hash 长度 64", h1.Length == 64, h1.Length.ToString());

        // ---- 9. 反复覆写不累积垃圾 ----
        using (SftpFetcher f = new SftpFetcher(Cfg(path)))
        {
            for (int i = 0; i < 10; i++) f.WriteAtomic("round " + i + "\n", "UTF-8");
            Snapshot s = f.Read();
            Check("连续覆写 10 次后内容正确", s.Text == "round 9\n", s.Text);
            var entries = f.ListDir("/var/log");
            Check("多次覆写后仍无垃圾文件", entries.Count == 1, "entries=" + entries.Count);
        }

        Console.WriteLine();
        Console.WriteLine("==================================");
        Console.WriteLine("PASS " + _pass + "   FAIL " + _fail);
        return _fail == 0 ? 0 : 1;
    }
}
