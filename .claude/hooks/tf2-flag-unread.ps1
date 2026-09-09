# PostToolUse guard: a flag SET or TESTED in engine source you just read, and never looked up.
#
# Why a hook and not care: B276. `AddBaseAnimatingInterpolatedVars` was printed to this terminal
# TWICE while reading which variables are animation-latched. The question asked was "which vars",
# the answer taken was "cycle, pose parameters, encoded controller", and this line, two above the
# answer, was read past both times:
#
#     if ( m_bClientSideAnimation ) flags |= EXCLUDE_AUTO_INTERPOLATE;
#
# It is the whole rule. A client-side-animated entity's cycle is not interpolated at all, and this
# project had been interpolating it since long before that session. The cost was a viewmodel that
# stopped animating, found by the owner rather than by any test.
#
# **The signal was in what was READ, not in what was written.** The flag never appeared in a
# comment, a commit or a diff, so no review of the change could have caught it — which is why this
# watches tool OUTPUT rather than edits.
#
# Deliberately narrow, because a warning nobody reads is worse than none. It fires only on a flag
# being ASSIGNED, OR-ed or MASKED — `|= FOO`, `& FOO`, `flags | FOO` — in output from a path under a
# known engine source tree. A flag merely mentioned, printed in a list, or named in a comment does
# not fire: reading a header full of `#define`s should be silent.
$ErrorActionPreference = 'Stop'

try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $event = $raw | ConvertFrom-Json
} catch { exit 0 }   # Never let the guard break the tool it guards.

$cmd = [string]$event.tool_input.command
if ([string]::IsNullOrWhiteSpace($cmd)) { exit 0 }

# **Only when the command actually reached engine source.** Matching on the OUTPUT alone would fire
# on this project's own comments, which quote these very flags — the false positive the sibling
# hooks already learned, and the reason `docs/` and `.claude/` are excluded below.
if ($cmd -notmatch 'source-sdk-2013|hl2sdk|/src/(game|public|engine)/') { exit 0 }
if ($cmd -match '\.claude[/\\]|docs[/\\]|RISKS\.md|DECISIONS\.md') { exit 0 }

$out = ''
foreach ($field in 'stdout', 'output', 'result') {
    $value = $event.tool_response.$field
    if (-not [string]::IsNullOrWhiteSpace($value)) { $out = [string]$value; break }
}

if ([string]::IsNullOrWhiteSpace($out)) { exit 0 }

# A flag being USED, not merely named. Three shapes, all of them a branch or a bitmask:
#
#   flags |= EXCLUDE_AUTO_INTERPOLATE      an option being turned on
#   if ( type & AE_TYPE_CLIENT )           a test that gates behaviour
#   LATCH_ANIMATION_VAR | EXCLUDE_...      a set being composed
#
# `[A-Z][A-Z0-9_]{5,}` keeps it to real flag names: it will not match `MAX`, a hex literal, or a
# type in shouting case, and the length floor drops most acronyms.
$patterns = @(
    '\|=\s*([A-Z][A-Z0-9_]{5,})',
    '&\s*([A-Z][A-Z0-9_]{5,})\s*\)',
    '\|\s*([A-Z][A-Z0-9_]{5,})'
)

$flags = New-Object System.Collections.Generic.HashSet[string]

foreach ($pattern in $patterns) {
    foreach ($match in [regex]::Matches($out, $pattern)) {
        $null = $flags.Add($match.Groups[1].Value)
    }
}

if ($flags.Count -eq 0) { exit 0 }

# **A per-session ledger, so the same flag is raised once.** Being told about `AE_TYPE_CLIENT` on
# every read of the same file is how a warning becomes wallpaper — and this one has to be read the
# first time to be worth anything.
$seenPath = Join-Path $env:TEMP "tf2demosalvage-flags-$($event.session_id).txt"
$seen = @{}

if (Test-Path $seenPath) {
    foreach ($line in Get-Content $seenPath) {
        if (-not [string]::IsNullOrWhiteSpace($line)) { $seen[$line.Trim()] = $true }
    }
}

$fresh = @($flags | Where-Object { -not $seen.ContainsKey($_) } | Sort-Object)

if ($fresh.Count -eq 0) { exit 0 }

try { Add-Content -Path $seenPath -Value $fresh } catch { }

$list = $fresh -join ', '

$message = @"
That engine source SETS or TESTS flags you have not looked up this session: $list

B276 is what this is for. A line reading "flags |= EXCLUDE_AUTO_INTERPOLATE" was printed here twice
while reading which variables are animation-latched, and read past both times. It is the whole
rule — that cycle is never interpolated — and this project had been interpolating it for years.
A viewmodel stopped animating and the owner found it, not a test.

Before using what you just read: grep each flag's DEFINITION and every place it is tested. A flag
changes what the line it sits on means, and the line you came for is usually the one below it.
"@

$payload = @{
    hookSpecificOutput = @{
        hookEventName     = 'PostToolUse'
        additionalContext = $message
    }
} | ConvertTo-Json -Depth 5 -Compress

Write-Output $payload
exit 0
