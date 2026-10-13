# Refuses an edit that puts a domain namespace or a domain project reference into the view.
#
# The rule it automates is D90 through D214: `Viewer3D` is a view and knows nothing a presenter or a model would.
# Its DOMAIN REACH - the types and members the built view assembly uses from Core, Content, Scene, Audio, Animation
# and GameSystems - went from 1,025 entries to none over eight slices (2026-10-13), and the view's project no longer
# references those assemblies. `ThinViewConformanceTests` holds that with three tests. This hook is the same rule
# at the moment of the edit, before a build.
#
# WHY A HOOK AND NOT ONLY THE TESTS. The thin view regressed once already with nothing holding it: every fix placed
# in `MainForm` compiled, and five reports in a week said "the MainForm call site is untested wiring" and were
# merged. The owner, 2026-10-12: "fuck i thought i had a hook for this already, but we need one". And the compiler
# does NOT hold the end state: the domain assemblies still arrive through Presentation and Render (`Device3D`
# implements Scene interfaces), so `using Tf2DemoSalvage.Scene` in the view builds. Lines are not the measure and
# this hook counts none: "the line number should groww though, thin view is about domain logic, not lines of code".
#
# THE RULE, decidable from the payload alone: an Edit or Write to a file under `managed/Tf2DemoSalvage.Viewer3D/`
# is denied when the text it writes (`new_string` or `content`) names `Tf2DemoSalvage.Core`, `.Content`, `.Scene`,
# `.Audio`, `.Animation` or `.GameSystems`. That covers a using, a qualified name and a ProjectReference alike.
# Every other file is untouched.
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

if (($path -replace '\\', '/') -notmatch '(^|/)managed/Tf2DemoSalvage\.Viewer3D/') { exit 0 }

$edit = $call.tool_input
$text = if ($null -ne $edit.PSObject.Properties['new_string']) { [string]$edit.new_string }
        elseif ($null -ne $edit.PSObject.Properties['content']) { [string]$edit.content }
        else { '' }

$named = [regex]::Match($text, 'Tf2DemoSalvage\.(Core|Content|Scene|Audio|Animation|GameSystems)\b')
if (-not $named.Success) { exit 0 }

$reason = "Blocked: this writes '" + $named.Value + "' into the view. The view reaches no domain type, member or " +
          "namespace (D214). Put the behaviour in Presentation, Scene or Render, with tests, and give the view one " +
          "call in plain values. If a real VIEW feature needs a domain type, stop and tell the owner."
@{ hookSpecificOutput = @{
      hookEventName            = 'PreToolUse'
      permissionDecision       = 'deny'
      permissionDecisionReason = $reason } } | ConvertTo-Json -Depth 4 -Compress
exit 0
