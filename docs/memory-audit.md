# Memory audit: proposal for the owner (2026-10-19)

**This is a proposal. Nothing in `docs/memory/`, the index or the auto-memory directory was changed.** Base: commit
`cd35ee02` (compacted index, 16,197 bytes, limit 17,100). Rule applied: the owner's 2026-10-19 direction (delete what a
later memory or a numbered decision fully supersedes; keep wrong turns and what killed them, everything about how the TF2
client or Valve's engine behaves, the owner's corrections in his words, and process rules still in force).

**Summary.** 260 repo files (every file except `MEMORY.md`, `README.md` included) and 11 auto-memory-only files were read
in full. The memory is lean already: almost every file is a lesson, a wrong turn or a measurement of the client, and stays.
**10 files are clean deletes** (a findings document, a fixed risk, a reversed or stated decision, or the global CLAUDE.md
owns them completely). **12 more are owned by a numbered decision but are process rules still in force**, so deleting them
removes their only always-loaded trigger: that is one batch question, not twelve deletes. **16 small files fold into 10
hosts.** **5 files carry a claim that is now false or dangling.** Index saving: about 1.5 KB for the clean deletes and
merges (16,197 to about 14.7 KB), about 2.1 KB if the batch question is answered "delete" (to about 14.0 KB).

**Two-copy drift found first (read this before executing anything).** `diff -rq --exclude=README.md docs/memory <auto dir>`
shows 80 files that differ, but 70 of those are only frontmatter formatting and restamps. The real body differences:

| File | Which copy is newer | What the newer copy has |
|---|---|---|
| `review-for-recall` | auto | "NEVER Opus, hook-enforced in block-expensive-subagents.ps1", owner 2026-10-15; repo says "Fable or Opus" |
| `tf2-is-a-shared-fixture` | auto | owner 2026-10-16: quit TF2 as soon as a task is done (memory pressure, hours); repo says leave it up |
| `one-subagent-and-prefer-cheap-models` | auto | new section "2026-10-15: any model may plan" |
| `where-the-game-and-clients-live` | auto | public beta thread row |
| `a-hook-is-not-live-until-a-control-fires-it`, `parallel-agents-lock-the-gate-too`, `valve-shape-beyond-parity` | auto | wording only (path, a D-number, one sentence) |
| `mutation-box-gets-maps-never-demos`, `mutation-runs-go-to-the-box` | repo | the "box is gone (D212)" banner |
| `MEMORY.md` | auto | one extra line for `a-worktree-copies-gcor-demos-never-lfs-pull` (auto index 16,304 bytes) |

So every consolidation below must start from the newer body of each file, and every edit must land in **both** copies
(the README rule). The auto copy also has a `README.md` and 11 files the repo lacks (table at the end).

## Counts

| Class | Repo files | Auto-only files |
|---|---|---|
| DELETE (clean) | 10 | 0 |
| ASK: delete if the owner accepts "decision number only" | 12 | 0 |
| MERGE (sources folded into a host; hosts keep their name) | 16 sources, 10 hosts | 0 |
| STALE, correct in place | 5 | 0 |
| ASK, other | 4 | 4 (move to global memory) |
| KEEP | 213 (nine of them are merge hosts) | 7 |

## 1. DELETE (clean): nothing in force loses its trigger

"Owner" is the document that carries the content. Each row was checked by reading the owner, not by name.

