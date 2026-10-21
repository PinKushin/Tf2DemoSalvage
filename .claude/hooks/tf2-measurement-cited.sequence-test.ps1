# Sequence test for tf2-measurement-cited.ps1. Run by hand:
#   pwsh -NoProfile -File <this> -Root <empty scratch dir>      Exit code = number of mismatches.
# A throwaway repo holds a committed RELEASE-NOTES.md with an old uncited figure, so a Write that keeps it is the
# control for "added lines only".
param([Parameter(Mandatory)][string]$Root)
$ErrorActionPreference = 'Stop'
$h = Join-Path $PSScriptRoot 'tf2-measurement-cited.ps1'
$fail = 0
$d = Join-Path $Root 'cited'
New-Item -ItemType Directory -Path (Join-Path $d 'docs/findings') -Force | Out-Null
git -C $d init -q; git -C $d config user.email t@t; git -C $d config user.name t
Set-Content (Join-Path $d 'RELEASE-NOTES.md') "# Notes`nThe viewer draws 67 fps.`n" -NoNewline
git -C $d add -A; git -C $d commit -qm init

function Run([string]$event, [hashtable]$in) {
    $o = @{ hook_event_name = $event; tool_name = 'Edit'; cwd = $d; tool_input = $in }
    $out = ($o | ConvertTo-Json -Compress) | pwsh -NoProfile -File $h
    if ($LASTEXITCODE -ne 0) { return "exit$LASTEXITCODE" }
    if ("$out" -match '"deny"') { 'deny' } elseif ("$out" -match 'additionalContext') { 'warn' } else { 'allow' }
}
function Check($name, $actual, $expected) {
    if ($actual -ceq $expected) { "PASS $name" } else { "FAIL $name : expected [$expected] got [$actual]"; $script:fail++ }
}
$rn = Join-Path $d 'RELEASE-NOTES.md'
$fd = Join-Path $d 'docs/findings/88-x.md'
Check 'release notes, uncited fps added: denied'     (Run 'PreToolUse' @{ file_path = $rn; old_string = 'a'; new_string = "a`nNow 120 fps." }) 'deny'
Check 'release notes, cited: allowed'                (Run 'PreToolUse' @{ file_path = $rn; old_string = 'a'; new_string = "a`nNow 120 fps (measurements/2026-10-21-a.md)." }) 'allow'
Check 'release notes, finding NN cite: allowed'      (Run 'PreToolUse' @{ file_path = $rn; old_string = 'a'; new_string = "a`nStall 40 ms gone (finding 88)." }) 'allow'
Check 'release notes, no figure: allowed'            (Run 'PreToolUse' @{ file_path = $rn; old_string = 'a'; new_string = "a`nA new menu." }) 'allow'
Check 'release notes, old uncited line kept: allowed' (Run 'PreToolUse' @{ file_path = $rn; old_string = 'The viewer draws 67 fps.'; new_string = "The viewer draws 67 fps.`nA menu." }) 'allow'
Check 'release notes Write keeping the old figure'  (Run 'PreToolUse' @{ file_path = $rn; content = "# Notes`nThe viewer draws 67 fps.`nA menu.`n" }) 'allow'
Check 'release notes Write adding a figure: denied'  (Run 'PreToolUse' @{ file_path = $rn; content = "# Notes`nThe viewer draws 67 fps.`nNow 3.2 MB per frame.`n" }) 'deny'
Check 'release notes at PostToolUse: silent'         (Run 'PostToolUse' @{ file_path = $rn; old_string = 'a'; new_string = "a`nNow 120 fps." }) 'allow'
Check 'finding, uncited ms added: warned'            (Run 'PostToolUse' @{ file_path = $fd; old_string = 'a'; new_string = "a`nThe frame is 14.5 ms." }) 'warn'
Check 'finding, at PreToolUse: not blocked'          (Run 'PreToolUse' @{ file_path = $fd; old_string = 'a'; new_string = "a`nThe frame is 14.5 ms." }) 'allow'
Check 'finding, cited: silent'                       (Run 'PostToolUse' @{ file_path = $fd; old_string = 'a'; new_string = "a`n14.5 ms (measurements/2026-10-21-a.md)" }) 'allow'
Check 'RISKS uncited fps: warned'                    (Run 'PostToolUse' @{ file_path = (Join-Path $d 'docs/RISKS.md'); old_string = 'a'; new_string = "a`n600 fps" }) 'warn'
Check 'other file with a figure: silent'             (Run 'PostToolUse' @{ file_path = (Join-Path $d 'docs/SPEC.md'); old_string = 'a'; new_string = "a`n600 fps" }) 'allow'
Check 'measurement file itself: silent'              (Run 'PostToolUse' @{ file_path = (Join-Path $d 'docs/measurements/README.md'); old_string = 'a'; new_string = "a`n600 fps" }) 'allow'
"--- $fail failed ---"
exit $fail
