# Refuses a performance measurement with nowhere to record it, a commit that leaves a measurement unanswered or
# rewrites what was written before its run, and a fix:/perf: commit that names no test of the decision it makes.
#
# WHY. The owner, 2026-10-21: "we need to get real numbers ... that's the scientific method, we need hooks that force
# you to use the Popperian method", narrowed the same day to: "things need to be measured and actually observed, not
# deduced or induced. If you make any reasoning decision, that decision needs to be tested in a way that you are
# actually testing what you are deciding." The lead had quoted a pub figure as if measured and a gap never paired
# ("about 600 against about 67"). The method is docs/measurements/README.md; this makes skipping it cost a refusal.
#
# THREE GATES, all PreToolUse, each decided from the payload plus `git` in the payload's cwd:
#
# 1. PRE-RUN (Bash|PowerShell|mcp__tf2__console). A measuring command - `tf2demoview ... --measure`,
#    `dotnet-trace collect|report`, a measuring Probe (`Tf2DemoSalvage.Probe ... -- timeline-cost|timeline-heap|
#    sample-cost|map-memory|process-memory|voice-memory|sound-read|voice-roundtrip`), or a console line turning on
#    `cl_showfps`/`net_graph` or running `timedemo` - is denied unless some docs/measurements/*.md is COMMITTED (read
#    from HEAD, so an unsaved draft does not count) with Observation, Question, Method and Control filled and Result
#    still a placeholder: the observation then has somewhere to land, and the committed form is the baseline gate 2
#    compares against. `# measurement: docs/measurements/<file>.md` (`// measurement:` in the TF2 console) names the
#    file; without it any open file will do. Hypothesis and Prediction are OPTIONAL: a missing one is a note, not a
#    refusal. ESCAPE: `# exploratory` (`// exploratory` in the console, Source's comment, so the cvar still parses).
#    An exploratory number is not an observation and may not be quoted as a figure in a doc. `--shot` is NOT gated:
#    a screenshot is evidence, not a figure (lead's review, 2026-10-21).
#
# 2. CLOSE THE LOOP (`git commit`, `git push`, `gh release`). Denied while any docs/measurements/*.md in the working
#    tree has a filled Result and a placeholder Verdict; while a file that states a Prediction has a Verdict that does
#    not say confirmed, falsified or inconclusive; or while a section that was FILLED before the run (Observation,
#    Question, Hypothesis, Prediction, Falsified if, Control, Method) differs now from its pre-run form, UNLESS the
#    section itself carries "added after", "changed from the plan" or "amendment" (owner, 2026-10-29: loosened after
#    three honest, labelled amendments blocked every commit; the label tells the reader, so nothing is hidden - it
#    does not check that the original text survives). THE RUN is the
#    first committed version with a filled Result; the pre-run form is the committed version before it (or HEAD,
#    while the Result exists only in the working copy). Before that, edits are free: the f12 entry's Control was
#    corrected before its run. A section that was a placeholder before the run may be filled later. A Result in the
#    very first commit has no pre-run form and is not checked. An open file with no Result does not block commits.
#
# 3. TEST THE DECISION (`git commit` whose message subject starts `fix:` or `perf:`). Denied unless the commit
#    touches tests/ (staged, or named in a `git add` in the same command) or the message carries both `Tested by:`
#    and `Observed:`; `No-test: <reason>` waives it. A `perf:` commit must also cite its before/after file
#    (`docs/measurements/<file>.md`), no waiver. Any commit message saying "fixed" or "faster" beside a figure
#    (fps, ms, MB, %, x) with no `measurements/<file>.md` or `finding NN` citation is denied.
#
# WHAT NO HOOK CAN SEE: that a test really tests the decision, that it was red first (only a commit history can show
# that), that the Control is a GOOD control, or that a pasted Result is what the instrument printed. A file committed
# one minute before its run passes; the commit is the record, not the delay. `git commit -F <file>` and editor
# commits are not read. These checks make the record exist; reading it is still review.
#
# Tests: tf2-measurement-gate.sequence-test.ps1 (builds throwaway repos; run by hand).

$ErrorActionPreference = 'Stop'

# git's stdout is decoded with the console code page. Launched from a shell on an OEM page (437), a UTF-8 blob read
# through `git cat-file` came back as mojibake ("…" as three characters) while Get-Content read the same bytes
# correctly, so every section with a non-ASCII character looked edited since its run and gate 2 denied every commit.
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $call = $raw | ConvertFrom-Json
    $tool = [string]$call.tool_name
    $cmd = [string]$call.tool_input.command
    if ([string]::IsNullOrWhiteSpace($cmd)) { exit 0 }
} catch { exit 0 }   # Never let the guard break the tool it guards.

$console = $tool -eq 'mcp__tf2__console'

