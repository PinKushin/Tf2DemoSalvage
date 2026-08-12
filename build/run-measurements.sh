#!/usr/bin/env bash
#
# Run one long measurement workload on the shared measurement box.
#
#   bash build/run-measurements.sh corpus            [--no-pull]
#   bash build/run-measurements.sh core              [--no-pull]
#   bash build/run-measurements.sh fuzz [seconds]    [--no-pull]
#
# `fuzz` runs on fuzz-box, the other three on mutation-box. The split is by WORKLOAD, not by
# project — that is the whole convention these boxes are organised under.
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

# `fuzz` takes an optional budget between the mode and the flag, so the flag is not always $2.
# Detected by shape rather than by position: a run is either given a number or it is not.
if [ "${2:-}" -eq "${2:-}" ] 2>/dev/null; then
  LONG_TARGET_SECONDS="$2"
  PULL="${3:-}"
else
  LONG_TARGET_SECONDS=14400
  PULL="${2:-}"
fi
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

# Mode is resolved before the pull, because it decides whether the demos are needed at all.
# `core` is entirely synthetic after the D25 split — it does not open a single demo — so making it
# depend on Git LFS gives it a way to fail that has nothing to do with what it measures.
case "$MODE" in
  corpus)  PROJECT="tests/Tf2DemoSalvage.Corpus.Tests"; NEEDS_CORPUS=1 ;;
  cli)     PROJECT="tests/Tf2DemoSalvage.Cli.Tests";    NEEDS_CORPUS=1 ;;
  core)    PROJECT="tests/Tf2DemoSalvage.Core.Tests";   NEEDS_CORPUS=0 ;;
  # Fuzzing builds Core and the harness and never opens a demo, so it takes no LFS bandwidth.
  # Same reasoning as `lfs: false` in .github/workflows/fuzz.yml, for the same reason.
  fuzz)    PROJECT="tests/Tf2DemoSalvage.Fuzz";         NEEDS_CORPUS=0 ;;
  *) echo "ERROR: unknown mode '$MODE'. Expected corpus, core, cli or fuzz." >&2; exit 2 ;;
esac

if [ "$PULL" != "--no-pull" ]; then
  git fetch --quiet origin main
  git reset --quiet --hard origin/main
  # Demos live in Git LFS. Without this the working tree holds ~130-byte pointer stubs and every
  # corpus test degrades to a passing no-op — RISKS B20, as a shell step.
  [ "$NEEDS_CORPUS" = 1 ] && git lfs pull
fi

if [ "$NEEDS_CORPUS" = 1 ]; then
  smallest=$(find tools/corpus/demos -name '*.dem' -printf '%s\n' 2>/dev/null | sort -n | head -1)
  if [ -z "$smallest" ] || [ "$smallest" -le 4096 ]; then
    echo "ERROR: corpus is missing or is LFS pointer stubs (smallest ${smallest:-none} bytes)." >&2
    exit 1
  fi
else
  smallest="n/a (synthetic project)"
fi

STAMP=$(date -u +%Y%m%dT%H%M%SZ)
SHA=$(git rev-parse --short HEAD)
OUT="$HOME/measurements/${STAMP}-${SHA}-tf2-${MODE}"
mkdir -p "$OUT"

echo "=== tf2demosalvage ${MODE} @ ${SHA} — $(date -Is)"
echo "smallest demo: ${smallest}"

