#!/usr/bin/env bash
# Proves the box lock survives the runner's own re-exec, and still refuses a real second holder.
#
# The lock function is EXTRACTED from run-measurements.sh rather than restated, as test-prune.sh does:
# a restated copy passes against a stale version of the logic.
#
# The failure this guards (fuzz-box, 2026-09-29 16:00): the runner takes the lock, pulls, then
# re-execs itself. `exec` keeps fd 9, but the second pass reopened it, which drops this process's
# hold and asks for the lock afresh on a NEW description. `git fetch` had left a background git
# (auto maintenance) holding the OLD description through an inherited fd 9, so the fresh request
# was refused and the run locked itself out: "held by: bash, git, git, git".
#
# Needs flock and a Linux shell: run it on a measurement box, not in Git Bash.
set -uo pipefail

runner="$(dirname "$0")/run-measurements.sh"
work="$(mktemp -d)"
LOCK="$work/measurement-box.lock"
lingering=""
holder=""
cleanup() {
  [ -n "$lingering" ] && kill "$lingering" 2>/dev/null
  [ -n "$holder" ] && kill "$holder" 2>/dev/null
  rm -rf "$work"
}
trap cleanup EXIT

eval "$(sed -n '/^take_box_lock() {/,/^}/p' "$runner")"
declare -F take_box_lock > /dev/null || { echo "FAIL: take_box_lock not found in $runner"; exit 1; }

fail=0
check() { if [ "$2" = "$3" ]; then echo "  ok: $1"; else echo "  FAIL: $1 - expected $3, got $2"; fail=1; fi; }

# 1. A first run takes a free lock.
first=$( (unset RUNNER_REEXECED; take_box_lock 2>/dev/null && echo taken) || echo refused)
check "a free lock is taken" "$first" taken

# 2. The re-exec with a child still holding the inherited description, all in one process as exec is.
reexec=$(
  unset RUNNER_REEXECED
  take_box_lock 2>/dev/null || { echo refused-first; exit; }
  sleep 30 &                      # inherits fd 9, like the background git
  echo "$!" > "$work/lingering.pid"
  export RUNNER_REEXECED=1        # what the runner's exec sets
  (take_box_lock 2>/dev/null && echo taken) || echo refused
)
lingering=$(cat "$work/lingering.pid" 2>/dev/null)
check "the re-exec keeps its own lock despite a lingering child" "$reexec" taken
[ -n "$lingering" ] && kill "$lingering" 2>/dev/null
lingering=""

# 3. The control: another run on its own description holds the lock, so we must be refused.
( exec 8>"$LOCK"; flock -n 8 && sleep 30 ) &
holder=$!
sleep 1
blocked=$( (unset RUNNER_REEXECED; take_box_lock 2>/dev/null && echo taken) || echo refused)
check "a lock held by another run is refused" "$blocked" refused

[ "$fail" = 0 ] && echo "PASS" || { echo "FAILED"; exit 1; }
