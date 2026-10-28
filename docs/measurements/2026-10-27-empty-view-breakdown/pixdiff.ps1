# Counts the pixels two same-size captures differ in, and the box around them. -WorldOnly leaves out the HUD's regions at
# 1280x720 (the top bar, y < 50; the chat, x < 420 and 430 <= y < 550; the crosshair, 630-660 x 360-380), so what remains
# is the world. Re-created 2026-10-28 from the record's description; the session's original was removed in cleanup.
# NOTE: PowerShell names are case-insensitive, so no variable here may share a parameter's name.
param([Parameter(Mandatory)][string]$PathA, [Parameter(Mandatory)][string]$PathB, [switch]$WorldOnly)
Add-Type -AssemblyName System.Drawing
$ia = New-Object System.Drawing.Bitmap -ArgumentList $PathA
$ib = New-Object System.Drawing.Bitmap -ArgumentList $PathB
if (-not $ia.Width -or $ia.Width -ne $ib.Width -or $ia.Height -ne $ib.Height) { throw "captures did not load or differ in size" }
function Hud([int]$x, [int]$y) { $y -lt 50 -or ($x -lt 420 -and $y -ge 430 -and $y -lt 550) -or ($x -ge 630 -and $x -lt 660 -and $y -ge 360 -and $y -lt 380) }
$n = 0; $x0 = [int]::MaxValue; $y0 = [int]::MaxValue; $x1 = -1; $y1 = -1
for ($y = 0; $y -lt $ia.Height; $y++) {
  for ($x = 0; $x -lt $ia.Width; $x++) {
    if ($WorldOnly -and (Hud $x $y)) { continue }
    if ($ia.GetPixel($x, $y).ToArgb() -ne $ib.GetPixel($x, $y).ToArgb()) {
      $n++; $x0 = [math]::Min($x0, $x); $y0 = [math]::Min($y0, $y); $x1 = [math]::Max($x1, $x); $y1 = [math]::Max($y1, $y)
    }
  }
}
"$($ia.Width)x$($ia.Height)$(if ($WorldOnly) { ' world only' }): $n pixels differ" + $(if ($n) { "; box ($x0,$y0)-($x1,$y1)" } else { '' })
