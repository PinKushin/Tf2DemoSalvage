#Requires -Version 7.2
<#
.SYNOPSIS
Validates the scripted era specimens staged in lcor and, only with -Apply, adds them to gcor (D240). A dry run is the
default and writes nothing.

.DESCRIPTION
The swap is ADDITIVE. It copies the new `tf2-<era>-build<n>-spec-{pov,stv}-<map>.dem` files into tools/corpus/demos
and leaves every old specimen where it is, because the old ones go only on the owner's word (D240). Retiring an old
specimen is a separate edit list in docs/measurements/2026-10-29-gcor-swap-prep.md.

**The `-spec-` tag stays in the gcor name.** Without it, the 2007 and 2008 targets would be the old file names exactly,
and every test that names those files would run its old expected values against new bytes. `Corpus.Demo` matches by
`Contains`, so the script also refuses a target that contains, or is contained by, an existing corpus file name, and a
target that contains a demo-name string literal in tests/ or tools/.

Each file must pass all of these checks:
- name: `tf2-<yyyy|live>-build<n>-spec-<pov|stv>-<map>.dem`.
- size: at least 4096 bytes (anything smaller is an LFS pointer stub), and no more than -MaxBytes.
- header: `HL2DEMO`, demo protocol 3, game directory `tf`, the map the name says, and frames > 0. A SourceTV file is
  recorded by `SourceTV Demo`; a POV file is not.
  A file still being recorded has frames 0, because the header is written last
  (docs/memory/a-header-written-last-is-absent.md). It is reported INCOMPLETE and not hashed.
- container: `tools/inspect_demo.py walk` must walk to the end, with dem_packet equal to the header's frames. This is
  the route the manifest names for its fields.
- protocol vs build: the header carries no build number. The build in the name is checked against the
  networkProtocol of the manifest's existing entry for that build, or against -ExpectedProtocol. A build with
  neither is UNVERIFIED, and that blocks -Apply. There is no protocol list (D211).
- buildings: Probe `buildings`, the production path, on POV and STV both. It must report at least two objects, each
  one's last state `building False placing False` with `pct 1`, and its own control line must read X of Y with
  Y > 0 and X = Y. Zero objects is a failure, not a vacuous pass.
- pairs: every era/build must have both a POV and a SourceTV half.

With -Apply, which also requires -Eras so that the eras entering gcor are a stated choice:
- each file is copied without overwriting, and the destination is re-hashed;
- the new manifest entries are spliced in front of `censusSpecimens`, and the result is parsed back as a check. If
  the anchor is not found exactly once, nothing is written and the fragment is printed for a hand Edit.
- `era`, `eraEvidence`, `source`, `whyItMatters`, `knownIssues` and `privacyNote` are written as TODO, because
  their content comes from each session's `version` lines in docs/measurements/2026-10-28-era-specimens-v2-plan.md
  and is never invented here.

The script never stages, commits, deletes or moves anything, and it never touches lcor.

.PARAMETER Source
The lcor folder holding the staged specimens.

.PARAMETER Names
Specific file names in -Source. The default is every `*-spec-pov-*` / `*-spec-stv-*` file; `spec0`/`spec1`/`spec2`
are earlier passes and never match.

.PARAMETER Eras
The era tokens to take (`2007`, `2010`, `live`, ...). Required with -Apply.

.PARAMETER ExpectedProtocol
`@{ 4231 = 15; 4269 = 15 }`: the protocol for a build the manifest does not date yet, from that session's `version`.

.PARAMETER SkipBuildings
Skips the Probe run (fast, for iterating on the other checks). Blocks -Apply.

.PARAMETER Destination
The gcor folder. Overridable only so -Apply can be rehearsed on a scratch copy.

.PARAMETER Manifest
The manifest to splice into. Overridable for the same reason as -Destination.

.PARAMETER SelfTest
Runs the script's own checks on the collision rule and the building-line parser, then exits.