# Classify before any git runs, so an ordinary command pays for nothing.
$unquoted = $cmd -replace '"[^"]*"', '""' -replace "'[^']*'", "''"
$searchy = $cmd -match '^\s*(git|grep|rg|gh|cat|echo|sed|head|tail|less)\b'
$measures = if ($console) { $cmd -match '\b(cl_showfps|net_graph)\s+"?[1-9]|\btimedemo\b' }
            elseif ($searchy) { $false }
            else {
                $unquoted -match 'tf2demoview\S*\s[^\n]*--measure\b' -or
                $unquoted -match '\bdotnet-trace\s+(collect|report)\b' -or
                $unquoted -match 'Tf2DemoSalvage\.Probe\b[^\n]*\s--\s+(timeline-cost|timeline-heap|sample-cost|map-memory|process-memory|voice-memory|sound-read|voice-roundtrip)\b'
            }
$commits = -not $console -and $cmd -match '\bgit\b[^\n|;&]*\scommit\b'
$closes = -not $console -and ($commits -or $cmd -match '\bgit\b[^\n|;&]*\spush\b|\bgh\s+release\b')
if (-not ($measures -or $closes)) { exit 0 }

function Out-Decision([string]$decision, [string]$text) {
    $o = if ($decision -eq 'context') { @{ hookEventName = 'PreToolUse'; additionalContext = $text } }
         else { @{ hookEventName = 'PreToolUse'; permissionDecision = $decision; permissionDecisionReason = $text } }
    @{ hookSpecificOutput = $o } | ConvertTo-Json -Depth 4 -Compress
    exit 0
}

$cwd = if ($call.cwd) { [string]$call.cwd } elseif ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { (Get-Location).Path }
$root = (& git -C $cwd rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $root) { exit 0 }
$root = [string]$root

$names = 'Observation', 'Question', 'Hypothesis', 'Prediction', 'Falsified if', 'Control', 'Method', 'Environment', 'Result', 'Verdict'
function Get-Sections([string]$text) {
    $s = @{}; $cur = $null
    foreach ($line in ($text -split "`r?`n")) {
        if ($line -match '^#{2,3}\s+(.+?)\s*$') {
            $h = $Matches[1]; $cur = $null
            foreach ($n in $names) { if ($h -match ('^' + [regex]::Escape($n) + '\b')) { $cur = $n; break } }
            if ($cur) { $s[$cur] = '' }
            continue
        }
        if ($cur) { $s[$cur] += $line + "`n" }
    }
    $s
}
function Test-Filled($s, [string]$n) {
    if (-not $s.ContainsKey($n)) { return $false }
    $b = $s[$n].Trim()
    $b -and $b -notmatch '(?s)^\(.*\)$'
}
function Show-Head([string]$rel) { $t = & git -C $root cat-file -p "HEAD:$rel" 2>$null; if ($LASTEXITCODE -eq 0) { $t -join "`n" } else { $null } }

$dir = Join-Path $root 'docs/measurements'
$files = if (Test-Path -LiteralPath $dir) { @(Get-ChildItem -LiteralPath $dir -Filter '*.md' | Where-Object Name -ne 'README.md') } else { @() }
$rel = { param($f) 'docs/measurements/' + $f.Name }

# --- gate 1: a measurement needs a committed, open file ---------------------------------------------------------
if ($measures) {
    if ($cmd -match '(#|//)\s*exploratory\b') {
        Out-Decision 'context' ('Exploratory run: its numbers are NOT observations. Do not quote them as figures in a doc, ' +
            'release note or commit message; register a docs/measurements/ file and rerun to get a number that counts.')
    }
    $named = if ($cmd -match '(#|//)\s*measurement:\s*(\S+\.md)') { ($Matches[2] -replace '\\', '/') -replace '^.*?(docs/measurements/)', '$1' } else { $null }
    $candidates = if ($named) { @($files | Where-Object { (& $rel $_) -eq $named -or $_.Name -eq $named }) } else { $files }
    $why = @()
    foreach ($f in $candidates) {
        $r = & $rel $f
        $head = Show-Head $r
        if ($null -eq $head) { $why += "$r is not committed (commit it before the run, so its pre-run form is on record)"; continue }
        $s = Get-Sections $head
        $missing = @('Observation', 'Question', 'Method', 'Control' | Where-Object { -not (Test-Filled $s $_) })
        if ($missing) { $why += "$r has no filled $($missing -join ', ') in its committed form"; continue }
        if ((Test-Filled $s 'Result') -or (Test-Filled (Get-Sections (Get-Content -LiteralPath $f.FullName -Raw)) 'Result')) { $why += "$r already has a Result (it is finished; start a new file)"; continue }
        $note = if (-not (Test-Filled $s 'Prediction')) { " It states no Prediction; that is optional, but a number fixed before the run is the only thing this run can prove wrong." } else { '' }
        Out-Decision 'context' "Measurement recorded against $r. Paste the raw output, Environment (load before and after, commit) and Verdict there.$note"
    }
    if ($named -and -not $candidates) { $why += "no file $named" }
    if (-not $why) { $why += 'there is no docs/measurements/*.md for this run' }
    Out-Decision 'deny' ("Blocked: this takes a performance measurement, and " + ($why -join '; ') + ". Write and commit " +
        "docs/measurements/YYYY-MM-DD-<subject>.md with Observation, Question, Method and Control filled (README there), " +
        "optionally name it with '# measurement: <file>'. For a throwaway look add '# exploratory' ('// exploratory' in " +
        "the TF2 console); its numbers may then not be quoted in any doc.")
}

