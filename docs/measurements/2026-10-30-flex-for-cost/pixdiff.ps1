# Counts the pixels two same-size captures differ in, and the box around them. Copied from
# docs/measurements/2026-10-27-empty-view-breakdown/pixdiff.ps1 (commit 21974cb6) without its -WorldOnly HUD mask: the
# identity check here compares every pixel.
param([Parameter(Mandatory)][string]$PathA, [Parameter(Mandatory)][string]$PathB)
Add-Type -AssemblyName System.Drawing
$ia = New-Object System.Drawing.Bitmap -ArgumentList $PathA
$ib = New-Object System.Drawing.Bitmap -ArgumentList $PathB
if (-not $ia.Width -or $ia.Width -ne $ib.Width -or $ia.Height -ne $ib.Height) { throw "captures did not load or differ in size" }
$n = 0; $x0 = [int]::MaxValue; $y0 = [int]::MaxValue; $x1 = -1; $y1 = -1
for ($y = 0; $y -lt $ia.Height; $y++) {
  for ($x = 0; $x -lt $ia.Width; $x++) {
    if ($ia.GetPixel($x, $y).ToArgb() -ne $ib.GetPixel($x, $y).ToArgb()) {
      $n++; $x0 = [math]::Min($x0, $x); $y0 = [math]::Min($y0, $y); $x1 = [math]::Max($x1, $x); $y1 = [math]::Max($y1, $y)
    }
  }
}
"$($ia.Width)x$($ia.Height): $n pixels differ" + $(if ($n) { "; box ($x0,$y0)-($x1,$y1)" } else { '' })
