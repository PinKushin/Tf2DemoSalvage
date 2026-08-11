#!/usr/bin/env bash
#
# Run this project's fuzz targets on the shared measurement box.
#
#   bash tools/fuzz/run-box-fuzz.sh <target> [seconds] [--no-pull]
#
#   target: bitreader | varint | container | snappy
#   seconds: per-target time budget, default 300
#
# Mirrors PokemonBattleJournal's build/run-measurements.sh deliberately - same lock, same
# layout, same reasoning - because the box is shared and a second lock scheme is the single
# worst mistake available there (see the log entry from 2026-08-10). This is this project's
# own script rather than an edit to PBJ's, so ownership of crontab edits on the box stays
# with the PBJ agent per the house rules in ~/measurement-schedule.md.
set -euo pipefail

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
export MSBUILDDISABLENODEREUSE=1

WORKDIR="${TF2_DIR:-$HOME/tf2demosalvage}"

# THE SAME PATH PBJ USES. Not named after this project - the machine is what is being
# serialised, not the repo. Do not `rm` it: flock is on the open file description, and
# unlinking the inode while a run holds it creates a second, unguarded lock rather than
# freeing the first one.
LOCK="/tmp/measurement-box.lock"

TARGET="${1:-}"
shift || true

PULL=1
SECONDS_BUDGET=300
for a in "$@"; do
  case "$a" in
    --no-pull) PULL=0 ;;
    *) SECONDS_BUDGET="$a" ;;
  esac
done

case "$TARGET" in
  bitreader|varint|container|snappy) ;;
  *)
    echo "usage: $0 {bitreader|varint|container|snappy} [seconds] [--no-pull]" >&2
    exit 2
    ;;
esac

# Refuse rather than queue - a second concurrent run corrupts the first one's results, it
# does not just delay it. Every long-running command below closes fd 9 with 9>&- so a
# finished run cannot hold the lock through an inherited descriptor (MSBuild node-reuse and
# VBCSCompiler both outlive the build that spawned them; disabling node reuse above is the
# other half of that same fix).
exec 9>"$LOCK"
if ! flock -n 9; then
  echo "ERROR: another measurement run holds $LOCK. One at a time." >&2
  command -v fuser >/dev/null && { echo "held by:" >&2; fuser -v "$LOCK" >&2 2>&1 || true; }
  exit 1
fi

cd "$WORKDIR"

if [ "$PULL" -eq 1 ]; then
  git fetch --quiet origin
  git reset --quiet --hard origin/main
fi

SHA="$(git rev-parse --short HEAD)"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT="$HOME/measurements/${STAMP}-${SHA}-fuzz-${TARGET}"
mkdir -p "$OUT"

echo "==> fuzz ${TARGET} (${SECONDS_BUDGET}s) on ${SHA} at $(date '+%F %H:%M:%S %Z'), output -> ${OUT}"

rm -rf "${HOME}/fuzz-out-${TARGET}"
mkdir -p "${HOME}/fuzz-out-${TARGET}" "${HOME}/findings-${TARGET}" "${HOME}/corpus-${TARGET}"

dotnet publish tests/Tf2DemoSalvage.Fuzz -c Release -o "${HOME}/fuzz-out-${TARGET}" --nologo -v q 9>&-

# Seed the container target with a synthesized valid demo, now that the publish above has
# produced a DLL to run. Without a seed, mutation has to discover an 8-byte magic AND grow the
# input past the 1072-byte header purely by chance - measured on 2026-08-11: 12 million
# executions, coverage never moved past the header check.
#
# Not a copy from tools/corpus/demos: those are Git LFS pointer files on this box (no git-lfs
# installed here), and copying the pointer text would seed the fuzzer with "version
# https://git-lfs..." instead of a demo. TF2FUZZ_SEED_PATH makes the harness write its own seed
# with DemoWriter instead - the same object the deterministic property tests already build -
# which is also more honest about what "valid" means: the writer's own opinion of the format,
# not a byte blob nobody here can regenerate.
#
# Before instrumentation on purpose: writing a file exercises none of the branches SharpFuzz
# rewrites, so running this uninstrumented and running it after are equivalent, and doing it
# first keeps the seed step's failure mode independent of instrumentation ever going wrong.
if [ "$TARGET" = "container" ]; then
  TF2FUZZ_SEED_PATH="${HOME}/corpus-${TARGET}/seed" \
    dotnet "${HOME}/fuzz-out-${TARGET}/Tf2DemoSalvage.Fuzz.dll" 9>&-
fi

# Instrument Core, not the harness: coverage feedback has to come from the code under test
# or the fuzzer explores nothing while still reporting a clean run - see the "no proof
# instrumentation happened" check in the GitHub workflow, which exists for the same reason.
before=$(stat -c%s "${HOME}/fuzz-out-${TARGET}/Tf2DemoSalvage.Core.dll")
sharpfuzz "${HOME}/fuzz-out-${TARGET}/Tf2DemoSalvage.Core.dll" 9>&-
after=$(stat -c%s "${HOME}/fuzz-out-${TARGET}/Tf2DemoSalvage.Core.dll")
echo "Core.dll ${before} -> ${after} bytes"
if [ "$after" -le "$before" ]; then
  echo "ERROR: instrumentation did not change Core.dll - the fuzzer would explore nothing." >&2
  exit 1
fi

TF2FUZZ_TARGET="$TARGET" "${HOME}/libfuzzer-dotnet" \
  --target_path="$(which dotnet)" \
  --target_arg="${HOME}/fuzz-out-${TARGET}/Tf2DemoSalvage.Fuzz.dll" \
  "${HOME}/corpus-${TARGET}" \
  -max_total_time="${SECONDS_BUDGET}" \
  -print_final_stats=1 \
  -artifact_prefix="${HOME}/findings-${TARGET}/" \
  2>&1 9>&- | tee "${OUT}/fuzz.log"

# A crash is written as the exact bytes that produced it - a regression fixture, not a bug
# report. Kept in the durable ~/findings-<target>/ location as well as the pruned run dir.
cp -r "${HOME}/findings-${TARGET}" "${OUT}/" 2>/dev/null || true
ls -1 "${HOME}/findings-${TARGET}" 2>/dev/null | head || echo "no findings"

# Same retention as PBJ's runner: last 30 runs per this script, findings kept separately.
ls -1dt "${HOME}/measurements/"*-fuzz-"${TARGET}"/ 2>/dev/null | tail -n +31 | while read -r old; do
  rm -rf "$old"
done

echo "==> done: ${OUT}"
