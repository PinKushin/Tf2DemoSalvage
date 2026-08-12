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
trx=$(find . -name "${pattern##*/}" -type f | head -1)

if [[ -z "$trx" || ! -f "$trx" ]]; then
    echo "$label: no .trx matching '$pattern' - the run produced no results file at all." >&2
    exit 1
fi

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
