# Per hash part: A against A on the common labels (the control), then B against A where A agreed with A.
param([string]$A1 = 'h1-A', [string]$A2 = 'h2-A', [string]$B = 'h3-B')
$o = $PSScriptRoot
function Read([string]$n) {
  $d = @{}
  Get-Content "$o/$n-hash.txt" | ForEach-Object {
    if ($_ -match 'frame hash at ([\d.]+): pose (\w+) particles (\w+) rest (\w+) cull (\w+) image (\w+)') {
      $d[$Matches[1]] = @{ pose = $Matches[2]; particles = $Matches[3]; rest = $Matches[4]; cull = $Matches[5]; image = $Matches[6] }
    }
  }
  $d
}
$x = Read $A1; $y = Read $A2; $z = Read $B
$common = $x.Keys | Where-Object { $y.ContainsKey($_) -and $z.ContainsKey($_) }
"labels: $A1 $($x.Count), $A2 $($y.Count), $B $($z.Count), common $(@($common).Count)"
foreach ($part in 'pose', 'particles', 'rest', 'cull', 'image') {
  $agree = @($common | Where-Object { $x[$_][$part] -eq $y[$_][$part] })
  $bSame = @($agree | Where-Object { $z[$_][$part] -eq $x[$_][$part] })
  "{0,-10} A=A on {1} of {2}; where A=A, B=A on {3} of {1}" -f $part, $agree.Count, @($common).Count, $bSame.Count
}
