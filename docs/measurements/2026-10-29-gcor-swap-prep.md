# Measurement 2026-10-29: gcor swap prep (the edit list, the dry run, the LFS cost)

This document prepares the swap that puts the scripted era specimens into gcor (D241). **Nothing here has been
applied.** tools/corpus/demos, the manifest, the tests and the gate floors are all unchanged. The swap runs as
`tools/specimen/gcor-swap.ps1`, which takes the final file list as its input. Why each test depends on its old demo is
section 3 of `2026-10-28-era-specimens-v2-plan.md`, and this document does not restate it.

## Observation

The owner: *"we will be changing the gcor, we have to"*, and *"copying is fine as long as it gets replaced with what
was there when it gets done"*. The old gcor demos stay until he says go. The eras are still being re-recorded: 2010
twice, 2011 and 2013, with 2008 and 2007 to follow, so the file set is not final.

## Question

1. What must the swap check, copy and write, given a file list that will change?
2. Which lines in tests, manifest, TIMELINE and gate must change, and when?
3. What does it cost against LFS's 1 GiB/month?

## Method

- I wrote the script and ran it dry over `F:\Tf2DemoSalvage-lcor`. I rehearsed `-Apply` against a scratch copy of
  gcor and the manifest, never the real ones.
- I collected references with the script's own listing, a `Select-String` of the old gcor stems over the C# code,
  plus `Grep` over docs, README and the workflows.
- I read the floor semantics from `build/assert-test-count.sh`: it checks a floor, not an equality.
- I read the per-demo test count from `CorpusContainerTests.CorpusFiles`, which yields one case per corpus file.
- I took LFS sizes from `git lfs ls-files --all --size` and the cache keys from the two workflows.

**Controls, each run before its check was believed:**

- **Buildings.** The 2008 `-spec-` pair must FAIL, because its dispenser was killed at 0.44 (plan, run 3). It fails,
  with the same reading as the plan's `pollbuild.ps1`.
- **Name hazard.** Re-running against a gcor that already holds the 2009 pair must refuse both files. It does
  (`same name as existing …`), and `-Apply` then refuses.
- **Self-test.** `-SelfTest` holds 7 of 7. An old gcor name is refused; a `-spec-` name passes; zero objects, a
  0.44 dispenser and a 9-of-10 control all fail.
- **Container walk.** The walker reports every demo as ending in a cut `dem_stop` (4 of 5 bytes). The old gcor 2008
  POV, the 2009 POV and z1800 show the same 4-byte tail, so it is the engine's writer and not a defect in the new
  files. The script accepts that tail and nothing else, and counts it as the `dem_stop 1` the manifest records.

## Environment

Worktree branch `feat/gcor-swap-prep` from `feat/era-specimens-v2-e` 9d273b71. The gcor files in this worktree are
LFS pointer stubs. No TF2, viewer or desktop was used. The Probe was built in Release by `dotnet run`.

## Result

### 1. The script: `tools/specimen/gcor-swap.ps1`

The help text holds the full contract (`Get-Help tools/specimen/gcor-swap.ps1 -Full`). The decisions that shaped it:

- **A dry run is the default. `-Apply` requires `-Eras`**, so the eras entering gcor are a stated choice. `-Apply`
  refuses if anything is not PASS, if a pair is incomplete, or if `-SkipBuildings` was given.
- **The gcor name keeps `-spec-`.** Without it, the 2007 and 2008 targets would be exactly the old file names. Every
  test naming those files would then run its old expected values against new bytes, which the rebasing rule forbids.
  `Corpus.Demo` matches with `Contains`. So the script refuses a target that nests with an existing gcor or lcor name,
  or that contains a demo-name literal harvested from `tests/` and `tools/`.
- **The build is not in the bytes.** It is checked against the protocol of the manifest's entry for that build.
  Builds 4231, 4269 and 11138802 have no entry, so they need `-ExpectedProtocol @{4231=15;4269=15;11138802=24}` taken
  from each session's `version` line. Without it they are UNVERIFIED, and UNVERIFIED blocks `-Apply`.
- **An unfinished recording has frames 0** (`docs/memory/a-header-written-last-is-absent.md`). It is reported
  INCOMPLETE and is not hashed.
- **The manifest is spliced, not re-serialised.** `ConvertTo-Json` would reflow the long notes and the one-line
  `censusSpecimens` objects. The new entries go in front of `censusSpecimens`, and the result must parse back with the
  expected count. The prose fields are written as `TODO`, and the commit fills them from the plan's session log.