.EXAMPLE
pwsh tools/specimen/gcor-swap.ps1
pwsh tools/specimen/gcor-swap.ps1 -Eras 2008,2009 -SkipBuildings
pwsh tools/specimen/gcor-swap.ps1 -Eras 2007,2008,2009,2010,2011,2013 -ExpectedProtocol @{4231=15;4269=15} -Apply
#>
[CmdletBinding()]
param(
    [string]$Source = 'F:\Tf2DemoSalvage-lcor',
    [string[]]$Names,
    [string[]]$Eras,
    [hashtable]$ExpectedProtocol = @{},
    [switch]$SkipBuildings,
    [long]$MaxBytes = 8MB,
    [string]$Destination,
    [string]$Manifest,
    [switch]$Apply,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
if (-not $Destination) { $Destination = Join-Path $Repo 'tools/corpus/demos' }
if (-not $Manifest) { $Manifest = Join-Path $Repo 'tools/corpus/manifest.json' }

$NamePattern = '^tf2-(?<era>\d{4}|live)-build(?<build>\d+)-spec-(?<mode>pov|stv)-(?<map>[a-z0-9_]+)\.dem$'

# The hazard is Corpus.Demo's `Contains`: a target matching a fragment some test passes, or two corpus names nested.
function Test-NameHazard([string]$Target, [string[]]$Files, [string[]]$Literals) {
    $t = [IO.Path]::GetFileNameWithoutExtension($Target)
    $hits = @()
    foreach ($f in $Files) {
        $s = [IO.Path]::GetFileNameWithoutExtension($f)
        if ($s -eq $t) { $hits += "same name as existing $f" }
        elseif ($t.Contains($s) -or $s.Contains($t)) { $hits += "nests with existing $f" }
    }
    foreach ($l in $Literals) {
        $s = $l -replace '\.dem$', ''
        if ($t.Contains($s)) { $hits += "contains the literal `"$l`"" }
    }
    $hits
}

# Last state per object from Probe `buildings` output, plus its control line.
function Read-Buildings([string[]]$Lines) {
    $last = [ordered]@{}
    $control = $null
    foreach ($line in $Lines) {
        if ($line -match '^tick\s+\d+ building (?<e>\d+): .* building (?<b>\w+) placing (?<p>\w+) .* pct (?<pct>[\d.]+) ') {
            $last[$Matches.e] = [pscustomobject]@{
                Entity = [int]$Matches.e; Building = $Matches.b; Placing = $Matches.p; Pct = [double]$Matches.pct
            }
        }
        elseif ($line -match 'control: the builder resolved to a listed player on (?<x>\d+) of (?<y>\d+) ticks') {
            $control = [pscustomobject]@{ Resolved = [int]$Matches.x; Total = [int]$Matches.y }
        }
    }
    $objects = @($last.Values)
    $unfinished = @($objects | Where-Object { $_.Building -ne 'False' -or $_.Placing -ne 'False' -or $_.Pct -ne 1 })
    $ok = $objects.Count -ge 2 -and $unfinished.Count -eq 0 -and $control -and $control.Total -gt 0 -and $control.Resolved -eq $control.Total
    [pscustomobject]@{ Ok = $ok; Objects = $objects; Unfinished = $unfinished; Control = $control }
}

if ($SelfTest) {
    $files = @('tf2-2008-build3420-stv-cp_granary.dem', 'tf2-2007-build3258-spec0-pov-cp_granary.dem')
    $literals = @('tf2-2008-build3420-stv-cp_granary', 'z1800', 'tf2-2007-build3258-pov')
    if (-not (Test-NameHazard 'tf2-2008-build3420-stv-cp_granary.dem' $files $literals)) { throw 'self-test: an old gcor name passed' }
    if (Test-NameHazard 'tf2-2008-build3420-spec-stv-cp_granary.dem' $files $literals) { throw 'self-test: a -spec- name was refused' }
    if (-not (Test-NameHazard 'tf2-2007-build3258-spec0-pov-cp_granary-x.dem' $files @())) { throw 'self-test: a nesting name passed' }
    $done = 'tick 9562 building 476: type 3 mode 0 team 2 builder 1 health 150/150 level 1 metal 0/200 building False placing False carried False mini False sapped False disabled False pct 1 shells 100'
    $half = 'tick 9000 building 485: type 0 mode 0 team 2 builder 1 health 66/150 level 1 metal 0/0 building True placing False carried False mini False sapped False disabled False pct 0.44 shells 0'
    $ctl = 'control: the builder resolved to a listed player on 10 of 10 ticks with a builder'
    if (-not (Read-Buildings @($done, ($done -replace '476', '485'), $ctl)).Ok) { throw 'self-test: two finished objects failed' }
    if ((Read-Buildings @($done, $half, $ctl)).Ok) { throw 'self-test: a 44 % dispenser passed' }
    if ((Read-Buildings @($ctl)).Ok) { throw 'self-test: zero objects passed' }
    if ((Read-Buildings @($done, ($done -replace '476', '485'), 'control: the builder resolved to a listed player on 9 of 10 ticks')).Ok) { throw 'self-test: a broken control passed' }
    Write-Host 'self-test: 7 of 7 checks hold'
    return
}

if ($Apply -and -not $Eras) { throw '-Apply needs -Eras: which eras enter gcor is the owner''s choice, not a default.' }
if ($Apply -and $SkipBuildings) { throw '-Apply cannot skip the buildings check.' }

# --- what already names a demo --------------------------------------------------------------------------------
$gcorNow = @(Get-ChildItem -LiteralPath $Destination -Filter *.dem | ForEach-Object Name)
$lcorNow = @(Get-ChildItem -LiteralPath $Source -Filter *.dem | ForEach-Object Name)
$literalPattern = '"(?<l>tf2-[a-z0-9]+-[a-z0-9_.-]*(?:pov|stv)[a-z0-9_.-]*)"'
$literals = @(Get-ChildItem -Path (Join-Path $Repo 'tests'), (Join-Path $Repo 'tools') -Recurse -Filter *.cs |
    Select-String -Pattern $literalPattern -AllMatches |
    ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups['l'].Value } | Sort-Object -Unique)

$manifestJson = Get-Content -Raw -LiteralPath $Manifest | ConvertFrom-Json
$protocolByBuild = @{}
foreach ($d in $manifestJson.demos) {
    if ($d.file -match '/tf2-\d{4}-build(?<b>\d+)-') { $protocolByBuild[[int]$Matches.b] = [int]$d.networkProtocol }
}
foreach ($k in $ExpectedProtocol.Keys) { $protocolByBuild[[int]$k] = [int]$ExpectedProtocol[$k] }

# --- selection ------------------------------------------------------------------------------------------------
$candidates = if ($Names) { $Names } else { $lcorNow | Where-Object { $_ -match $NamePattern } }
$candidates = @($candidates | Where-Object {
        -not $Eras -or ($_ -match $NamePattern -and $Eras -contains $Matches.era)
    })
if ($candidates.Count -eq 0) { throw "No staged specimen in $Source matches the selection." }

$probeProject = Join-Path $Repo 'tools/Tf2DemoSalvage.Probe'
$inspect = Join-Path $Repo 'tools/inspect_demo.py'
$env:MSBUILDDISABLENODEREUSE = '1'

$results = foreach ($name in $candidates) {
    $r = [ordered]@{ Name = $name; Verdict = 'PASS'; Problems = [Collections.Generic.List[string]]::new() }
    $path = Join-Path $Source $name
    if ($name -notmatch $NamePattern) { $r.Verdict = 'FAIL'; $r.Problems.Add('name does not follow tf2-<era>-build<n>-spec-<pov|stv>-<map>.dem'); [pscustomobject]$r; continue }
    $r.Era = $Matches.era; $r.Build = [int]$Matches.build; $r.Mode = $Matches.mode; $r.Map = $Matches.map
    if (-not (Test-Path -LiteralPath $path)) { $r.Verdict = 'FAIL'; $r.Problems.Add("not found in $Source"); [pscustomobject]$r; continue }

    $size = (Get-Item -LiteralPath $path).Length
    $r.SizeBytes = $size
    if ($size -lt 4096) { $r.Problems.Add("$size bytes: an LFS pointer stub, not a demo") }
    if ($size -gt $MaxBytes) { $r.Problems.Add("$size bytes is over -MaxBytes $MaxBytes (era specimens stay short)") }

    $h = [byte[]]::new(1072)
    $fs = [IO.File]::OpenRead($path); try { [void]$fs.Read($h, 0, 1072) } finally { $fs.Dispose() }
    $text = { param($o) $s = [Text.Encoding]::UTF8.GetString($h, $o, 260); $s.Substring(0, [Math]::Max(0, $s.IndexOf([char]0))) }
    $hdr = [ordered]@{
        Stamp = [Text.Encoding]::ASCII.GetString($h, 0, 8); DemoProtocol = [BitConverter]::ToInt32($h, 8)
        NetworkProtocol = [BitConverter]::ToInt32($h, 12); ServerName = & $text 16; ClientName = & $text 276
        Map = & $text 536; GameDir = & $text 796; Time = [BitConverter]::ToSingle($h, 1056)
        Ticks = [BitConverter]::ToInt32($h, 1060); Frames = [BitConverter]::ToInt32($h, 1064); Signon = [BitConverter]::ToInt32($h, 1068)
    }
    $r.Header = [pscustomobject]$hdr
    if ($hdr.Frames -le 0) {
        $r.Verdict = 'INCOMPLETE'; $r.Problems.Add('header frames 0: still recording, or the client died before writing the header')
        [pscustomobject]$r; continue
    }
    if ($hdr.Stamp -ne "HL2DEMO`0") { $r.Problems.Add("stamp '$($hdr.Stamp)'") }
    if ($hdr.DemoProtocol -ne 3) { $r.Problems.Add("demo protocol $($hdr.DemoProtocol)") }
    if ($hdr.GameDir -ne 'tf') { $r.Problems.Add("game dir '$($hdr.GameDir)'") }
    if ($hdr.Map -ne $r.Map) { $r.Problems.Add("header map '$($hdr.Map)' but the name says '$($r.Map)'") }
    $isStv = $hdr.ClientName -eq 'SourceTV Demo'
    if (($r.Mode -eq 'stv') -ne $isStv) { $r.Problems.Add("named $($r.Mode) but recorded by '$($hdr.ClientName)'") }

    if ($protocolByBuild.ContainsKey($r.Build)) {
        $r.ProtocolEvidence = "manifest/-ExpectedProtocol: build $($r.Build) is protocol $($protocolByBuild[$r.Build])"
        if ($hdr.NetworkProtocol -ne $protocolByBuild[$r.Build]) { $r.Problems.Add("protocol $($hdr.NetworkProtocol), but build $($r.Build) is $($protocolByBuild[$r.Build])") }
    }
    else {
        $r.ProtocolEvidence = "UNVERIFIED: no manifest entry or -ExpectedProtocol for build $($r.Build)"
        if ($r.Verdict -eq 'PASS') { $r.Verdict = 'UNVERIFIED' }
    }

    $walk = & python -I $inspect walk $path 2>&1
    $counts = [ordered]@{}
    foreach ($line in $walk) { if ("$line" -match '^\s+(?<c>dem_\w+)\s+(?<n>\d+)$') { $counts[$Matches.c] = [int]$Matches.n } }
    $r.CommandCounts = $counts
    if (-not ($walk -match 'MATCH$' -and -not ($walk -match 'MISMATCH'))) { $r.Problems.Add('dem_packet count does not match header frames (inspect_demo.py walk)') }
    # Every demo the engine writes ends in a 4-byte dem_stop, which the walker reports as truncated: the old gcor
    # specimens and z1800 all do (measured 2026-10-29), so only a cut anywhere else is a defect.
    $cut = @($walk -match 'TRUNCATED|!! unknown' | Where-Object { "$_" -notmatch 'TRUNCATED: dem_stop header at offset \d+ has 4 of 5 bytes' })
    if ($cut) { $r.Problems.Add('container walk: ' + ($cut -join '; ')) }
    elseif ($walk -match 'TRUNCATED: dem_stop') { $counts['dem_stop'] = 1 }  # the walker stops before counting it

    if (-not $SkipBuildings) {
        $out = & dotnet run --project $probeProject -c Release -- buildings $path 2>&1
        if ($LASTEXITCODE -ne 0) { $r.Problems.Add("Probe buildings exited $LASTEXITCODE") }
        $b = Read-Buildings @($out | ForEach-Object { "$_" })
        $r.Buildings = $b
        if (-not $b.Ok) {
            $what = ($b.Objects | ForEach-Object { "entity $($_.Entity) building $($_.Building) placing $($_.Placing) pct $($_.Pct)" }) -join '; '
            $ctl = if ($b.Control) { "$($b.Control.Resolved) of $($b.Control.Total)" } else { 'missing' }
            $r.Problems.Add("buildings not all finished: $($b.Objects.Count) objects [$what], control $ctl")
        }
    }

    foreach ($hz in Test-NameHazard $name ($gcorNow + ($lcorNow | Where-Object { $_ -ne $name })) $literals) { $r.Problems.Add("name: $hz") }

    if ($r.Problems.Count -gt 0 -and $r.Verdict -ne 'INCOMPLETE') { $r.Verdict = 'FAIL' }
    if ($r.Verdict -in 'PASS', 'UNVERIFIED') { $r.Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    [pscustomobject]$r
}

# --- pairs ----------------------------------------------------------------------------------------------------
$pairProblems = foreach ($g in ($results | Where-Object Era | Group-Object { "$($_.Era) build $($_.Build)" })) {
    $modes = @($g.Group | ForEach-Object Mode)
    foreach ($m in 'pov', 'stv') { if ($modes -notcontains $m) { "$($g.Name): no $m half staged" } }
}

# --- report ---------------------------------------------------------------------------------------------------
Write-Host ("`n{0} - {1} candidate(s) from {2}`n" -f ($(if ($Apply) { 'APPLY' } else { 'DRY RUN (nothing written; -Apply to copy)' })), $results.Count, $Source)
foreach ($r in $results) {
    $hdr = $r.Header
    $line = '{0,-10} {1}' -f $r.Verdict, $r.Name
    if ($hdr) { $line += ('  p{0} {1:N0} B {2:0.0} s {3} frames' -f $hdr.NetworkProtocol, $r.SizeBytes, $hdr.Time, $hdr.Frames) }
    if ($r.Sha256) { $line += "  sha256 $($r.Sha256)" }
    Write-Host $line
    if ($r.ProtocolEvidence) { Write-Host "           $($r.ProtocolEvidence)" }
    foreach ($p in $r.Problems) { Write-Host "           - $p" }
}
foreach ($p in $pairProblems) { Write-Host "PAIR       $p" }

$good = @($results | Where-Object Verdict -eq 'PASS')
$added = ($good | Measure-Object SizeBytes -Sum).Sum
Write-Host ("`n{0} of {1} pass. Adding them puts {2:N0} bytes ({3:0.0} MB) into LFS." -f $good.Count, $results.Count, $added, ($added / 1MB))
if ($Eras -contains 'live' -or (-not $Eras -and ($results | Where-Object Era -eq 'live'))) {
    Write-Host 'OWNER: the live pair is protocol 24, already in gcor (2013 and z1800); gcor grows only for a new protocol.'
}
$builds2010 = @($results | Where-Object Era -eq '2010' | ForEach-Object Build | Sort-Object -Unique)
if ($builds2010.Count -gt 1) { Write-Host "OWNER: 2010 has $($builds2010.Count) builds ($($builds2010 -join ', ')), both late protocol 15; one or both?" }

# --- what else must change ------------------------------------------------------------------------------------
$n = $good.Count
$gate = Select-String -LiteralPath (Join-Path $Repo 'build/gate.sh') -Pattern '^run Tf2DemoSalvage\.Corpus\.Tests\s+corpus\s+(\d+)'
$ci = Select-String -LiteralPath (Join-Path $Repo '.github/workflows/test.yml') -Pattern "corpus\.trx' (\d+) Corpus|-lt (\d+)\)"
Write-Host "`nEdits that follow from adding $n file(s) (the full list with reasons: docs/measurements/2026-10-29-gcor-swap-prep.md):"
foreach ($m in @($gate) + @($ci)) {
    $old = [int]($m.Matches[0].Groups[1].Value + $m.Matches[0].Groups[2].Value)
    $why = if ($m.Line -match '-lt') { 'the materialised-demo check: gcor file count' } else { 'arithmetic: CorpusContainerTests has one case per gcor file; confirm on the .trx' }
    Write-Host ("  {0}:{1}  {2} -> {3} ({4})" -f [IO.Path]::GetRelativePath($Repo, $m.Path), $m.LineNumber, $old, ($old + $n), $why)
}
$timeline = Get-Content -Raw -LiteralPath (Join-Path $Repo 'docs/TIMELINE.md')
foreach ($b in ($good | ForEach-Object Build | Sort-Object -Unique)) {
    if ($timeline -notmatch "\b$b\b") { Write-Host "  docs/TIMELINE.md  Dated anchors: no row for build $b; add one from its session's version line" }
}
$sweep = @($good | Where-Object { $_.Era -match '^\d{4}$' -and [int]$_.Era -ge 2007 -and [int]$_.Era -le 2013 })
if ($sweep) { Write-Host "  CorpusVoiceCodecEraTests.IsEraSpecimenFrom2007To2013 takes $($sweep.Count) of them: each must declare vaudio_speex (run it gcor-only)" }
$oldStems = $gcorNow | Where-Object { $_ -match '^tf2-\d{4}-build\d+-(pov|stv)-' } | ForEach-Object { $_ -replace '\.dem$', '' }
$refs = Get-ChildItem -Path (Join-Path $Repo 'tests'), (Join-Path $Repo 'tools'), (Join-Path $Repo 'managed'), (Join-Path $Repo 'benchmarks') -Recurse -Include *.cs |
    Select-String -SimpleMatch -Pattern $oldStems
Write-Host "  $(@($refs).Count) code line(s) name an old era specimen; they stay while it stays, and are re-derived when it retires:"
foreach ($ref in $refs) { Write-Host ('    {0}:{1}' -f [IO.Path]::GetRelativePath($Repo, $ref.Path), $ref.LineNumber) }

if (-not $Apply) { return }

# --- apply ----------------------------------------------------------------------------------------------------
$blocked = @($results | Where-Object Verdict -ne 'PASS')
if ($blocked -or $pairProblems) { throw "Refusing -Apply: $($blocked.Count) file(s) did not pass and $(@($pairProblems).Count) pair problem(s)." }

$entries = foreach ($r in $good) {
    $target = Join-Path $Destination $r.Name
    if (Test-Path -LiteralPath $target) { throw "$target exists; refusing to overwrite." }
    Copy-Item -LiteralPath (Join-Path $Source $r.Name) -Destination $target
    $copied = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($copied -ne $r.Sha256) { throw "$target hashed $copied after the copy, not $($r.Sha256)." }
    Write-Host "copied $($r.Name)"
    $hdr = $r.Header
    [ordered]@{
        file = "demos/$($r.Name)"; sha256 = $r.Sha256; sizeBytes = $r.SizeBytes
        source = 'TODO: scripted, tools/specimen/tf2ds_specimen.cfg + the era layer; cfg commit and launch from the plan''s session log'
        pointOfView = $(if ($r.Mode -eq 'stv') { 'SourceTV' } else { 'POV' }); build = $r.Build
        demoProtocol = $hdr.DemoProtocol; networkProtocol = $hdr.NetworkProtocol; serverName = $hdr.ServerName; clientName = $hdr.ClientName
        map = $hdr.Map; gameDir = $hdr.GameDir; playbackTimeSeconds = [Math]::Round([double]$hdr.Time, 2)
        playbackTicks = $hdr.Ticks; playbackFrames = $hdr.Frames; signonLengthBytes = $hdr.Signon; commandCounts = $r.CommandCounts
        era = 'TODO'; eraEvidence = 'TODO: the session''s version lines'; whyItMatters = 'TODO'; knownIssues = 'TODO'; privacyNote = 'TODO'
    }
}
# ConvertTo-Json writes CRLF on Windows; the manifest is LF (.gitattributes).
$fragment = ($entries | ForEach-Object { (($_ | ConvertTo-Json -Depth 5) -replace "`r`n", "`n") -replace '(?m)^', '    ' }) -join ",`n"
$raw = Get-Content -Raw -LiteralPath $Manifest
$anchor = [regex]'\n    \}\n  \],\n  "censusSpecimens"'
if ($anchor.Matches($raw).Count -ne 1) {
    Write-Host "manifest anchor not found exactly once; NOT written. Splice this into demos[] by hand:`n$fragment"
    throw 'manifest not updated.'
}
$spliced = $anchor.Replace($raw, "`n    },`n$fragment`n  ],`n  `"censusSpecimens`"", 1)
$check = $spliced | ConvertFrom-Json
if ($check.demos.Count -ne $manifestJson.demos.Count + $good.Count) { throw 'spliced manifest did not parse back with the expected entry count; NOT written.' }
[IO.File]::WriteAllText($Manifest, $spliced)
Write-Host "manifest: $($good.Count) entries added ($($check.demos.Count) total). Fill every TODO before committing; then git add the demos (LFS) and the manifest."
