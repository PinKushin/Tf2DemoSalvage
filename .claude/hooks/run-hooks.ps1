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
#   - any `permissionDecision: deny`  -> one deny, every deny reason (and any exit-2 reason) joined;
#   - any exit 2                      -> exit 2, every exit-2 reason on stderr;
#   - otherwise any `ask`             -> one ask;
#   - otherwise an `allow` carrying `updatedInput` (quiet-verbose-tools' command rewrite) -> passed through, the first;
#   - PostToolUse `decision: block`   -> one block, reasons joined;
#   - every `additionalContext`       -> joined into one.
#
# FAIL-OPEN, as before, but NOT SILENT: a missing or throwing script is skipped, as a crashed hook was a non-blocking
# error before, and the skip is written to run-hooks.log beside this file. A run past its time budget stops starting
# hooks and answers with what it has, so a slow hook cannot cost the denies already found.
#
# KNOWN CEILING: a hook that ends WITHOUT an explicit `exit` after a native command that returned 2 reads as an exit-2
# block here, where alone its process exited 0. Every hook ends with an explicit `exit`; keep it that way.
#
# Every variable here is prefixed `__rh`: a hook called with `&` sees its caller's variables through dynamic scope, so a
# hook reading a name it never assigned would otherwise get this script's value.
#
# Usage, in settings.json:  pwsh -NoProfile -File <this file> <script.ps1> [<script.ps1> ...]
# A relative script path resolves against this file's folder.
#
# A NEW HOOK JOINS A DISPATCHER'S LIST, not a new `pwsh` entry of its own, or the timeouts come back. A hook with a
# harness `if` filter or a matcher no list shares stays its own entry. Two copies exist and are kept identical: the
# global ~/.claude/hooks/run-hooks.ps1 and the Tf2DemoSalvage repo's .claude/hooks/run-hooks.ps1 (a public repo cannot
# point into a home folder).
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$__rhScripts)

$__rhBudget = [System.Diagnostics.Stopwatch]::StartNew()
$__rhBudgetSeconds = 45
$__rhLog = Join-Path $PSScriptRoot 'run-hooks.log'
$__rhRaw = [Console]::In.ReadToEnd()
$__rhEvent = 'PreToolUse'
try {
    $__rhParsed = ($__rhRaw | ConvertFrom-Json).hook_event_name
    if ($__rhParsed) { $__rhEvent = [string]$__rhParsed }
} catch { $__rhEvent = 'PreToolUse' }

$__rhStderr = [Console]::Error
$__rhRefusals = [System.Collections.Generic.List[string]]::new()
$__rhDenies = [System.Collections.Generic.List[string]]::new()
$__rhAsks = [System.Collections.Generic.List[string]]::new()
$__rhBlocks = [System.Collections.Generic.List[string]]::new()
$__rhContexts = [System.Collections.Generic.List[string]]::new()
$__rhAllow = $null
$__rhBlocked = $false

function Write-RhLog([string]$__rhLine) {
    try {
        [System.IO.File]::AppendAllText($__rhLog, ('{0:u} {1}{2}' -f [DateTime]::UtcNow, $__rhLine, [Environment]::NewLine))
    } catch {
        # Logging must not break the hooks it reports on.
        $null = $_
    }
}