**Dry run, 2026-10-29 ~13:40** (each file's sha256 is printed in full by the script):

```
FAIL       tf2-2008-build3420-spec-pov-cp_granary.dem  p14 1,814,563 B 312.0 s 5283 frames
           - buildings not all finished: 2 objects [entity 477 ... pct 1; entity 486 building True placing False pct 0.44], control 1732 of 1732
FAIL       tf2-2008-build3420-spec-stv-cp_granary.dem  p14   786,149 B   (same 0.44 dispenser), control 1588 of 1588
PASS       tf2-2009-build3862-spec-pov-cp_granary.dem  p15 1,865,596 B 339.6 s 5897 frames  sha256 a635be1e…5e45
PASS       tf2-2009-build3862-spec-stv-cp_granary.dem  p15 1,084,769 B 348.3 s 4840 frames  sha256 9b0294ef…c05d
PASS       tf2-2011-build4604-spec-pov-cp_granary.dem  p16 3,119,444 B 264.9 s 13530 frames sha256 a133bb4f…c341
PASS       tf2-2011-build4604-spec-stv-cp_granary.dem  p16 1,043,987 B 255.6 s 3954 frames  sha256 1f180a17…2979
PASS       tf2-2013-build1729296-spec-pov-cp_granary.dem p24 3,135,865 B 238.2 s 14384 frames sha256 03162bfb…9130
PASS       tf2-2013-build1729296-spec-stv-cp_granary.dem p24 1,114,516 B 239.1 s 3862 frames  sha256 29029c3d…b8f6
UNVERIFIED tf2-live-build11138802-spec-{pov,stv}-cp_granary.dem  (no -ExpectedProtocol given)
6 of 10 pass. Adding them puts 11,364,177 bytes (10.8 MB) into LFS.
```

- 2007 has only `spec0`, and 2010 only `spec1`/`spec2`, so neither is selected yet. A 2010 4231 `-spec-` pair
  appeared later the same afternoon (p15, 2,590,482 / 1,000,815 B; UNVERIFIED until it is given
  `-ExpectedProtocol`). The set is live.
- The 2011 and 2013 `-spec-` pairs pass the buildings check. This is the first measurement of them with the
  end-of-chain check: in the plan's differences table, every `spec` pair after 3862 was unfinished. Either these
  are the newer re-records, or the table has gone stale for them. **The lead should confirm which recording each
  file is before the swap.**

### 2. The edit list

**Phase 1: add (old demos stay).** These edits fall out of adding N files:

| where | now | change | evidence |
|---|---|---|---|
| `build/gate.sh:1633` `run Tf2DemoSalvage.Corpus.Tests corpus` | 156 | 156 + N, with its reason | arithmetic: `CorpusContainerTests` has one case per gcor file. Confirm on the `.trx` (a floor, so it is optional, but the floor is supposed to track the count) |
| `.github/workflows/test.yml:708` `corpus.trx` floor | 156 | 156 + N | same |
| `.github/workflows/test.yml:524` materialised-demo check `-lt 10` | 10 | 10 + N | the gcor file count |
| `tools/corpus/manifest.json` `demos[]` | 10 entries | N entries spliced by `-Apply`. Fill each TODO from the plan's session log (`version` lines, cfg commit, launch recipe) | — |
| `manifest.json:4` "it is now 4 and 14.5 MB" | stale already | the new count and size | — |
| `manifest.json:6` "protocol 15 has POV only" | true today | false once the 3862 STV is in | — |
| `manifest.json:7` "three of the four entries" | stale already | restate as "every era specimen is scripted on a dated client (D241)" | — |
| `manifest.json:76` (2007 STV: "the only file carrying `svc_VoiceData` in quantity") | true today | false: 2008/3862 carry 43–44 each, and 2010 4231 carries 266 (plan, second pass) | measured |
| `docs/TIMELINE.md` Dated anchors (`:51`–`:64`) | no 2010 rows | if 2010 enters: `10 June 2010, 15, build 4231` and `16 July 2010, 15, build 4269`, from their `version` lines (plan, the 2010 sections); live 11138802 only if live enters | measured |
| `docs/TIMELINE.md:243`–`:244` (late-p15 dialect "after build 3862 … by 9 November 2010") | bounded | **measure first.** If 4231 writes six-bit types and VectorXY numbering, the upper bound moves to 10 June 2010 | not yet measured |
| `docs/DECISIONS.md` | — | the swap decision, drafted in the plan's "Prepared for the swap commit" section. It takes the next free number (D243 after the 2026-10-29 renumber), with D240's number note | — |

Lines checked and left alone:

- `TIMELINE.md:114` ("closed — four demos") is about the recovered protocol 21/22 lcor demos, not the era specimens.
  Plan §3's list of TIMELINE edits names it wrongly, and it needs no edit.
- `TIMELINE.md:101`–`:104` cites the old 2007 file, and stays true.
- `RECORDING_CHECKLIST.md` needs nothing for the swap.

**What changes on add without any edit, and must be read on the run:**

- **`CorpusVoiceCodecEraTests.IsEraSpecimenFrom2007To2013`** (`tests/Tf2DemoSalvage.Corpus.Tests/Net/CorpusVoiceCodecEraTests.cs:111`)
  picks up every new 2007–2013 file, and each must declare `vaudio_speex`.
  - Measured: 2008 and 3862 (session logs); 2010 ×2, 2011 and 2013 (plan, "Voice codec per era").
  - Not yet measured: 2007's re-record.
  - `tf2-live-` is excluded by its name.
- **The 45 files that sweep `Corpus.Files()`/`FilesWithSchema()`** now run over the new demos in CI. That is the
  coverage this swap is for, and any failure there is a finding about the new bytes, not about the swap. Read each
  set-level test's count, not only the total: `CorpusDamageTests`, `CorpusCeltSpeexVoiceTests`, `CorpusProtocol15Tests`
  and `CorpusVoiceCodecEraTests` (plan §3 D2).
- **On a full local run, the same file name sits in gcor and in lcor**, because `DemoCorpus.Files` concatenates both
  without dedup. `CorpusContainerTests` would then show two cases with one name. After the swap commit, the lcor
  copies have to be moved out of `F:\Tf2DemoSalvage-lcor`. That is for the owner or the lead; this task deletes and
  moves nothing.

**Phase 2: retire (on the owner's go, one old demo at a time).** Each line below keeps working while its old demo
stays. "Re-derive" means the new expected value comes from fresh evidence on the new demo and is never copied. The
class letters are plan §3's.

| class | file:line | old demo | on retire |
|---|---|---|---|
| A bytes | `managed/Tf2DemoSalvage.Core/Schema/KnownSchema.cs:22` | 2007 POV | re-point the comment only: the 2007 `spec0` POV payload is byte-identical to the embedded `.bin` (plan, "Section 3A's prediction") |
| A bytes | `tests/Tf2DemoSalvage.Core.Tests/Schema/KnownSchemaTests.cs:68`, `:91` | 2007 STV / POV | swap in the new demo's file SHA; the payload SHA stays |
| A bytes | `tests/Tf2DemoSalvage.Corpus.Tests/Schema/CorpusSchemaTests.cs:33`, `:44`, `:75` | 2007 STV / POV | exact `==` on the name, so the new files are never selected until this changes. The 64 KiB cut reproduces on `spec0` |
| A bytes | `tests/Tf2DemoSalvage.Corpus.Tests/Schema/CorpusEntityRoundTripTests.cs:130` | 2007 STV | re-derive |
| A session | `tests/Tf2DemoSalvage.Corpus.Tests/AppliedTimeCorpusTests.cs:27`, `:65`, `:102`, `:134` | 2013 STV foundry | re-derive (map changes too) |
| A session | `tests/Tf2DemoSalvage.Corpus.Tests/Scene/ProbeTrackSelectionCorpusTests.cs:22`, `:33` | 2013 STV | re-derive; it needs two candidates, so check the new demo has them |
| A session | `tests/Tf2DemoSalvage.Corpus.Tests/Scene/PlayerAtCorpusTests.cs:20` | 2013 STV | re-derive |
| A session | `tests/Tf2DemoSalvage.Corpus.Tests/LifeStateCorpusDiagnostic.cs:45`, `:80` | 2013 POV | re-derive |
| A session | `tests/Tf2DemoSalvage.Corpus.Tests/Scene/CorpusObserverModeDiagnostic.cs:42` | 2013 POV ("where the owner saw it") | keep the old demo, or re-derive |
| A session | `tests/Tf2DemoSalvage.Corpus.Tests/Scene/CorpusRecorderPredictionTests.cs:35`, `:113`, `:149` | 2009 POV | re-derive |
| A session | `tests/Tf2DemoSalvage.Corpus.Tests/Scene/OffHandProbe.cs:128` | 2013 POV | re-derive |
| A cite | `managed/Tf2DemoSalvage.Core/Scene/ScenePropTrack.cs:2218`; `tests/Tf2DemoSalvage.Core.Tests/Container/RecordedUserCommandTests.cs:15`; `…/Scene/EntityModelTests.cs:13`; `…/Net/UserMessageBodyTests.cs:271`; `…/Scene/SendTableConformanceTests.cs:50` | various | citations of the old file; they stay true of git history. Re-point only if the reader can no longer open the file |
| A | `README.md:93`–`:95` opaque-bit counts | 2007 STV, 2009 POV, 2013 STV | re-measure |
| A | `benchmarks/Tf2DemoSalvage.Benchmarks/ParseBenchmarks.cs:41` | 2013 STV | owner's call (changing it breaks the benchmark history) |
| B map | `tests/Tf2DemoSalvage.Corpus.Tests/PeriodMapChecksumTests.cs:80`–`:82`, `:120`, `:176` | 2007/2008 granary | granary stays, so rename and re-run |
| B map | `tests/Tf2DemoSalvage.Corpus.Tests/MapCrcVariantSearchDiagnostic.cs:50` | 2007 POV | rename |
| B map | `tests/Tf2DemoSalvage.Corpus.Tests/Scene/FogDecodeTests.cs:100`, `:118`, `:119`, `:135` | 2011 STV viaduct, 2008 pair, 2013 STV foundry | 2008 renames; 2011/2013 are re-derived on granary |
| B map | `tests/Tf2DemoSalvage.Corpus.Tests/PovSkyFogCorpusTests.cs:20`, `:33` | 2013 POV badlands, 2011 POV viaduct | re-derive on granary |
| B map | `tests/Tf2DemoSalvage.Corpus.Tests/Scene/CorpusFootstepLayoutTests.cs:15`–`:17` | 2009 POV, 2008 STV, 2013 POV | re-derive (the movement differs by session) |
| B map | `tests/Tf2DemoSalvage.Corpus.Tests/DirectorShotCorpusTests.cs:37`, `:57` | 2013 POV | re-derive |
| B UI | `tests/Tf2DemoSalvage.Viewer3D.UiTests/CaptureUiTests.cs:56`, `MapFetchUiTests.cs:36` | 2008 STV | rename; phase-2 UI run (desktop lock) |
| B UI | `tests/Tf2DemoSalvage.Viewer3D.UiTests/ViewerSession.cs:110`; `tests/Tf2DemoSalvage.Viewer3D.Tests/LaunchOptionWiringTests.cs:49`, `PlaylistMapLoadTests.cs:36` | 2013 POV, opened **by direct path** | rename. These do not go through `Corpus.Demo`, so a missing file fails rather than skips |
| C era | `tests/Tf2DemoSalvage.Corpus.Tests/Scene/CorpusPlayerFlagsTests.cs:12`, `:13` | 2009 / 2013 POV | rename and re-run |
| C era | `tests/Tf2DemoSalvage.Corpus.Tests/Net/CorpusProtocol15Tests.cs:42` | 2009 POV | rename |
| C era | `tests/Tf2DemoSalvage.Corpus.Tests/RetiredWireNameCorpusTests.cs:46`–`:49`, `:77` | 2007/2008/2009/2011 POV, 2013 STV | rename |
| C mode | `tests/Tf2DemoSalvage.Corpus.Tests/AudienceSplitCorpusTests.cs:22`, `:23`; `SpectatorGuiCorpusTests.cs:82`, `:83` | 2011 pair | rename |
| C | `tests/Tf2DemoSalvage.Corpus.Tests/Scene/CorpusItemAnimSlotTests.cs:59`, `:60` | 2011 pair | rename |
| C | `tests/Tf2DemoSalvage.Corpus.Tests/Scene/CorpusServerConVarTests.cs:31`, `:34` | 2011 STV, 2013 POV | rename |
| C | `tests/Tf2DemoSalvage.Corpus.Tests/Scene/ViewmodelClassAgreementTests.cs:127` | 2009 POV (two viewmodels) | rename; check the new demo carries both |
| C | `tests/Tf2DemoSalvage.Corpus.Tests/Scene/CorpusRecorderFeetTests.cs:25`, `CorpusRetimeViewTests.cs:20` | 2013 POV | rename |
| C | probe usage comments: `tools/Tf2DemoSalvage.Probe/Probes/AnimationEventProbe.cs:25`–`:26`, `PoseParameterProbe.cs:26`–`:27`, `SimulationLagProbe.cs:23`, `BaselineProbe.cs:97` | 2013 STV, 2007 POV | rename |
| — | `manifest.json` old entries; `:336` (B24's specimen) | each retired demo | move the entry to a "retired, in git history" note that keeps its source (plan §3) |

**Blockers on retiring, per plan §3:**

- **The 2013 POV** carries the UTF-8 name finding, which is unresolved. It stays.
- **The 2007 STV** is the only `svc_VoiceData` carrier in quantity *today*. That stops being true once the new 2008,
  2009 or 2010 files are in.
- **Each retire changes the LFS cache key again** (section 3).

### 3. LFS

**Sizes** (bytes, from the files):

| set | MB | source |
|---|---|---|
| gcor today, 10 files | 21.3 | manifest `sizeBytes`, summed |
| the 6 passing today (2009, 2011, 2013 pairs) | 11.4 | dry run |
| the full set: 2007, 2008, 2009, 2010 ×2, 2011, 2013 pairs | **~23.0** | 2009/2011/2013 measured. 2008 is its current `-spec-` (2.60). 2010 4231 is its `-spec-` pair, which appeared during this work (3.59). 2010 4269 is `spec2` (2.82). **2007 is interpolated** at 2.6, the size of 2008 and 2009 under the same script |
| plus the live pair, if it enters | +5.5 | dry run |
| LFS storage over all history (`git lfs ls-files --all`) | ~467 | 21.3 in gcor plus 445.7 from demos removed earlier |

**Bandwidth.** Both caches are keyed on the pointers: `test.yml:508` on `hashFiles('tools/corpus/demos/*.dem')`, and
`mutation.yml:151`/`:157` on the LFS object list. Each key change costs one full gcor fetch per workflow.

- `test.yml` runs on pushes to `main` and on PRs. Merges are local, so one `main` run is a single fetch; a PR adds
  one more.
- In `mutation.yml`, the `cli` job fills the cache and the `corpus` job reuses it.

| step | gcor after | fetches | cost |
|---|---|---|---|
| add (phase 1) | 21.3 + 23.0 = **44.3 MB** | test.yml main + mutation | **~89 MB**; ~133 MB with a PR run |
| retire (phase 2), keeping z1800 and the 2013 POV | ~33 MB | the same | ~66–99 MB |
| both in one month | — | — | **~150–230 MB, 15–21 % of 1 GiB** |

- Storage after the add is ~490 MB, under the free 1 GiB.
- Every refetch after a cache eviction (7 days unused) now costs 44.3 MB rather than 21.3 MB until the old demos
  retire.
- With the live pair, add 5.5 MB to every figure.

**D81 (archive.org fetch).** It is decided and not implemented: no workflow fetches from archive.org, and both still
run `git lfs pull`. With a fetch layer the swap would cost no LFS bandwidth at all. The new set's sha256 values are
already printed by the script, and that is the integrity half of such a layer. The recommendation from the plan
holds:
- swap within LFS now (the cost above is small);
- upload the new set to the owner's archive.org collection, keyed by these checksums;
- build the fetch layer as its own task, without holding up the swap.

## Verdict

The swap is ready to run on the final file list:

```
pwsh tools/specimen/gcor-swap.ps1 -Eras <chosen> -ExpectedProtocol @{4231=15;4269=15}         # dry run
pwsh tools/specimen/gcor-swap.ps1 -Eras <chosen> -ExpectedProtocol @{4231=15;4269=15} -Apply  # on the go
```

After `-Apply`:
1. Fill the manifest TODOs.
2. Make the phase-1 edits above.
3. Run both gate phases and the full-corpus superset (D241), then read the four set-level counts.
4. Commit the demos (LFS), the manifest, the floors and the decision entry together.

Retiring is a separate commit for each old demo, on the owner's word.

**Owner's calls still open:**
- the 2010 pairs: one build or both (both are late p15);
- the live pair: p24 is already in gcor;
- the 2013 POV's UTF-8 finding;
- the benchmark subject.

**Before the swap, the lead must confirm** which recording each 2011/2013 `-spec-` file is, because they pass the
buildings check that the differences table says they fail.
