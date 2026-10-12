# Refuses an edit that ADDS a line to the view's domain-reach list.
#
# The rule it automates is D90 through D214: `Viewer3D` is a view and knows nothing a presenter or a model would.
# What it knows is measured as DOMAIN REACH - the types and members the built view assembly uses from Core, Content,
# Scene, Audio, Animation and GameSystems - and `ThinViewConformanceTests` holds that to
# `tests/Tf2DemoSalvage.Viewer3D.Tests/thin-view-domain-reach.txt`, line for line. The test fails on a use that is
# not listed, so the one way to make new domain code in the view pass is to add its line to the list. This hook is
# that door, shut.
#
# WHY A HOOK AND NOT ONLY THE TEST. The thin view regressed once already with nothing holding it: the view project
# references the domain assemblies, so every fix placed in `MainForm` compiled, and five reports in a week said "the
# MainForm call site is untested wiring" and were merged. The owner, 2026-10-12: "fuck i thought i had a hook for
# this already, but we need one". A test whose list can be edited to match is a habit; the list has to be unable to
# grow. Lines are not the measure and this hook counts none: "the line number should groww though, thin view is
# about domain logic, not lines of code".
#
# THE RULE, decidable from the payload alone:
#   Edit   denied when `new_string` holds a non-empty line that `old_string` does not - an entry added or changed.
#          Removing lines is the list shrinking, and is allowed.
#   Write  denied when the list already exists. A removal goes through Edit, where it can be seen to be one; a
#          whole-file write could carry anything. A Write that CREATES the list is allowed: that is the test's own
#          output being committed for the first time.
# Every other file is untouched, including the test's `thin-view-domain-reach.actual.txt`.
#
# The end state is the list empty and the view's project references to the domain removed, at which point the
# compiler enforces this and the hook, the list and the test can all go.
#
# PreToolUse on Edit|Write: reads the tool call from stdin.

$ErrorActionPreference = 'Stop'

try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $call = $raw | ConvertFrom-Json
    $path = [string]$call.tool_input.file_path
    if ([string]::IsNullOrWhiteSpace($path)) { exit 0 }
} catch { exit 0 }   # Never let the guard break the tool it guards.

if (($path -replace '\\', '/') -notmatch '(^|/)thin-view-domain-reach\.txt$') { exit 0 }

function Deny([string]$why) {
    $reason = "Blocked: $why The view's domain-reach list only shrinks (D214). Move the behaviour out of the view - " +
              "into Presentation, Scene or Render, with tests - and give the view one call; then the entry is not " +
              "needed. If a real VIEW feature needs a domain type, stop and tell the owner; do not add the line."
    @{ hookSpecificOutput = @{
          hookEventName            = 'PreToolUse'
          permissionDecision       = 'deny'
          permissionDecisionReason = $reason } } | ConvertTo-Json -Depth 4 -Compress
    exit 0
}

function Lines([string]$text) {
    @(($text -split "`r?`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

$edit = $call.tool_input

if ($null -ne $edit.PSObject.Properties['new_string']) {
    $before = [System.Collections.Generic.HashSet[string]]::new([string[]](Lines ([string]$edit.old_string)), [StringComparer]::Ordinal)
    $added = @(Lines ([string]$edit.new_string) | Where-Object { -not $before.Contains($_) })

    if ($added.Count -gt 0) {
        Deny ("this edit adds or changes " + $added.Count + " line(s) of the list, the first being '" + $added[0] + "'.")
    }

    exit 0
}

if ($null -ne $edit.PSObject.Properties['content']) {
    $full = [System.IO.Path]::IsPathRooted($path) ? $path : (Join-Path ($env:CLAUDE_PROJECT_DIR ?? (Get-Location).Path) $path)

    if (Test-Path -LiteralPath $full -PathType Leaf) {
        Deny "the list exists and a whole-file write could carry anything; remove lines with Edit, where a removal can be seen to be one."
    }
}

exit 0
