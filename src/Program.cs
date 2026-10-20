using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RemoteTxtViewer
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            // 关键：必须在任何 SSH 相关类型被 JIT 之前挂上资源解析器，
            // 这样 Renci.SshNet.dll 能从 exe 内部加载出来（单文件绿色版）。
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            SetProcessDPIAware();

            RunApp();
        }

        // 单独一个方法 + NoInlining：确保 Main 的 JIT 不会提前去解析 SSH 类型。
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunApp()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            string simpleName;
            try { simpleName = new AssemblyName(args.Name).Name; }
            catch { return null; }

            if (simpleName != "Renci.SshNet") return null;

            try
            {
                using (System.IO.Stream s =
                    typeof(Program).Assembly.GetManifestResourceStream("Renci.SshNet.dll"))
                {
                    if (s == null) return null;
                    byte[] buf = new byte[s.Length];
                    int read = 0;
                    while (read < buf.Length)
                    {
                        int n = s.Read(buf, read, buf.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    return Assembly.Load(buf);
                }
            }
            catch { return null; }
        }
    }
}