# --- gate 2: close the loop ---------------------------------------------------------------------------------------
$frozen = 'Observation', 'Question', 'Hypothesis', 'Prediction', 'Falsified if', 'Control', 'Method'
$why = @()
foreach ($f in $files) {
    $r = & $rel $f
    $now = Get-Sections (Get-Content -LiteralPath $f.FullName -Raw)
    if ((Test-Filled $now 'Result') -and -not (Test-Filled $now 'Verdict')) { $why += "$r has a Result and no Verdict" }
    elseif ((Test-Filled $now 'Result') -and (Test-Filled $now 'Prediction') -and $now['Verdict'] -notmatch '(?i)\b(confirmed|falsified|inconclusive)\b') {
        $why += "$r states a Prediction, so its Verdict must say confirmed, falsified or inconclusive"
    }
    # The freeze line is the RUN, not the first commit: the first committed version with a filled Result. The
    # baseline is the committed version just before it. Before any Result is committed, a filled working copy is
    # judged against HEAD; with no Result anywhere, pre-run edits are free. (The first version froze at first commit,
    # and refused the f12 entry, whose Control was corrected before its run.)
    # cat-file, not show: `git show <40-hex>:<path>` stats the argument as a file first and dies with "Filename too
    # long" under a deep worktree, which silently read every first form as empty (caught by this hook's own control).
    $history = @(& git -C $root log --reverse --format=%H -- $r 2>$null)
    $base = $null; $ranAt = $null
    for ($i = 0; $i -lt $history.Count; $i++) {
        $v = Get-Sections ((& git -C $root cat-file -p "$($history[$i]):$r" 2>$null) -join "`n")
        if (Test-Filled $v 'Result') { $ranAt = $i; break }
        $base = $history[$i]
    }
    if ($null -eq $ranAt -and -not (Test-Filled $now 'Result')) { continue }   # not run yet
    if (-not $base) { continue }   # Result in the very first commit (or never committed): nothing before the run
    $then = Get-Sections ((& git -C $root cat-file -p "${base}:$r" 2>$null) -join "`n")
    foreach ($n in $frozen) {
        if ($now.ContainsKey($n) -and $now[$n] -match '(?i)\b(added after|changed from the plan|amendment)\b') { continue }   # labelled: the reader is told
        if ((Test-Filled $then $n) -and (-not $now.ContainsKey($n) -or $now[$n].Trim() -cne $then[$n].Trim())) {
            $why += "$r changed '## $n' since its last pre-run commit ($($base.Substring(0, 8)))"
        }
    }
}
if ($why) {
    Out-Decision 'deny' ("Blocked: " + ($why -join '; ') + ". Write the Verdict (what the observation answers; confirmed, " +
        "falsified or inconclusive when there was a Prediction). Sections written before the run are never edited: a " +
        "wrong prediction stays and the Verdict says how wrong. Restore the first form with the file tools.")
}

# --- gate 3: a fix or perf decision names its test -------------------------------------------------------------------
if ($commits) {
    $cite = '(?i)measurements/[\w.-]+\.md|\bfinding\s+\d+'
    if ($cmd -match '(?i)\b(fixed|faster)\b' -and $cmd -match '(?i)\d+(\.\d+)?\s*(fps|ms|MB|%|x)\b' -and $cmd -notmatch $cite) {
        Out-Decision 'deny' ("Blocked: the commit message claims 'fixed' or 'faster' beside a figure and cites " +
            "no observation. Add the docs/measurements/<file>.md (or finding NN) that observed it, or drop the number.")
    }
    if ($cmd -match "(?m)(-m\s*[`"']|<<\s*'?EOF'?\s*\r?\n)\s*(fix|perf)(\([^)]*\))?!?:") {
        $kind = $Matches[2]
        $staged = @(& git -C $root diff --cached --name-only 2>$null)
        if ($cmd -match '\scommit\b[^\n]*\s(-a|--all)\b') { $staged += @(& git -C $root diff --name-only 2>$null) }
        $touchesTests = ($staged -match '^tests/') -or ($cmd -match '\bgit\s+add\b[^\n;&|]*\btests[/\\]')
        $tested = $cmd -match '(?m)Tested by:\s*\S' -and $cmd -match '(?m)Observed:\s*\S'
        $waived = $cmd -match '(?m)No-test:\s*\S'
        if ($kind -eq 'perf' -and $cmd -notmatch 'measurements/[\w.-]+\.md') {
            Out-Decision 'deny' "Blocked: a perf: commit cites the before/after observation it rests on - name docs/measurements/<file>.md in the message."
        }
        if (-not ($touchesTests -or $tested -or $waived)) {
            Out-Decision 'deny' ("Blocked: a ${kind}: commit is a decision, and nothing here tests it. Stage the regression " +
                "test under tests/ (red first), or put 'Tested by: <test or measurement file>' and 'Observed: <what was " +
                "seen>' in the body, or 'No-test: <reason>' if it truly cannot be tested.")
        }
    }
}
exit 0
