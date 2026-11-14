# Refuses `git merge` of a branch named for a bug (fix/b734-..., perf/b599-...) while that bug's docs/RISKS.md heading,
# as the BRANCH has it, still says the item is open.
#
# WHY. The owner, 2026-11-14, as B734's sub-view sprites were about to merge with its impacts, ropes and no-Z order filed
# as open beside it: "fix those other bugs before the merge, i hate you leaving bugs open before a merge, they should not
# murge until there is basically no bugs to do with it left" (D258). A RISKS entry listing "still open" parts was the
# lead's habit of merging a slice and filing the rest; a sentence in a memory did not stop it the first time
# (an-open-list-is-not-a-stopping-point, D249), so the merge itself is refused.
#
# HOW. For each `fix|perf|feat|refactor/b<N>` branch named in a `git merge` command, the hook reads `docs/RISKS.md` from
# that branch (`git show <branch>:docs/RISKS.md`, in the repo the command's last `cd`/`-C` names, else the session cwd)
# and finds the heading `## B<N> — ...`. If the heading contains the word "open" (any case), the merge is denied and the
# heading is quoted. A heading that says "needs owner" passes: an item only the owner can unblock (a decision, a capture
# by eye) is the one legitimate thing to leave, and the heading then says so in words the owner reads.
#
# WHAT IT CANNOT SEE: an open part written only in the entry's body, not its heading, or a bug with no B number in its
# branch name. Keep the heading honest: it is the line this reads. Fails open (exit 0) when the branch or file cannot be
# read, so a typo does not block unrelated work.
#
# Tests: tf2-merge-closes-its-bug.tests.json.

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

    $call = $raw | ConvertFrom-Json
    if ([string]$call.tool_name -notin @('Bash', 'PowerShell')) { exit 0 }

    $command = [string]$call.tool_input.command
    if ($command -notmatch '\bgit\b[^\n;&|]*\bmerge\b' -or $command -match '\bmerge\s+--abort\b') { exit 0 }

    $branches = [regex]::Matches($command, '(?i)\b((?:fix|perf|feat|refactor)/b(\d+)[\w\-./]*)') |
        ForEach-Object { [pscustomobject]@{ Branch = $_.Groups[1].Value; Number = $_.Groups[2].Value } }
    if (-not $branches) { exit 0 }

    # The repo: the last `cd <dir>` or `git -C <dir>` in the command, else the session's cwd.
    $dir = [string]$call.cwd
    foreach ($m in [regex]::Matches($command, '(?:\bcd\s+|\bgit\s+-C\s+)("([^"]+)"|''([^'']+)''|(\S+))')) {
        $dir = @($m.Groups[2].Value, $m.Groups[3].Value, $m.Groups[4].Value) | Where-Object { $_ } | Select-Object -First 1
    }
    if ($dir -match '^/([a-zA-Z])/(.*)$') { $dir = "$($Matches[1]):/$($Matches[2])" }

    $refusals = @()

    foreach ($b in $branches) {
        $risks = & git -C $dir show "$($b.Branch):docs/RISKS.md" 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $risks) { continue }

        $heading = $risks | Where-Object { $_ -match "^## B$($b.Number) " } | Select-Object -First 1
        if (-not $heading) { continue }

        if ($heading -match '(?i)\bopen\b' -and $heading -notmatch '(?i)needs owner') {
            $refusals += "$($b.Branch): $heading"
        }
    }

    if ($refusals.Count -eq 0) { exit 0 }

    $reason = "Blocked: the branch's own bug is still open in docs/RISKS.md, and a bug's branch merges only when its " +
        "work is done (owner, 2026-11-14, D258: `"they should not merge until there is basically no bugs to do with it " +
        "left`"). Fix the open parts on this branch, then update the heading. Only an item that needs the owner (a " +
        "decision, a capture by eye) may stay, with `"needs owner`" in the heading.`n" + ($refusals -join "`n")

    @{ hookSpecificOutput = @{ hookEventName = 'PreToolUse'; permissionDecision = 'deny'; permissionDecisionReason = $reason } } |
        ConvertTo-Json -Depth 4 -Compress
    exit 0
} catch {
    [Console]::Error.WriteLine("tf2-merge-closes-its-bug: $($_.Exception.Message)")
    exit 0
}
