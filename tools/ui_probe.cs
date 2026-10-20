using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

internal static class UiProbe
{
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessageStr(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "shot";
        string outPath = args.Length > 1 ? args[1] : System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "probe.png");

        Process p = null;
        string pidEnv = Environment.GetEnvironmentVariable("RTV_PID");
        if (!string.IsNullOrEmpty(pidEnv))
        {
            int wantPid;
            if (int.TryParse(pidEnv, out wantPid))
            {
                try { p = Process.GetProcessById(wantPid); }
                catch { p = null; }
            }
        }
        if (p == null)
        {
            string nameEnv = Environment.GetEnvironmentVariable("RTV_PROC");
            string procName = string.IsNullOrEmpty(nameEnv) ? "RemoteTxtViewer" : nameEnv;
            Process[] ps = Process.GetProcessesByName(procName);
            if (ps.Length == 0) { Console.WriteLine("NOT_RUNNING(" + procName + ")"); return 2; }
            p = ps[0];
        }

        int w, h;
        IntPtr hWnd = MainWindow((uint)p.Id, out w, out h);
        if (hWnd == IntPtr.Zero) { Console.WriteLine("NO_WINDOW"); return 3; }

        Console.WriteLine("window " + w + "x" + h);

        if (cmd == "dump")
        {
            Console.WriteLine("--- child windows ---");
            EnumChildWindows(hWnd, delegate(IntPtr hw, IntPtr l)
            {
                StringBuilder cn = new StringBuilder(128);
                GetClassName(hw, cn, 128);
                StringBuilder tx = new StringBuilder(256);
                GetWindowText(hw, tx, 256);
                RECT r;
                GetClientRect(hw, out r);
                Console.WriteLine("  " + cn.ToString().PadRight(22) + " " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top) + "  |" + tx.ToString() + "|");
                return true;
            }, IntPtr.Zero);
            return 0;
        }

        if (cmd == "list")
        {
            IntPtr lb = FindListBox(hWnd);
            if (lb == IntPtr.Zero) { Console.WriteLine("NO_LISTBOX"); return 4; }
            int count = SendMessage(lb, 0x018B, IntPtr.Zero, IntPtr.Zero).ToInt32();
            int ih = SendMessage(lb, 0x01A1, IntPtr.Zero, IntPtr.Zero).ToInt32();
            Console.WriteLine("items=" + count + " itemHeight=" + ih);
            for (int i = 0; i < count; i++)
            {
                int len = SendMessage(lb, 0x018A, (IntPtr)i, IntPtr.Zero).ToInt32();   // LB_GETTEXTLEN
                Console.WriteLine("  [" + i + "] len=" + len);
            }
            return 0;
        }

        if (cmd == "dumpdlg")
        {
            string title = args.Length > 2 ? args[2] : "";
            IntPtr dlg = FindWindowByTitle((uint)p.Id, title);
            if (dlg == IntPtr.Zero) { Console.WriteLine("NO_DIALOG " + title); return 7; }
            Console.WriteLine("--- children of |" + title + "| ---");
            EnumChildWindows(dlg, delegate(IntPtr hw, IntPtr l)
            {
                StringBuilder cn = new StringBuilder(128);
                GetClassName(hw, cn, 128);
                StringBuilder tx = new StringBuilder(256);
                GetWindowText(hw, tx, 256);
                Console.WriteLine("  " + cn.ToString().PadRight(42) + " |" + tx.ToString() + "|");
                return true;
            }, IntPtr.Zero);
            return 0;
        }

        if (cmd == "setedit")
        {
            string text = args.Length > 2 ? args[2] : "";
            IntPtr rtb = FindChildByClass(hWnd, "RichEdit");
            if (rtb == IntPtr.Zero) { Console.WriteLine("NO_EDIT_CONTROL"); return 10; }
            SendMessageStr(rtb, 0x000C, IntPtr.Zero, text);   // WM_SETTEXT
            System.Threading.Thread.Sleep(400);
            Console.WriteLine("editor text set, chars=" + text.Length);
            return 0;
        }

        if (cmd == "getedit")
        {
            IntPtr rtb = FindChildByClass(hWnd, "RichEdit");
            if (rtb == IntPtr.Zero) { Console.WriteLine("NO_EDIT_CONTROL"); return 10; }
            int len = (int)SendMessage(rtb, 0x000E, IntPtr.Zero, IntPtr.Zero);   // WM_GETTEXTLENGTH
            Console.WriteLine("editor chars=" + len);
            return 0;
        }

        if (cmd == "shotwin")
        {
            string title = args.Length > 2 ? args[2] : "";
            string dst = args.Length > 3 ? args[3] : outPath;

            IntPtr dlg = FindWindowByTitle((uint)p.Id, title);
            if (dlg == IntPtr.Zero) { Console.WriteLine("NO_WINDOW " + title); return 9; }

            RECT r;
            GetWindowRect(dlg, out r);
            int dw = r.Right - r.Left;
            int dh = r.Bottom - r.Top;

            Save(dlg, dw, dh, dst);
            Console.WriteLine("saved |" + title + "| " + dw + "x" + dh + " -> " + dst);
            return 0;
        }

        if (cmd == "windows")
        {
            Console.WriteLine("--- top-level windows of pid ---");
            EnumWindows(delegate(IntPtr hw, IntPtr l)
            {
                uint pid;
                GetWindowThreadProcessId(hw, out pid);
                if (pid != p.Id) return true;
                StringBuilder sb = new StringBuilder(256);
                GetWindowText(hw, sb, 256);
                if (sb.Length > 0)
                    Console.WriteLine("  " + (IsWindowVisible(hw) ? "vis" : "hid") + " |" + sb.ToString() + "|");
                return true;
            }, IntPtr.Zero);
            return 0;
        }

        if (cmd == "clickat")
        {
            int x = 0, y = 0;
            if (args.Length > 2) int.TryParse(args[2], out x);
            if (args.Length > 3) int.TryParse(args[3], out y);

            IntPtr ts = FindToolStrip(hWnd);
            IntPtr target = (ts != IntPtr.Zero) ? ts : hWnd;
            IntPtr lp = (IntPtr)((y << 16) | (x & 0xFFFF));
            SendMessage(target, 0x0201, (IntPtr)1, lp);
            SendMessage(target, 0x0202, IntPtr.Zero, lp);
            System.Threading.Thread.Sleep(1200);
            Console.WriteLine("clicked at " + x + "," + y + " on " + (ts != IntPtr.Zero ? "toolstrip" : "form"));
            return 0;
        }

        if (cmd == "dlgok")
        {
            string title = args.Length > 2 ? args[2] : "";
            string btnText = args.Length > 3 ? args[3] : "确定";

            IntPtr dlg = FindWindowByTitle((uint)p.Id, title);
            if (dlg == IntPtr.Zero) { Console.WriteLine("NO_DIALOG " + title); return 7; }

            IntPtr btn = FindChildByText(dlg, "BUTTON", btnText);
            if (btn == IntPtr.Zero) { Console.WriteLine("NO_BUTTON " + btnText); return 8; }

            SendMessage(btn, 0x00F5, IntPtr.Zero, IntPtr.Zero);   // BM_CLICK
            System.Threading.Thread.Sleep(800);
            Console.WriteLine("clicked [" + btnText + "] in |" + title + "|");
            return 0;
        }

        if (cmd == "click")
        {
            int index = 0;
            if (args.Length > 2) int.TryParse(args[2], out index);

            IntPtr lb = FindListBox(hWnd);
            if (lb == IntPtr.Zero) { Console.WriteLine("NO_LISTBOX"); return 4; }

            int count = SendMessage(lb, 0x018B, IntPtr.Zero, IntPtr.Zero).ToInt32();
            int ih = SendMessage(lb, 0x01A1, IntPtr.Zero, IntPtr.Zero).ToInt32();
            if (index < 0 || index >= count) { Console.WriteLine("BAD_INDEX count=" + count); return 5; }

            int x = 30;
            int y = ih * index + ih / 2;
            IntPtr lp = (IntPtr)((y << 16) | (x & 0xFFFF));
            SendMessage(lb, 0x0201, (IntPtr)1, lp);        // WM_LBUTTONDOWN
            SendMessage(lb, 0x0202, IntPtr.Zero, lp);      // WM_LBUTTONUP

            System.Threading.Thread.Sleep(700);
            Console.WriteLine("clicked item " + index + " of " + count);
        }

        if (cmd == "clickbtn")
        {
            // 按标题点一个按钮（例如 "返回实时" / "收藏"）
            string text = args.Length > 2 ? args[2] : "";
            IntPtr btn = FindChildByText(hWnd, "Button", text);
            if (btn == IntPtr.Zero) { Console.WriteLine("NO_BUTTON " + text); return 6; }
            PostMessage(btn, 0x00F5, IntPtr.Zero, IntPtr.Zero);   // BM_CLICK
            System.Threading.Thread.Sleep(900);
            Console.WriteLine("clicked button " + text);
        }

        Save(hWnd, w, h, outPath);
        Console.WriteLine("saved " + outPath);
        return 0;
    }

    private static void Save(IntPtr hWnd, int w, int h, string path)
    {
        using (Bitmap bmp = new Bitmap(w, h))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(hWnd, hdc, 2);
                g.ReleaseHdc(hdc);
            }
            bmp.Save(path, ImageFormat.Png);
        }
    }

    private static IntPtr MainWindow(uint targetPid, out int w, out int h)
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;
        int bw = 0, bh = 0;

        EnumWindows(delegate(IntPtr hw, IntPtr l)
        {
            uint pid;
            GetWindowThreadProcessId(hw, out pid);
            if (pid != targetPid || !IsWindowVisible(hw)) return true;

            StringBuilder sb = new StringBuilder(512);
            GetWindowText(hw, sb, 512);
            if (sb.Length == 0) return true;

            RECT r;
            GetWindowRect(hw, out r);
            long area = (long)(r.Right - r.Left) * (long)(r.Bottom - r.Top);
            if (area > bestArea)
            {
                bestArea = area;
                best = hw;
                bw = r.Right - r.Left;
                bh = r.Bottom - r.Top;
            }
            return true;
        }, IntPtr.Zero);

        w = bw; h = bh;
        return best;
    }

    private static IntPtr FindToolStrip(IntPtr parent)
    {
        IntPtr found = IntPtr.Zero;

        EnumChildWindows(parent, delegate(IntPtr hw, IntPtr l)
        {
            if (found != IntPtr.Zero) return true;
            StringBuilder cn = new StringBuilder(128);
            GetClassName(hw, cn, 128);
            if (cn.ToString().IndexOf("WindowsForms10.Window", StringComparison.OrdinalIgnoreCase) < 0) return true;

            RECT r;
            GetClientRect(hw, out r);
            int w = r.Right - r.Left;
            int h = r.Bottom - r.Top;
            if (w > 900 && h >= 26 && h <= 40) found = hw;
            return true;
        }, IntPtr.Zero);

        return found;
    }

    private static IntPtr FindWindowByTitle(uint targetPid, string title)
    {
        IntPtr found = IntPtr.Zero;

        EnumWindows(delegate(IntPtr hw, IntPtr l)
        {
            uint pid;
            GetWindowThreadProcessId(hw, out pid);
            if (pid != targetPid) return true;

            StringBuilder sb = new StringBuilder(256);
            GetWindowText(hw, sb, 256);
            if (sb.ToString() == title) { found = hw; return false; }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    private static IntPtr FindListBox(IntPtr parent)
    {
        IntPtr found = IntPtr.Zero;
        int bestW = 0;

        EnumChildWindows(parent, delegate(IntPtr hw, IntPtr l)
        {
            StringBuilder cn = new StringBuilder(128);
            GetClassName(hw, cn, 128);
            if (cn.ToString().IndexOf("LISTBOX", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                RECT r;
                GetClientRect(hw, out r);
                int wid = r.Right - r.Left;
                if (wid > bestW) { bestW = wid; found = hw; }
            }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    private static IntPtr FindChildByClass(IntPtr parent, string className)
    {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, delegate(IntPtr hw, IntPtr l)
        {
            if (found != IntPtr.Zero) return true;
            StringBuilder cn = new StringBuilder(128);
            GetClassName(hw, cn, 128);
            if (cn.ToString().IndexOf(className, StringComparison.OrdinalIgnoreCase) >= 0) found = hw;
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static IntPtr FindChildByText(IntPtr parent, string className, string text)
    {
        IntPtr found = IntPtr.Zero;

        EnumChildWindows(parent, delegate(IntPtr hw, IntPtr l)
        {
            if (found != IntPtr.Zero) return true;

            StringBuilder cn = new StringBuilder(128);
            GetClassName(hw, cn, 128);
            if (cn.ToString().IndexOf(className, StringComparison.OrdinalIgnoreCase) < 0) return true;

            StringBuilder tx = new StringBuilder(256);
            GetWindowText(hw, tx, 256);
            if (tx.ToString() == text) found = hw;
            return true;
        }, IntPtr.Zero);

        return found;
    }
}
