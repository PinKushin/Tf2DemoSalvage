# Pixel comparison of two shots: how many pixels differ, the largest channel difference, and the differing bounding box.
param([Parameter(Mandatory)][string]$Left, [Parameter(Mandatory)][string]$Right)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$a = [System.Drawing.Bitmap]::new((Resolve-Path $Left).Path)
$b = [System.Drawing.Bitmap]::new((Resolve-Path $Right).Path)
if ($a.Width -ne $b.Width -or $a.Height -ne $b.Height) { throw 'sizes differ' }
$rect = [System.Drawing.Rectangle]::new(0, 0, $a.Width, $a.Height)
$fmt = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
$da = $a.LockBits($rect, 'ReadOnly', $fmt); $db = $b.LockBits($rect, 'ReadOnly', $fmt)
$n = $da.Stride * $a.Height
$pa = [byte[]]::new($n); $pb = [byte[]]::new($n)
[Runtime.InteropServices.Marshal]::Copy($da.Scan0, $pa, 0, $n); [Runtime.InteropServices.Marshal]::Copy($db.Scan0, $pb, 0, $n)
$a.UnlockBits($da); $b.UnlockBits($db)
$diff = 0; $max = 0; $x0 = $a.Width; $y0 = $a.Height; $x1 = -1; $y1 = -1
for ($i = 0; $i -lt $n; $i += 4) {
  $d = [Math]::Max([Math]::Max([Math]::Abs($pa[$i] - $pb[$i]), [Math]::Abs($pa[$i + 1] - $pb[$i + 1])), [Math]::Abs($pa[$i + 2] - $pb[$i + 2]))
  if ($d -gt 0) {
    $diff++; if ($d -gt $max) { $max = $d }
    $p = $i / 4; $x = $p % $a.Width; $y = [Math]::Floor($p / $a.Width)
    if ($x -lt $x0) { $x0 = $x }; if ($x -gt $x1) { $x1 = $x }; if ($y -lt $y0) { $y0 = $y }; if ($y -gt $y1) { $y1 = $y }
  }
}
"{0} vs {1}: {2} of {3} pixels differ, max channel {4}, box ({5},{6})-({7},{8})" -f (Split-Path $Left -Leaf), (Split-Path $Right -Leaf), $diff, ($n / 4), $max, $x0, $y0, $x1, $y1
