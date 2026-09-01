# PostToolUse guard: a dotnet build or test that FAILED must not read as success.
#
# Why a hook and not care: the failure is invisible and it happened. A build was checked with
# `grep -E "error C"`, which matches `error CS` and NOT the Sonar analyzers' `error S`. The build
# had failed, the check printed nothing, "built" was reported, and the next twenty minutes measured
# a stale binary and published the numbers. The agent chose the wrong pattern; a hook does not have
# to choose one.
#
# Reads the tool's OUTPUT rather than its exit code, because the exit code is routinely thrown away:
# these commands are almost always piped into grep or head, and a pipeline reports the LAST stage's
# status. That is the same reason a gate run once reported `Failed: 3` through a pipe as exit 0.
$ErrorActionPreference = 'Stop'
try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $event = $raw | ConvertFrom-Json
    $cmd = $event.tool_input.command
    if ([string]::IsNullOrWhiteSpace($cmd)) { exit 0 }
} catch { exit 0 }   # Never let the guard break the tool it guards.

# Only for an actual dotnet INVOCATION. Matching the vocabulary rather than the verb is the false
# positive the sibling hooks already learned: a file that documents this rule necessarily contains
# every string it matches on.
$subject = $cmd
try {
    . "$PSScriptRoot/../../../.claude/hooks/lib/CommandText.ps1"
    $subject = Remove-HeredocBodies -Command $cmd
} catch { $subject = $cmd }

if ($subject -notmatch '(^|[\s;|&(])dotnet(\.exe)?\s+(test|build|publish|pack|msbuild)') { exit 0 }

# The response shape differs between Bash and PowerShell and between versions, so take whatever
# text is there rather than trusting one field to exist.
$out = ''
try {
    $r = $event.tool_response
    if ($r -is [string]) { $out = $r }
    else { $out = (($r.stdout, $r.stderr, $r.output, $r.content) -join "`n") }
} catch { exit 0 }
if ([string]::IsNullOrWhiteSpace($out)) { exit 0 }

# `error` followed by ANY analyzer prefix: CS from the compiler, S from SonarAnalyzer, CA from the
# .NET analyzers, IDE from the style rules. The point of the class is that a hand-written pattern
# for one of them silently misses the rest.
$errors = [regex]::Matches($out, '(?m)^.*\berror\s+[A-Z]{1,4}\d{3,5}\b.*$') |
    ForEach-Object { $_.Value.Trim() } |
    Select-Object -Unique -First 5

if (-not $errors) { exit 0 }

$reason = "This build or test run FAILED and the output may have been filtered in a way that hid it. " +
          "Zero Warnings is a standing requirement here and analyzer errors are not just 'error CS' - " +
          "SonarAnalyzer emits 'error S', the .NET analyzers 'error CA', so a grep for one misses the " +
          "others. Do not treat the previous command as having succeeded, and do not run or measure " +
          "the binaries it did not produce. First errors:`n`n" + ($errors -join "`n")

@{ hookSpecificOutput = @{
      hookEventName = 'PostToolUse'
      additionalContext = $reason } } | ConvertTo-Json -Depth 5 -Compress
