# Sequence test for tf2-measurement-gate.ps1 (stateful: it reads git history). Run by hand:
#   pwsh -NoProfile -File <this> -Root <empty scratch dir>      Exit code = number of mismatches.
# Builds throwaway repos under -Root. The real entry is seeded from commit 66539d53 (docs/measurements branch) so the
# control does not flip when the lead fills in the live file's Result.
param([Parameter(Mandatory)][string]$Root)
$ErrorActionPreference = 'Stop'
$h = Join-Path $PSScriptRoot 'tf2-measurement-gate.ps1'
$repo = (& git -C $PSScriptRoot rev-parse --show-toplevel)
$f12 = (& git -C $repo show '66539d53:docs/measurements/2026-10-21-tf2-vs-viewer-f12.md') -join "`n"
if (-not $f12) { throw 'cannot read the seeded entry from 66539d53' }
$rel = 'docs/measurements/2026-10-21-tf2-vs-viewer-f12.md'
$fail = 0

function New-Repo([string]$name) {
    $d = Join-Path $Root $name
    New-Item -ItemType Directory -Path (Join-Path $d 'docs/measurements'), (Join-Path $d 'tests') -Force | Out-Null
    git -C $d init -q; git -C $d config user.email t@t; git -C $d config user.name t
    Set-Content (Join-Path $d 'docs/measurements/README.md') 'method'
    git -C $d add -A; git -C $d commit -qm init
    $d
}
function Put([string]$d, [string]$text, [switch]$Commit) {
    Set-Content -LiteralPath (Join-Path $d $rel) -Value $text -NoNewline
    if ($Commit) { git -C $d add -A; git -C $d commit -qm "entry" }
}
function Run([string]$d, [string]$cmd, [string]$tool = 'Bash') {
    $o = @{ hook_event_name = 'PreToolUse'; tool_name = $tool; cwd = $d; tool_input = @{ command = $cmd } }
    $out = ($o | ConvertTo-Json -Compress) | pwsh -NoProfile -File $h
    if ($LASTEXITCODE -ne 0) { return "exit$LASTEXITCODE" }
    if ("$out" -match '"deny"') { 'deny' } elseif ("$out" -match 'additionalContext') { 'note' } else { 'allow' }
}
function Check($name, $actual, $expected) {
    if ($actual -ceq $expected) { "PASS $name" } else { "FAIL $name : expected [$expected] got [$actual]"; $script:fail++ }
}
$withMethod = $f12 -replace '(?s)(## Method\s*\n)\(.*?\)', '$1tf2demoview f12.dem --tick 30000 --first-person --measure 20 +fps_max 0; TF2 via mcp__tf2__console.'
$measure = 'pwsh run-exclusive.ps1 tf2demoview f12.dem --tick 30000 --first-person --measure 20 +fps_max 0'