| File | Why | Owner (verified) | Links to repoint |
|---|---|---|---|
| `rename-decompiled-functions` | D174 was reversed 2026-09-14 and the memory only says so; the practice is retired | `DECISIONS.md` D174 (8825-8873): both owner quotes, the reversal, "existing renames stay, case by case" | `DECISIONS.md:8868` cites the path (drop the cite); `engine-reader-before-implementer.md:26` `[[rename-decompiled-functions]]` becomes D174 |
| `sticking-friction-is-out-of-scope` | A stated divergence, not an open item | D175 (8875-8904) holds the five reads, the always-false gate and the "reopen only for a body-against-body port" condition; it also cites HANDOFF item 3 | none |
| `a-whole-timeline-can-leak-the-future` | Its own description says FIXED by B548; it was kept "for the pattern" | `RISKS.md` B548 (39703+): cause, fix, "the window was right"; D217 step 3 (`DECISIONS.md:10215`) holds the last-write-wins scalars | `RISKS.md:39707` cites the path (drop it); index line 64 |
| `tf2-barely-uses-dynamic-lights` | Digest of finding 66, which it names | `findings/66` (rows 60-66: the FIXME, the elight overrides, the Dragon's Fury allocator; line 75: ten gcor plus nine lcor demos = the "19 demos", line 78: zero lights) | `RISKS.md:37669` `docs/memory/tf2-barely-uses-dynamic-lights.md stands` becomes `findings/66` |
| `the-hud-draws-at-tone-map-scale-one` | Digest of finding 78 | `findings/78` (~114-117: `CViewRender::Render` resets the scale, `ToneMapScaleAfterPostProcessing`, the logged scale) | index only |
| `unlitgeneric-has-no-detailtint` | Digest of finding 78 | `findings/78` line 41: `SetupVars` never assigns `m_nDetailTint`, memset -1, unlitgeneric_dx9.cpp:72-89 | index only |
| `water-lod-lives-in-a-material-proxy` | Digest of finding 71 | `findings/71` lines 14-20 and 47 (the `WaterLOD` proxy, the `$forceexpensive` default set by `SHADER_INIT_PARAMS`) | index only |
| `a-tf2-face-is-the-scene` | Digest of finding 75 | `findings/75` lines 9-36 (the excluded send props, `studio.cpp:1584`, `EXPRESSION` events, `.vfe` path) | index only |
| `a-model-names-itself` | One finding-75 line | `findings/75` line 88 (phonemes named from `pszName`) and line 65 (`ResetFlexWeights` rests at zero in range) | index only |
| `nunit-over-xunit-never-deprecated-packages` | Restates the global CLAUDE.md section word for word (including both "looks like diligence" habits), and its claim "Tf2DemoSalvage is on xUnit, being upgraded to v3" is **false** (Core.Tests csproj:30: migrated off xunit.v3 2026-08-12; `Directory.Packages.props` pins NUnit 5) | global `~/.claude/CLAUDE.md` "Test Frameworks" + `block-standards-violations.ps1` header | index only |

Index bytes freed by this table: 590.

## 2. ASK (one batch question): twelve process rules owned by a decision number

All twelve are fully restated by a numbered decision, and the task rule says that is a delete. They are also rules the owner
named as still in force, and `DECISIONS.md` is read by number, never loaded: the index line is the only always-loaded
trigger. **Question for the owner: delete all twelve and keep one `index-notes.md` line of decision pointers (cheap), delete
without the pointers, or keep them?** Default if unanswered: keep. Index bytes freed: 642 (about 520 if the pointer line is kept).

| File | Owner | Sentence missing from the owner (move it first, or lose it) |
|---|---|---|
| `american-english-for-new-text` | D158 (8317-8335), complete | none (also drop the `index-notes.md` line 8) |
| `keep-packages-current` | D191 (9244-9264), complete | none; `runner-migration-notices-are-not-defects.md:11` repoints to D191 |
| `ci-gates-are-soft-for-now` | D195 (9331+), complete | none |
| `no-known-protocol-list` | D211 (9601-9617), complete | none |
| `a-demo-without-its-map-fails-soft` | D224 (10793-10822), complete | none |
| `auto-exposure-settles` | D192 (9265-9276), complete | none |
| `interp-is-the-watchers-setting` | D190 (9231-9242), complete | none |
| `no-unit-test-opens-a-real-demo` | D213 (9680-9712), complete incl. the maps-and-models quotes and shared inputs | "say this in every implementer brief" (process line); `nunit-shared-fixture-is-the-standard.md:40` repoints to D213 |
| `users-never-touch-environment-variables` | D210 (9551-9599) | "Docs trap: README and RELEASE-NOTES said set TF2_FOLDER; any user doc naming an env var is a bug unless it says scripts only" is **not** in D210 |
| `debug-is-what-the-owner-runs` | D176 (8906-8925) | "say which build a frame rate came from" and the exact `dotnet-trace collect --profile dotnet-sampled-thread-time --format Speedscope -- <exe> <args>` are **not** in D176; `a-measure-needs-focus.md:35` repoints |
| `unread-by-production-is-dead` | D180 (9006-9018) | "restart the LSP after deletions; its index goes stale" is not in D180; `an-unused-method-may-be-the-engines.md:64` repoints |
| `a-picked-option-is-not-a-voiced-decision` | D183 (9074-9091) | "before offering a menu, check it contains the engine's own answer and what the rule bounds" is not in D183 (it survives in `a-parity-rule-may-bound-the-cost`) |

## 3. MERGE: lessons, owner quotes and facts all move; each host keeps its filename

Each source becomes a section of the host whose heading carries the slug in backticks (`## \`slug\` ...`), so
`host.md#slug` stays citable (the fold convention in `one-place-or-it-drifts`). "Body from" says which copy is newer.

| Host (kept) | Sources folded in | Body from | Links to repoint |
|---|---|---|---|
| `gate-once-per-batch` | `gate-once-per-merge-not-per-commit`, `parent-branch-defers-the-gate` | repo | `full-corpus-at-most-once-a-day`, `push-when-work-piles-up` link to the first; keep the `celt.dll`/`cp -n` fact and both owner quotes |
| `take-the-desktop-lock-dont-defer` | `one-run-at-a-time`, `announce-before-launching`, `my-own-processes-are-mine`, `a-lock-holder-must-time-itself-out` | repo | `parallel-agents-lock-the-gate-too` (one-run), `a-with-switch-expression-can-hang-the-compiler` (my-own). `a-gui-exe-does-not-hold-the-lock` stays separate: `.claude/skills/tf2-run/SKILL.md:10` pins its path |
| `lcor-is-not-in-a-worktree` | `a-worktree-lcor-is-a-junction` | repo (auto differs by one blank line) | `check-the-worktree-before-every-git-command`; external pins stay valid (CLAUDE.md:27, `build/run-local-mutation.ps1:40`, two UI tests). Candidate third section: the auto-only `a-worktree-copies-gcor-demos-never-lfs-pull` |
| `an-entity-index-does-not-name-a-track` | `a-handle-resolves-through-its-serial` | repo | `tests/Tf2DemoSalvage.Core.Tests/Scene/ActiveWeaponHandleTests.cs:11` |
| `mutation-box-gets-maps-never-demos` | `mutation-runs-go-to-the-box` (stale body: prescribes the gone box) | repo (has the D212 banner) | `DECISIONS.md:9114` stays valid; rewrite the "verify on mutation-box" lines to `.github/workflows/mutation.yml`, keep the owner quote about "extremely narrow scoped" |
| `tf2-is-a-shared-fixture` | `record-specimens-with-the-tf2-mcp` | **auto** (has "quit it when a task is done") | `tools/specimen/tf2ds_specimen.cfg:7`, `download-every-demo-found` |
| `review-subagents-deeply` | `read-the-diff-before-merging-agent-work`, `review-for-recall` | **auto** for `review-for-recall` | `credits-queue` links `review-for-recall` |
| `never-stop-a-working-subagent` | `no-interim-status-replies` | repo (auto differs by quoting only) | `agents-ask-the-owner-client-questions` links `no-interim-status-replies`; `never-use-fable` links the host |
| `take-your-own-screenshot` | `take-the-golden-shot-dont-ask`, `delete-captures-after-measuring` | repo | `.claude/skills/*/SKILL.md` pin the host only |
| `push-when-work-piles-up` | `verify-origin-main-after-push` | repo | none |

Index bytes freed: 876 (the hosts keep their lines).

## 4. STALE: false or dangling claims, correct in place

| File | What is false or dangling | Action |
|---|---|---|
| `README.md` | `[[a-fold-leaves-its-paths-behind]]` has no file; it is now a section of `one-place-or-it-drifts` | point to `one-place-or-it-drifts.md#a-fold-leaves-its-paths-behind` |
| `log-findings-to-the-measurement-box-log` | `[[no-paid-compute-oracle-dropped]]` exists only in the auto copy, so the repo link dangles | mirror that file into the repo, or cite D212 instead |
| `mutation-score-is-not-the-goal` | "Where runs land": `~/measurements/...` on mutation-box, "scheduled daily"; the box is gone (D212) | replace with a pointer to `.github/workflows/mutation.yml`; the rest stays |
| `rewind-is-ours-parity-first` | "restore instant rewind with checkpoints after parity": D217 has since built checkpoints plus decode-forward | add a status line naming D217; keep the owner quotes and the "say so the moment a change regresses a capability" miss |
| `lcor-is-not-in-a-worktree` | "the main pool is 49" demos; `F:\Tf2DemoSalvage-lcor` now lists 64 entries (not all necessarily demos; I counted names, not demos) | re-measure by demo count or drop the number |

## 5. ASK, other

1. **`stryker-safe-mode-needs-an-inline-comment`**: `tests/STRYKER-NOTES.md` (222-283) now owns the CS1620 / `is not { }`
   shapes, "config cannot stop it", `disable once all`, character spans. Not in it: the `try/catch` and `while (true)`
   shapes, the A/B proving `all` over the bare form, "the log names only the first trigger per method", "add the comment in
   the same change (grep the diff)", "recovered methods made a run 27 min to 6 h 14", "count `Safe Mode!` not `Safe Mode`".
   Move those six into STRYKER-NOTES then delete? Or keep.
2. **`no-task-cards`**: D169/D171 hold the owner quotes and the first-mate model. Not in them: a subagent called
   `spawn_task` itself (owner quote), and "grep the memory directory for the rule itself, not only the index loaded at
   start". The agent system prompts now carry "never call spawn_task". Delete after moving those two lines to D169?
3. **`edit-files-with-the-file-tools`**: its first half repeats global CLAUDE.md and the `prefer-edit-tools.ps1` header; the
   sections on `replace_all` as a claim about every site, doc comments stranded by inserting above a signature, the C#-not-
   Python preference, and the `sed` reflex are unique. Keep whole, or trim the first half?
4. **Subagent cap is spread over three files and they disagree**: `one-subagent-and-prefer-cheap-models` ("2026-10-18: 2
   agents at most", auto copy adds "any model may plan"), `gate-once-per-batch` (later the same day: one implementer until
   UI can run beside phase 1), `parallel-agents-lock-the-gate-too` (four agents, `-Shared`). What is the cap today? The
   first file is also a chronology of superseded caps: keep as owner history, or collapse to the current rule and quote?

## Auto-memory-only files (11; the repo has none of them)

Deleting any of these is permanent: they are in no git history.

| File | Class | Reason |
|---|---|---|
| `a-worktree-copies-gcor-demos-never-lfs-pull` | KEEP, mirror into the repo | project rule, owner 2026-10-19, new today; not personal; its index line exists only in the auto index; fold into `lcor-is-not-in-a-worktree` later |
| `fuzz-cov-is-the-driver-read-ft` | KEEP | measurement quirk; `docs/FUZZING.md` already explains `ft:` but not "`cov: 8` and 62 counters are the native driver", so promoting one sentence there would let it go |
| `gray-screenshot-is-cua-driver` | KEEP | client/tool fact with the origin traced |
| `next-project-engine-agnostic-test-framework` | KEEP | history of an owner decision, dropped then reopened with his quote |
| `no-paid-compute-oracle-dropped` | KEEP | holds the owner's 2026-10-15 correction ("it came back, the mut didnt") that is in neither D212 nor global CLAUDE.md; only the rule and the Oracle fact are duplicated. Delete only after the quote is carried |
| `no-remote-subagents` | KEEP | tool rule, no other owner |
| `start-ghidra-after-a-restart` | KEEP | process rule with the exact recipe |
| `owner-is-he`, `owner-shell-is-powershell`, `answer-the-owner-plainly`, `csharp-for-every-project` | ASK: move to `~/.claude/memory/`, folded into one `owner-profile` | all four would help on a different project, which is the repo's own test for global memory; the repo rule keeps personal items out of `docs/memory/` |

## What I did not verify

- The twelve memories that cite findings (`a-config-is-a-program`, `a-default-is-not-a-constant`,
  `a-demo-names-a-map-version`, `a-model-shaders-color-is-gamma`, `a-paused-frame-walks-no-animation-event`,
  `a-reference-count-can-depend-on-fps`, `a-tf2-corpse-is-simulated-not-sent`, `bone-merge-sends-no-position`,
  `driving-tf2-demo-playback`, `material-variables-split-three-ways`, `one-demo-player-serves-camera-and-recorder`,
  `struct-padding-is-on-disk`) were not compared line by line with their findings. They carry wrong turns and measurements,
  so they are KEEP under the rule either way.
- `instrument-bugs-outnumber-decoder-bugs` (28 KB) is the largest file and the most cited; it was read, and I propose no
  change, but a split by section would be a separate decision.
- Doc passes after any delete: also run the `[[ ]]` dangling-link check (`grep -o "\[\[[^]]*\]\]"` against the file list).

## Execution order when the owner approves

1. Decide section 2 and section 5 questions. 2. Do the clean deletes and merges in the **auto** directory first, then mirror
byte for byte (body) into `docs/memory/`. 3. Repoint the links in the "Links to repoint" columns (including the five outside
`docs/memory/`: `DECISIONS.md:8868`, `RISKS.md:37669` and `:39707`, `ActiveWeaponHandleTests.cs:11`,
`tools/specimen/tf2ds_specimen.cfg:7`). 4. Rebuild `MEMORY.md` (about 14.7 KB; about 14.0 KB with section 2). 5. Re-run the
dangling-link check and `diff -rq`.
