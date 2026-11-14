# Sequence test for warn-unreviewed-merge.ps1. pwsh -NoProfile -File <this>; exit code = mismatches.
# Stateful (reviewed.log), hence a sequence test. Builds a throwaway repo named Tf2DemoSalvage under TEMP.
$ErrorActionPreference = 'Stop'
$h = Join-Path $PSScriptRoot 'warn-unreviewed-merge.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ("rvw-test-" + [guid]::NewGuid())
$repo = Join-Path $root 'Tf2DemoSalvage'
$env:CLAUDE_LEASE_DIR = Join-Path $root 'locks'
New-Item -ItemType Directory -Path $repo | Out-Null
$fail = 0
function G { git -C $repo @args 2>&1 | Out-Null; if ($LASTEXITCODE) { throw "git $args failed" } }
function Call($cmd, $tool = 'Bash', $cwd = $repo) {
    $o = @{ hook_event_name = 'PreToolUse'; tool_name = $tool; cwd = $cwd; tool_input = @{ command = $cmd } }
    (($o | ConvertTo-Json -Compress) | pwsh -NoProfile -File $h) -join ''
}
function Check($name, $actual, $expected) {
    if ($actual -ceq $expected) { "PASS $name" } else { "FAIL $name : expected [$expected] got [$actual]"; $script:fail++ }
}
function Verdict($out) { if ($out -match '"permissionDecision":"deny"' -and $out -match 'MERGE REVIEW RULE') { 'deny' } elseif ($out -eq '') { 'silent' } else { "other:$out" } }
try {
    G init -q -b main; G config user.email t@t; G config user.name t
    New-Item -ItemType Directory "$repo/docs" | Out-Null
    'a' | Set-Content "$repo/a.txt"; G add .; G commit -qm base
    G checkout -qb feat/code; 'b' | Set-Content "$repo/b.cs"; G add .; G commit -qm code
    G checkout -q main; G checkout -qb feat/docs; 'x' | Set-Content "$repo/docs/n.md"; 'x' | Set-Content "$repo/docs/d.json"; 'x' | Set-Content "$repo/README.md"; G add .; G commit -qm docs
    G checkout -q main
    $sha = (git -C $repo rev-parse feat/code).Trim()
    $log = Join-Path $env:CLAUDE_LEASE_DIR 'reviewed.log'

    $other = Join-Path $root 'elsewhere'; New-Item -ItemType Directory $other | Out-Null
    $wt = Join-Path $root 'wt-plain'; G worktree add -q $wt -b wt-branch
    $slash = $repo -replace '\\', '/'
    $bash = '/' + $slash.Substring(0, 1).ToLowerInvariant() + $slash.Substring(2)
    Check 'unreviewed code merge denies'     (Verdict (Call 'git merge --no-ff feat/code')) 'deny'
    Check 'deny names the sha'               ((Call 'git merge feat/code') -match [regex]::Escape($sha)) $true
    Check 'deny is never an ask'             ((Call 'git merge feat/code') -match '"ask"') $false
    Check 'PowerShell tool denies too'       (Verdict (Call 'git merge feat/code' 'PowerShell')) 'deny'
    Check 'cd into the repo from elsewhere'  (Verdict (Call "cd $slash && git merge feat/code" 'Bash' $other)) 'deny'
    Check 'cd with a Git Bash path'          (Verdict (Call "cd $bash && git merge feat/code" 'Bash' $other)) 'deny'
    Check 'git -C the repo from elsewhere'   (Verdict (Call "git -C `"$repo`" merge feat/code" 'Bash' $other)) 'deny'
    Check 'worktree without the name'        (Verdict (Call 'git merge feat/code' 'Bash' $wt)) 'deny'
    Check 'docs-only merge is silent'        (Verdict (Call 'git merge --no-ff feat/docs')) 'silent'
    Check 'merging main into a branch is silent' (Verdict (Call 'git merge main')) 'silent'
    Check 'merging origin/main is silent'    (Verdict (Call 'git merge --no-edit origin/main')) 'silent'
    Check 'non-merge git command is silent'  (Verdict (Call 'git status')) 'silent'
    Check 'non-Tf2DemoSalvage cwd is silent' (Verdict (Call 'git merge feat/code' 'Bash' $root)) 'silent'
    Check 'unknown branch is silent'         (Verdict (Call 'git merge nope')) 'silent'
    Check 'marker command is silent'         (Verdict (Call "echo review-done $sha")) 'silent'
    Check 'marker recorded the sha'          ((Get-Content $log) -join ',') $sha
    Check 'reviewed merge is silent'         (Verdict (Call 'git merge --no-ff feat/code')) 'silent'
    G checkout -q feat/code; 'c' | Set-Content "$repo/c.cs"; G add .; G commit -qm more; G checkout -q main
    Check 'new commit makes it unreviewed'   (Verdict (Call 'git merge feat/code')) 'deny'
    Check 'bad JSON input is silent'         (Verdict (('not json{' | pwsh -NoProfile -File $h) -join '')) 'silent'
} finally {
    Remove-Item -Recurse -Force $root
}
"$fail mismatch(es)"
exit $fail