# --- gate 1 ---
$a = New-Repo 'g1'
Check 'measure, no entry at all: denied'                  (Run $a $measure) 'deny'
Check 'same, # exploratory: allowed with a note'           (Run $a "$measure # exploratory") 'note'
Check 'unrelated command: allowed silently'                (Run $a 'dotnet build Tf2DemoSalvage.slnx') 'allow'
Check 'grep for --measure is not a measurement'            (Run $a 'grep -n -- "tf2demoview x --measure" CLAUDE.md') 'allow'
Check 'git commit mentioning --measure is not one'         (Run $a 'git commit -m "docs: tf2demoview --measure 20"') 'allow'
Put $a $f12
Check 'real f12 entry, uncommitted: denied'                (Run $a $measure) 'deny'
Put $a $f12 -Commit
Check 'real f12 entry as committed (Method placeholder): denied' (Run $a $measure) 'deny'
Put $a $withMethod -Commit
Check 'f12 entry with Method filled, committed: allowed'   (Run $a $measure) 'note'
Check 'dotnet-trace collect: allowed (open entry)'         (Run $a 'dotnet-trace collect -p 1') 'note'
Check 'measuring probe: allowed (open entry)'              (Run $a 'dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- voice-memory x.dem') 'note'
Check 'named marker, wrong file: denied'                   (Run $a "$measure # measurement: docs/measurements/nope.md") 'deny'
Check 'named marker, right file: allowed'                  (Run $a "$measure # measurement: $rel") 'note'
Check 'console cl_showfps 1: allowed (open entry)'         (Run $a 'cl_showfps 1' 'mcp__tf2__console') 'note'
Check 'console cl_showfps 0 is not a measurement'          (Run $a 'cl_showfps 0' 'mcp__tf2__console') 'allow'
$b = New-Repo 'g1b'
Check 'console net_graph 1, no entry: denied'              (Run $b 'net_graph 1' 'mcp__tf2__console') 'deny'
Check 'console with // exploratory: note'                  (Run $b 'net_graph 1 // exploratory' 'mcp__tf2__console') 'note'
Check 'non-measuring probe: allowed silently'              (Run $b 'dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- carried x.dem 5') 'allow'
Check 'tf2demoview without --shot/--measure: allowed'      (Run $b 'tf2demoview.exe f12.dem --tick 5') 'allow'
Check 'tf2demoview --shot is evidence, not a figure: allowed' (Run $b 'pwsh run-exclusive.ps1 tf2demoview.exe f12.dem --tick 5 --shot out.png') 'allow'
Check '--measure inside a quoted string: allowed'          (Run $b 'echo "tf2demoview f12.dem --measure 20"') 'allow'
Check '--measure in a grep: allowed'                       (Run $b 'grep -rn "tf2demoview .* --measure" docs') 'allow'
Check 'control: --measure, no entry: denied'               (Run $b $measure) 'deny'
Put $b ($withMethod -replace '(?s)(## Result\s*\n)\(.*?\)', '$1median 312 fps') -Commit
Check 'entry already has a Result: denied'                 (Run $b $measure) 'deny'

# --- gate 2 ---
$c = New-Repo 'g2'
Put $c $withMethod -Commit
Check 'commit with an open entry (no Result): allowed'     (Run $c 'git commit -m "docs: x"') 'allow'
Put $c ($withMethod -replace '(?s)(## Result\s*\n)\(.*?\)', '$1median 312 fps, raw in out.txt')
Check 'Result filled, Verdict placeholder: commit denied'  (Run $c 'git commit -m "docs: x"') 'deny'
Check 'same: push denied'                                  (Run $c 'git push origin main') 'deny'
Check 'same: gh release denied'                            (Run $c 'gh release create v1') 'deny'
$done = $withMethod -replace '(?s)(## Result\s*\n)\(.*?\)', '$1median 312 fps' -replace '(?s)(## Verdict\s*\n)\(.*?\)', '$1Confirmed: ratio 4.6x.'
Put $c $done
Check 'Result and Verdict filled: commit allowed'          (Run $c 'git commit -m "docs: x"') 'allow'
Put $c ($done -replace 'Confirmed: ratio 4.6x.', 'TF2 is faster.')
Check 'Prediction stated, Verdict lacks the word: denied'  (Run $c 'git commit -m "docs: x"') 'deny'
Put $c ($done -replace 'median 250-450 fps', 'median 250-500 fps')
Check 'Prediction edited after first commit: denied'       (Run $c 'git commit -m "docs: x"') 'deny'

