#!/usr/bin/env bash
#
# Run one long measurement workload on the shared measurement box.
#
#   bash build/run-measurements.sh corpus   [--no-pull]
#   bash build/run-measurements.sh core     [--no-pull]
#
# Modelled on PokemonBattleJournal's build/run-measurements.sh, deliberately: the boxes are
# shared BY WORKLOAD, not by project, so two repos run mutation testing on the same machine and
# must agree about how.
#
# THE LOCK IS SHARED WITH EVERY OTHER PROJECT ON THIS BOX
# `/tmp/measurement-box.lock` guards the BOX, not the project — which is why it is named for the
# box. Giving this repo its own lock file would let two repos run at once, which is the
# one thing the lock exists to prevent: Stryker rebuilds mutated copies continuously, and a
# build failure caused by a concurrent job reads as a SURVIVING MUTANT, not as an error. Two
# locks would silently corrupt both projects' results rather than colliding loudly.
#
# WHERE THE SCHEDULE LIVES — not here
# `ssh mutation-box cat '~/measurement-schedule.md'` is the slot map, and `crontab -l` on the box
# outranks it. Copying either into this comment produces a third version that goes stale. The
# booked slots for this repo as of 2026-08-10 are 09:00 daily (core), 09:20 daily (cli) and 20:00
# Sunday (corpus); the PokemonBattleJournal agent owns all crontab edits on both boxes.
set -euo pipefail

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

# MSBuild node reuse leaves daemons alive after the build that spawned them, and they inherit
# fd 9 — which held the lock long after a finished run in PBJ's experience.
export MSBUILDDISABLENODEREUSE=1

MODE="${1:-corpus}"
PULL="${2:-}"
REPO="$HOME/tf2demosalvage"
LOCK="/tmp/measurement-box.lock"

exec 9>"$LOCK"
if ! flock -n 9; then
  echo "ERROR: another measurement run holds $LOCK. One at a time." >&2
  # Deleting the lock FILE does nothing: flock is on the open file description, not the path.
  command -v fuser >/dev/null && { echo "held by:" >&2; fuser -v "$LOCK" >&2 2>&1 || true; }
  exit 1
fi

cd "$REPO"

if [ "$PULL" != "--no-pull" ]; then
  git fetch --quiet origin main
  git reset --quiet --hard origin/main
  # Demos live in Git LFS. Without this the working tree holds ~130-byte pointer stubs and every
  # corpus test degrades to a passing no-op — RISKS B20, as a shell step.
  git lfs pull
fi

smallest=$(find tools/corpus/demos -name '*.dem' -printf '%s\n' 2>/dev/null | sort -n | head -1)
if [ -z "$smallest" ] || [ "$smallest" -le 4096 ]; then
  echo "ERROR: corpus is missing or is LFS pointer stubs (smallest ${smallest:-none} bytes)." >&2
  exit 1
fi

STAMP=$(date -u +%Y%m%dT%H%M%SZ)
SHA=$(git rev-parse --short HEAD)
OUT="$HOME/measurements/${STAMP}-${SHA}-tf2-${MODE}"
mkdir -p "$OUT"

echo "=== tf2demosalvage ${MODE} @ ${SHA} — $(date -Is)"
echo "smallest demo: ${smallest} bytes"

case "$MODE" in
  corpus)  PROJECT="tests/Tf2DemoSalvage.Corpus.Tests" ;;
  core)    PROJECT="tests/Tf2DemoSalvage.Core.Tests" ;;
  cli)     PROJECT="tests/Tf2DemoSalvage.Cli.Tests" ;;
  *) echo "ERROR: unknown mode '$MODE'. Expected corpus, core or cli." >&2; exit 2 ;;
esac

cd "$PROJECT"
dotnet tool restore 9>&-

# `9>&-` on every long command: an inherited fd 9 keeps the lock held by any surviving child,
# and .NET leaves build servers alive on purpose.
#
# The exit code is CAPTURED, not discarded. `| tee log || true` turns a threshold violation and a
# refusal-to-start alike into exit 0, so the cron log reads as success either way — house rule 5
# on the box, and not hypothetical: the 2026-08-10 core calibration exited non-zero on
# "Final mutation score is below threshold break. Crashing..." and this script reported success.
# The summary and report copy still have to run, hence the capture rather than letting `set -e`
# abort here.
set +e
dotnet stryker 2>&1 9>&- | tee "${OUT}/stryker.log"
STATUS=${PIPESTATUS[0]}
set -e

# The score line and the survivors are what a reader wants; the full log stays in the run dir.
grep -E "mutation score|Killed:|Survived:|Timeout:|No Coverage" "${OUT}/stryker.log" \
  | tail -8 > "${OUT}/summary.txt" || true
cat "${OUT}/summary.txt" 2>/dev/null || true

cd "$REPO"
if [ -d "${PROJECT}/StrykerOutput" ]; then
  latest=$(ls -1dt "${PROJECT}/StrykerOutput/"*/ 2>/dev/null | head -1)
  [ -n "$latest" ] && cp -r "${latest}reports" "${OUT}/" 2>/dev/null || true
fi

# Keep the last 30 runs, matching PBJ so one box does not accumulate two conventions.
ls -1dt "${HOME}/measurements/"*/ 2>/dev/null | tail -n +31 | while read -r old; do
  rm -rf "$old"
done

echo "=== done — $(date -Is), results in ${OUT}, stryker exit ${STATUS}"
exit "$STATUS"
