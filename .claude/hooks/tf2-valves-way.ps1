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
STANDING DECISION - do not re-open it, do not offer an alternative, do not ask which way:

  Valve's way. Always. (D89, D129, D131.)

  - Parity is the first principle. Performance never buys a departure from it, and in this
    project doing what the engine does has made it FASTER every time.
  - Read the engine before designing. Every branch, and the overrides - a base returning a
    constant changes what a guard means.
  - A divergence is a defect, whatever it costs to fix. "Bigger than pretty much any other
    since its such a low level change" - the owner, on the one that was nearly deferred.
  - The target is BETTER than TF2, not equal: we draw less and moved skinning to the GPU.
  - If a divergence is found, FIX IT. Report what was done, not a menu of what could be.

  Ask only when the engine itself is ambiguous, or when a change would alter something the
  owner can see. Never ask which of Valve's way and another way to take.
'@

@{ hookSpecificOutput = @{
      hookEventName = 'UserPromptSubmit'
      additionalContext = $reminder } } | ConvertTo-Json -Depth 5 -Compress
