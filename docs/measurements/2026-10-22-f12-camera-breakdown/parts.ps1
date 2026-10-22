param([string]$File, [int]$Skip = 5)
# Frame-weighted steady mean of each phase and part named on the viewer's own per-second lines.
$lines = @(Get-Content $File | Where-Object { $_ -match 'frame rate .* mean over (\d+) frames' }) | Select-Object -Skip $Skip
$sum = [ordered]@{}; $frames = 0
foreach ($l in $lines) {
    $n = [int]([regex]::Match($l, 'mean over (\d+) frames').Groups[1].Value); $frames += $n
    $body = ($l -split 'frames: ', 2)[1]
    foreach ($m in [regex]::Matches($body, '(?<k>[A-Za-z][^,;:]*?) (?<v>[0-9.]+)( ms)?(?=[,;]|$)')) {
        $k = $m.Groups['k'].Value.Trim(); $v = [double]$m.Groups['v'].Value
        if (-not $sum.Contains($k)) { $sum[$k] = 0.0 }; $sum[$k] += $v * $n
    }
}
"steady seconds $($lines.Count), frames $frames"
foreach ($k in $sum.Keys) { if ($k -match '^(sound|camera|advance|hud|draw|unaccounted|camera .*)$') { '{0,-40} {1,6:N2}' -f $k, ($sum[$k] / $frames) } }
$ms = (@(Get-Content $File | Where-Object { $_ -match 'frame rate' }) | Select-Object -Skip $Skip | ForEach-Object { $n=[int]([regex]::Match($_, 'mean over (\d+)').Groups[1].Value); [pscustomobject]@{n=$n; ms=[double]([regex]::Match($_, '\), ([0-9.]+) ms;').Groups[1].Value)} })
'frame ms {0:N2}' -f (($ms | ForEach-Object { $_.n * $_.ms } | Measure-Object -Sum).Sum / $frames)
