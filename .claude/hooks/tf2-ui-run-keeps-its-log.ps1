# Refuses a UI-suite test run whose output is piped into a filter without the whole output kept in a file.
#
# WHY. A UI failure's evidence is in its message: FillDialog's window dump, the foreground holder, the timeline. On
# 2026-11-17 a three-suite soak for B773 piped `dotnet test tests/Tf2DemoSalvage.Viewer3D.UiTests` into
# `grep ... -A1 | head -30`, and B558's first "dialog stayed open after OK" failure in weeks kept one line of its
# message; the window dump that would have said why was gone with the run. CLAUDE.md already said "do not filter the
# gate's output down to summary lines while iterating, because you lose which test failed", and that did not stop it.
#
# HOW. A Bash or PowerShell command running `dotnet test` on a project whose name contains "UiTests", with a `|` after
# it, passes only if the same command also writes the output to a file (`> file`, `>> file`, `tee`). A run with no
# pipe, or redirected whole to a file, passes.
#
# THE CORRECT FORM:  dotnet test tests/Tf2DemoSalvage.Viewer3D.UiTests > "<scratchpad>/run.log" 2>&1; grep ... run.log
#
# Fails open (exit 0) on unreadable input. Tests: tf2-ui-run-keeps-its-log.tests.json.

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

    $call = $raw | ConvertFrom-Json
    if ([string]$call.tool_name -notin @('Bash', 'PowerShell')) { exit 0 }

    $command = [string]$call.tool_input.command

    $run = [regex]::Match($command, '\bdotnet\s+test\b[^\n;&]*UiTests')
    if (-not $run.Success) { exit 0 }

    $after = $command.Substring($run.Index)
    if ($after -notmatch '\|') { exit 0 }

    # Kept whole: the run's output goes to a file, by redirect or tee, before any filter reads it.
    if ($after -match '(?<![<2&])>\s*["'']?[^\s|&]+' -or $after -match '\btee\b') { exit 0 }

    $reason = 'This UI-suite run pipes its output into a filter and keeps no copy. A UI failure''s evidence is its whole ' +
        'message (the window dump, the foreground holder, the timeline), and B558''s 2026-11-17 failure lost it this way. ' +
        'Write the run to a file first: dotnet test tests/Tf2DemoSalvage.Viewer3D.UiTests > "<scratchpad>/run.log" 2>&1; ' +
        'then grep the file.'
    @{ hookSpecificOutput = @{ hookEventName = 'PreToolUse'; permissionDecision = 'deny'; permissionDecisionReason = $reason } } |
        ConvertTo-Json -Compress
    exit 0
}
catch {
    [Console]::Error.WriteLine("tf2-ui-run-keeps-its-log: $($_.Exception.Message)")
    exit 0
}
