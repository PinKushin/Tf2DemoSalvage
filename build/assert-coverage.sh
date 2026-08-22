#!/usr/bin/env bash
# Fails unless one ASSEMBLY's coverage in a Cobertura report meets a floor.
#
# **The per-assembly part is the whole point, and a gate without it is worse than none.** A
# coverage run reports every assembly it loaded, and the file-level `line-rate` on the root element
# is the average across all of them — including ones the suite under test never touches. Measured
# 2026-08-22 on this repository:
#
#   Core.Tests   file total  88.8 line / 83.9 branch
#                Core ONLY   95.2 line / 89.3 branch    (dragged down by Audio at 0)
#
#   Cli.Tests    file total  37.1 line / 33.5 branch
#                Cli ONLY    99.3 line / 97.1 branch    (dragged down by Core at 56)
#
# So a naive gate reads 37% for a suite that covers its own assembly at 99%, and the obvious fix —
# lowering the floor until it passes — produces a number that can never fail. Same shape as every
# other instrument fault in this repository: the measurement was of the wrong quantity, and the
# reflex is to adjust the threshold rather than the instrument.
#
# Floors are a ratchet like the test counts in gate.sh: set below the current number so ordinary
# churn does not redden the build, high enough that a cliff does. Raise them when coverage rises;
# lowering one is a decision to state out loud in the same commit that caused it.
set -euo pipefail

report=$1
assembly=$2
lineFloor=$3
branchFloor=$4

if [[ ! -f "$report" ]]; then
    echo "coverage: no report at '$report' — the collector did not run." >&2
    exit 1
fi

# The package element for this assembly. Cobertura writes rates as fractions with a lot of
# precision; awk turns them into percentages so the floors read as whole numbers.
#
# **`|| true` is load-bearing, and leaving it off made the most useful failure silent.** Under
# `set -euo pipefail` a grep that matches nothing exits 1, which aborts the script AT THIS LINE —
# so the "assembly is not in the report" branch below was unreachable and a mistyped assembly name
# failed with exit 1 and no output at all. Caught by testing the failure paths rather than only the
# passing one, which is the whole reason to test a gate in both directions.
rates=$(grep -oE "<package name=\"$assembly\" line-rate=\"[0-9.]+\" branch-rate=\"[0-9.]+\"" "$report" | head -1 || true)

if [[ -z "$rates" ]]; then
    echo "coverage: '$assembly' is not in $report." >&2
    echo "Assemblies present:" >&2
    grep -oE '<package name="[^"]+"' "$report" | sed -E 's/<package name="/  /; s/"$//' >&2
    exit 1
fi

line=$(echo "$rates" | grep -oE 'line-rate="[0-9.]+"' | grep -oE '[0-9.]+')
branch=$(echo "$rates" | grep -oE 'branch-rate="[0-9.]+"' | grep -oE '[0-9.]+')

linePercent=$(awk -v v="$line" 'BEGIN { printf "%.1f", v * 100 }')
branchPercent=$(awk -v v="$branch" 'BEGIN { printf "%.1f", v * 100 }')

echo "$assembly: $linePercent% line (floor $lineFloor), $branchPercent% branch (floor $branchFloor)"

failed=0

if awk -v v="$linePercent" -v f="$lineFloor" 'BEGIN { exit !(v < f) }'; then
    echo "$assembly: line coverage $linePercent% is below the floor of $lineFloor%." >&2
    failed=1
fi

if awk -v v="$branchPercent" -v f="$branchFloor" 'BEGIN { exit !(v < f) }'; then
    echo "$assembly: branch coverage $branchPercent% is below the floor of $branchFloor%." >&2
    failed=1
fi

exit "$failed"
