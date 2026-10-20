using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class MkLnk
{
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length < 2)
        {
            Console.WriteLine("usage: mklnk <lnkPath> <target> [icon] [desc]");
            return 1;
        }

        string lnkPath = args[0];
        string target = args[1];
        string icon = args.Length > 2 && args[2].Length > 0 ? args[2] : target;
        string desc = args.Length > 3 ? args[3] : "";
        string workdir = Path.GetDirectoryName(target);

        try
        {
            if (File.Exists(lnkPath)) File.Delete(lnkPath);

            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            object lnk = shell.GetType().InvokeMember("CreateShortcut",
                BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });

            Type lt = lnk.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { workdir });
            lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { icon + ",0" });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { desc });
            lt.InvokeMember("WindowStyle", BindingFlags.SetProperty, null, lnk, new object[] { 1 });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);

            // 通知 Shell 重新提取图标
            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
            System.Threading.Thread.Sleep(200);
            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);

            bool ok = File.Exists(lnkPath);
            Console.WriteLine("created : " + lnkPath);
            Console.WriteLine("target  : " + target);
            Console.WriteLine("icon    : " + icon);
            Console.WriteLine("iconOK  : " + File.Exists(icon));
            Console.WriteLine("targetOK: " + File.Exists(target));
            Console.WriteLine("result  : " + (ok ? "OK" : "FAILED"));
            return ok ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine("error: " + ex.GetType().Name + ": " + ex.Message);
            return 3;
        }
    }
}
