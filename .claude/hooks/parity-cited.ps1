# Refuses a commit that changes parity-sensitive code without citing the engine.
#
# The rule it automates is D89/D133: work under managed/Tf2DemoSalvage.{Scene,Render,Viewer3D,
# Presentation} is Valve-parity work, and a parity change whose commit cites no engine source is
# either undocumented or unread. Every legitimate renderer commit in this repo already carries a
# citation (clientleafsystem.cpp:1758, viewrender.cpp:4577, source-sdk-2013 paths), so the gate
# costs nothing when the discipline is followed and blocks exactly the commit that skipped it.
#
# The escape hatch is [no-parity] with the reason in the message - the same shape as
# build/gate.sh's floors, which refuse a drop until the reason is written next to it.
#
# PreToolUse on Bash: reads the tool call from stdin, acts only on git commit commands, and only
# when files in the parity buckets are actually staged in this repo.

$ErrorActionPreference = 'Stop'

$call = [Console]::In.ReadToEnd() | ConvertFrom-Json

$command = $call.tool_input.command

if (-not $command -or $command -notmatch 'git\s+(-\S+\s+)*commit') {
    exit 0
}

$repo = $env:CLAUDE_PROJECT_DIR

if (-not $repo) {
    exit 0
}

# What is actually staged HERE. A commit driven from another directory stages nothing in this
# repo and passes untouched.
$staged = git -C $repo diff --cached --name-only 2>$null

if (-not $staged) {
    exit 0
}

$parity = @($staged | Where-Object {
    $_ -match '^managed/Tf2DemoSalvage\.(Scene|Render|Viewer3D|Presentation)/.*\.cs$'
})

if ($parity.Count -eq 0) {
    exit 0
}

# The commit text travels inside the command (-m or an inline heredoc), so the citation check
# reads the command itself. An engine citation is a .cpp/.h reference or an SDK path; the
# escape hatch must name its reason to be worth typing.
if ($command -match '\.(cpp|h)\b|source-sdk|\[no-parity\]') {
    exit 0
}

$reason = "This commit stages parity-sensitive code (" +
    (($parity | Select-Object -First 3) -join ', ') +
    ") and its message cites no engine source. Valve's way is the standing decision (D89): " +
    "read the engine, cite what was read (file.cpp:line or a source-sdk path) in the commit " +
    "body, or mark the commit [no-parity] with the reason it needs none."

@{
    hookSpecificOutput = @{
        hookEventName            = 'PreToolUse'
        permissionDecision       = 'deny'
        permissionDecisionReason = $reason
    }
} | ConvertTo-Json -Depth 4 -Compress

exit 0
