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
# reads the command itself - but only from `git commit` ONWARD.
#
# **Scanning the whole command was a hole, and it let one through the day this was written.** A
# commit that first appended a RISKS entry quoting `econ_entity.cpp:1167` and then committed was
# allowed, because the citation matched text that was being written to a FILE rather than to the
# commit message. The check's input was wider than its claim - the same fault the instrument
# rules are about - so it now looks only at the part of the command that carries the message.
# Located with the same regex that admitted the command, because `git -C <path> commit` contains
# no literal "git commit" and an IndexOf would answer -1 - which Substring turns into a throw, in
# a hook, on every commit from another directory.
$at = [regex]::Matches($command, 'git\s+(-\S+\s+)*commit')

$message = $at.Count -gt 0 ? $command.Substring($at[$at.Count - 1].Index) : $command

# An engine citation is a .cpp/.h reference or an SDK path; the escape hatch must name its reason
# to be worth typing.
if ($message -match '\.(cpp|h)\b|source-sdk|\[no-parity\]') {
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
