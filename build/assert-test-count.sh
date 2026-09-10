#!/usr/bin/env bash
# Fails unless a .trx reports at least the expected number of executed tests.
#
# "Passed!" is not the result. The count is. Two failures produce a green run and no visible
# complaint:
#
#   - A test host that dies partway prints a pass line with a truncated total. Observed on this
#     machine 2026-08-10: `Passed! - Failed: 0, Passed: 630 ... Total: 630` against a suite of
#     646, when a native library killed the runner mid-run. Nothing in that line is a warning.
#   - `dotnet test --filter` matching nothing exits 0 and prints no summary at all, so a renamed
#     fixture silently tests nothing.
#
# A floor rather than an equality check: exact counts make every added test a red build, while
# the floor still catches both cases above, which are the ones that hide.
set -euo pipefail

pattern=$1
expected=$2
label=$3

# Only the basename is used. The caller passes a glob for readability at the call site, but
# matching on the name alone is what makes this work identically regardless of which
# TestResults directory the runner chose to write into.
#
# **`.claude/worktrees` is pruned, and leaving it in made the gate measure another session's tree.**
# A spun-off task runs in a worktree UNDER this repository, so it has its own
# `tests/*/TestResults/core.trx`; `find .` reached them and `head -1` took whichever the filesystem
# offered first. Measured 2026-09-10 with two chips running: this repo's core.trx said 1851, the two
# worktrees said 1843 and 1846, and the gate failed against its own correct floor by reading a
# stranger's file. The dangerous direction is the other one — a worktree with MORE tests would have
# passed a floor this tree does not meet.
#
# **Several matches is an ambiguity, and this script does not get to resolve it silently.** Taking
# the first, or the newest, would be a guess about which run the caller meant, and guessing is the
# whole failure mode the file exists to prevent. It says which files and stops.
mapfile -t found < <(
    find . -path ./.claude/worktrees -prune -o -name "${pattern##*/}" -type f -print
)

if [[ ${#found[@]} -eq 0 ]]; then
    echo "$label: no .trx matching '$pattern' - the run produced no results file at all." >&2
    exit 1
fi

if [[ ${#found[@]} -gt 1 ]]; then
    echo "$label: ${#found[@]} files match '${pattern##*/}', so which one holds this run is a guess:" >&2
    printf '  %s\n' "${found[@]}" >&2
    echo "Delete the stale TestResults directories, or scope the search." >&2
    exit 1
fi

trx=${found[0]}

# The counters element carries the authoritative totals; parsing the console line instead would
# reintroduce the truncation problem this script exists to catch.
executed=$(grep -oE 'total="[0-9]+"' "$trx" | head -1 | grep -oE '[0-9]+')
failed=$(grep -oE 'failed="[0-9]+"' "$trx" | head -1 | grep -oE '[0-9]+')

echo "$label: $executed executed, $failed failed (floor $expected)"

if [[ "${failed:-0}" -gt 0 ]]; then
    echo "$label: $failed test(s) failed." >&2
    exit 1
fi

if [[ "${executed:-0}" -lt "$expected" ]]; then
    echo "$label: only $executed tests executed, expected at least $expected." >&2
    echo "Either the test host died partway, or tests were removed - if removed, lower the floor." >&2
    exit 1
fi
