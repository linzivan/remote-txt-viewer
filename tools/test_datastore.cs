using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RemoteTxtViewer
{
    internal static class TestRunner
    {
        private static int _pass = 0;
        private static int _fail = 0;

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) { _pass++; Console.WriteLine("  PASS  " + name); }
            else { _fail++; Console.WriteLine("  FAIL  " + name + "   -> " + detail); }
        }

        private static string FreshDir(string tag)
        {
            string d = Path.Combine(Path.GetTempPath(), "rtv_test_" + tag + "_" + DateTime.Now.Ticks.ToString());
            Directory.CreateDirectory(d);
            return d;
        }

        public static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            string root = Path.Combine(Path.GetTempPath(), "rtv_tests_" + DateTime.Now.Ticks.ToString());
            Directory.CreateDirectory(root);
            Console.WriteLine("test root: " + root);

            TestChangeLog(Path.Combine(root, "hist"));
            TestChangeLogExpiry(Path.Combine(root, "hist2"));
            TestFavorites(Path.Combine(root, "fav"));

            try { Directory.Delete(root, true); } catch { }

            Console.WriteLine();
            Console.WriteLine("==================================");
            Console.WriteLine("PASS " + _pass + "   FAIL " + _fail);
            return _fail == 0 ? 0 : 1;
        }

        // ---------------- ChangeLog ----------------
        private static void TestChangeLog(string dir)
        {
            Console.WriteLine();
            Console.WriteLine("[ChangeLog] 基本功能 + 持久化");
            ChangeLog log = new ChangeLog(dir);

            Check("新目录为空", log.Count == 0, "Count=" + log.Count);

            ChangeEntry a = log.Add("line1\nline2\n", DateTime.UtcNow, -1, 2, true);
            Check("首条是 baseline", a.IsBaseline, "IsBaseline=" + a.IsBaseline);
            Check("Add 后 Count=1", log.Count == 1, "Count=" + log.Count);

            System.Threading.Thread.Sleep(20);
            ChangeEntry b = log.Add("line1\nline2\nline3\n", DateTime.UtcNow, 1, 3, false);
            System.Threading.Thread.Sleep(20);
            ChangeEntry c = log.Add("line1\nline2\nline3\nline4\n", DateTime.UtcNow, 1, 4, false);
            Check("Add 后 Count=3", log.Count == 3, "Count=" + log.Count);
            Check("新增行数=1", b.NewLines == 1, "NewLines=" + b.NewLines);

            List<ChangeEntry> snap = log.Snapshot();
            Check("快照按时间升序", snap.Count == 3 && snap[0].Id < snap[2].Id, "count=" + snap.Count);
            Check("最新在最后", snap[snap.Count - 1].Id == c.Id, "lastId=" + snap[snap.Count - 1].Id);

            Check("GetText 最新正确", log.GetText(c) == "line1\nline2\nline3\nline4\n", "got=" + log.GetText(c).Replace("\n", "\\n"));
            Check("GetText 旧的正确", log.GetText(a) == "line1\nline2\n", "got=" + log.GetText(a).Replace("\n", "\\n"));

            // 重新加载：模拟重启
            ChangeLog log2 = new ChangeLog(dir);
            Check("重启后仍为 3 条", log2.Count == 3, "Count=" + log2.Count);
            List<ChangeEntry> s2 = log2.Snapshot();
            Check("重启后正文可从磁盘读回",
                log2.GetText(s2[s2.Count - 1]) == "line1\nline2\nline3\nline4\n",
                "got=" + log2.GetText(s2[s2.Count - 1]).Replace("\n", "\\n"));
            Check("重启后 line 数保留", s2[s2.Count - 1].TotalLines == 4, "TotalLines=" + s2[s2.Count - 1].TotalLines);

            // 中文 + 大内容
            string big = "";
            for (int i = 0; i < 500; i++) big += "第 " + i + " 行 中文内容测试 abcdef\n";
            log2.Add(big, DateTime.UtcNow, 500, 500, false);
            ChangeLog log3 = new ChangeLog(dir);
            List<ChangeEntry> s3 = log3.Snapshot();
            Check("大内容(中文)往返一致", log3.GetText(s3[s3.Count - 1]) == big,
                "len " + log3.GetText(s3[s3.Count - 1]).Length + " vs " + big.Length);

            log3.Clear();
            Check("Clear 后 Count=0", log3.Count == 0, "Count=" + log3.Count);
            Check("Clear 后目录内无内容文件",
                Directory.GetFiles(dir, "c_*.txt").Length == 0,
                "files=" + Directory.GetFiles(dir, "c_*.txt").Length);
        }

        // ---------------- 24 小时清理 ----------------
        private static void TestChangeLogExpiry(string dir)
        {
            Console.WriteLine();
            Console.WriteLine("[ChangeLog] 24 小时过期清理");
            ChangeLog log = new ChangeLog(dir);
            log.Add("old\n", DateTime.UtcNow, -1, 1, true);
            System.Threading.Thread.Sleep(20);
            log.Add("new\n", DateTime.UtcNow, 1, 1, false);
            Check("清理前 2 条", log.Count == 2, "Count=" + log.Count);

            // 把索引里第一条的时间改成 25 小时前
            string idx = Path.Combine(dir, "index.tsv");
            string[] lines = File.ReadAllLines(idx, Encoding.UTF8);
            StringBuilder sb = new StringBuilder();
            long oldTicks = DateTime.Now.AddHours(-25).Ticks;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("#") || lines[i].Length == 0) { sb.AppendLine(lines[i]); continue; }
                string[] f = lines[i].Split('\t');
                if (i == 1) f[1] = oldTicks.ToString();   // 第一条数据行
                sb.AppendLine(string.Join("\t", f));
            }
            File.WriteAllText(idx, sb.ToString(), new UTF8Encoding(false));

            ChangeLog reloaded = new ChangeLog(dir);
            Check("过期条目被清掉，只剩 1 条", reloaded.Count == 1, "Count=" + reloaded.Count);
            Check("剩下的那条是新的", reloaded.GetText(reloaded.Snapshot()[0]) == "new\n",
                "got=" + reloaded.GetText(reloaded.Snapshot()[0]).Replace("\n", "\\n"));
            Check("过期条目的内容文件也删了",
                Directory.GetFiles(dir, "c_*.txt").Length == 1,
                "files=" + Directory.GetFiles(dir, "c_*.txt").Length);
        }

        // ---------------- Favorites ----------------
        private static void TestFavorites(string dir)
        {
            Console.WriteLine();
            Console.WriteLine("[FavoritesStore] 增删改查 + 持久化");
            FavoritesStore fav = new FavoritesStore(dir);
            Check("初始为空", fav.Count == 0, "Count=" + fav.Count);

            FavoriteItem a = fav.Add("订单异常现场", "AAA\nBBB\n", "root@1.2.3.4 · app.log", 2);
            Check("收藏成功", fav.Count == 1, "Count=" + fav.Count);
            Check("名称正确", a.Name == "订单异常现场", "name=" + a.Name);
            Check("行数记录", a.Lines == 2, "lines=" + a.Lines);

            System.Threading.Thread.Sleep(20);
            FavoriteItem b = fav.Add("", "CCC\n", "src2", 1);
            Check("空名自动用时间", b.Name.Length > 0 && b.Name.Contains("-"), "name=" + b.Name);

            List<FavoriteItem> items = fav.Items();
            Check("返回 2 条", items.Count == 2, "Count=" + items.Count);
            Check("最新的排最前", items[0].Id == b.Id, "first=" + items[0].Name);

            Check("重命名成功", fav.Rename(a.Id, "改名后的收藏"), "ret");
            Check("重命名后名称已变", fav.Items()[1].Name == "改名后的收藏", "name=" + fav.Items()[1].Name);
            Check("空名重命名被拒", !fav.Rename(a.Id, "   "), "ret");

            Check("正文读回正确", fav.GetText(a) == "AAA\nBBB\n", "got=" + fav.GetText(a).Replace("\n", "\\n"));

            FavoritesStore fav2 = new FavoritesStore(dir);
            Check("重启后 2 条", fav2.Count == 2, "Count=" + fav2.Count);
            List<FavoriteItem> i2 = fav2.Items();
            bool found = false;
            for (int i = 0; i < i2.Count; i++)
                if (i2[i].Name == "改名后的收藏" && fav2.GetText(i2[i]) == "AAA\nBBB\n") found = true;
            Check("重启后名称+正文都对", found, "not found");

            Check("删除成功", fav2.Delete(a.Id), "ret");
            Check("删除后剩 1 条", fav2.Count == 1, "Count=" + fav2.Count);
            Check("删除后文件也没了",
                Directory.GetFiles(dir, "f_*.txt").Length == 1,
                "files=" + Directory.GetFiles(dir, "f_*.txt").Length);
        }
    }
}
