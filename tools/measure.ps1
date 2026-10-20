param([string]$Img)

$root = Split-Path $PSScriptRoot -Parent
if (-not $Img) { $Img = Join-Path $root "shot.png" }

Add-Type -AssemblyName System.Drawing

$bmp = [System.Drawing.Bitmap]::FromFile($Img)
Write-Output ("image {0}x{1}" -f $bmp.Width, $bmp.Height)

function ScanLine($y) {
  $runs = @()
  $inRun = $false
  $start = 0
  for ($x = 600; $x -lt 1320; $x++) {
    $c = $bmp.GetPixel($x, $y)
    $dark = ($c.R -lt 170 -and $c.G -lt 170 -and $c.B -lt 170)
    if ($dark -and -not $inRun) { $inRun = $true; $start = $x }
    elseif (-not $dark -and $inRun) {
      $inRun = $false
      if (($x - $start) -ge 1) { $runs += ("{0}-{1}" -f $start, ($x - 1)) }
    }
  }
  if ($inRun) { $runs += ("{0}-1320" -f $start) }
  return ($runs -join " ")
}

foreach ($y in 306, 311, 313, 340, 345, 347, 372, 377, 404, 409, 436, 441, 468, 473, 500, 505, 532, 537, 564, 569) {
  Write-Output ("y={0}: {1}" -f $y, (ScanLine $y))
}

$bmp.Dispose()