foreach ($__rhScript in $__rhScripts) {
    if ($__rhBudget.Elapsed.TotalSeconds -gt $__rhBudgetSeconds) {
        Write-RhLog "budget of $__rhBudgetSeconds s spent before $__rhScript; it and the rest did not run"
        break
    }

    $__rhPath = if ([System.IO.Path]::IsPathRooted($__rhScript)) { $__rhScript } else { Join-Path $PSScriptRoot $__rhScript }

    if (-not (Test-Path -LiteralPath $__rhPath)) {
        Write-RhLog "missing: $__rhPath"
        continue
    }

    [Console]::SetIn([System.IO.StringReader]::new($__rhRaw))

    # Each script's own stderr, caught so an exit-2 reason can also ride in a deny that outranks it.
    $__rhCaught = [System.IO.StringWriter]::new()
    [Console]::SetError($__rhCaught)
    $global:LASTEXITCODE = 0
    $__rhText = ''

    # Captured before it is piped anywhere, the exit code read straight after.
    try {
        $__rhCaptured = & $__rhPath *>&1
        $__rhCode = $LASTEXITCODE
        $__rhText = ($__rhCaptured | Out-String).Trim()
    } catch {
        Write-RhLog "threw: $__rhPath - $($_.Exception.Message)"
        continue
    } finally {
        [Console]::SetError($__rhStderr)
    }

    if ($__rhCode -eq 2) {
        $__rhBlocked = $true
        $__rhReason = (($__rhCaught.ToString().Trim()), $__rhText | Where-Object { $_ }) -join "`n"

        if ($__rhReason) {
            $__rhRefusals.Add($__rhReason)
        }

        continue
    }

    if (-not $__rhText) {
        continue
    }

    $__rhJson = $null

    foreach ($__rhCandidate in @($__rhText, ($__rhText -split "`r?`n")[-1])) {
        try {
            $__rhJson = $__rhCandidate | ConvertFrom-Json -ErrorAction Stop
            break
        } catch {
            $__rhJson = $null
        }
    }

    if ($null -eq $__rhJson) {
        continue
    }

    $__rhSpecific = $__rhJson.hookSpecificOutput

    if ($null -ne $__rhSpecific) {
        switch ([string]$__rhSpecific.permissionDecision) {
            'deny' { $__rhDenies.Add([string]$__rhSpecific.permissionDecisionReason) }
            'ask' { $__rhAsks.Add([string]$__rhSpecific.permissionDecisionReason) }
            'allow' {
                if ($null -eq $__rhAllow -and $null -ne $__rhSpecific.updatedInput) {
                    $__rhAllow = $__rhSpecific
                }
            }
        }

        if ($__rhSpecific.additionalContext) {
            $__rhContexts.Add([string]$__rhSpecific.additionalContext)
        }
    }

    if ([string]$__rhJson.decision -eq 'block') {
        $__rhBlocks.Add([string]$__rhJson.reason)
    }
}

function Join-Rh($__rhList) { ($__rhList | Where-Object { $_ }) -join "`n`n" }

if ($__rhDenies.Count -gt 0) {
    $__rhOut = @{ hookEventName = $__rhEvent; permissionDecision = 'deny'; permissionDecisionReason = (Join-Rh (@($__rhDenies) + @($__rhRefusals))) }

    if ($__rhContexts.Count -gt 0) {
        $__rhOut.additionalContext = Join-Rh $__rhContexts
    }

    @{ hookSpecificOutput = $__rhOut } | ConvertTo-Json -Depth 8 -Compress
    exit 0
}

if ($__rhBlocked) {
    $__rhStderr.WriteLine((Join-Rh $__rhRefusals))
    exit 2
}

if ($__rhAsks.Count -gt 0) {
    $__rhOut = @{ hookEventName = $__rhEvent; permissionDecision = 'ask'; permissionDecisionReason = (Join-Rh $__rhAsks) }

    if ($__rhContexts.Count -gt 0) {
        $__rhOut.additionalContext = Join-Rh $__rhContexts
    }

    @{ hookSpecificOutput = $__rhOut } | ConvertTo-Json -Depth 8 -Compress
    exit 0
}

if ($null -ne $__rhAllow) {
    $__rhOut = @{
        hookEventName = $__rhEvent
        permissionDecision = 'allow'
        permissionDecisionReason = [string]$__rhAllow.permissionDecisionReason
        updatedInput = $__rhAllow.updatedInput
    }

    if ($__rhContexts.Count -gt 0) {
        $__rhOut.additionalContext = Join-Rh $__rhContexts
    }

    @{ hookSpecificOutput = $__rhOut } | ConvertTo-Json -Depth 8 -Compress
    exit 0
}

$__rhResult = @{}

if ($__rhBlocks.Count -gt 0) {
    $__rhResult.decision = 'block'
    $__rhResult.reason = Join-Rh $__rhBlocks
}

if ($__rhContexts.Count -gt 0) {
    $__rhResult.hookSpecificOutput = @{ hookEventName = $__rhEvent; additionalContext = (Join-Rh $__rhContexts) }
}

if ($__rhResult.Count -gt 0) {
    $__rhResult | ConvertTo-Json -Depth 8 -Compress
}

exit 0
