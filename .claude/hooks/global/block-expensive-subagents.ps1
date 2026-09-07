# PreToolUse guard: subagents run on a cheap model, and this covers Workflow as well as Agent.
#
# BACKED UP at Tf2DemoSalvage/.claude/hooks/global/block-expensive-subagents.ps1, byte-identical, the
# same way docs/memory/ mirrors the assistant's memory. Nothing in that repo wires it - it is global,
# lives in ~/.claude/hooks/, and the copy exists only so a machine wipe does not take it.
#
# WHAT WENT WRONG, 2026-09-07. A scouting workflow spawned five Opus agents and the owner stopped it
# mid-run: "omg you subagented to opus 5 models fuck".
#
# THE GAP WAS WORKFLOW, not Agent, and the first version of this header got that wrong. A project
# hook (Tf2DemoSalvage/.claude/hooks/subagent-policy.ps1) had matched Agent since D145 and worked;
# it was missed because only ~/.claude/settings.json was checked, not the project's. The Workflow
# tool was covered by nothing. That project hook has since been removed in favour of this one, which
# the owner wanted global: "i wanted this global anyway, so ahaving it in the project repo doest
# work". Its concurrency backstop went with it - this guard checks the model only.
#
# WHY WORKFLOW IS THE DANGEROUS ONE. The Agent tool takes `model` as a field, so a bad value is easy
# to see. The Workflow tool takes a SCRIPT, and each agent() call inside it inherits the main-loop
# model unless opts.model is set - silently, and multiplied by however many agents it fans out to.
#
# The Workflow check is a regex over the script text, which is crude on purpose. It cannot follow a
# model chosen by a variable, so it demands a literal. A workflow that genuinely needs to compute
# one can be run by the owner; the common case is a script that simply forgot, and that is the case
# worth catching.
#
# PowerShell rather than bash+jq: jq is not installed on this machine, and a hook whose parser is
# missing exits 0 and silently allows everything while looking installed.
$ErrorActionPreference = 'Stop'
try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $payload = $raw | ConvertFrom-Json
    $tool = $payload.tool_name
    $toolInput = $payload.tool_input
} catch { exit 0 }   # Never let the guard break the tool it guards.

if ([string]::IsNullOrWhiteSpace($tool)) { exit 0 }

# The models a subagent may use. Anything else - sonnet, opus, an inherited default, a typo - is
# refused.
#
# HAIKU ONLY, 2026-09-07, and the reason is consumption rather than versioning.
#
# The owner's first framing was that the sonnet alias is not pinned - "we cant control which sonnet
# model version we use so it uses 5 and not 4.6" - and then corrected it in the next breath, which
# is the framing that governs: "unless 5 is more effecient than 4.6 then it makes sense i guess but
# haiku uses less than any sonnet by what ive seen", formed after looking at a token-cost chart.
#
# So the rule does NOT rest on 4.6 being cheaper than 5, which is unmeasured here and might be
# backwards. It rests on haiku costing less than any sonnet. Worth stating plainly because a rule
# defended by the wrong argument gets relaxed the moment that argument is questioned.
#
# This REVERSES the position the deleted project hook held (2026-09-06): "i dont really trust haiku,
# it just seemed horrible compared to sonnet and sonnet 4.6 used less tokens than haiku it seemed
# like, while giving me better code".
#
# AND THE EARLIER PREFERENCE WAS ABOUT REWORK, NOT RAW CONSUMPTION, which is the part that makes the
# reversal coherent rather than a flip-flop. The owner, clarifying: "4.6 is what i thought used less
# tokens or at leased used them better because the code had less bugs". Tokens per GOOD outcome -
# fewer bugs meaning fewer rounds of fixing - not tokens per call. On that measure a model with
# worse output can lose while being cheaper per token.
#
# What killed it is that the option is gone: "since we can only get 5, thats going to probably use
# more tokens, even when antho says it shouldnt lol". The cheap-but-good sonnet the preference was
# built on is not purchasable, so the comparison is now haiku against a sonnet expected to cost MORE
# than the one that lost to it.
#
# THE CONDITION IS WHAT MAKES HAIKU VIABLE, and it is load-bearing rather than a caveat: "as long as
# you review the haiku specific bugs should be cought and fixed". The rework argument only favours a
# better model when the rework is unpaid for. Review is what pays for it - so D145's requirement is
# not a leftover here, it is the thing holding this rule up, and a session that stops reviewing
# subagent output has removed the reason haiku was acceptable.
$allowed = @('haiku')

function Deny([string]$reason) {
    @{ hookSpecificOutput = @{
          hookEventName = 'PreToolUse'
          permissionDecision = 'deny'
          permissionDecisionReason = $reason } } | ConvertTo-Json -Depth 5 -Compress
    exit 0
}

if ($tool -eq 'Agent') {
    $model = $toolInput.model

    # An omitted model inherits the session's, which is the expensive one. Absence is the failure
    # this is here to catch, so it is denied rather than allowed.
    if ([string]::IsNullOrWhiteSpace($model)) {
        Deny(("Blocked: the Agent call names no model, so it would inherit this session's - " +
              "which is the expensive one. Subagents run on haiku here. Pass model: 'haiku'."))
    }

    if ($allowed -notcontains $model.ToLowerInvariant()) {
        Deny(("Blocked: subagent model '$model' is not allowed - subagents run on haiku. 'sonnet' " +
              "is refused too, on consumption rather than capability: haiku uses less than any " +
              "sonnet. If a task genuinely needs a bigger model, say so and the owner will run it."))
    }

    exit 0
}

if ($tool -ne 'Workflow') { exit 0 }

# A named or saved workflow has no script here to inspect. Let it through rather than guessing -
# the alternative is refusing every saved workflow on a rule this hook cannot check.
$script = $toolInput.script
if ([string]::IsNullOrWhiteSpace($script)) { exit 0 }

# Every agent( ... ) call in the script. Deliberately greedy about what counts as a call and
# conservative about what counts as a model: a missed match allows, a loose model match would
# defeat the point.
$calls = [regex]::Matches($script, 'agent\s*\(')

if ($calls.Count -eq 0) { exit 0 }

# Count agent( calls carrying a literal model: 'haiku' within the following window. The window is
# generous because a call is often several lines of prompt before its opts.
$named = 0
foreach ($call in $calls) {
    $start = $call.Index
    $length = [Math]::Min(4000, $script.Length - $start)
    $window = $script.Substring($start, $length)

    if ($window -match "model\s*:\s*['`"]haiku['`"]") { $named++ }
}

if ($named -lt $calls.Count) {
    $missing = $calls.Count - $named
    Deny(("Blocked: $missing of $($calls.Count) agent() calls in this workflow do not say " +
          "{ model: 'haiku' }. A workflow agent INHERITS the main-loop model unless opts.model is " +
          "set, so this would fan out on the expensive one - which is how five Opus agents got " +
          "spawned on 2026-09-07. The check wants a literal, so a computed model will not pass; if " +
          "you need one, say so and the owner will run it."))
}

exit 0
