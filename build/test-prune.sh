#!/usr/bin/env bash
# Proves the run-directory prune deletes only this project's runs, and only the excess.
#
# The prune function is EXTRACTED from run-measurements.sh rather than restated here. A test that
# restates the logic passes against a stale copy of it, which is the failure this whole area keeps
# producing - the previous glob prune had a verification step that passed in both of the cases it
# was written to catch.
#
# The fixture makes the OTHER projects' directories the oldest present, so an unscoped prune takes
# them first. A fixture where they are newest would let a broken prune pass.
set -euo pipefail

runner="$(dirname "$0")/run-measurements.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
export HOME="$work"
mkdir -p "$work/measurements"

# 34 of ours (4 over the limit of 30) plus neighbours, oldest first so ours are the newest.
make() { mkdir -p "$work/measurements/$1"; [ -n "${2:-}" ] && echo "$2" > "$work/measurements/$1/.owner"; touch -d "$3" "$work/measurements/$1"; }

make "20260101T000000Z-aaa-fuzz"            pbj        "2026-01-01"   # ends in -fuzz, like ours
make "20260101T000001Z-bbb-stryker-core"    pbj        "2026-01-02"
make "20260101T000002Z-ccc-unmarked-legacy" ""         "2026-01-03"   # no marker at all
i=0
while [ "$i" -lt 34 ]; do
  make "2026021${i}T000000Z-sha${i}-tf2-core" tf2demosalvage "2026-02-01 00:00:$(printf '%02d' "$i")"
  i=$((i + 1))
done

# Extract and run just the prune, exactly as the runner defines it.
eval "$(sed -n '/^RUN_OWNER=/,/^}/p' "$runner")"
prune_own_runs

ours=$(find "$work/measurements" -mindepth 1 -maxdepth 1 -type d -exec test -f '{}/.owner' \; -print 2>/dev/null | while read -r d; do [ "$(cat "$d/.owner")" = tf2demosalvage ] && echo "$d"; done | wc -l)
fail=0
check() { if [ "$2" = "$3" ]; then echo "  ok: $1 ($2)"; else echo "  FAIL: $1 - expected $3, got $2"; fail=1; fi; }

check "ours pruned to 30"            "$ours" 30
check "pbj -fuzz run survived"       "$([ -d "$work/measurements/20260101T000000Z-aaa-fuzz" ] && echo yes || echo no)" yes
check "pbj stryker run survived"     "$([ -d "$work/measurements/20260101T000001Z-bbb-stryker-core" ] && echo yes || echo no)" yes
check "unmarked legacy dir survived" "$([ -d "$work/measurements/20260101T000002Z-ccc-unmarked-legacy" ] && echo yes || echo no)" yes

[ "$fail" = 0 ] && echo "PASS" || { echo "FAILED"; exit 1; }
