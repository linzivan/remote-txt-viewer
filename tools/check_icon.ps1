param(
  [string]$Exe,
  [string]$Out
)

$root = Split-Path $PSScriptRoot -Parent
if (-not $Exe) { $Exe = Join-Path $root "RemoteTxtViewer.exe" }
if (-not $Out) { $Out = Join-Path $root "icons\exe_icon_check.png" }

Add-Type -AssemblyName System.Drawing

$ico = [System.Drawing.Icon]::ExtractAssociatedIcon($Exe)
if ($null -eq $ico) { Write-Output "NO_ICON"; exit 1 }

$src = $ico.ToBitmap()
Write-Output ("extracted icon: " + $src.Width + "x" + $src.Height)

$scale = 8
$big = New-Object System.Drawing.Bitmap ($src.Width * $scale), ($src.Height * $scale)
$g = [System.Drawing.Graphics]::FromImage($big)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.Clear([System.Drawing.Color]::FromArgb(238, 241, 246))
$g.DrawImage($src, 0, 0, $big.Width, $big.Height)
$g.Dispose()

$big.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()
$src.Dispose()

Write-Output ("saved " + $Out)
