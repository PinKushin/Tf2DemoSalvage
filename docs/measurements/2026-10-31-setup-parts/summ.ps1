# Per-run mean of each `roots split (...)` part and of the roots total, leaving the first moment-cost line out.
# Anchors on `roots split \(` because `blend` appears twice in the line (the record's Method section).
param([string[]]$Files = @('01.txt', '02.txt', '03.txt'), [string]$Dir = $PSScriptRoot)

$parts = [ordered]@{
    blend   = 'roots split \(blend ([0-9.]+)'
    base    = 'base ([0-9.]+), accumulate'
    accum   = 'accumulate ([0-9.]+) of which'
    layers  = 'layer sampling ([0-9.]+) over'
    samples = 'layer sampling [0-9.]+ over ([0-9.]+)'
    adjust  = 'adjust ([0-9.]+)'
    loop    = 'loop ([0-9.]+), ik'
    ik      = 'ik ([0-9.]+), finish'
    finish  = 'finish ([0-9.]+);'
    unsplit = 'unsplit (-?[0-9.]+)\)'
    roots   = 'setup [0-9.]+ \(roots ([0-9.]+) over'
    built   = 'setup [0-9.]+ \(roots [0-9.]+ over ([0-9.]+) built'
    elig    = 'eligible [0-9.]+ over ([0-9.]+)'
    kids    = 'children ([0-9.]+) over'
    prepose = 'prepose ([0-9]+) over'
    anim    = ', anim ([0-9.]+)'
}

foreach ($file in $Files) {
    $lines = Select-String -Path (Join-Path $Dir $file) -Pattern 'moment cost' | Select-Object -Skip 1
    $row = [ordered]@{ run = $file; n = @($lines).Count }
    foreach ($name in $parts.Keys) {
        $values = foreach ($line in $lines) {
            if ($line.Line -match $parts[$name]) { [double]$Matches[1] }
        }
        $row[$name] = if ($values) { [math]::Round(($values | Measure-Object -Average).Average, 3) } else { 'n/a' }
    }
    [pscustomobject]$row
}
