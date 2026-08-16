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

# The fuzz corpus is shared with the GitHub runner through release assets.
#
# **The point is not speed, it is architecture.** This box is ARM64 and the runner is x64, so an
# input discovered on one is otherwise NEVER executed on the other — a fault that only manifests on
# one architecture stays unreachable from the machine whose fuzzer found the input that triggers it.
# No budget increase substitutes for pooling; more time on one architecture explores more of the
# same architecture.
#
# **Why a release asset and not an Actions cache.** A cache has no public endpoint and cannot be read
# from outside a runner, which rules it out as a shared store. A release asset on a public repo is
# readable by plain curl with no credential; only writing needs a token.
#
# **One asset PER TARGET, which diverges from the shape TcgDex wrote up, and deliberately.** They
# fuzz one target and publish one `corpus.tar.zst`. This project fuzzes four, and CI runs them as
# four PARALLEL matrix jobs — four jobs publishing one asset is a last-writer-wins race that would
# silently discard three targets' findings every night. Per-target assets make each job the sole
# writer of its own, so there is no race to lose.
GH_REPO="PinKushin/Tf2DemoSalvage"
CORPUS_TAG="fuzz-corpus"
CORPUS_TOKEN_FILE="${HOME}/.tf2demosalvage-gh-token"

# Fold the shared corpus for one target into its local directory.
#
# **A fetch failure is never fatal.** The local corpus and the generated seed still make the run
# useful; it simply starts further back. The first run has nothing to fetch, and saying so plainly
# matters — "none published yet" and "the network broke" must not read the same.
fetch_shared_corpus() {
  local target="$1" dir="$2" url before
  url="https://github.com/${GH_REPO}/releases/download/${CORPUS_TAG}/corpus-${target}.tar.zst"

  if ! curl -fsSL -o "/tmp/shared-${target}.tar.zst" "$url"; then
    echo "    ${target}: nothing published yet, or the fetch failed; using the local corpus only"
    return 0
  fi

  rm -rf "/tmp/shared-${target}" && mkdir -p "/tmp/shared-${target}"

  if tar -C "/tmp/shared-${target}" -xf "/tmp/shared-${target}.tar.zst" 2>/dev/null; then
    before=$(find "$dir" -type f | wc -l)
    # -n so a local input of the same name is never overwritten: the local one may be the
    # reproducer for something this box found and has not published.
    cp -n "/tmp/shared-${target}"/* "$dir/" 2>/dev/null || true
    echo "    ${target}: merged ${before} -> $(find "$dir" -type f | wc -l) inputs"
  else
    echo "    WARNING: ${target}'s asset did not unpack; using the local corpus only" >&2
  fi

  rm -rf "/tmp/shared-${target}" "/tmp/shared-${target}.tar.zst"
}

# Replace one target's release asset with the current corpus.
#
# The upload endpoint REFUSES a duplicate asset name rather than replacing it, so the old asset is
# deleted first. Every HTTP status is checked: a failed publish that printed success would leave the
# two machines quietly diverging, which is the exact problem this exists to solve.
publish_shared_corpus() {
  local target="$1" dir="$2" id old code cfg

  # **The token goes in a curl config file, never on the command line.** Arguments are visible in
  # `ps` to every process on this box, and three projects share it as the same user. Trap-cleaned so
  # a failure part-way cannot leave a readable token behind.
  cfg=$(mktemp)
  chmod 600 "$cfg"
  trap 'rm -f "$cfg"' RETURN
  printf 'header = "Authorization: Bearer %s"\n' "$(cat "$CORPUS_TOKEN_FILE")" > "$cfg"
  local auth=(--config "$cfg" -H "Accept: application/vnd.github+json")

  tar -C "$dir" -cf - . | zstd -19 -T0 -q -o "/tmp/publish-${target}.tar.zst" -f

  id=$(curl -fsS "${auth[@]}" \
    "https://api.github.com/repos/${GH_REPO}/releases/tags/${CORPUS_TAG}" | jq -r '.id // empty')
  [ -n "$id" ] || { echo "    publish: release ${CORPUS_TAG} not found" >&2; return 1; }

  old=$(curl -fsS "${auth[@]}" \
    "https://api.github.com/repos/${GH_REPO}/releases/${id}/assets" \
    | jq -r ".[]|select(.name==\"corpus-${target}.tar.zst\")|.id")
  [ -n "$old" ] && curl -fsS -X DELETE "${auth[@]}" \
    "https://api.github.com/repos/${GH_REPO}/releases/assets/${old}" >/dev/null

  code=$(curl -s -o "/tmp/publish-${target}.json" -w '%{http_code}' -X POST "${auth[@]}" \
    -H "Content-Type: application/zstd" --data-binary "@/tmp/publish-${target}.tar.zst" \
    "https://uploads.github.com/repos/${GH_REPO}/releases/${id}/assets?name=corpus-${target}.tar.zst")
  rm -f "/tmp/publish-${target}.tar.zst"

  if [ "$code" != "201" ]; then
    echo "    publish ${target}: HTTP ${code} -- $(jq -r '.message // "no message"' "/tmp/publish-${target}.json")" >&2
    return 1
  fi
  echo "    ${target}: published $(find "$dir" -type f | wc -l) inputs"
}

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

# `corpus` mutation is OFF, and refuses rather than warns.
#
# Coverage capture cannot succeed for that project: Stryker's Microsoft.Testing.Platform runner
# talks to its test server over JSON-RPC with a hard 180-second limit, and the instrumented corpus
# suite takes about 6 m 18 s. The call is cancelled every time, so Stryker falls back to running
# the whole suite for every mutant - which is the measured 18 h 07 m run, and it produces a 100 %
# score made of 1142 timeouts rather than kills. See RISKS.md B34.
#
# A refusal rather than a note, because the failure is expensive and looks like a result. Someone
# reading a 100 % score has no way to tell it apart from a real one, and the run costs three
# quarters of a day of a shared box on the way there.
#
# ALLOW_CORPUS_MUTATION=1 overrides it for anyone deliberately re-measuring.
#
# Two ways this lifts for real, and the second is the better one:
#
#   - Split the corpus project so each capture fits inside 180 s. Fixes the schedule.
#   - Cover the corpus-only paths with SYNTHETIC tests in Tf2DemoSalvage.Core.Tests, which
#     captures coverage fine and now mutates in 22 minutes. Fixes the harness instead.
#
# The second is worth preferring on its own merits, not just for speed. A corpus test can only
# exercise the paths its ten demos happen to take, so it is a poor mutation harness at any
# runtime - which is the same argument docs/memory/tests-before-codecs.md already makes about
# corpus tests not substituting for unit tests.
if [ "$MODE" = corpus ] && [ -z "${ALLOW_CORPUS_MUTATION:-}" ]; then
  echo "ERROR: corpus mutation is disabled - coverage capture cannot succeed (RISKS.md B34)." >&2
  echo "       The instrumented suite exceeds Stryker's 180s test-server RPC limit, so every" >&2
  echo "       mutant runs the whole suite: 18h07m for a score that is 1142 timeouts." >&2
  echo "       Mutate 'core' instead. Set ALLOW_CORPUS_MUTATION=1 to override deliberately." >&2
  exit 2
fi

if [ "$PULL" != "--no-pull" ] && [ -z "${RUNNER_REEXECED:-}" ]; then
  # GIT_LFS_SKIP_SMUDGE on the reset, then an EXPLICIT pull only when the demos are wanted.
  #
  # `git reset --hard` is a checkout, so it runs the LFS smudge filter, and the filter DOWNLOADS
  # any object it does not have cached. That makes every mode pay LFS bandwidth for the corpus,
  # including `core`, which never opens a demo - directly contradicting the reason `core` was
  # given NEEDS_CORPUS=0 in the first place.
  #
  # It is not costing anything today only because the cache is already warm: `.git/lfs` on the box
  # is 314 MB across 16 objects, so the reset materialises from disk. The bill arrives the first
  # time a demo is ADDED - a `core` run would fetch it silently, and the free tier is 1 GiB a
  # month against a corpus whose history is already 305 MB.
  #
  # Skipping the smudge leaves pointer stubs, which is correct for the synthetic modes and
  # harmless for the others: the explicit `git lfs pull` below restores real content, and the
  # size check after it is what proves the restore happened.
  GIT_LFS_SKIP_SMUDGE=1 git fetch --quiet origin main
  GIT_LFS_SKIP_SMUDGE=1 git reset --quiet --hard origin/main
  # Demos live in Git LFS. Without this the working tree holds ~130-byte pointer stubs and every
  # corpus test degrades to a passing no-op — RISKS B20, as a shell step.
  [ "$NEEDS_CORPUS" = 1 ] && git lfs pull

  # RE-EXEC, because the reset above may have just rewritten THIS FILE while bash is reading it.
  #
  # bash does not load a script up front; it reads it lazily and remembers a byte OFFSET. Rewrite
  # the file underneath a running bash and it carries on reading at the old offset into new
  # content, so it silently skips or splices lines. Nothing reports an error — the run simply does
  # not do what the file on disk says.
  #
  # Caught 2026-08-12 by the fix that exposed it. A concurrency change had been merged and pushed,
  # the run reported the right SHA, and both the `concurrency:` echo and the `--concurrency` flag
  # were absent from the output: one test server, still single threaded, still on pace for 93
  # minutes. The commit was correct and deployed and the running script was a hybrid of two
  # versions.
  #
  # `exec` replaces the process and re-reads the new file from byte zero, so the second pass runs
  # exactly what was pulled. RUNNER_REEXECED stops it looping, and the lock survives because exec
  # keeps the open fd 9 — the same inheritance that makes `9>&-` necessary elsewhere works for us
  # here.
  echo "re-exec after pull, so the running script matches the pulled one"
  exec env RUNNER_REEXECED=1 bash "$REPO/build/run-measurements.sh" "$@"
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

# Keep the last 30 of THIS PROJECT'S runs, identified by a marker FILE, not by a name pattern.
#
# The glob approach was wrong twice and PBJ caught both, with live examples on the boxes:
#
#   - `*-tf2-*` misses this project's own older runs. `...-f910e8b-fuzz-container` and
#     `...-0a2960c-fuzz-bitreader` carry no `-tf2-` infix, so they would never be reaped.
#   - PBJ's natural own-glob is `*-fuzz/`, and our fuzz runs are named `<stamp>-<sha>-tf2-fuzz`,
#     which ENDS in `-fuzz`. A glob written to delete only PBJ's runs deletes ours.
#
# Worse, the verification suggested alongside that fix - check the scoped glob matches fewer
# directories than the unscoped one - PASSES in both broken cases. Both globs do match fewer, just
# not the right fewer, so the check is insensitive to the defect it was written to catch. A name is
# a guess about a naming convention; a marker is a fact written by the run itself, and it does not
# drift when either project renames a mode.
#
# Unmarked directories are LEFT ALONE deliberately. The conservative failure is an old directory
# surviving; deleting on absence of a marker re-creates the original bug for any project that has
# not adopted one yet.
RUN_OWNER="tf2demosalvage"

prune_own_runs() {
  kept=0
  while IFS= read -r dir; do
    [ -n "$dir" ] || continue
    [ -f "${dir}.owner" ] || continue
    [ "$(cat "${dir}.owner" 2>/dev/null)" = "$RUN_OWNER" ] || continue

    kept=$((kept + 1))
    if [ "$kept" -gt 30 ]; then
      rm -rf "$dir"
    fi
  done <<PRUNE_LIST
$(ls -1dt "${HOME}/measurements/"*/ 2>/dev/null)
PRUNE_LIST
}

