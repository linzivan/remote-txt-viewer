param(
  [string]$Out,
  [string]$ConfigDir,
  [int]$WaitMs = 4000,
  [string]$Exe,
  [switch]$NoLaunch
)

# 路径都相对脚本位置推导，不写死个人目录
$root = Split-Path $PSScriptRoot -Parent
if (-not $Out)       { $Out = Join-Path $root "shot_pane.png" }
if (-not $ConfigDir) { $ConfigDir = Join-Path $env:TEMP "rtv_ui_test" }

# 用 PrintWindow 抓窗口位图：窗口不需要在前台，不抢焦点、不遮挡用户

Add-Type -AssemblyName System.Drawing

$code = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public class WinInfo
{
    public IntPtr Hwnd = IntPtr.Zero;
    public int W;
    public int H;
    public string Title = "";
}

public class WinShot
{
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    public static WinInfo BiggestWindow(uint targetPid)
    {
        WinInfo best = new WinInfo();
        long bestArea = 0;

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
                best.Hwnd = hw;
                best.W = r.Right - r.Left;
                best.H = r.Bottom - r.Top;
                best.Title = sb.ToString();
            }
            return true;
        }, IntPtr.Zero);

        return best;
    }
}
'@

if (-not ("WinShot" -as [type])) { Add-Type -TypeDefinition $code -Language CSharp }

$exe = if ($Exe) { $Exe } else { Join-Path $root "RemoteTxtViewer.exe" }

if (-not $NoLaunch) {
  $procName = [System.IO.Path]::GetFileNameWithoutExtension($exe)
  Get-Process $procName -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 400
  $env:RTV_CONFIG_DIR = $ConfigDir
  $env:RTV_NO_ACTIVATE = "1"
  $p = Start-Process -FilePath $exe -PassThru
  Start-Sleep -Milliseconds $WaitMs
} else {
  $p = Get-Process RemoteTxtViewer -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($null -eq $p) { Write-Output "NOT_RUNNING"; exit 2 }
  Start-Sleep -Milliseconds $WaitMs
}

if ($p.HasExited) { Write-Output "CRASHED exit=$($p.ExitCode)"; exit 1 }

$info = [WinShot]::BiggestWindow([uint32]$p.Id)
if ($info.Hwnd -eq [IntPtr]::Zero -or $info.W -le 0) {
  Write-Output ("NO_WINDOW pid={0}" -f $p.Id)
  exit 3
}

$bmp = New-Object System.Drawing.Bitmap $info.W, $info.H
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [WinShot]::PrintWindow($info.Hwnd, $hdc, 2)
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

Write-Output ("pid={0} title='{1}' size={2}x{3} printwindow={4} saved={5}" -f $p.Id, $info.Title, $info.W, $info.H, $ok, $Out)
