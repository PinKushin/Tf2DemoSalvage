# PreToolUse guard: a local Stryker run must be scoped to a line range; anything wider goes to the mutation box.
#
# BACKED UP at Tf2DemoSalvage/.claude/hooks/global/block-local-mutation-runs.ps1, byte-identical.
#
# WHY. 2026-10-03: a local `dotnet stryker --mutate **/EntityModels.cs` held the machine-wide desktop lock for hours
# (local Stryker takes it), and the UI gate, the playback gate and another agent queued behind it. The owner: "that run
# will take forever locally, unless its extreamly narrow scoped, you verify that by waiting on a box run, or running it
# manuall on the box", then "yea that might need a hook too".
#
# THE RULE, deterministic: a local `dotnet stryker` / `dotnet-stryker` is allowed only when EVERY --mutate / -m pattern
# carries a line range `{a..b}` spanning at most 80 lines. No --mutate at all (the whole project) is denied. A command
# run on the box (`ssh mutation-box ...` / `ssh fuzz-box ...`) is not local and is allowed.
$ErrorActionPreference = 'Stop'
try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $command = [string](($raw | ConvertFrom-Json).tool_input.command)
    if ([string]::IsNullOrWhiteSpace($command)) { exit 0 }
} catch { exit 0 }   # Never let the guard break the tool it guards.

if ($command -notmatch '(?i)\bdotnet(\.exe)?\s+stryker\b|\bdotnet-stryker\b') { exit 0 }
if ($command -match '(?i)\bssh\s+(\S+@)?(mutation-box|fuzz-box)\b') { exit 0 }

$maxLines = 80
$patterns = [regex]::Matches($command, '(?i)(?:--mutate|(?<!\S)-m)(?:\s+|=)("[^"]*"|''[^'']*''|\S+)')

$ok = $patterns.Count -gt 0
foreach ($p in $patterns) {
    $value = $p.Groups[1].Value.Trim('"', "'")
    $range = [regex]::Match($value, '\{(\d+)\.\.(\d+)\}')
    if (-not $range.Success) { $ok = $false; break }
    $span = [int]$range.Groups[2].Value - [int]$range.Groups[1].Value + 1
    if ($span -lt 1 -or $span -gt $maxLines) { $ok = $false; break }
}
if ($ok) { exit 0 }

$reason = "Blocked: local Stryker must be scoped to at most $maxLines lines - every --mutate needs a line range, e.g. " +
          "--mutate ""**/File.cs{120..180}"". It takes the desktop lock, and a whole-file run starves the UI and playback " +
          "gates for hours (owner, 2026-10-03). Verify a wider campaign on mutation-box: wait for its nightly run, or, when " +
          "flock -n /tmp/measurement-box.lock is free and crontab -l shows nothing due, run it there by hand in your own " +
          "git worktree inside remote tmux under the flock (ssh mutation-box ...)."
@{ hookSpecificOutput = @{
      hookEventName = 'PreToolUse'
      permissionDecision = 'deny'
      permissionDecisionReason = $reason } } | ConvertTo-Json -Depth 5 -Compress
exit 0
