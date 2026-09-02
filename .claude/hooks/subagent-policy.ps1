# Enforces the owner's subagent policy: one at a time, and on a cheap model.
#
# The policy, in his words (docs/memory/one-subagent-and-prefer-cheap-models.md):
#   "id still say no more than 1 right now, i know if you get to like 5 agents running at once,
#    tokens get used up fast, so keep a cap of like 3 overall, if they are lesser models"
#   "this sort of sabatage writting would be fine too, its not fixing and making new code, its
#    testing, so lowest model available for it, like reading"
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

# The cap the owner set. One now; three is the wall he named, never a target.
$Concurrent = 1

# Agent types the policy says are cheap-eligible: they read, they grep, or they mechanically
# invert one line and check a test went red. None of them authors anything kept.
$CheapOnly = @('engine-reader', 'instrument-auditor', 'sabotage-verifier')

$Cheap = @('haiku', 'sonnet')

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
        "turn. Pass model: 'haiku' for reading, quoting, auditing or sabotage; 'sonnet' when it " +
        "genuinely needs more."
}
elseif ($CheapOnly -contains $type -and $Cheap -notcontains $model) {
    $deny = "The '$type' agent runs on a cheap model by policy - it reads, greps or mechanically " +
        "inverts one line, and authors nothing that is kept. Pass model: 'haiku' (or 'sonnet' " +
        "with a reason), not '$model'."
}
else {
    $running = Get-Running

    if ($running.Count -ge $Concurrent) {
        $deny = "$($running.Count) subagent(s) already running and the cap is $Concurrent. Wait " +
            "for the completion notification before spawning another - concurrent agents burn " +
            "the shared usage limit, and one of them holding a file mid-sabotage has already " +
            "broken an unrelated build here."
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
