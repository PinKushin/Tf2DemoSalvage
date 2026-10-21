# Flags a performance figure written into a public doc without the file that observed it.
#
# WHY. The owner, 2026-10-21: nothing counts as a number until it was observed, and "we need to get real numbers".
# Figures had been travelling from a chat estimate into findings and release notes with no record behind them, and a
# number with no citation cannot be told apart from one that was measured. docs/measurements/README.md is the method.
#
# THE RULE. A LINE ADDED by an Edit or Write (Edit: new_string lines absent from old_string; Write: lines absent from
# the file at HEAD) that holds a figure - digits then fps, ms, or MB per frame - and no `measurements/<file>.md` or
# `finding NN` on the same line:
#   RELEASE-NOTES.md                                   PreToolUse: DENIED (it ships to users)
#   README.md, docs/RISKS.md, docs/findings/*.md       PostToolUse: a warning the model reads, never a block
#
# NOISE, said plainly: of 492 such lines already in those files only 7 carry a citation, and many are legitimate -
# a frame budget, a timer interval, a cvar's default. Added lines only, so old text is never re-flagged, but a new
# paragraph about timers will draw the warning. That is why it stays soft outside RELEASE-NOTES.
#
# Tests: tf2-measurement-cited.sequence-test.ps1.

$ErrorActionPreference = 'Stop'

try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $call = $raw | ConvertFrom-Json
    $path = ([string]$call.tool_input.file_path) -replace '\\', '/'
    if ([string]::IsNullOrWhiteSpace($path)) { exit 0 }
} catch { exit 0 }   # Never let the guard break the tool it guards.

$event = if ($call.hook_event_name) { [string]$call.hook_event_name } else { 'PostToolUse' }
$release = $path -match '(^|/)RELEASE-NOTES\.md$'
$soft = $path -match '(^|/)(README\.md|docs/RISKS\.md|docs/findings/[^/]+\.md)$' -and $path -notmatch '/docs/measurements/'
if (-not ($release -or $soft)) { exit 0 }
if ($release -ne ($event -eq 'PreToolUse')) { exit 0 }   # release notes are judged before, the rest after

$in = $call.tool_input
if ($null -ne $in.PSObject.Properties['old_string']) {
    $before = [string]$in.old_string; $after = [string]$in.new_string
} else {
    $after = [string]$in.content; $before = ''
    $cwd = if ($call.cwd) { [string]$call.cwd } else { Split-Path $path }
    $root = (& git -C $cwd rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -eq 0 -and $root) {
        $full = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($path)) { $path } else { Join-Path $root $path })) -replace '\\', '/'
        $r = $full.Substring(([string]$root).Length).TrimStart('/')
        $t = & git -C $root cat-file -p "HEAD:$r" 2>$null
        if ($LASTEXITCODE -eq 0) { $before = $t -join "`n" }
    }
}
$had = [Collections.Generic.HashSet[string]]::new([string[]]($before -split "`r?`n" | ForEach-Object { $_.Trim() }))
$bad = @($after -split "`r?`n" | Where-Object {
    -not $had.Contains($_.Trim()) -and
    $_ -match '\d\s*(fps\b|ms\b|MB per frame|MB/frame)' -and
    $_ -notmatch '(?i)measurements/[\w.-]+\.md|\bfinding\s+\d+'
} | ForEach-Object { $_.Trim() })
if (-not $bad) { exit 0 }

$list = ($bad | Select-Object -First 5) -join ' | '
if ($release) {
    @{ hookSpecificOutput = @{ hookEventName = 'PreToolUse'; permissionDecision = 'deny'
        permissionDecisionReason = "Blocked: RELEASE-NOTES gets a performance figure with no citation: $list. Cite the docs/measurements/<file>.md that observed it (or finding NN) on the same line, or leave the number out." } } |
        ConvertTo-Json -Depth 4 -Compress
} else {
    @{ hookSpecificOutput = @{ hookEventName = 'PostToolUse'
        additionalContext = "Uncited performance figure added to ${path}: $list. If it was observed, cite (measurements/<file>.md) or (finding NN) on the line; if it was estimated or reasoned, say so in words; if it is a budget or setting rather than a measurement, leave it." } } |
        ConvertTo-Json -Depth 4 -Compress
}
exit 0