# Freeze line = the first commit carrying a filled Result; baseline = the committed version before it.
$g = New-Repo 'g2b'
Put $g $withMethod -Commit
$ctl2 = $withMethod -replace 'Run each side twice', 'Run each side twice, back to back'
Put $g $ctl2
Check 'Control edited before any Result (uncommitted): allowed' (Run $g 'git commit -m "docs: x"') 'allow'
Put $g $ctl2 -Commit
Check 'Control edited and committed before any Result: allowed' (Run $g 'git commit -m "docs: x"') 'allow'
$ran = $ctl2 -replace '(?s)(## Result\s*\n)\(.*?\)', '$1median 312 fps' -replace '(?s)(## Verdict\s*\n)\(.*?\)', '$1Confirmed: ratio 4.6x.'
Put $g $ran -Commit
Check 'Result committed, sections as baseline: allowed'    (Run $g 'git commit -m "docs: x"') 'allow'
Put $g ($ran -replace 'back to back', 'an hour apart')
Check 'Control edited after the Result commit: denied'     (Run $g 'git commit -m "docs: x"') 'deny'
Put $g ($ran -replace 'back to back', 'an hour apart') -Commit
Check 'same edit once committed: still denied'             (Run $g 'git commit -m "docs: x"') 'deny'
# A labelled amendment is the owner's loosening (2026-10-29): the section says it was changed after the run, so the
# reader is told and nothing is hidden.
Put $g ($ran -replace 'back to back', 'back to back. Added after the run (review): twice more')
Check 'Control edited after the run, labelled "Added after": allowed' (Run $g 'git commit -m "docs: x"') 'allow'
Put $g ($ran -replace 'back to back', 'back to back. Changed from the plan: twice more')
Check 'Control edited after the run, labelled "Changed from the plan": allowed' (Run $g 'git commit -m "docs: x"') 'allow'
$k = New-Repo 'g2c'
Put $k $ran -Commit
Put $k ($ran -replace 'back to back', 'an hour apart')
Check 'Result in the very first commit: no baseline, allowed' (Run $k 'git commit -m "docs: x"') 'allow'

# The real f12 entry, its history replayed commit by commit (Control was edited before the run, in 1f10ce4e).
$real = New-Repo 'g2real'
foreach ($sha in '42e4ee37', '66539d53', '1f10ce4e', '4193c95f') {
    Put $real ((& git -C $repo show "${sha}:$rel") -join "`n") -Commit
}
Check 'real f12 entry history: commit allowed'             (Run $real 'git commit -m "docs: x"') 'allow'
Put $c ($done -replace 'Run each side twice', 'Run each side once')
Check 'Control edited after first commit: denied'          (Run $c 'git commit -m "docs: x"') 'deny'

# --- gate 3 ---
$e = New-Repo 'g3'
Check 'fix: with nothing: denied'                          (Run $e 'git commit -m "fix: the wait"') 'deny'
Check 'fix: with Tested by + Observed: allowed'            (Run $e "git commit -m `"fix: the wait`" -m `"Tested by: FooTests`nObserved: red then green`"") 'allow'
Check 'fix: with Tested by only: denied'                   (Run $e "git commit -m `"fix: the wait`" -m `"Tested by: FooTests`"") 'deny'
Check 'fix: with No-test reason: allowed'                  (Run $e "git commit -m `"fix: typo`" -m `"No-test: comment only`"") 'allow'
Check 'fix: with tests/ in git add: allowed'               (Run $e 'git add src/a.cs tests/ATests.cs && git commit -m "fix: the wait"') 'allow'
Set-Content (Join-Path $e 'tests/BTests.cs') 'x'; git -C $e add tests/BTests.cs
Check 'fix: with a staged test: allowed'                   (Run $e 'git commit -m "fix(scene): the wait"') 'allow'
git -C $e reset -q
Check 'heredoc fix: subject, nothing: denied'              (Run $e "git commit -m `"`$(cat <<'EOF'`nfix: the wait`n`nbody`nEOF`n)`"") 'deny'
Check 'perf: with test but no measurement file: denied'    (Run $e 'git add tests/x && git commit -m "perf: fewer draws"') 'deny'
Check 'perf: citing its file + tested: allowed'            (Run $e 'git add tests/x && git commit -m "perf: fewer draws" -m "before/after docs/measurements/2026-10-21-a.md"') 'allow'
Check 'docs: commit claiming 2x faster uncited: denied'    (Run $e 'git commit -m "docs: the viewer is 2x faster"') 'deny'
Check 'same claim citing the file: allowed'                (Run $e 'git commit -m "docs: 2x faster (measurements/2026-10-21-a.md)"') 'allow'
Check 'feat: commit, no test needed: allowed'              (Run $e 'git commit -m "feat: a menu"') 'allow'

"--- $fail failed ---"
exit $fail