STAMP=$(date -u +%Y%m%dT%H%M%SZ)
SHA=$(git rev-parse --short HEAD)
OUT="$HOME/measurements/${STAMP}-${SHA}-tf2-${MODE}"
mkdir -p "$OUT"
# Written before any work, so a run killed halfway is still attributable and still prunable.
echo "$RUN_OWNER" > "${OUT}/.owner"

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

    # Fold in whatever the x64 runner has found before spending any budget, so this ARM64 run
    # actually executes those inputs.
    fetch_shared_corpus "$target" "$corpus_dir"

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

  # Minimise, then publish back so the runner starts from what this box learned.
  #
  # **Gated on a clean run, and that gate is the important part.** A run that found a crash must not
  # overwrite the shared corpus: minimising drops inputs that do not increase coverage, and a
  # crashing input is never in the corpus to begin with (libFuzzer only adds coverage-increasing
  # ones). Publishing after a finding would be tidying up around the evidence.
  #
  # Writing needs a credential, unlike reading. Without the token the run still succeeds and simply
  # does not publish — the box keeps its own corpus and nothing is lost.
  if [ "$FUZZ_STATUS" = 0 ]; then
    echo "=== minimising and publishing the shared corpus"
    for target in bitreader varint container snappy; do
      corpus_dir="$HOME/corpus-${target}"
      min_dir="${corpus_dir}-min"
      before_count=$(find "$corpus_dir" -type f | wc -l)

      rm -rf "$min_dir" && mkdir -p "$min_dir"
      TF2FUZZ_TARGET="$target" "$HOME/libfuzzer-dotnet" \
        --target_path="${FUZZ_OUT}/Tf2DemoSalvage.Fuzz" \
        -merge=1 "$min_dir" "$corpus_dir" 9>&- > "${OUT}/merge-${target}.log" 2>&1 || true

      # Only take the minimised result if it produced one. A merge that failed leaves an empty
      # directory, and publishing that would delete the shared corpus for every machine.
      if [ "$(find "$min_dir" -type f | wc -l)" -gt 0 ]; then
        rm -rf "$corpus_dir" && mv "$min_dir" "$corpus_dir"
        echo "    ${target}: ${before_count} -> $(find "$corpus_dir" -type f | wc -l) after merge"
      else
        rm -rf "$min_dir"
        echo "    ${target}: merge produced nothing; keeping the unminimised corpus" >&2
      fi

      if [ -r "$CORPUS_TOKEN_FILE" ]; then
        publish_shared_corpus "$target" "$corpus_dir" \
          || echo "    WARNING: ${target} publish failed; the local corpus is unaffected" >&2
      fi
    done

    [ -r "$CORPUS_TOKEN_FILE" ] || \
      echo "    no token at ${CORPUS_TOKEN_FILE}; fetched but did not publish"
  else
    echo "=== not publishing: the run found something, and the corpus stays as it was"
  fi

  prune_own_runs

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
# CONCURRENCY: use the whole box. Stryker defaults to cores/2, which on a 3-core machine is
# integer-divided down to 1 - so every mutation run this box has ever done was SINGLE THREADED,
# and nothing in the output says so. Measured 2026-08-12: the core run spent 22:49:14 to 00:23:23
# testing 1879 mutants, a flat 3.01 s each, while build, discovery, coverage capture and every
# compile-rollback cycle together took 88 seconds. The wall clock is per-mutant execution and
# nothing else, so it divides by concurrency almost exactly.
#
# The default is conservative because Stryker assumes a developer's machine that has to stay
# usable while it runs. This box is dedicated and serialised by the lock, so halving it buys
# nothing and costs 3x.
#
# Not put in stryker-config.json on purpose: that file is shared with local runs, where leaving
# the default is right for exactly the reason above.
STRYKER_CONCURRENCY="${STRYKER_CONCURRENCY:-$(nproc)}"
echo "concurrency: ${STRYKER_CONCURRENCY} of $(nproc) cores"

set +e
if [ -n "${MEASURE_TIMEOUT:-}" ]; then
  echo "hard limit: ${MEASURE_TIMEOUT}"
  timeout --signal=INT --kill-after=120 "$MEASURE_TIMEOUT" dotnet stryker --concurrency "$STRYKER_CONCURRENCY" 2>&1 9>&- \
    | tee "${OUT}/stryker.log"
else
  dotnet stryker --concurrency "$STRYKER_CONCURRENCY" 2>&1 9>&- | tee "${OUT}/stryker.log"
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

prune_own_runs

echo "=== done — $(date -Is), results in ${OUT}, stryker exit ${STATUS}"
exit "$STATUS"
