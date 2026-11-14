# PreToolUse DENY: `git merge <branch>` in the Tf2DemoSalvage repo when the branch head has not been marked
# reviewed and the merge touches non-doc files. Owner asked for it 2026-10-14; made a deny 2026-11-13.
#
# THE RULE (Tf2DemoSalvage docs/memory/review-for-recall.md): every merge = the lead's `/code-review high`
# run BEFORE reading any agent report (independence), plus one `caveman:cavecrew-reviewer` agent - Fable
# for large/structural diffs, Sonnet for small ones, never the lead's own model (cross-model review). Owner: "id rather false psitives then misses";
# measured 2026-10-14, each of four reviewers found something no other did, so one pass is not enough.
#
# WHY A DENY, NOT A WARNING OR AN ASK. As a warning (2026-10-14 to 2026-11-13) it was ignored: the lead's own
# `/code-review high` was skipped on all five B724 merges, and the owner noticed. An "ask" was rejected by the
# owner, 2026-11-13: "i dont want you stuck wating for me to approve a hook its wastes time and i dont pay enough
# attention to what im approving to catch anyuthing anyway". A deny costs no one's attention: the assistant
# runs the reviews, marks the sha, and merges again. A merge the owner explicitly orders without review is marked
# the same way, and the assistant says so.
#
# MARKER. After both reviews, run (Bash or PowerShell):   echo review-done <sha>
# The hook catches that command and appends the sha to ~/.claude/locks/reviewed.log (same trick as
# lease-release in lease-shared-mcp.ps1; the echo itself runs harmlessly). The sha is the branch HEAD, so any
# commit added after the review makes the branch unreviewed again - which is the point.
#
# WHERE THE MERGE RUNS. The hook's own cwd is the session's start folder, which is often another worktree, so
# the command's own `cd <path>` (last one before the merge) or `git -C <path> merge` wins over it. A path is in
# scope when its git COMMON dir names Tf2DemoSalvage: worktrees such as F:/wt-b724r do not carry the name in
# their own path (the cwd-name test missed every one of them until 2026-11-13).
# Docs-only merges (every changed file under docs/ or *.md) pass silently.
#
# FAIL-SAFE: any error allows the call with no output. Test: warn-unreviewed-merge.sequence-test.ps1.
# Override for the test: CLAUDE_LEASE_DIR (where reviewed.log lives).
$ErrorActionPreference = 'Stop'
$dir = if ($env:CLAUDE_LEASE_DIR) { $env:CLAUDE_LEASE_DIR } else { Join-Path $HOME '.claude/locks' }
try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $p = $raw | ConvertFrom-Json
    if ($p.tool_name -notin 'Bash', 'PowerShell') { exit 0 }
    $cmd = [string]$p.tool_input.command
    $log = Join-Path $dir 'reviewed.log'

    if ($cmd -match 'review-done\s+([0-9a-fA-F]{7,40})\b') {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        Add-Content -LiteralPath $log -Value $Matches[1].ToLowerInvariant()
        exit 0
    }
    $m = [regex]::Match($cmd, '\bgit\s+(?:-C\s+("[^"]+"|''[^'']+''|\S+)\s+)?merge\s+([^;&|\r\n]*)')
    if (-not $m.Success) { exit 0 }
    $branch = ($m.Groups[2].Value -split '\s+' | Where-Object { $_ -and -not $_.StartsWith('-') } | Select-Object -First 1)
    if (-not $branch) { exit 0 }
    # Merging the trunk INTO a branch brings in only work every merge to the trunk already reviewed: nothing to review.
    if ($branch -match '^(origin/)?(main|master)$') { exit 0 }

    $cwd = if ($p.cwd) { [string]$p.cwd } else { (Get-Location).Path }
    $before = $cmd.Substring(0, $m.Index)
    $cds = [regex]::Matches($before, '(?:^|[;&|]\s*|\s)(?:cd|Set-Location|Push-Location)\s+("[^"]+"|''[^'']+''|[^\s;&|]+)')
    if ($cds.Count -gt 0) { $cwd = $cds[$cds.Count - 1].Groups[1].Value.Trim('"', "'") }
    if ($m.Groups[1].Success) { $cwd = $m.Groups[1].Value.Trim('"', "'") }
    if ($cwd -match '^/([a-zA-Z])/(.*)$') { $cwd = "$($Matches[1]):/$($Matches[2])" }   # Git Bash /f/x -> F:/x
    if (-not (Test-Path -LiteralPath $cwd)) { exit 0 }

    Push-Location -LiteralPath $cwd
    try {
        $common = git rev-parse --path-format=absolute --git-common-dir 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]$common -notmatch 'Tf2DemoSalvage') { exit 0 }
        # no pipe after git: Select-Object -First leaves $LASTEXITCODE null (found by the test)
        $sha = git rev-parse $branch 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $sha) { exit 0 }
        $sha = ([string]$sha).Trim().ToLowerInvariant()
        $seen = if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log } else { @() }
        # a marker may be a short sha: match by prefix
        if ($seen | Where-Object { $_ -and $sha.StartsWith($_.Trim().ToLowerInvariant()) }) { exit 0 }
        $files = git diff --name-only "HEAD...$branch" 2>$null
        if ($LASTEXITCODE -ne 0) { exit 0 }
        $code = @($files | Where-Object { $_ -and $_ -notmatch '^docs/' -and $_ -notmatch '\.md$' })
        if ($code.Count -eq 0) { exit 0 }
    } finally { Pop-Location }

    $msg = "MERGE REVIEW RULE: branch '$branch' (head $sha) is not marked reviewed, and the merge touches $($code.Count) non-doc file(s). " +
        "Rule (docs/memory/review-for-recall.md): every merge = the lead's '/code-review high', run BEFORE reading any agent report, " +
        "PLUS one 'caveman:cavecrew-reviewer' agent - Fable for large or structural diffs, Sonnet for small ones, never your own model (cross-model). " +
        "Do them now, without asking the owner; when both are done and each finding verified, run: echo review-done $sha   then merge again."
    @{ hookSpecificOutput = @{ hookEventName = 'PreToolUse'; permissionDecision = 'deny'; permissionDecisionReason = $msg } } | ConvertTo-Json -Depth 5 -Compress
} catch {
    try {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        Add-Content -LiteralPath (Join-Path $dir 'lease.log') -Value ("{0:o} warn-unreviewed-merge WARN allowed: {1}" -f (Get-Date), $_)
    } catch { [Console]::Error.WriteLine("warn-unreviewed-merge: could not log: $_") }
}
exit 0