if [ "$MODE" = fuzz ]; then
  # Budgets are PER TARGET and deliberately unequal, because the two kinds of target reward time
  # differently. docs/FUZZING.md measured bitreader and varint saturating at roughly 15,000
  # executions: `ft:` stops moving and the remaining runtime finds nothing, so those two are a
  # regression guard rather than a search and get a few minutes. container and snappy each read a
  # length out of the data and let it drive further reads or an allocation, which is the shape
  # that keeps paying — both found real bugs on their first deterministic run — so they get hours.
  # Overridable only so the mode itself can be smoke-tested on the box in a couple of minutes.
  # A real run leaves it alone; a scheduled run that quietly used the smoke value would be the
  # "reads like a measurement, measures nothing" failure this repo keeps running into.
  SHORT_TARGET_SECONDS="${FUZZ_SHORT_SECONDS:-600}"
  FUZZ_OUT="$HOME/tf2-fuzz-out"

  rm -rf "$FUZZ_OUT"
  # $PROJECT, not `.`: the Stryker modes cd into their project and this block runs before that,
  # with the repo root still current.
  dotnet publish "$PROJECT" -c Release -o "$FUZZ_OUT" --nologo -v q 9>&-

  # Instrument Core, not the harness: the coverage feedback has to come from the code under test
  # or the fuzzer explores nothing. Publishing un-instruments, so this runs after it, never before.
  before=$(stat -c%s "${FUZZ_OUT}/Tf2DemoSalvage.Core.dll")
  sharpfuzz "${FUZZ_OUT}/Tf2DemoSalvage.Core.dll" 9>&-
  after=$(stat -c%s "${FUZZ_OUT}/Tf2DemoSalvage.Core.dll")
  echo "instrumented Core.dll: ${before} -> ${after} bytes"
  # The only proof instrumentation happened. Without it the fuzzer runs happily at full speed and
  # explores one path, which is indistinguishable from a clean run in every other output it gives.
  if [ "$after" -le "$before" ]; then
    echo "ERROR: sharpfuzz did not grow Core.dll - the run would explore nothing." >&2
    exit 1
  fi

  FUZZ_STATUS=0
  for target in bitreader varint container snappy; do
    case "$target" in
      container|snappy) budget="$LONG_TARGET_SECONDS" ;;
      *)                budget="$SHORT_TARGET_SECONDS" ;;
    esac

    corpus_dir="$HOME/corpus-${target}"
    findings_dir="$HOME/findings-${target}"
    mkdir -p "$corpus_dir" "$findings_dir"

    # The container target's input opens with an 8-byte magic and a 1072-byte fixed header before
    # anything varies, and mutation from an empty corpus does not cross that reliably. The seed is
    # generated by the harness from DemoWriter rather than copied from tools/corpus — this box
    # never fetches the demos.
    if [ "$target" = container ] && [ -z "$(ls -A "$corpus_dir")" ]; then
      echo "seeding ${target}"
      TF2FUZZ_SEED_PATH="${corpus_dir}/seed" dotnet "${FUZZ_OUT}/Tf2DemoSalvage.Fuzz.dll" 9>&-
    fi

    before_count=$(find "$corpus_dir" -type f | wc -l)
    echo "=== ${target}: ${budget}s, corpus ${before_count} entries — $(date -Is)"

    # TF2FUZZ_CRASH_DIR is what actually preserves a reproducer here; libFuzzer's own
    # -artifact_prefix writes nothing in this setup (see the harness's Preserving()).
    # -artifact_prefix stays set anyway so that if a future toolchain does start writing
    # artifacts, they land beside ours rather than in the working directory.
    set +e
    TF2FUZZ_TARGET="$target" TF2FUZZ_CRASH_DIR="$findings_dir" "$HOME/libfuzzer-dotnet" \
      --target_path="${FUZZ_OUT}/Tf2DemoSalvage.Fuzz" \
      -max_total_time="$budget" \
      -artifact_prefix="${findings_dir}/" \
      -print_final_stats=1 \
      "$corpus_dir" 2>&1 9>&- | tee "${OUT}/fuzz-${target}.log"
    status=${PIPESTATUS[0]}
    set -e

    after_count=$(find "$corpus_dir" -type f | wc -l)
    echo "${target}: corpus ${before_count} -> ${after_count}, exit ${status}"

    # A corpus that never grows across runs is the tell that instrumentation broke, and it is
    # worth seeing even though it is not by itself an error — a saturated target legitimately
    # stops growing, which is exactly what bitreader and varint are expected to do.
    if [ "$after_count" = "$before_count" ]; then
      echo "note: ${target} found no new input this run (expected once a target saturates)."
    fi

    # A crash MUST leave a reproducer behind. If it did not, the run found a defect and lost the
    # only thing that makes it actionable, and that has to be visible in the log rather than
    # inferred later from an empty directory.
    #
    # An earlier version of this block tried to recover the input by replaying the corpus one
    # entry at a time. That does not work and the reason is worth keeping: libFuzzer adds only
    # coverage-increasing inputs, so an input that crashes is never added. Measured directly —
    # replaying all 26 corpus entries against a target that had just crashed isolated nothing,
    # because the crash arrived on the first mutated input after `#27 INITED`. The harness writes
    # the bytes itself now, which is the only place they provably exist.
    if [ "$status" != 0 ]; then
      saved=$(find "$findings_dir" -name 'crash-*.bin' | wc -l)
      echo "${target}: exit ${status}, ${saved} reproducer(s) saved"
      if [ "$saved" = 0 ]; then
        echo "WARNING: ${target} exited ${status} but saved no reproducer." \
             "The finding is in the log only - check TF2FUZZ_CRASH_DIR is reaching the harness." >&2
      fi
    fi

    [ "$status" != 0 ] && FUZZ_STATUS="$status"
  done

  # A crash artifact is the exact bytes that caused it — a regression fixture, not a bug report.
  # Copied into the run directory as well as kept in ~/findings-*, because the run directory is
  # pruned to the last 30 and the findings are the part that must not be lost.
  for target in bitreader varint container snappy; do
    found=$(ls -1 "$HOME/findings-${target}" 2>/dev/null | wc -l)
    [ "$found" = 0 ] && continue
    echo "FINDINGS: ${target} has ${found} crash artifact(s)."
    cp -r "$HOME/findings-${target}" "${OUT}/" 2>/dev/null || true
  done

  ls -1dt "${HOME}/measurements/"*/ 2>/dev/null | tail -n +31 | while read -r old; do
    rm -rf "$old"
  done

  echo "=== done — $(date -Is), results in ${OUT}, fuzz exit ${FUZZ_STATUS}"
  exit "$FUZZ_STATUS"
