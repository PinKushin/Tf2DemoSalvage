# UserPromptSubmit: put the standing principle in front of every turn.
#
# Why a hook and not a line in CLAUDE.md: it IS a line in CLAUDE.md, and in D89, D129 and D131, and
# it still got asked again. On 2026-09-01 the owner had to say it four times in one session - "we
# should be copying valve", "valve is god to us basically", "if its a diversion it should be fixed",
# "always valves way" - each time after an alternative was offered as though the decision were open.
#
# The failure is not forgetting the rule. It is treating a settled decision as a question, which
# looks like diligence and costs the owner the same answer over and over.
$ErrorActionPreference = 'Stop'

$reminder = @'
STANDING DECISION, settled - never re-open or offer an alternative: Valve's way, 100% parity (D89, D129, D131).
Read the engine (every branch, every override) before designing. A divergence is a defect: fix it, whatever it
costs, and report what was done. Performance never buys a departure. Ask only if the engine itself is ambiguous
or a change alters something the owner can see.
'@

@{ hookSpecificOutput = @{
      hookEventName = 'UserPromptSubmit'
      additionalContext = $reminder } } | ConvertTo-Json -Depth 5 -Compress
