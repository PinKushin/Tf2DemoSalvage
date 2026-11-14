# One process for many hooks: reads the hook payload once and runs each named hook script in this pwsh, in order.
#
# WHY. Every hook used to be its own `pwsh -NoProfile -File` entry, and each `pwsh` start costs 0.3-1 s on a quiet
# machine. A Bash call ran about fourteen of them in parallel. Under load (the gate, a build and the viewer together),
# some starts took longer than the 15 s timeout. The harness then said "A hook didn't answer in time, so it didn't check
# this action" (owner, 2026-11-14) - the check silently did not happen. One start per tool call instead of fourteen.
#
# HOW EACH SCRIPT STILL SEES ITS OWN STDIN. Every hook reads its payload with [Console]::In.ReadToEnd(). Before each
# script this swaps in a fresh reader over the same text, so each reads the whole payload, as it did alone. A script's
# `exit N` ends only that script when it is called with `&`, and its code is read from $LASTEXITCODE.
#
# MERGING, as the harness merges parallel hooks:
#   - any `permissionDecision: deny`  -> one deny, every deny reason joined;
#   - any exit 2                      -> exit 2. Its reason was already written to stderr by the script itself
#                                        ([Console]::Error), and captured stdout text is forwarded to stderr too;
#   - otherwise any `ask`             -> one ask;
#   - PostToolUse `decision: block`   -> one block, reasons joined;
#   - every `additionalContext`       -> joined into one.
#
# FAIL-OPEN, as before: a script that throws is skipped. A crashed hook used to be a non-blocking error, and must not
# start blocking every tool call because it now shares a process.
#
# Usage, in settings.json:  pwsh -NoProfile -File C:/Users/pinku/.claude/hooks/run-hooks.ps1 <script.ps1> [<script.ps1> ...]
# A relative script path resolves against this folder.
#
# A NEW HOOK JOINS A DISPATCHER'S LIST, not a new `pwsh` entry of its own, or the timeouts come back. A hook with a
# harness `if` filter or a matcher no list shares stays its own entry. This is a copy of the global
# ~/.claude/hooks/run-hooks.ps1, for the project hooks (a public repo cannot point into a home folder); change both.
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Scripts)

$raw = [Console]::In.ReadToEnd()
$eventName = $null
try { $eventName = ($raw | ConvertFrom-Json).hook_event_name } catch { $eventName = $null }

$stderr = [Console]::Error
$refusals = [System.Collections.Generic.List[string]]::new()
$denies = [System.Collections.Generic.List[string]]::new()
$asks = [System.Collections.Generic.List[string]]::new()
$blocks = [System.Collections.Generic.List[string]]::new()
$contexts = [System.Collections.Generic.List[string]]::new()
$blocked = $false

foreach ($script in $Scripts) {
    $path = if ([System.IO.Path]::IsPathRooted($script)) { $script } else { Join-Path $PSScriptRoot $script }

    if (-not (Test-Path -LiteralPath $path)) {
        continue
    }

    [Console]::SetIn([System.IO.StringReader]::new($raw))

    # Each script's own stderr, caught so an exit-2 reason can also ride in a deny that outranks it.
    $caught = [System.IO.StringWriter]::new()
    [Console]::SetError($caught)
    $global:LASTEXITCODE = 0
    $text = ''

    try {
        $captured = & $path *>&1
        $code = $LASTEXITCODE
        $text = ($captured | Out-String).Trim()
    } catch {
        continue
    } finally {
        [Console]::SetError($stderr)
    }

    if ($code -eq 2) {
        $blocked = $true
        $reason = (($caught.ToString().Trim()), $text | Where-Object { $_ }) -join "`n"

        if ($reason) {
            $refusals.Add($reason)
        }

        continue
    }

    if (-not $text) {
        continue
    }

    $json = $null

    foreach ($candidate in @($text, ($text -split "`r?`n")[-1])) {
        try {
            $json = $candidate | ConvertFrom-Json -ErrorAction Stop
            break
        } catch {
            $json = $null
        }
    }

    if ($null -eq $json) {
        continue
    }

    $specific = $json.hookSpecificOutput

    if ($null -ne $specific) {
        switch ([string]$specific.permissionDecision) {
            'deny' { $denies.Add([string]$specific.permissionDecisionReason) }
            'ask' { $asks.Add([string]$specific.permissionDecisionReason) }
        }

        if ($specific.additionalContext) {
            $contexts.Add([string]$specific.additionalContext)
        }
    }

    if ([string]$json.decision -eq 'block') {
        $blocks.Add([string]$json.reason)
    }
}

$joined = { param($list) ($list | Where-Object { $_ }) -join "`n`n" }

if ($denies.Count -gt 0) {
    $out = @{ hookEventName = $eventName; permissionDecision = 'deny'; permissionDecisionReason = (& $joined (@($denies) + @($refusals))) }

    if ($contexts.Count -gt 0) {
        $out.additionalContext = & $joined $contexts
    }

    @{ hookSpecificOutput = $out } | ConvertTo-Json -Depth 5 -Compress
    exit 0
}

if ($blocked) {
    $stderr.WriteLine((& $joined $refusals))
    exit 2
}

if ($asks.Count -gt 0) {
    $out = @{ hookEventName = $eventName; permissionDecision = 'ask'; permissionDecisionReason = (& $joined $asks) }

    if ($contexts.Count -gt 0) {
        $out.additionalContext = & $joined $contexts
    }

    @{ hookSpecificOutput = $out } | ConvertTo-Json -Depth 5 -Compress
    exit 0
}

$result = @{}

if ($blocks.Count -gt 0) {
    $result.decision = 'block'
    $result.reason = & $joined $blocks
}

if ($contexts.Count -gt 0) {
    $result.hookSpecificOutput = @{ hookEventName = $eventName; additionalContext = (& $joined $contexts) }
}

if ($result.Count -gt 0) {
    $result | ConvertTo-Json -Depth 5 -Compress
}

exit 0