fi

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
#
# MEASURE_TIMEOUT bounds a run that might not fit its window. `corpus` is the case it exists for:
# the local figure times the box's measured ~8x puts it near 12 hours against an 11-hour gap
# between PBJ's evening finish and its 07:00 start, and a run that overruns does not fail — it
# holds the lock and silently skips a neighbour's job. Exceeding the bound answers the sizing
# question by itself, so the bound is the measurement, not a safety net.
#
# SIGINT rather than SIGTERM: Stryker writes its report on interrupt, so a bounded run still says
# how far it got. `--kill-after` covers it ignoring that.
set +e
if [ -n "${MEASURE_TIMEOUT:-}" ]; then
  echo "hard limit: ${MEASURE_TIMEOUT}"
  timeout --signal=INT --kill-after=120 "$MEASURE_TIMEOUT" dotnet stryker 2>&1 9>&- \
    | tee "${OUT}/stryker.log"
else
  dotnet stryker 2>&1 9>&- | tee "${OUT}/stryker.log"
fi
STATUS=${PIPESTATUS[0]}
set -e

# 124 is `timeout`'s own code for "the limit was reached", and it must not be read as a Stryker
# result: the run was cut off, so whatever score is in the log covers only part of the mutant set.
# A truncated Stryker run prints "All mutants have been tested" and a plausible percentage anyway
# — that is how 37.74% was once reported from 1215 of 1954 mutants.
if [ "$STATUS" = 124 ]; then
  echo "TIMED OUT after ${MEASURE_TIMEOUT}. Any score below is over a PARTIAL mutant set." >&2
fi

# The score line and the survivors are what a reader wants; the full log stays in the run dir.
grep -E "mutation score|Killed:|Survived:|Timeout:|No Coverage" "${OUT}/stryker.log" \
  | tail -8 > "${OUT}/summary.txt" || true
cat "${OUT}/summary.txt" 2>/dev/null || true

# THE COUNT, not just the score. A percentage alone cannot say whether the run finished.
#
# On 2026-08-10 a run of this project printed "All mutants have been tested, and your mutation
# score has been calculated" and 37.74% having accounted for 1215 of 1954 mutants. The figure was
# internally consistent with the subset it held, so nothing looked wrong, and it was believed and
# acted on. The same shape as a test runner reporting `Passed! - 630` against a 646-test suite:
# the verdict is fine and the COUNT is the tell.
#
# Zero is the case that must shout. A scoped run over code nobody touched legitimately produces no
# mutants and a clean report in seconds, which at a glance is identical to a misconfigured `since`
# target that mutates nothing at all. Success and "nothing was measured" have to look different.
planned=$(grep -oE '[0-9]+[[:space:]]+total mutants will be tested' "${OUT}/stryker.log" \
  | tail -1 | grep -oE '^[0-9]+') || true
accounted=$(awk '/^(Killed|Survived|Timeout):[[:space:]]/ { total += $2 } END { print total + 0 }' \
  "${OUT}/stryker.log")

echo "mutants: ${accounted} accounted, ${planned:-unknown} planned"
if [ "$accounted" = 0 ]; then
  echo "ERROR: zero mutants tested. This is NOT a pass - nothing was measured." >&2
  [ "$STATUS" = 0 ] && STATUS=3
elif [ -n "${planned:-}" ] && [ "$accounted" -lt "$planned" ]; then
  # Two causes, and the size tells them apart. RuntimeError mutants are counted as planned but
  # appear in no status line the cleartext reporter prints, so this gap is the ONLY evidence they
  # happened - a small number here is that, and is worth seeing. A gap of hundreds is a run that
  # stopped early, and then the score below is over a partial set. Measured examples: 2 of 957 on
  # a healthy run, 739 of 957 on a truncated one.
  echo "GAP: $((planned - accounted)) of ${planned} mutants in no status line." \
       "A few means RuntimeError mutants, which are reported nowhere else." \
       "Many means the run stopped early and the score covers a partial set." >&2
fi

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
