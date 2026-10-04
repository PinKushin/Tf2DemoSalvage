# PreToolUse guard: no task chips (spawn_task), from the main loop or any subagent.
#
# BACKED UP at Tf2DemoSalvage/.claude/hooks/global/block-task-chips.ps1, byte-identical.
#
# WHY. A chip starts a separate session the owner has to launch by hand, which then works outside the
# main session's coordination - often on the same files, so its merge needs help. Tf2DemoSalvage D169/D171
# and the no-task-cards memory said so, but only in memory, and subagents never read memory: on
# 2026-10-03 a CI subagent filed two chips. The owner: "thats a subagent job or a you job, every time.
# those chips are annoying because I have to start them, and they act more aggressively and outside your
# coordination ... i thought there was even a hook for this?" There was not; this is it.
#
# What to do instead: a subagent reports the follow-up in its result; the main session does it or
# delegates it to a subagent.
$ErrorActionPreference = 'Stop'
try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $payload = $raw | ConvertFrom-Json
} catch { exit 0 }   # Never let the guard break the tool it guards.

if ($payload.tool_name -notlike '*spawn_task') { exit 0 }

@{ hookSpecificOutput = @{
      hookEventName = 'PreToolUse'
      permissionDecision = 'deny'
      permissionDecisionReason = ("Blocked: no task chips. Put the follow-up in your report instead; " +
          "the main session does it or delegates it to a subagent (owner, 2026-10-03; D169/D171).") } } |
    ConvertTo-Json -Depth 5 -Compress
exit 0
