# Enforces the owner's subagent policy: a concurrency cap, and cheap models for the cheap work.
#
# The policy, in his words (docs/memory/one-subagent-and-prefer-cheap-models.md):
#   "id still say no more than 1 right now, i know if you get to like 5 agents running at once,
#    tokens get used up fast, so keep a cap of like 3 overall, if they are lesser models"
#   "this sort of sabatage writting would be fine too, its not fixing and making new code, its
#    testing, so lowest model available for it, like reading"
#
# **Raised to 3 on 2026-09-06 (D145):** "ill let 3 agents run at once of the sonnet 4.6 models, and
# you review their work". The three he named as the wall became the working number, on the condition
# that the parent reviews what comes back — which this script cannot check and does not pretend to.
#
# Three modes, one script, wired to three events:
#   Gate  - PreToolUse on Agent. Refuses a spawn that omits `model`, that picks an expensive
#           model for an agent type the policy says is cheap-eligible, or that would exceed the
#           concurrency cap.
#   Start - SubagentStart. Records a running agent.
#   Stop  - SubagentStop. Clears one.
#
# **The ledger is timestamped and self-healing on purpose.** A counter that only ever increments
# is one killed agent away from blocking every future spawn, and a hook that wedges the tool it
# guards gets deleted rather than fixed. Entries older than the stale window are ignored, so the
# worst case is a window where the cap is looser than intended rather than a permanent refusal.
# It counts on the way out (reading the file) rather than trusting a single number, which is the
# ledger rule this project already wrote down.

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Gate', 'Start', 'Stop')]
    [string] $Mode
)

$ErrorActionPreference = 'Stop'

# **The count is NOT the rule any more, and this number is a runaway backstop rather than a policy.**
# The owner, 2026-09-06:
#
#   "really idc how many subagents are run because im pretty sure most of the time it wont be more
#    than 3 or 4 anyway, but they need to be cheap sonnet models, and reviewed"
#
# So the two conditions that matter are the MODEL, enforced below and now applied to every agent
# type rather than a list of them, and REVIEW, which a hook cannot check at all. This is left at a
# number well above the three or four he expects purely so a loop that spawns without bound trips
# something instead of running the budget down.
$Concurrent = 8

# **The cheap-eligible TYPE list is gone**, and its absence is the change. It named three agents
# that read, grep or mechanically invert a line, and let every other type pick what it liked — which
# is backwards, since the budget does not care which type spent it. The model rule below now applies
# to all of them.
#
# **Sonnet only, and haiku is OUT on measured grounds rather than taste.** The owner, 2026-09-06:
# "i dont really trust haiku, it just seemed horrible compared to sonnet and sonnet 4.6 used less
# tokens than haiku it seemed like, while giving me better code". A model that is both worse and
# not cheaper has nothing left to recommend it, so the cheap tier is one model wide.
$Cheap = @('sonnet')

# Older than this and an entry is assumed dead rather than running.
$StaleMinutes = 45

$ledger = Join-Path ([System.IO.Path]::GetTempPath()) 'claude-subagents-tf2demosalvage.txt'

function Get-Running {
    if (-not (Test-Path $ledger)) {
        return @()
    }

    $cutoff = (Get-Date).AddMinutes(-$StaleMinutes)

    return @(
        Get-Content $ledger |
            Where-Object { $_ -match '\S' } |
            Where-Object {
                $stamp = [datetime]::MinValue

                # An unparseable line is dropped rather than counted: a corrupt ledger must not
                # be able to block every spawn.
                [datetime]::TryParse($_, [ref] $stamp) -and $stamp -gt $cutoff
            }
    )
}

$payload = [Console]::In.ReadToEnd()

switch ($Mode) {
    'Start' {
        Add-Content -Path $ledger -Value (Get-Date -Format 'o')
        exit 0
    }

    'Stop' {
        $running = Get-Running

        if ($running.Count -gt 0) {
            Set-Content -Path $ledger -Value ($running | Select-Object -Skip 1)
        }
        else {
            Set-Content -Path $ledger -Value ''
        }

        exit 0
    }
}

# Gate.
$call = $payload | ConvertFrom-Json

$input_ = $call.tool_input

$type = [string] $input_.subagent_type
$model = [string] $input_.model

$deny = $null

if ([string]::IsNullOrWhiteSpace($model)) {
    $deny = "This Agent call passes no `model`, so the subagent inherits the parent's - the " +
        "expensive default, and the exact mistake that cost most of a five-hour budget in one " +
        "turn. Pass model: 'sonnet'."
}
elseif ($Cheap -notcontains $model) {
    # **Every agent type, not just the reading ones.** The owner: "they need to be cheap sonnet
    # models, and reviewed". The old rule named three cheap-eligible types and let anything else
    # pick what it liked; the cost that rule was written for does not care which type spent it.
    $deny = "Subagents run on cheap models here - the owner's rule is 'they need to be cheap " +
        "sonnet models, and reviewed'. Pass model: 'sonnet' - haiku is out, measured worse AND " +
        "not cheaper. Not '$model'."
}
else {
    $running = Get-Running

    if ($running.Count -ge $Concurrent) {
        $deny = "$($running.Count) subagent(s) already running and the cap is $Concurrent. Wait " +
            "for a completion notification before spawning another. Concurrent agents burn the " +
            "shared usage limit, and two of them must not touch the same files - one holding a " +
            "file mid-sabotage has already broken an unrelated build here, so give each a " +
            "disjoint area and review what every one of them returns."
    }
}

if ($null -eq $deny) {
    exit 0
}

@{
    hookSpecificOutput = @{
        hookEventName            = 'PreToolUse'
        permissionDecision       = 'deny'
        permissionDecisionReason = $deny
    }
} | ConvertTo-Json -Depth 4 -Compress

exit 0
