# Refuses a corpus test run that would walk lcor, and asks before one that says it will.
#
# WHY. `dotnet test tests/Tf2DemoSalvage.Corpus.Tests` picks up every demo in tools/corpus/local unless
# TF2DEMOSALVAGE_GCOR_ONLY=1 is set, and a --filter does not narrow that: each selected class still walks every demo.
# On 2026-11-17 the lead ran three fog and soundscape classes that way to "check" B544, a sampling change and not a decode
# change. It held the Corpus.Tests binaries for 70 minutes, blocked both merge gates, and was killed when the owner asked
# why. The owner: "yea that would have taken hours if not a whole day or more". The memory
# full-corpus-at-most-once-a-day already said "agents never run the full corpus unless told", and that sentence did not
# stop it, so the command is checked here.
#
# HOW. A Bash or PowerShell command that runs `dotnet test` over Corpus.Tests or the whole solution, or `build/gate.sh`:
#   - with TF2DEMOSALVAGE_GCOR_ONLY=1           -> passes (gcor, minutes);
#   - with TF2DEMOSALVAGE_GCOR_ONLY=0           -> asks (a deliberate full superset, at most once a day, decode changes
#                                                  only; the owner approves at the prompt);
#   - with neither, `dotnet test` on Corpus.Tests -> denied: it defaults to lcor. gate.sh with neither passes, because
#                                                  the script itself defaults to gcor.
#
# THE CORRECT FORM: TF2DEMOSALVAGE_GCOR_ONLY=1 dotnet test tests/Tf2DemoSalvage.Corpus.Tests --filter "..."
#
# Fails open (exit 0) on unreadable input. Tests: tf2-corpus-run-is-gcor.tests.json.

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

    $call = $raw | ConvertFrom-Json
    if ([string]$call.tool_name -notin @('Bash', 'PowerShell')) { exit 0 }

    $command = [string]$call.tool_input.command

    $corpusTest = $command -match '\bdotnet\s+test\b[^\n;&|]*(Corpus\.Tests|\.slnx?\b)'
    $gate = $command -match '\bgate\.sh\b'
    if (-not $corpusTest -and -not $gate) { exit 0 }

    if ($command -match 'TF2DEMOSALVAGE_GCOR_ONLY\s*=\s*["'']?1') { exit 0 }

    if ($command -match 'TF2DEMOSALVAGE_GCOR_ONLY\s*=\s*["'']?0') {
        $reason = 'A FULL corpus superset (gcor + every lcor demo) takes hours. It runs at most once a day, for a decode ' +
            'change, and only when the owner asked (full-corpus-at-most-once-a-day). Approve only if that is this run.'
        @{ hookSpecificOutput = @{ hookEventName = 'PreToolUse'; permissionDecision = 'ask'; permissionDecisionReason = $reason } } |
            ConvertTo-Json -Compress
        exit 0
    }

    if (-not $corpusTest) { exit 0 }

    $reason = 'This corpus run has no TF2DEMOSALVAGE_GCOR_ONLY=1, so it walks every lcor demo: hours, and a --filter does ' +
        'not narrow the demos, only the classes (2026-11-17: 70 minutes held for a sampling change). Run it on gcor: ' +
        'TF2DEMOSALVAGE_GCOR_ONLY=1 dotnet test tests/Tf2DemoSalvage.Corpus.Tests --filter "...". ' +
        'A full superset is TF2DEMOSALVAGE_GCOR_ONLY=0, once a day, decode changes, owner asked.'
    @{ hookSpecificOutput = @{ hookEventName = 'PreToolUse'; permissionDecision = 'deny'; permissionDecisionReason = $reason } } |
        ConvertTo-Json -Compress
    exit 0
}
catch {
    [Console]::Error.WriteLine("tf2-corpus-run-is-gcor: $($_.Exception.Message)")
    exit 0
}
