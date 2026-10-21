using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RemoteTxtViewer
{
    static class TestEditor
    {
        static int pass = 0, fail = 0;

        static void Check(string name, bool ok, string detail)
        {
            if (ok) { pass++; Console.WriteLine("  OK   " + name); }
            else { fail++; Console.WriteLine("  FAIL " + name + "  <- " + detail); }
        }

        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== 时间线「保存者用户名」测试 ===");

            string dir = Path.Combine(Path.GetTempPath(), "rtv_editor_test_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);

            try
            {
                // 1. 写入一条本机保存记录，带用户名
                ChangeLog log = new ChangeLog(dir);
                log.Add("line1\nline2\n", DateTime.Now, 2, 2, true, false, "");
                log.Add("line1\nline2\nline3\n", DateTime.Now, 1, 3, false, true, "18321");

                List<ChangeEntry> list = log.Snapshot();
                Check("两条记录都在", list.Count == 2, "count=" + list.Count);

                ChangeEntry last = list[list.Count - 1];
                Check("保存者 = 18321", last.Editor == "18321", "Editor='" + last.Editor + "'");
                Check("标记为本机编辑", last.IsLocalEdit, "IsLocalEdit=" + last.IsLocalEdit);
                Check("行数 = 3", last.TotalLines == 3, "lines=" + last.TotalLines);

                // 2. 初始化记录没有保存者
                Check("初始记录无保存者", list[0].Editor == "", "Editor='" + list[0].Editor + "'");

                // 3. 重新加载（模拟重启），用户名要从索引里恢复
                ChangeLog log2 = new ChangeLog(dir);
                List<ChangeEntry> list2 = log2.Snapshot();
                Check("重启后仍是 2 条", list2.Count == 2, "count=" + list2.Count);

                bool found = false;
                for (int i = 0; i < list2.Count; i++)
                    if (list2[i].IsLocalEdit && list2[i].Editor == "18321") found = true;
                Check("重启后用户名持久化", found, "未找到 Editor=18321 的记录");

                // 4. 用户名含特殊字符（空格/中文）也要能正确往返
                ChangeLog log3 = new ChangeLog(dir);
                log3.Add("x\n", DateTime.Now, 0, 1, false, true, "张 三");
                ChangeLog log4 = new ChangeLog(dir);
                List<ChangeEntry> list4 = log4.Snapshot();
                bool found2 = false;
                for (int i = 0; i < list4.Count; i++)
                    if (list4[i].Editor == "张 三") found2 = true;
                Check("含空格中文用户名往返", found2, "未找到 '张 三'");

                // 5. 旧索引（只有 8 列，没有用户名）要能读，Editor 为空而不是崩溃
                long oldId = DateTime.Now.Ticks - 10000000;
                File.WriteAllText(Path.Combine(dir, "index.tsv"),
                    oldId + "\t" + DateTime.Now.Ticks + "\t\t5\t5\t50\t0\t1\n",
                    new UTF8Encoding(false));
                // 索引里引用的快照文件必须存在，否则加载时会被跳过
                File.WriteAllText(Path.Combine(dir, "c_" + oldId + ".txt"), "a\nb\nc\nd\ne\n",
                    new UTF8Encoding(false));
                ChangeLog log5 = new ChangeLog(dir);
                List<ChangeEntry> list5 = log5.Snapshot();
                Check("旧 8 列索引可读", list5.Count == 1, "count=" + list5.Count);
                if (list5.Count == 1)
                {
                    Check("旧记录 Editor 为空", list5[0].Editor == "", "Editor='" + list5[0].Editor + "'");
                    Check("旧记录 IsLocalEdit 保留", list5[0].IsLocalEdit, "IsLocalEdit=" + list5[0].IsLocalEdit);
                }

                Console.WriteLine();
                Console.WriteLine("通过 " + pass + " 项，失败 " + fail + " 项");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }

            return fail == 0 ? 0 : 1;
        }
    }
}
