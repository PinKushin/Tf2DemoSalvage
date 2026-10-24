# Analysis of the existing rung-2 raw outputs (no new runs). Usage: pwsh camera-growth.ps1 [-Drop 10] [-MinDiff 0.03]
# Parses each 'frame rate ...' per-second line (stage means after 'mean over N frames:' and every 'name value' after 'parts:')
# and each 'moment cost' line, drops the first -Drop per-second samples (loading/JIT), takes the plain mean of the rest per run,
# then per condition: mean of run means, spread = max-min of run means. Flag = |R2-R1| > max(spread R1, spread R2).
param([int]$Drop = 10, [double]$MinDiff = 0.03)
$dir = $PSScriptRoot
$conds = [ordered]@{ M0 = '01','06','07'; R1 = '03','09','11'; R2 = '05','08','10' }
$culture = [Globalization.CultureInfo]::InvariantCulture

function Read-Run([string]$file, [int]$drop) {
    $samples = @(); $moments = @()
    foreach ($line in Get-Content $file) {
        if ($line -match '^\s+frame rate .* mean over (\d+) frames: (.*?); parts: (.*)$') {
            $h = @{}; $stageText = $Matches[2]; $partsText = $Matches[3]
            foreach ($kv in ($stageText -split ', ')) { if ($kv -match '^(\w+) ([\d.]+)') { $h["stage $($Matches[1])"] = [double]::Parse($Matches[2], $culture) } }
            foreach ($kv in ($partsText -split ', ')) { if ($kv -match '^(.+) ([\d.]+)\s*$') { $h[$Matches[1]] = [double]::Parse($Matches[2], $culture) } }
            $samples += , $h
        } elseif ($line -match 'moment cost, mean over \d+ rebuilds: ([\d.]+) ms = (.*?); built') {
            $h = @{ 'moment total' = [double]::Parse($Matches[1], $culture) }
            foreach ($m in [regex]::Matches($Matches[2], '(?<![\w/])(sample|drawlist|models|pose|weapons|viewmodel|lighting|simulate|wornlight|setup|skin|anim|reports|collate|baked|place|undrawn|advance|rest) ([\d.]+)')) {
                $k = "moment $($m.Groups[1].Value)"; if (-not $h.ContainsKey($k)) { $h[$k] = [double]::Parse($m.Groups[2].Value, $culture) }
            }
            $moments += , $h
        }
    }
    $keep = @($samples | Select-Object -Skip $drop) + @($moments | Select-Object -Skip ([int][math]::Floor($drop / 2)))
    $sum = @{}; $n = @{}
    foreach ($s in $keep) { foreach ($k in $s.Keys) { $sum[$k] += $s[$k]; $n[$k]++ } }
    $mean = @{}; foreach ($k in $sum.Keys) { $mean[$k] = $sum[$k] / $n[$k] }
    $mean['_samples'] = $samples.Count; $mean['_kept'] = @($samples | Select-Object -Skip $drop).Count
    $mean
}

$runs = @{}
foreach ($c in $conds.Keys) { $runs[$c] = @(foreach ($id in $conds[$c]) { Read-Run (Get-ChildItem $dir -Filter "$id-*.txt")[0].FullName $Drop }) }
"samples per run (all/kept): " + (($runs.Keys | ForEach-Object { "$_=" + (($runs[$_] | ForEach-Object { "$($_['_samples'])/$($_['_kept'])" }) -join ',') }) -join '  ')
$keys = $runs.Values | ForEach-Object { $_ } | ForEach-Object { $_.Keys } | Where-Object { $_ -notlike '_*' } | Sort-Object -Unique
function Stat($c, $k) {
    $v = @($runs[$c] | Where-Object { $_.ContainsKey($k) } | ForEach-Object { $_[$k] })
    if ($v.Count -eq 0) { return @{ m = $null; s = $null } }
    @{ m = ($v | Measure-Object -Average).Average; s = (($v | Measure-Object -Maximum).Maximum - ($v | Measure-Object -Minimum).Minimum) }
}
$fmt = { param($x) if ($null -eq $x) { '-' } else { $x.ToString('0.00', $culture) } }
"| part | M0 | R1 | R2 | R2-R1 | spread M0 | spread R1 | spread R2 | flag |"
"|---|---|---|---|---|---|---|---|---|"
foreach ($k in $keys) {
    $a = Stat 'M0' $k; $b = Stat 'R1' $k; $c = Stat 'R2' $k
    $both = ($null -ne $b.m -and $null -ne $c.m)
    if ($both) { $d = $c.m - $b.m; if ([math]::Abs($d) -lt $MinDiff -and $k -notlike 'stage *') { continue } }
    elseif ((@($a.m, $b.m, $c.m) | Where-Object { $_ -ne $null } | Measure-Object -Maximum).Maximum -lt 0.05) { continue }
    $f = ''
    if ($both -and [math]::Abs($d) -gt [math]::Max($b.s, $c.s)) { $f = if ($d -lt 0) { 'FELL' } else { 'GREW' } }
    $ds = if ($both) { $d.ToString('+0.00;-0.00', $culture) } else { '-' }
    "| $k | $(& $fmt $a.m) | $(& $fmt $b.m) | $(& $fmt $c.m) | $ds | $(& $fmt $a.s) | $(& $fmt $b.s) | $(& $fmt $c.s) | $f |"
}
