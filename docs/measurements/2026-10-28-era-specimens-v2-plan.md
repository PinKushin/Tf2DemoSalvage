# Measurement 2026-10-28: era specimens v2, Phase A (inventory, swap cost, per-era cfg feasibility)

Plan record for re-recording every gcor era specimen with `tools/specimen/tf2ds_specimen.cfg`. Phase A is read-only and
launched no TF2 client. Raw output is in `2026-10-28-era-specimens-v2-plan/`.

## Observation

The owner, 2026-10-28: *"They dont match and test enough, I manually just did random stuff, they cant be compared against
each other."* Then, settling the swap: *"we will be changing the gcor, we have to"*. And on copying the cfg into a game
folder: *"and copying is fine as long as it gets replaced with what was there when it gets done"*.

`tools/corpus/manifest.json` agrees with him. Each era specimen was "recorded by the owner" by hand, on three different
maps (granary, badlands, viaduct, foundry), lasting between two and six minutes, and following no shared script.

## Question

1. Which era × point of view does gcor hold, and which period clients can actually run?
2. What does replacing each gcor demo cost: every test, fixture and expected value that names it, plus LFS?
3. How was the current-build specimen exec'd? (Answered below. The owner's ruling then settled the rule conflict.)
4. Which steps of the cfg cannot run in each era, judged from that era's own binaries and data?

## Method

- Inventory: `ls` the period-client folders, read each `tf/steam.inf`, and list `tf/maps/*.bsp`. For 2011, whose maps
  sit inside a GCF, `grep -aoE '[a-z0-9_]+\.bsp'` lists the GCF's directory names. The control is that the same grep
  lists `koth_viaduct.bsp`, the map that era's specimen used.
- Swap cost: `git grep` for every specimen filename in code, tests and docs, plus `Grep` counts.
- Per-era commands: `era_strings.sh` checks, for each name the cfg uses, whether it appears as a whole NUL-delimited
  string in that era's `engine.dll`, `server.dll` or `client.dll`. A ConCommand or ConVar name is registered from a
  string literal. `era_strings2.sh` re-checks every ABSENT as a case-insensitive substring.
  **Control:** `record`, `kill`, `joinclass` and `+attack2` must be present in every era, and they are.
  **The control caught a false negative:** pass 1 reported `heavyweapons` ABSENT for 2007 and 2008, and pass 2 found
  it in both. So pass 1's ABSENT is believed only where pass 2 also says `none`.
- Headers: AlliedModders hl2sdk, read through the GitHub API because the local clone is behind a git hook here.
- The `wait` semantics come from published source: a diff of `tier1/commandbuffer.cpp` between hl2sdk `orangebox` and
  source-sdk-2013.

**Withdrawn instrument.** A byte grep of the old demos for `m_nPlayerCond*` is **not evidence**. `dem_datatables`
names are bit-packed strings, so a name matches only when it happens to fall byte-aligned. The control failed:
`m_nPlayerCond` came back absent on 2007 STV, 2009 and 2011, all of which must carry it. Each era's condition field set
comes from the production decoder (the `schema` probe), and that is deferred to Phase B (see Question 4).

## Environment

Worktree `feat/era-specimens-v2` from `origin/main` 59b95325. Nothing was built and no client was launched. Another
agent's B259 timed runs were in progress.

## Result

### 1. Era inventory

| era | gcor today | client on disk | runnable? | `cp_granary` | `cp_badlands` |
|---|---|---|---|---|---|
| 2007, b3258, p11 | POV + STV, cp_granary | `F:\tf2-builds\tf2-2007\Team Fortress 2` (steam.inf 1.0.0.5; `RUN_TF2.exe` + `steamemu.ini`) | yes, `hl2.exe` | yes | **no** |
| 2008, b3420, p14 | POV + STV, cp_granary | `F:\tf2-builds\tf2-2008` (1.0.2.2) | yes | yes | yes |
| 2009, b3862, p15 (early dialect) | **POV only**, cp_badlands | **`D:\team fortress 2`** (1.0.5.9; `engine.dll` dated "Jun  4 2009"). This is not listed in `where-the-game-and-clients-live.md` | yes | yes | yes |
| 2010, 1.1.0.0, p15 (late dialect, B440) | none | `D:\tf2-builds\tf2-2010-1.1.0.0`, which holds `client.dll`/`engine.dll`/`steam.inf` only | **no** | — | — |
| 2011, b4604, p16 | POV + STV, koth_viaduct | `F:\tf2-builds\tf2-2011\Team Fortress 2` (GCFs; `rev.ini` REVEmu "IZI LAN") | yes | yes (GCF) | yes (GCF) |
| 2013, b1729296, p24 | POV cp_badlands + STV cp_foundry | `F:\tf2-builds\tf2-2013\TF2` | yes | yes, plus `.nav` | yes, no `.nav` |
| live | `z1800.dem` (a real 2020 match) | Steam install | yes | yes | yes |

- `probe-2011` and `probe-2013` hold DLLs only and cannot run.
- A late-protocol-15 (2010) client means a ~3 GB archive.org download, and that needs the owner's yes. It **adds** an
  era rather than redoing one, so it is outside this task.
- **No gcor era is lacking a runnable client**, so every old specimen can be re-recorded. The exceptions are bytes and
  findings that a re-recording cannot reproduce (section 3).

**The current-build specimen predates its own script.** The live install holds `tf/cfg/tf2ds_specimen.cfg`, byte-identical
to the repo version apart from CRLF, with mtime 2026-10-02 23:42. It also holds `tf/tf2ds_specimen.dem`, 2,882,730 bytes,
mtime 23:38. Commit f13f6b49 records "153.5 s, 9828 frames" for that recording. Then ed392c15 moved the enemy bots to the
end of the script, after the recording was made. So the existing live specimen (and lcor's
`tf2ds_specimen-2026-10-02.dem`) is **not** a baseline for the current script.

Both files are leftovers in the live game folder, contrary to `check-the-worktree-before-every-git-command.md` ("the game
folder keeps nothing of ours"). Under the owner's ruling, "what was there" in the live `tf/cfg` is now that file, so a
live session restores it unchanged. Removing the two files is for the owner, and nothing was deleted here.

### 2. How it was exec'd, and the rule now in force

No document records it. The physical evidence above says the cfg was copied into the live `tf/cfg/`. The owner's ruling
(quoted above) now governs every session:

- Before copying, hash and copy aside (to `D:\tf2ds-specimen-backup\<era>\`) any file of the same name already in that
  client's `tf/cfg/`. Do the same for `tf/cfg/config.cfg`, which the client rewrites on quit (`host_writeconfig`).
- After the session, put back exactly what was there. Re-copy the original, or, where nothing existed, **move** the
  placed cfg out to the backup folder. This agent never deletes files. Then verify by hash and listing.
- Log every copy and restore here, in a `## Session log` section added in Phase B.
- **The restore control is a listing diff, not the files we named.** The period `tf/` folders already hold files the
  client rewrites: `stats.txt`, `tf2_playerstats.dmx`, `gamestate.txt`, `modelsounds.cache`, `demoheader.tmp` and
  `voice_ban.dt`.
  - Before each session, take a hashed listing (name, size, sha256) of `tf/` (top level), `tf/cfg/` and
    `tf/screenshots/`.
  - After it, take the same listing and diff the two. Every changed or new entry is restored from the pre-session copy
    or moved out.
  - The empty diff is what "restored" means, and it is pasted into the session log.
- Only cfgs are copied in. The client itself writes demos, `console.log` (`-condebug`) and the `con_logfile` output into
  `tf/`, and these are **moved** out to lcor or the scratchpad afterwards. If the owner wants an exact pre-session
  listing restored, a file nothing existed for has to be removed, and that removal is the owner's or the lead's to do.

### 3. The swap: what names each gcor demo, by what it depends on

There are 147 occurrences across 60 files (`Grep` count, `docs/` included). Each code reference is classified by what a
replacement must reproduce. **Rebasing rule:** an expected value on a new demo comes only from fresh evidence on that
demo (a measurement, the SDK, or the engine), never copied from the old one.

**A. Exact bytes. These cannot survive a swap and must be re-derived or the old demo kept.**

- `managed/Tf2DemoSalvage.Core/Schema/KnownSchema.cs` + `Schema/Data/build3258-datatables.bin` (embedded) +
  `KnownSchemaTests.cs`. This is the 2007 POV's `dem_datatables` payload and its SHA-256. It is what decodes the
  truncated 2007 SourceTV (B24).
  **Prediction for Phase B:** the schema is a property of the build, so a new 2007 POV's payload is byte-identical to the
  embedded `.bin`. If the hashes match, the resource stays and only the demo SHA in the test changes. If they differ,
  that is a finding.
- `CorpusEntityRoundTripTests.cs` and `CorpusSchemaTests.cs` (the 2007 STV/POV by filename: the 64 KiB cap, `kept`).
- Comments citing ticks and entity facts in a specific file:
  - `ScenePropTrack.cs:2218` (2007 POV tick 5261)
  - `RecordedUserCommandTests.cs:15` (2013 POV tick 3511)
  - `EntityModelTests.cs:13` (2013 STV, 119 …)
  - `UserMessageBodyTests.cs:271` (captured from the 2008 POV)
  - `SendTableConformanceTests.cs:50`
  - the docs listed by `Grep` (RISKS has 39 occurrences)

  These are citations. They stay true of the old file, so each is re-pointed only if the old demo leaves gcor.
- `ProbeTrackSelectionCorpusTests` (2013 STV: "puts two …"), `PlayerAtCorpusTests`, `AppliedTimeCorpusTests` (4×,
  2013 STV), `LifeStateCorpusDiagnostic`, `CorpusObserverModeDiagnostic` ("where the owner saw it"), and
  `CorpusRecorderPredictionTests` (3×, 2009 POV). Each asserts on events at moments in that session, so each is
  re-derived on the new demo.
- `README.md:93-95`: the opaque-bit counts per specimen are re-measured.
- `benchmarks/.../ParseBenchmarks.cs:41`: the benchmark subject. Changing it breaks the benchmark history, so that is
  the owner's call.

**B. Map content. These survive only if the map is kept.**

- `PeriodMapChecksumTests` (2007/2008 POV, 2008 STV, cp_granary).
- `MapCrcVariantSearchDiagnostic` (2007 POV).
- `FogDecodeTests` (2011 STV viaduct, 2008 pair granary, 2013 STV foundry).
- `PovSkyFogCorpusTests` (2013 POV badlands, 2011 POV viaduct).
- `CorpusFootstepLayoutTests` (2009 POV, 2008 STV, 2013 POV).
- `DirectorShotCorpusTests`.
- UI tests: `CaptureUiTests` and `MapFetchUiTests` (2008 STV); `ViewerSession.cs:110` and the `Viewer3D.Tests`
  launch/playlist tests (2013 POV).

**C. Era or mode only. These survive any re-recording of the same build and mode, with a name change only.**

- `CorpusPlayerFlagsTests` (frozen flag bit 5 in 2009, bit 6 in 2013).
- `CorpusProtocol15Tests` (`June2009Pov`).
- `RetiredWireNameCorpusTests`.
- `AudienceSplitCorpusTests` and `SpectatorGuiCorpusTests` (the 2011 pair).
- `CorpusItemAnimSlotTests` (2011 pair).
- `CorpusServerConVarTests`.
- `ViewmodelClassAgreementTests` (2009 POV, two viewmodels).
- `CorpusRecorderFeetTests` and `CorpusRetimeViewTests` (2013 POV).
- The probes' usage comments.

**D. Name pattern: a silent-membership risk and a collision.**

- `CorpusVoiceCodecEraTests.IsEraSpecimenFrom2007To2013` selects any `tf2-YYYY-build…` filename in 2007–2013.
- lcor is picked up automatically.
- **`Corpus.Demo(fragment)` (`tests/Tf2DemoSalvage.Corpus.Tests/Corpus.cs:90`) matches by `Contains` and takes
  `FirstOrDefault`.** If an old full name sits inside a new one, `Corpus.Demo("tf2-2007-build3258-pov-cp_granary")` could
  open the new file on a full local run. Old expected values would then run against new bytes. A suffix such as
  `<old-name>-spec.dem` is therefore unsafe.

**Proposal:** stage the new demos in lcor as `tf2-<year>-build<n>-spec-<pov|stv>-cp_granary.dem`.
- No old full name is a substring of that name: checked against every `Corpus.Demo("…")` fragment found in section 3,
  each of which carries mode and map after the build.
- It still passes `IsEraSpecimenFrom2007To2013`, because `name[8..]` starts with `-build`.
- So the era-gated tests exercise it on a full local run, which is part of the coverage proof.

**D2. Consumers keyed on content, invisible to a filename grep.**
- 45 test files sweep `Corpus.Files()` / `FilesWithSchema()` (80 call sites, counted with `Grep`). They are listed by
  that grep, which is the reproduction.
- Some assert set-level facts that a specific gcor file may be the only one to satisfy:
  - `CorpusVoiceCodecEraTests` ("some demo uses Speex");
  - `CorpusDamageTests`, which presumably needs a damage message at protocol ≤14. Not yet read, so this is an
    interpolation;
  - `CorpusCeltSpeexVoiceTests`;
  - `CorpusProtocol15Tests`.
- Under `TF2DEMOSALVAGE_GCOR_ONLY` such a test turns into a skip or a failure when its only witness leaves gcor.
- The swap commit must run these with the new gcor and read each one's count, not only the gate total.
- No test reads `manifest.json` (`Grep` over `*.cs`, `*.sh`, `*.py` and `*.yml`: no match).

**E. Gate.** The floors in `build/gate.sh` count tests per project, and parametrised corpus tests scale with gcor's file
count. A swap that changes the number of gcor files moves those counts. The CI check in `.github/workflows/test.yml:524`
also requires at least 10 materialised demos. The swap commit adjusts each one with its reason written beside it.

**F. Out of scope:** `z1800.dem` (227 references). It is a real 2020 multi-player match, which a solo script cannot
re-create. It stays.

**LFS cost.** gcor today is 10 blobs, about 21.3 MB.

- Size estimate, **interpolated**: the live POV run of the cfg was 2.88 MB for 153.5 s, and SourceTV on a listen server
  is 0.18–0.23 MB/min (manifest note). So the run is about 3 MB POV plus about 0.6 MB STV per era. Five eras × 2 is
  about 18 MB, plus z1800 at 9 MB, for about 27 MB in total.
- Per-fetch cost: CI fetches the corpus once per change to the pointer files. The cache key is
  `hashFiles('tools/corpus/demos/*.dem')`, and `mutation.yml` pulls as well. So the swap costs one full fetch per
  workflow cache, roughly 30–50 MB, against the 1 GiB/month allowance.
- Storage: old blobs stay in LFS history (21 + 27 MB is well under the free 1 GiB storage).

The cost is small. LFS is not what makes the swap expensive; section 3A is.

**D81 status.** Decided (owner, 2026-08-24: "they are backed up on archive … we will start pulling from there"), and
**not implemented**. No code or workflow fetches from archive.org; `test.yml` and `mutation.yml` still `git lfs pull`.

**Recommendation:** swap within LFS now, and also upload the new set to the owner's archive.org collection, with
checksums in `manifest.json`, so D81's fetch layer has its first entries. Building the fetch layer is its own task, and
the swap should not wait on it.

**manifest.json / TIMELINE.md updates at the swap:**
- `manifest.json`: per new demo, `file`, `sha256`, `sizeBytes`, `source` (cfg commit + era override +
  `version` output), and `pointOfView`. Add `map`, `era`, and the census figures, plus a note that the era specimens are
  scripted.
- Old entries that leave gcor move to a "retired, in git history" note that keeps their sources.
- `docs/TIMELINE.md`: the specimen row ("closed — four demos") and the protocol-15 row (both modes once the 2009 STV
  exists).

**What the old specimens found, and what the new set must reproduce:**

Each RISKS entry that cites a gcor specimen was read (heading and status line) and classified:
- **era/mode**: a re-recording of the same build and mode reproduces it;
- **content**: it needs a particular thing to happen in the session, so check that the cfg does it;
- **map**;
- **subject only**: the demo was merely the file the work happened on.

| B | what it is | specimens cited | class | reproduced by the cfg? |
|---|---|---|---|---|
| B24 | SourceTV cuts the schema at 64 KiB on build 3258; KnownSchema embeds the POV's 85,063 bytes | 2007 POV/STV | era/mode + exact bytes (3A) | yes, if both modes are recorded. Predict the same cut |
| B26 | the Damage user message's older layout at protocol ≤14 | 2007 POV | content: the recorder must take damage | yes: `explode` and fall in `ds_move` (check in the census) |
| B44 | pre-2013 demos decode one positioned player; not a defect | 2007 POV, 2008 STV, 2009 POV, 2011 STV, 2013 STV | era/mode | yes |
| B45 | team/class coverage; every delta wiped the entity | 2007 POV, 2011 STV, 2013 POV | era/mode | yes |
| B132 | entities reach the table with no properties | 2011 STV | era/mode | yes |
| B146 | the UI thread stalls on load | 2013 POV | subject only | n/a |
| B172 | footsteps and landing sounds | 2009 POV | content: movement and landing | yes (`ds_move`, `+jump`) |
| B225 | a viewmodel is drawn while the recorder spectates | 2013 POV | content: the recorder spectating | partial: death time after `kill`. Re-derive the span |
| B247 | `dem_stringtables` is not needed (closed) | 2007 POV | era/mode | yes |
| B270 | bone controllers are unimplemented, measured | 2013 STV | content: entities that send controllers | re-measure. The cfg builds a sentry (2011+) |
| B291 | `CalcAutoplaySequences` | 2007 POV | map | yes, with cp_granary |
| B370 | granary's shutter doors | 2008 STV | map + content (doors opening) | only if a door cycles. Unverified |
| B372 | projectiles reach the timeline | 2007, 2008 pair | content: projectiles fired | yes: rockets, pipes, flares in `ds_fire` |
| B377, B382 | the interpolation pair and per-variable histories | 2007 POV | era/mode | yes |
| B380 | the 2008 sticky launcher's sequence numbers | 2008 pair, 2013 POV | content: demoman slot 2 | yes (`ds_slots2`) |
| B389 | a probe bug | 2013 STV | subject only | n/a |
| B440 | build 3862's early p15 dialect | 2009 POV | era/mode | yes, on `D:\team fortress 2` |
| B441 | the Speex frame width (the subject was a 2012 lcor demo) | 2007 STV, cited only | subject only | n/a |
| B450 | POV recorder prediction | 2009 POV | era/mode | yes |
| B452 | POV fog/skybox through `dem_stringtables` | 2013 POV | era/mode | yes |
| B501 | 2007–2009 footstep flags (FL_ONTRAIN) | 2008 STV, 2009 POV, 2013 POV | era/mode + movement | yes |

Findings not under a B-number:

| old specimen | finding | reproduced? |
|---|---|---|
| 2008 pair | no `dem_stringtables` at p14 | yes (era) |
| 2011 pair | audience-split tables; "a local SourceTV is not a proxy" | yes (mode) |
| whichever gcor demo the Phase B census shows carrying `svc_VoiceData` | the only Speex in gcor; `CorpusVoiceCodecEraTests` relies on it | **no, not with the cfg as written**: it has no voice step. RECORDING_CHECKLIST places the voice in the 2007 session, but which file carries it is **unmeasured** |
| 2013 POV | a UTF-8 player name, which found a real bug | **only if the name is non-ASCII**: the cfg sets no name |
| 2013 STV | six minutes of entity churn, the only long one | partial: the cfg runs about 2.5–4 min |

**Do not drop any old demo that a "no" or "partial" row depends on** until the new set reproduces it. Keeping the 2007
POV for the KnownSchema bytes is cheap (1.2 MB) if the Phase B hash prediction fails.

### 4. Per-era cfg feasibility

**There are no period ETFCond headers anywhere.**
- hl2sdk `orangebox` has no `game/shared/tf/`.
- hl2sdk `tf2` and source-sdk-2013 carry only the modern `tf_shareddefs.h`, where ETFCond runs from 0 to 130.
- No era binary before live contains a single `TF_COND_` string. Live has 134 in `server.dll`.

So the per-era condition numbering **cannot be checked from headers**. It has to come from three places:
- the field set the decoder reads (`m_nPlayerCond`, `…Ex`, `…Ex2`, …), which bounds the count;
- `addcond`'s bound check in each era's `server.dll`, via Ghidra on `D:`;
- the dry run's demo, which shows which bit actually lands.

Any statement that "conditions 74+ postdate March 2013" is **unverified** (it comes from changelog recall).

`wait` semantics are identical in orangebox and 2013 (`commandbuffer.cpp`). The only difference is that 2013 gates
`wait` on `IsWaitEnabled()`, and every era's `engine.dll` has `sv_allow_wait_command`, so read it back at session start.
The cfg's alias-chaining design therefore holds in every era. **Evidence class: published source for the orangebox
snapshot, not the 3258 binary.**

| cfg step | 2007 | 2008 | 2009 | 2011 | 2013 | evidence |
|---|---|---|---|---|---|---|
| `cl_disablehtmlmotd` | absent | absent | absent | ok | ok | strings (pass 2) |
| `mp_disable_respawn_times` | absent | ok | ok | ok | ok | strings |
| `tf_bot_quota` / `tf_bot_add` (bots of both teams) | absent | absent | absent | ok, **no `.nav` in the GCF** | ok, `cp_granary.nav` ships | strings, map listing |
| `addcond` / `removecond` (the whole `ds_conds` section) | **absent** | **absent** | ok | ok | ok | strings |
| `build 2 0` / `build 0 0` | `build %d`: one argument, so object 2 may not be the sentry (**interpolated**: dispenser 0, entrance 1, exit 2, sentry 3) | same | same | `build %d %d` (`<building> <mode>`) | same | client format strings |
| `disguise 5 -2` | `disguise %d %d` | same | no format string | ok | ok | strings. Whether `-2` = enemy team is unverified |
| `hidepanel info`, `jointeam`, `joinclass`, `taunt`, `kill`, `explode`, `slot1-3`, `+attack2`, `+reload`, `record`, `stop`, `tv_enable`, `tv_record`, `con_logfile`, `version` | ok | ok | ok | ok | ok | strings |
| map `cp_badlands` | **absent** | ok | ok | ok | ok (no `.nav`) | map listing |

An unknown command is harmless: it prints `Unknown command` and the chain continues. Under RECORDING_CHECKLIST that line
is a finding, and Phase B writes it in the checklist. A step with the **wrong meaning** (`build 2 0` before 2011) is not
harmless, and it needs an override.

## Verdict

Every gcor era can be re-recorded on a client already on disk, the 2009 one included (`D:\team fortress 2`), which also
closes the missing protocol-15 SourceTV. The swap's cost is in section 3A, not LFS.

**Proposals, each needing the lead's or the owner's word:**

1. **One map for every era: `cp_granary`.** It is the only shipped map present in every era from 2007 to live that
   also has a `.nav` (in 2013) for bots. `cp_badlands` does not exist in 2007. It keeps the 2007/2008 map-content tests
   (3B) and moves the 2009, 2011 and 2013 ones. Comparability across eras is the owner's stated goal.
2. **Layering instead of a fork.**
   - Split the trailing `ds_start` call out of `tf2ds_specimen.cfg` into a three-line runner:
     `exec tf2ds_specimen; exec tf2ds_era_<year>; ds_start`.
   - `tf2ds_era_2007.cfg` and `2008.cfg` redefine `ds_conds` to go straight to `ds_finish`, because there is no
     `addcond`, and redefine `ds_engineer_build` to the one-argument `build`.
   - `2009.cfg` redefines `ds_engineer_build` only.
   - `2011.cfg` may need nothing, pending the dry run.
   - Both modes for every era go in the shared layer: `tv_record` beside `record` in `ds_start`, and `tv_stoprecord` in
     `ds_finish`, with `tv_enable 1` before the map. This changes the shared script, so it is the owner's call.
3. **Coverage the cfg skips that the old specimens or the checklist carry:**
   - `say` / `say_team`
   - a UTF-8 `name` and a name change
   - `pause` / `unpause`
   - `spec_next`
   - `mp_restartgame`
   - a capture
   - deliberate unknown commands
   - voice

   All but voice are console commands that can go into the shared layer. **Voice** needs `voice_inputfromfile` and a
   wav in the game folder, and the copy ruling covers only cfgs, so it needs the owner's word. Without it, whichever old gcor demo the
   census shows carrying Speex stays.

**Phase B blockers to settle before the desktop is used:**

- **(a) The tf2 MCP cannot drive a period client as configured.** `Tf2Session` requires `tf_win64.exe` in
  `TF2MCP_GAME_DIR`, even to attach, and tails `console.log` under that folder. Every period client is `hl2.exe`. All
  eras support `-usercon -condebug +rcon_password` (engine strings).
  The options:
  - Extend Tf2Mcp (the owner's repo) with an exe/folder override.
  - Repoint the MCP's environment per era. That is configuration, so it is the owner's call.
- **(b) Launching the archive.org period clients**, including the 2007 `steamemu` and 2011 REVEmu loaders, is running
  downloaded executables. The owner has run them before, but this session needs his yes.
- **(c) The desktop lock.** `tf2-is-a-shared-fixture.md` records the owner saying TF2 does not hold the lock. The task
  says to hold it for every client session. This plan follows the task, since holding the lock is the conservative
  choice. A first boot and a map load are what load the machine, and neither runs until "B259 timed runs finished".
- **(d) Timing is frame-deterministic, not wall-clock.** `wait` counts frames, and an unfocused client runs at about 37
  fps. So the dry run times the chain from `con_logfile` echo markers, and seconds per era are reported rather than
  assumed.

Deferred to Phase B, under the lock once it is free:
- each era's condition field set from the `schema` probe;
- the KnownSchema hash prediction;
- the dry runs, which start on the oldest era (2007) without `record`.

## Prepared for the swap commit — the decision entry (take the next free number at commit; D240 at time of writing)

> **D240 — gcor's era specimens are re-recorded from one script, and the old ones are replaced (2026-10-28)**
>
> The owner, 2026-10-28, on the hand-recorded era specimens: *"They dont match and test enough, I manually just did
> random stuff, they cant be compared against each other."* And, when the swap was put to him as his call: *"we will be
> changing the gcor, we have to"*. On copying the specimen cfg into a client's `tf/cfg/`: *"and copying is fine as long
> as it gets replaced with what was there when it gets done"*.
>
> What followed: every gcor era specimen is re-recorded with `tools/specimen/tf2ds_specimen.cfg` on that era's period
> client. Each is staged in lcor, censused and diffed against the demo it replaces, and swapped into gcor only once the
> full gate (phases 1 and 2) and the full-corpus superset pass. [At commit, add only what the owner has ruled on by
> then: the map, the per-era override layer, and `tv_record` in the shared script are still open proposals as of this
> draft.]
> Expected values on a new demo are re-derived from fresh evidence, never copied. An old specimen stays where it carries
> a finding the new set does not reproduce (`docs/measurements/2026-10-28-era-specimens-v2-plan.md`, section 3).
> Supersedes the manifest's description of era specimens as ad-hoc recordings. It does not change D81, under which the
> corpus's eventual home is still archive.org.

## Missing specimens: every cell with nothing today (owner, 2026-10-28: *"can we also maybe check for any missing ones?"*)

Read-only. Sources:
- `docs/TIMELINE.md`: dated anchors and the gap table;
- `tools/corpus/manifest.json`;
- the protocol census TIMELINE cites (471 lcor headers);
- archive.org item metadata. Only the `/metadata/` JSON was read for sizes; no file was fetched.

"gcor" below means committed; "lcor" means local only. Ranked by the order the owner gave: a protocol never seen first,
then a dated window, then a POV/STV pair.

| rank | gap | what exists today | what could close it | cost / needs |
|---|---|---|---|---|
| 1 | **protocol 12–13** (9 Oct → 15 Nov 2007), never seen | nothing | a build from those five weeks. None is on disk. `tf2-old-manifests-chunks` covers "roughly 2007 to 2012" and might hold one, but which builds it holds is unknown until its `versions.txt` is read | proposal: fetch `versions.txt` + `revemureference.txt` (small text files from a 35.3 GB item) to learn which builds it holds. The item's own uploader says "the game crashed on map load". Any build means an owner yes and a new executable |
| 1 | **protocol 17** (15 Jun → 1 Nov 2011), never seen | nothing | same: a manifest/chunk build if `versions.txt` lists one. Nothing on disk | as above |
| 1 | **protocol 20** (3 Nov → ~Dec 2011), never seen | nothing; the 471 lcor headers jump from 19 to 21 | same | as above |
| 1 | **protocol 23** (after build 5126 → 25 Mar 2013), never seen | nothing | same | as above |
| 2 | **late protocol-15 dialect** (after Aug 2009, six-bit types; B440), dated window | 2 SourceTV demos in lcor; no gcor specimen; no runnable client | (a) `TF2 1.1.0.0 (19 Jul 2010).zip`, 2,986,998,821 B: the client whose `engine.dll`/`client.dll` are already on `D:`, and a ZIP, so it can be dated statically first (D30). (b) `Team Fortress 2 v1.0.9.5.7z`, 1,799,062,913 B (2010). (c) the no-Steam 1.0.9.5 `.rar`, 2,385,225,096 B (1 Jul 2010, LAN only) | owner yes for one download + a new executable. (a) is preferred: it is the only ZIP, and its build is already half-identified |
| 2 | **build 1.1.4.6** (2010/11, protocol unknown: 15 or 16) | nothing | `TF2 1.1.4.6 with cosmetics lol.7z`, 4,024,425,179 B (same item as 1.1.0.0) | a 7z cannot be partially read (D30), so it is the whole 4 GB to learn its protocol. Lowest value unless it turns out to sit inside a gap |
| 2 | protocols 18, 19, 21, 22 (dated by server banners, *inferred*) | lcor only: full ESEA/i46 matches. Owner ruling: full real demos stay out of gcor | a short **period-client** specimen. No client for 4735/4743/4833/5126 is on disk | the same `versions.txt` question |
| 3 | **protocol 15 (build 3862) SourceTV** | POV only, in gcor | **`D:\team fortress 2`, on disk.** Folded into the Phase B run list | none beyond the Phase B rules |
| 3 | protocol 16 (build 4604) pair | both, in gcor | re-record (Phase B) | — |
| 3 | protocol 24, early (build 1729296) pair | POV badlands + STV foundry: **two different sessions**, so not a pair | **on disk: `F:\tf2-builds\tf2-2013`, in Phase B.** It becomes a true same-session pair | — |
| 3 | live (protocol 24, today) scripted specimen | `tf2ds_specimen-2026-10-02.dem` in lcor (predates the final script, POV only); `z1800` (2020 match) in gcor | **on disk: the live install, in Phase B**, POV + STV | — |
| — | **2014–2019** (the Speex→CELT voice transition; `CorpusVoiceCodecEraTests`'s Ignore) | protocol 24 throughout; no specimen in that range | **no demo recorded today can close it.** It needs a client build from those years, and Steam serves only the current depot (manifest downloads of old depots need the owner's account and decision) | TIMELINE already classifies this as a dating gap for a period CLIENT. Marked unclosable by a recording on hand |
| — | **dating** of 18–23 (when each was current) | windows from banners + file times, *inferred* | **no demo can close it** (TIMELINE "a protocol cannot date a demo"); only a stamped client does | as TIMELINE says |
| — | **a second player** (voice client↔player mapping, mid-game join, slot reuse; RECORDING_CHECKLIST 19–24) | none | not a missing era: a second Steam session or machine. Bots cover only some of it, and only in 2011+ | owner's call; outside this task |

### What `tf2-old-manifests-chunks` holds (downloaded 2026-10-28 with the owner's yes; read, nothing executed)

`D:\tf2-builds-dl\tf2-old-manifests-chunks\`:
- `versions.txt`: 32,267 B, sha256 `45e826959dab63ec16ad59b8da535cd0d9f24a0493d876777748f5f6fa4b1ba4`;
- `revemureference.txt`: 1,211 B, sha256 `7313c7e60ad5f29561e06d15b9b2b95988727f64f4b7ec6af476cc23d42e284f`.

`versions.txt` lists **387 dated depot-manifest sets**, one per update (each names `engine 216 n`, `tf2 441/442/443 n`),
from **1.0.0.0, 2007/09/17, to 1.2.6.4, 2013/04/19**. That range covers **every never-seen protocol window**:

| gap | builds the manifest list has inside the window (version, date) |
|---|---|
| 12–13 (9 Oct → 15 Nov 2007) | 1.0.0.6 10/25, 1.0.0.7 10/31 + 11/01, 1.0.0.8 11/07, 1.0.0.9 11/15 |
| 17 (15 Jun → 1 Nov 2011) | 1.1.5.8 06/17 … 1.1.8.0 10/28, about 40 sets |
| 20 (3 Nov → ~Dec 2011) | 1.1.8.1 11/02 … 1.1.9.2 12/23 |
| 23 (after build 5126 → 25 Mar 2013) | 1.2.x through 1.2.6.0 03/19 |
| late 15 / 1.1.4.6 | 1.0.6.3 2009/08/13 onward; 1.1.4.6 2011/04/20 |

**Limits, stated as found:**
- A manifest set listed is not proof that its chunks are in the 15.5 GB `chunks_tf2.zip`. The uploader calls the item
  "very messy".
- The uploader's own assembly "crashed on map load".
- `revemureference.txt` says these builds need RevEmu to launch, from 1.1.0.0 (2010/07/23) on also `-steam`, and an
  engine.dll patch for 1.1.0.6–1.1.1.2. RevEmu is not in the item.
- No protocol number is in either file. Each build's protocol comes from its `engine.dll` (D30).

**Proposal, the cheapest way to settle all four gaps:**
1. Fetch `manifests_tf2.zip`, a fraction of the item; its size is in the item metadata.
2. Use a reader **we write** (never the item's `example_extract.py`, which is downloaded code) to locate `bin/engine.dll`
   in one manifest per gap.
3. Pull just that file's chunks.
4. Read the protocol constants statically, as D30 did.

That dates each gap's build without launching anything. Running such a build later would need RevEmu, which is not on
disk for those versions, so that is a separate owner decision.

### The 2010 client 1.1.0.0 (downloaded 2026-10-28 with the owner's yes; extracted, nothing executed)

- **Zip:** `D:\tf2-builds-dl\tf-2-1.1.0.0-19-jul-2010\TF2 1.1.0.0 (19 Jul 2010).zip`, 2,986,998,821 B. MD5
  `ad72616d9b107a4295abe4fb5596ba5e` and SHA-1 `4f3f9c8ad2fa2504857ed681483e5b75d86b67a7` both equal archive.org's
  `/metadata/tf-2-1.1.0.0-19-jul-2010/files`.
- **Zip-slip check:** none of its 39,012 entries is absolute or contains `../`.
- **Extracted** into the new folder `D:\tf2-builds-dl\tf2-2010-1.1.0.0-extracted\TF2 1.1.0.0 (19 Jul 2010)\`:
  38,261 files, equal to the zip's 38,261 file entries.
  - **Instrument trap, worth keeping:** MSYS `unzip` refused all 8,559 `hl2/` entries ("hl2 exists but is not
    directory"). Its `.exe` magic resolves `hl2` to `hl2.exe`. The `hl2/` subtree was taken with Windows `tar.exe -k`
    instead, and the file count above is the control that the extraction is now whole.
- **Identity:**
  - `tf/steam.inf` reads `PatchVersion=1.1.0.0`.
  - `engine.dll` carries the date "Jul 16 2010" and is byte-identical to the copy already at
    `D:\tf2-builds\tf2-2010-1.1.0.0\bin\engine.dll` (`cmp`).
  - It ships `tf/bin/server.dll` and 43 maps, `cp_granary.bsp` among them (no `.nav`).
  - Its protocol is **15, interpolated**: between build 3862 (15) and 9 Nov 2010 (15). The dialect is unmeasured until a
    demo exists.
- **Its loader** is `Launch Team Fortress 2.bat`. The bat deletes `rev-client_440.log`, runs `rmdir ./RemStorage`, then
  `cd platform\SmartSteamEmu` and `start SmartSteamLoader.exe`. A `rev.ini` (RevEmu) also ships.
  `revemureference.txt` puts 1.1.0.0 of 2010/07/19–21 under "Revemu 9.83", before the "-steam required" builds.

**What would run, pending the owner's separate yes (nothing has):**
1. `hl2.exe` direct, through `mcp__tf2__launch gameDirectory=<that folder> executable=hl2.exe extraArguments="+tv_enable 1"`.
   This is the first try, with the same procedure as every era.
2. If `hl2.exe` will not start without its emulator, the fallback is `platform\SmartSteamEmu\SmartSteamLoader.exe`.
   That is a new executable, and the loader cannot carry RCON arguments, so this needs a further decision. The `.bat`
   itself is never run: it deletes a log and a folder.

That would close the **late protocol-15 dialect** with a POV + SourceTV pair, the gcor cell B440 has lacked.

**Folded into Phase B:**
- 2009 STV (new cell);
- the 2013 same-session pair (new cell);
- the live scripted pair.

**Proposals for the owner, smallest first:**
1. the two text files from `tf2-old-manifests-chunks` (kilobytes), to learn whether builds inside 12–13, 17, 20 or 23 exist at all;
2. the 1.1.0.0 ZIP (2.99 GB) for the late-15 dialect;
3. anything else only if (1) shows it inside a gap.

## Phase B run procedure (exact; nothing below runs until "B259 timed runs finished")

**Order:** 2007 (dry run first), then 2008, 2009, 2011, 2013, live. Oldest first, because the oldest refuses most.

**Tf2Mcp** (owner decision 1): branch `feat/per-era-game` in `F:\source\repos\Tf2Mcp`, commit `3a2485a`.
- `launch` takes `gameDirectory`, `executable` (a bare file name inside that folder) and `consoleLog`, held for the
  session.
- Tests went from 11 to 17 and all pass, with zero warnings. Sabotaged twice: each break reddened only its own test.
- **The running MCP server is the old binary.** `tf2mcp.exe` (PID 33816) holds `src/Tf2Mcp/bin/Debug`, which is why
  the change was built in Release. Using it means:
  1. merge in Tf2Mcp;
  2. stop that server process (it is the session's own MCP connection, so this is the owner's or lead's step);
  3. `dotnet build` Debug;
  4. reconnect the MCP.

**Per era, under `run-exclusive.ps1` for the whole session:**

0. **Launcher first** (owner, 2026-10-29: *"the agent should always look for a bat or shortcut first"*).
   - List the folder, one level up and one down, for `.bat`, `.cmd`, `.lnk` and loader executables (`RUN_TF2.exe`,
     `SmartSteamLoader.exe` + `.ini`, `tf2.exe`, `rev.ini`/`steamemu.ini`), and read each one.
   - Start the client the way it ships, with the RCON arguments on the launcher's own command line. Hash the launcher
     before and restore it after.
   - `hl2.exe` raw only where no launcher exists.
   - A client the owner must click is driven over RCON with scratchpad `rcon.ps1`, because the tf2 MCP's launch guard
     refuses to attach while a client exists.
   - `+fps_max 66` goes on the launch line in every case.

1. **Before.** Take a hashed listing (name, size, sha256) of `<G>/tf/`, `<G>/tf/cfg/` and `<G>/tf/screenshots/` into
   `D:\tf2ds-specimen-backup\<era>\before.txt`, where `<G>` is the era's folder.
   - For each file about to be placed (`tf/cfg/tf2ds_specimen.cfg`, `tf/cfg/tf2ds_era_<era>.cfg`,
     `tf/cfg/tf2ds_era_pre2011.cfg` for ≤2009, `tf/cfg/tf2ds_dryrun.cfg`, `tf/voice_input.wav`), copy any existing one
     aside with its hash.
   - Copy `tf/cfg/config.cfg` aside too.
2. **Place.** Copy from `tools/specimen/` with `cp -n`: those cfgs and `voice_input.wav`, sha256
   `1D3E14C04325E69DBE0450CE037C3D36150CAE1EAAFBE69AF4A3C03794EA0B2D` (written by `make-voice-wav.ps1`).
   **Live is the exception:** its `tf/cfg/tf2ds_specimen.cfg` already exists (the 2026-10-02 leftover). Copy it aside,
   overwrite it, and put it back afterwards.
3. **Launch:** `mcp__tf2__launch gameDirectory=<G> executable=hl2.exe extraArguments="+tv_enable 1"`. For live, leave
   both empty.
   - **2011 (REVEmu):** its `.bat` first runs `reg add HKCU\Software\Valve\Steam\ActiveProcess /v SteamClientDll`,
     which is a registry write. Try `hl2.exe` direct first. If that fails, the registry write is reported for the
     owner's yes, not run.
   - **2007 (steamemu):** `hl2.exe` direct first. `RUN_TF2.exe` is the fallback the owner allowed, but it cannot
     carry RCON arguments, so it can only be driven by hand.
4. **Read the build.** `version` goes into the session log, then `sv_allow_wait_command` and `cl_savescreenshotstosteam`
   are read back (the latter set to 0 where it exists).
5. **Load the map.** `map cp_granary`, then `wait_for` the map. `tv_enable` was set at launch.
6. **Dry run (first session on each era).** `con_logfile tf2ds_dry_<era>.log; exec tf2ds_specimen; exec tf2ds_era_<era>; exec tf2ds_dryrun; ds_start`.
   - Then `wait_for "tf2ds_specimen done"`.
   - Each step's time comes from the `tf2ds_mark` lines.
   - Every `Unknown command` and refusal goes into `docs/RECORDING_CHECKLIST.md` as an era finding.
   - The capture and building outcomes are checked in the demo of the real run.
7. **Real run.** `map cp_granary` again, then `exec tf2ds_specimen; exec tf2ds_era_<era>; ds_start`, then
   `wait_for "tf2ds_specimen done"`. The client is reused across eras' steps, never relaunched to reset (shared-fixture
   rule).
8. **Collect.** Move `tf/tf2ds_specimen.dem` and `tf/tf2ds_specimen_stv.dem` to lcor as
   `tf2-<year>-build<n>-spec-pov-cp_granary.dem` and `…-spec-stv-cp_granary.dem`. Move `console.log` and the dry-run log
   to the scratchpad.
9. **After.** `quit`. Restore every placed file from step 1, or move it out where nothing existed, and restore
   `config.cfg`.
   - Take the after-listing and diff it against `before.txt`. **The empty diff is pasted here.**
   - Any residue that would need a deletion goes to the lead.
10. **Census each new demo against the one it replaces:**
    - protocol, duration;
    - classes and conditions seen;
    - event kinds;
    - packet-delta histogram;
    - voice codec;
    - `teamplay_point_captured`;
    - the built objects' classes;
    - for 2007, the `dem_datatables` sha256 against `build3258-datatables.bin` (the 3A prediction);
    - the decoder's condition field set per era (section 4).

    Each condition's bit is read from the demo after its `addcond`.

## Session log (Phase B)

### 2007, build 3258 — `F:\tf2-builds\tf2-2007\Team Fortress 2`, `hl2.exe` direct (no `RUN_TF2.exe`)

**Before (23:3x):**
- Hashed listing taken (`before.txt`, scratchpad `era-2007/`).
- `tf/cfg/config.cfg` (sha256 `89f8d443…2334d`) and `tf/console.log` (`1e793cd5…14943`, 6,805 B) copied aside.
- No `tf2ds_*` file and no `voice_input.wav` existed. Placed with `cp -n`: `tf2ds_specimen.cfg`, `tf2ds_era_2007.cfg`,
  `tf2ds_era_pre2011.cfg`, `tf2ds_dryrun.cfg`, `tf/voice_input.wav`.
- Desktop lock held by a background `run-exclusive.ps1` holder for the session.

**Attempts 1 and 2, Steam ONLINE: the client hung at startup.**
- Both launches went through `mcp__tf2__launch` (attempt 2 added `+map cp_granary`).
- The command line was right, but RCON never opened; no TCP or UDP socket was bound by `hl2.exe`.
- `console.log` ends right after `autoexec.cfg`/`pinconfig.cfg` ("Comanglia' frames config loaded"). No `+` command ran:
  no `map`, no `tv_enable` line.
- Attempt 2's client was `Responding=False`, and the coordinator force-killed it at the owner's request.
- After-listing diff: only `tf/console.log` changed (6,805 → 18,851 B); `config.cfg` was unchanged. The hung session's log
  is kept as `console-hang-steam-online.log`.
- **Not a build finding**: the owner then put Steam in offline mode (`loginusers.vdf` `WantsOfflineMode "1"`).

**Attempt 3, Steam OFFLINE: the client launched.** RCON answered.
- `version`: `Protocol version 11`, `Exe version 1.0.0.5 (tf)`, `Exe build: 18:14:51 Oct  9 2007 (3258)`.
- `sv_allow_wait_command 1`.
- `map cp_granary` loaded, with SourceTV on port 27020 (`delay 30.0s`).

**Dry run (no record), about 3.5 min from `ds_start` to `tf2ds_specimen done`. Every step ran in order.**

Refused by the build, which are findings for RECORDING_CHECKLIST:
- `cl_disablehtmlmotd`, `mp_respawnwavetime`, `mp_disable_respawn_times` and `tf_bot_quota` printed `Unknown command:`.
  `tf_bot_add` (×4) and every `addcond`/`removecond` did the same.
- **Instrument note:** `mp_respawnwavetime` was a whole NUL-delimited string in the 2007 `server.dll`, and still not a
  command. A string present is not a registered ConVar, so section 4's "ok" for it in 2007 is wrong.
- The two deliberate unknowns were refused as intended.

Worked:
- `say` and `say_team` (printed with `*DEAD*`, since the player was not alive yet).
- `jointeam`, every `joinclass`, `taunt`/`kill` (`suicided`), `explode`.
- `name tf2ds_renamed` (later lines read `tf2ds_renamed suicided.`).
- `pause` ("paused the game"), `mp_restartgame 1` ("The game will restart in 1 second"), `disconnect`.

Finding:
- **The UTF-8 name failed**: `name Пётр大将` printed the cvar's help and current value (`"name" = "PinKushin"`), the
  behaviour of `name` with no argument. Build 3258 drops the non-ASCII token when it reads it from a cfg.

Not yet confirmed:
- `build 3`/`build 0`, `setpos` + capture, and voice print nothing. They are read from the recorded demo.

**Instrument trap:** `con_logfile <file>` diverted this build's console away from `-condebug`'s `console.log`. That file
stopped at 23:51:18, so `mcp__tf2__wait_for` (which tails `console.log`) never saw the marker, and the wait timed out
with the chain already done. The con_logfile is written only at the chain's end or when it closes. For this era, wait on
the con_logfile itself, not on `wait_for`.

**tv_delay:** build 3258 clamps `tv_delay` to at least 10 (`min. 10.000000`). The shared script now stops SourceTV 726
frames after the POV (`ds_pov_stop`, `wait 726`, `ds_stv_stop`), so the SourceTV demo keeps its last 10 s. This applies to
every era, for comparability.

**Real run** (`tv_delay 0` → clamped to 10, `map cp_granary`, then `exec tf2ds_specimen; exec tf2ds_era_2007; ds_start`):
- Started; "Recording SourceTV demo to tf2ds_specimen_stv.dem...".
- The player's name at start was `tf2ds_renamed`, carried over from the dry run, because the UTF-8 rename fails.
- It finished: `Completed demo, recording time 275.9, game frames 4764`. The predecessor agent had already moved both
  demos to lcor as `tf2-2007-build3258-spec-pov-cp_granary.dem` (1,357,904 B) and `…-spec-stv-…` (582,673 B) when it was
  stopped (by accident), between collect and restore.
- **This pair predates the two script fixes below** (fps_max, sv_cheats). 2007 shows no "Can't use cheat" line, but its
  `setpos`/capture is unverified. It is re-recorded after the other eras for comparability; the first pair stays in lcor,
  renamed apart.

**Restore (resumed by a second agent, 00:16):** no `hl2.exe` running. Against `before.txt`:
- `tf/console.log` and `tf/cfg/config.cfg`: the session versions were moved to the scratchpad, and the aside copies were
  put back.
- The two con_logfiles, the four placed cfgs and `voice_input.wav` were moved out.
- **Residue, the lead's or owner's call:**
  - `tf/demoheader.tmp` was rewritten by the client, and no pre-session copy of it was kept.
  - Outside the listed dirs, the client also rewrote `platform/config/{ingamedialogconfig,serverbrowser}.vdf` and
    `tf/DownloadLists/cp_granary.lst`, which have no pre-copy either.
  - From 2008 on, the listing and the aside copies cover `tf/DownloadLists/` and `platform/config/`, and every file in
    the listed dirs is copied aside, so any change is restorable.

### Two script findings, measured on 2013 (build 5252), fixed in the shared cfg

1. **`fps_max` is `notconnected`.** The cfg runs on a loaded map, so its `fps_max 66` prints "Can't set fps_max when
   connected" and does nothing. The 2013 client was at `fps_max 0`, and the whole chain ran in 32.7 s: every `wait` is
   frames. `fps_max 66` now goes on the launch line (`+fps_max 66`) or before `map`. The 2013 aborted demos are kept in
   the scratchpad as `aborted-fps0-*`.
2. **The owner's class cfgs switch cheats off.** `scout.cfg` … `soldier.cfg` exec his `pinconfig.cfg`, and that ends
   `wait 60; sv_cheats 0`. So every `joinclass` turns cheats off 60 frames later, and `setpos` was refused ("Can't use
   cheat command setpos in multiplayer"); `addcond` is a cheat too. The same `pinconfig.cfg` ships in the 2007, 2008,
   2011 and 2013 folders. The shared cfg now sets `sv_cheats 1` right before `setpos` and before the first `addcond`.
   Run 1 on 2013 (cheats off) is kept in the scratchpad as `run1-cheatsoff-*`.

The starting name is now set to `tf2ds_specimen` before `ds_start` on every era, so each run starts from the same
userinfo whatever the last session left.

### The tf2 MCP cannot attach while a client exists (instrument)

`~/.claude/hooks/guard-tf2-fixture.ps1` denies `mcp__tf2__launch` whenever any `hl2`/`tf_win64` process exists, which
includes the attach case. A client started outside the MCP (a `.bat`, a `.lnk` the owner double-clicks) therefore cannot
be driven through it. The workaround here is a minimal Source RCON client (scratchpad `rcon.ps1`), which uses the
password Tf2Mcp stores, plus reading `console.log` directly. No hook or configuration was changed. An attach exemption is
the owner's call.

### 2008, build 3420 — `F:\tf2-builds\tf2-2008`: refused to start, waiting on the owner's double-click

- **Before:** `before.txt`, and every file in the listed dirs copied aside (104). Placed: the four cfgs
  (`tf2ds_era_2008`, `tf2ds_era_pre2011`, specimen, dryrun) and `voice_input.wav`.
- **Attempt 1:** `mcp__tf2__launch gameDirectory=F:\tf2-builds\tf2-2008 executable=hl2.exe
  extraArguments="-window +tv_enable 1"`. It timed out, with no RCON.
- **Attempt 2:** `hl2.exe -game tf -window -condebug`, with the working directory set to the game folder (Start-Process,
  no Tf2Mcp arguments). It showed the dialog **`CFileSystem_Steam::Init() failed: failed to find steam interface`**
  (read through UI Automation).
  - Neither attempt wrote a `console.log`.
  - Steam was offline. `HKCU\…\ActiveProcess\SteamClientDll` was untouched (`…\Steam\steamclient.dll`).
- **Start-Process on the `.lnk`** (unmodified, and with arguments added) started no process within 30 s. Its
  WorkingDirectory `D:\Desktop\tf2-retail\dump` does not exist on this machine.
- **What the build reads:**
  - `bin\` holds `FileSystem_Steam.dll` and `steam_api.dll`, and no `FileSystem_Stdio.dll`.
  - The strings in `FileSystem_Steam.dll` are `steam.dll`, `Steam006` and `SteamAPI_GetSteamInstallPath`, so it loads
    `Steam.dll` from the Steam install and asks it for `Steam006`.
- The owner says the shipped shortcut works for him. So the recipe is **his double-click on the edited shortcut**:
  - `Start Team Fortress 2.lnk`, original sha256 `D8D5057B…0373`, copied aside.
  - Its Arguments are `-game tf -window -condebug -usercon +ip 127.0.0.1 +hostport 27025 +rcon_password <Tf2Mcp's>
    +net_start +tv_enable 1`.
  - Restore: copy the aside bytes back and verify the hash.
- After the failed attempts, the listing diff showed only the placed files. The client wrote nothing.

### 2009, build 3862 — the owner's 3862 extract, waiting on his double-click

- `D:\team fortress 2` (also `PatchVersion=1.0.5.9`) was prepared and given one attempt:
  - `mcp__tf2__launch … extraArguments="-appid 440 -steam +tv_enable 1"` timed out, with no `console.log`.
  - Its `tf2.lnk` targets `T:\client\steamapps\stopthispain\team fortress 2\hl2.exe`, a drive this machine lacks.
  - On the coordinator's word, the folder was then **restored and left untouched**: the placed files were moved out,
    and the listing diff is empty.
- The canonical 2009 client is now `D:\tf2-builds-dl\team-fortress-2-3862\team fortress 2 3862\team fortress 2\`:
  - Before-listing taken; cfgs and wav placed.
  - `tf2.lnk` original sha256 `2CFA0465…2D88`, copied aside.
  - Its Arguments are the shipped `-game tf -appid 440 -steam -windowed -w 800 -h 600`, then the same RCON arguments as
    2008.

### 2013, build 5252 (exe 1729296) — `F:\tf2-builds\tf2-2013\TF2`: done

- **Launch:** `mcp__tf2__launch gameDirectory=F:\tf2-builds\tf2-2013\TF2 executable=hl2.exe extraArguments="+tv_enable 1"`
  launched raw: it ships `FileSystem_Stdio.dll` and its own `bin\Steam.dll`.
- **Build:** `version` printed `Protocol version 24`, `Exe version 1729296 (tf)`, `Exe build: 17:24:29 Mar 25 2013 (5252)`.
  `sv_allow_wait_command 1`.
- **Run 3** (after both fixes): `fps_max 66; name tf2ds_specimen; map cp_granary`, `tv_delay 10`.
  - Every mark ran in order; the only refusals were the two deliberate unknowns and the owner's
    `cl_hud_playerclass_use_playermodel`.
  - POV `Completed demo, recording time 179.2, game frames 10963`; SourceTV `recording time 182.6`.
- **lcor:**
  - `tf2-2013-build1729296-spec-pov-cp_granary.dem`: 2,459,096 B, sha256 `0a651dd2…983c`.
  - `tf2-2013-build1729296-spec-stv-cp_granary.dem`: 940,557 B, `a992336f…e3d9`.
- **Restore:** `console.log`, the placed cfgs, the wav and `DownloadLists/cp_granary.lst` were moved out. `config.cfg`,
  `demoheader.tmp`, `modelsounds.cache`, `tf2_playerstats.dmx` and `platform/config/ingamedialogconfig.vdf` were
  restored from aside. **Diff against `before.txt`: empty.**

### 2011, build 4604 — `F:\tf2-builds\tf2-2011\Team Fortress 2`: done

- **Registry:**
  - Before the session, `HKCU\Software\Valve\Steam\ActiveProcess\SteamClientDll` was String
    `C:\Program Files (x86)\Steam\steamclient.dll` (saved in the scratchpad's `era-2011/reg-before.txt`).
  - It was set to `""`, as the shipped `Team Fortress 2.bat` does.
  - After the session it was set back and read back: String, same value.
- **Attempt 1** went through `mcp__tf2__launch` with Tf2Mcp's default arguments (`-insecure -novid -windowed -noborder
  …`) plus `-steam -appid 440`. The client loaded its cfgs, then shut down cleanly without opening RCON (`console.log`
  ends in font and material teardown).
- **Attempt 2: the bat's own arguments plus the RCON ones.** The client stayed up, and RCON answered.
  - Command: `hl2.exe -game tf -steam -appid 440 -condebug -usercon +ip 127.0.0.1 +hostport 27025 +rcon_password …
    +net_start +fps_max 66 +tv_enable 1`, with the working directory set to the game folder.
  - It was driven through `rcon.ps1`.
- **Build:** `Protocol version 16`, `Exe version 1.1.5.8 (tf)`, `Exe build: 13:46:52 Jun 15 2011 (4604)`. `status` says
  `secure`, because there was no `-insecure`. `cl_savescreenshotstosteam` is an unknown command in this build.
- **Run:**
  - Every mark ran in order, and there was no cheat refusal.
  - POV `recording time 175.0, game frames 11139`; SourceTV `179.9`.
- **lcor:**
  - `tf2-2011-build4604-spec-pov-cp_granary.dem`: sha256 `3464e7fa…13eb1`, 2,563,188 B.
  - `tf2-2011-build4604-spec-stv-cp_granary.dem`: `3b999e85…c01b`, 894,814 B.
- **Restore:** the same moves as 2013, plus `tf/cfg/user.scr` restored. **Diff against `before.txt`: empty.**

### Live, build 11138802 — the Steam install: done

- **Launch:** `mcp__tf2__launch extraArguments="+fps_max 66 +tv_enable 1"`, which starts `tf_win64.exe`.
- **Build:** `version` printed `Build Label 11138802`, protocol 24.
- **Before:** the 2026-10-02 leftover `tf/cfg/tf2ds_specimen.cfg` was copied aside and moved out, then the current one
  placed.
- **Run 1, Steam offline:** 70 s in, at the demoman step, the server dropped its own local client: `STEAMAUTH: Client
  PinKushin received failure code 1 … Disconnect: Client not connected to Steam`. The kicked pair is kept in the
  scratchpad (`run1-steamauth-kick-*`). **Finding: a live listen server with Steam offline needs `sv_lan 1`.**
- **Run 2:**
  - `sv_lan` read `0`, was set to `1` for the session, then set back to `0` and read back before quit.
  - Every mark ran.
  - POV `recording time 278.1, game frames 5946`; that is about 21 fps, below `fps_max 66`, but the chain counts frames.
  - SourceTV `260.2`.
- **lcor:**
  - `tf2-live-build11138802-spec-pov-cp_granary.dem`: 2,412,088 B, `d017823c…7099`.
  - `tf2-live-build11138802-spec-stv-cp_granary.dem`: 1,654,479 B, `e83851ca…6a36`.
- **Restore:** `console.log`, `demoheader.tmp`, `tf2_playerstats.dmx` and `config.cfg` were put back from aside. Both
  2026-10-02 leftovers are back byte-identical: the cfg, and `tf/tf2ds_specimen.dem`, which this run's recording had
  overwritten in place. **Diff against `before.txt`: empty.**

### The downloaded builds: inventory (read-only) and run recipes

All three were booted by the owner first, 2026-10-29; only after that did he clear them to be launched here. Executables
were checked with `Get-AuthenticodeSignature`, and nothing was run during the inventory. "Extracted" counts are files.

| build | folder | files | steam.inf | engine.dll date | executables (size, sha256 prefix, signature) |
|---|---|---|---|---|---|
| 1.1.0.0 | `D:\tf2-builds-dl\tf-2-1.1.0.0-19-jul-2010\TF2 1.1.0.0 (19 Jul 2010)\TF2 1.1.0.0 (19 Jul 2010)\` (the owner's extract) | 38,261 | 1.1.0.0 | Jul 16 2010 | `hl2.exe` 103,760 `EDB01AEA` Valid; `platform\SmartSteamEmu\SmartSteamLoader.exe` 232,448 `44E6E203` NotSigned; `VTF2ItemsTool.exe` 58,880 and `bin\VTF2ItemsTool64/86.exe` NotSigned; `Launch Team Fortress 2.bat`; `rev.ini` |
| 3862 (2009) | `D:\tf2-builds-dl\team-fortress-2-3862\team fortress 2 3862\team fortress 2\` (the owner's extract) | 55,829 | 1.0.5.9 | Jun  4 2009 | `hl2.exe` 98,304 `C59B406E` NotSigned; `tf2.lnk` (original `2CFA0465`); `tf\*.bat` are Valve's reslist scripts |
| 1.0.9.5 | `D:\tf2-builds-dl\tf2-2010-1.0.9.5-extracted\Team Fortress 2\` | 38,761 (= the 7z's 38,761 files, 901 dirs; no absolute or `..` entry) | 1.0.9.5 | Jun  8 / Jun 10 2010 | `hl2.exe` 103,760 `D044773D` Valid; `tf2.exe` 44,032 `4865E3B9` NotSigned (a ShellExecuteEx loader); REVEmu `steam.dll`, `steamclient.dll`, `rev.ini` in the root; `VTF2ItemsTool64/86.exe` NotSigned |

**Run recipes** (RCON args = `-condebug -usercon +ip 127.0.0.1 +hostport 27025 +rcon_password <Tf2Mcp's stored> +net_start`):

- **1.0.9.5:** `hl2.exe -game tf -steam <RCON args> +fps_max 66 +tv_enable 1`, with the working directory set to the
  game folder, started directly. It ran first time.
  - `version` printed `Protocol version 15`, `Exe version 1.0.9.5 (tf)`, `Exe build: 16:13:38 Jun 10 2010 (4231)`.
  - It runs `secure`, and its REVEmu steamid is `STEAM_0:0:797930894`.
- **1.1.0.0:** the owner reports that `hl2.exe` started bare errors for want of `gameinfo.txt` (no `-game tf`). Its
  `Launch Team Fortress 2.bat` runs `del rev-client_440.log`, `rmdir ./RemStorage`,
  `cd platform\SmartSteamEmu`, then `start SmartSteamLoader.exe`.
  - `SmartSteamEmu.ini` has `Target = ..\..\hl2.exe`, `CommandLine = -steam -game tf`, `AppId = 440` and
    `Offline = 0`.
  - Recipe: copy the ini aside (sha256 `5F8635E0…3373`), then extend `CommandLine` with the RCON args,
    `+fps_max 66 +tv_enable 1` (scratchpad `ini-args.ps1`, which asserts exactly one match).
  - Start `SmartSteamLoader.exe` with the working directory set to `platform\SmartSteamEmu`. The `.bat` itself is not
    run, because it deletes a log and a folder.
  - Restore the ini from aside and verify the hash.
- **3862:** the owner double-clicks the edited `tf2.lnk`. Its shipped Arguments come first, then the RCON args.
- **2008:** the owner double-clicks the edited `Start Team Fortress 2.lnk`, with the same arrangement.

### 2010, build 4231 (1.0.9.5): done

- **Launch:** started raw. Its root has no `.bat`/`.lnk`; `tf2.exe` is the shipped loader and was not used (step 0
  postdates this run).
- **Build:** `Protocol version 15`, `Exe build: 16:13:38 Jun 10 2010 (4231)`.
- **Run:**
  - POV `recording time 287.2, game frames 4594` (about 16 fps); SourceTV `281.6`.
  - The only refusals were the two deliberate unknowns.
- **lcor:**
  - `tf2-2010-build4231-spec-pov-cp_granary.dem`: 1,596,834 B, `fabd8315…05aa`.
  - `tf2-2010-build4231-spec-stv-cp_granary.dem`: 969,619 B, `382db165…d877`.
- **Restore:** diff against `before.txt` empty.
- **Residue outside the listing:** the emulator appended to the root's `rev-client.log`, and no pre-copy exists.

### 2010, build 4269 (1.1.0.0): done

- **Launch:**
  - `SmartSteamEmu.ini` `CommandLine` was extended (`ini-args.ps1`).
  - `SmartSteamLoader.exe` was started with its working directory set to `platform\SmartSteamEmu`; the loader started
    `hl2.exe`, and RCON answered.
  - The ini was restored from aside straight after (hash match).
- **Build:** `Protocol version 15`, `Exe build: 16:21:09 Jul 16 2010 (4269)`.
- **Run:**
  - POV `280.6 s, 4457 frames`; SourceTV `283.8`.
  - The only refusals were the two deliberate unknowns.
- **lcor:**
  - `tf2-2010-build4269-spec-pov-cp_granary.dem`: 1,630,885 B, `b8cbd10e…4050`.
  - `tf2-2010-build4269-spec-stv-cp_granary.dem`: 1,104,843 B, `2ab3dded…9c16`.
- **Restore:** diff empty. The emulator appended to the root's `rev-client.log`.

### 2007 re-record (with the fixed script): froze when started raw, now waiting on the owner's click

- The first 2007 pair was renamed apart in lcor to `tf2-2007-build3258-spec0-{pov,stv}-cp_granary.dem`.
  `spec-pov` is not a substring of `spec0-pov`, so `Corpus.Demo` cannot confuse the two.
- **Attempt:** `mcp__tf2__launch … executable=hl2.exe extraArguments="+fps_max 66 +tv_enable 1"`.
  - `hl2.exe` went `Responding=False` after the owner's config banner, and wrote no line after it.
  - UIA showed one `Valve001` window and no dialog; the owner saw nothing pop up either.
  - Steam was offline (`WantsOfflineMode "1"`).
  - Killed after 9 min (its PID was this session's launch). The folder was restored, diff empty.
- **Diagnosis, unconfirmed:**
  - `RUN_TF2.exe` (37,376 B) carries the string `-game tf -steam -console`, imports `GetCommandLineA`, and starts
    `hl2.exe`. Tf2Mcp passes neither `-steam` nor `-console`.
  - The owner thinks the first 2007 recording was his own launch. The record above says `mcp__tf2__launch`.
    **Unresolved.**
- **Recipe now:** a NEW shortcut, scratchpad `TF2-2007-specimen.lnk`, so nothing in the game folder is edited.
  - It targets `RUN_TF2.exe` with its working directory set to the game folder, and its Arguments are the RCON args
    plus `+fps_max 66 +tv_enable 1`.
  - The owner double-clicks it.

### Census of the first pass: coverage missing in every era, and the script fixed (2026-10-29, 02:00–02:30)

`tf2demosalvage -s` was run over every new pair and every old gcor specimen (scratchpad `census/`).
- **No new specimen carried `teamplay_point_captured`**, though the old ones did.
- Engineer buildings were not confirmed.
- No bot appeared.

What the bytes showed:
- **Instrument note.** The script's own commands are recorded in `dem_consolecmd` with a leading space
  (`command " setpos …"`); a grep for `command "setpos` misses them. Control: `command " kill"` hits 12 times.
  Forwarded commands (`joinclass`, `build`, `addcond`) are not recorded there at all.
- **Capture.**
  - On the old 2008 SourceTV demo, the solo mid capture landed 1,324 ticks after the last starttouch.
  - On a 2013 test demo, `teamplay_point_captured` came 1,608 ticks (24 s) after `setpos`.
  - The first pass held the point 698 ticks (2011) and 1,095 ticks (1.0.9.5). The hold is now `wait 3960`.
- **`wait` is not one frame.** On 2013 at `fps_max 66`, 462 waits spanned 244 ticks (about 1.9 waits per tick).
  Wall time therefore comes only from the marks.
- **Respawn.** A dead player re-joining the SAME class waits the full respawn: death→spawn was 428 ticks against 50 for
  a different class (2013, every death in the run). So `ds_engineer_build` and `ds_spy_disguise`, which follow their own
  class, ran while dead. Both now join scout first.
- **Buildings.**
  - The `buildings` probe on the first 2013 pass shows the sentry `placing True` and never built.
  - In a hand test, a dispenser at (-1536 300 -340) reached `m_bBuilding 1`.
  - Both now go beside mid. `impulse 101` refills metal, because the sentry's 130 leaves too little for the dispenser.
- **Bots.** `tf_bot_add` and `bot -team blue …` add nothing on 2013 (hand test, `status`) and print nothing. Recorded,
  not chased: the old specimens had no bots either.
- **Verification on 2013 (v5)**, from the `-e` traces:
  - POV: `teamplay_point_captured` 1, `m_bBuilding 1` twice (CObjectSentrygun, CObjectDispenser; started, not
    finished: see the differences table), `svc_VoiceData` 81,
    `svc_SetPause` 2, 13 deaths.
  - SourceTV: the same.

### Second pass, every self-startable era on the verified script

The first-pass pairs are kept in lcor as `…-spec1-…`; the 2007 first pair is `…-spec0-…`.

"Built" in this table counts `m_bBuilding 1` (construction started), not finished; no dispenser here finished
(the build-times section and the differences table, below).

| era | lcor pair (`…-spec-{pov,stv}-cp_granary.dem`) | POV s | captured | sentry/dispenser built | svc_VoiceData | refusals |
|---|---|---|---|---|---|---|
| 2013, 5252 | `tf2-2013-build1729296` (POV `c221c466…`, STV `b02cf42d…`) | 201.0 | 1 | 2 | 81 | none |
| 2011, 4604 | `tf2-2011-build4604` (`5bb15ce5…`, `11952ade…`) | 202.4 | 1 | 2 | **0** | `cl_hud_playerclass_use_playermodel` (owner's cfg) |
| 2010, 4231 | `tf2-2010-build4231` (`972bd82c…`, `ad7a5187…`) | 308.8 | 1 | 2 | 266 | none |
| 2010, 4269 | `tf2-2010-build4269` (`1fe9aac2…`, `f8e499a9…`) | 314.0 | 1 | 2 | 44 | none |
| live, 11138802 | `tf2-live-build11138802` (`a49f13c7…`, `16bfa996…`) | 200.5 | 1 | 2 | **0** | none |

- No voice in 2011 or live: `voice_inputfromfile` was accepted silently. Recorded as an era finding, not chased.
- **REVEmu rewrites the registry.** The 2011, 1.0.9.5 and 1.1.0.0 clients rewrite
  `HKCU\…\ActiveProcess\SteamClientDll` to their own `bin\steamclient.dll` at launch.
  - It was found set to the 1.0.9.5 path before the 2011 re-record, and with a trailing space after 1.1.0.0.
  - It explains why 2011 shuts itself down on the first launch after the value is blanked and runs on the second.
  - After every such session it was set back to `C:\Program Files (x86)\Steam\steamclient.dll` and read back.
- Every folder's diff against its before-listing is empty.

### Section 3A's prediction, confirmed; and the state left at 03:47

**KnownSchema bytes, which is arithmetic over the files** (scratchpad `dt_hash.py`, which walks the protocol-3
container):
- The 2007 `spec0` POV's `dem_datatables` is 85,063 B with sha256 `ea020a81…e637`. That is byte-identical to
  `managed/Tf2DemoSalvage.Core/Schema/Data/build3258-datatables.bin` and to the old gcor POV.
- The `spec0` SourceTV's payload is cut at exactly 65,536 B (`c5397d22…79f2`), identical to the old gcor STV. B24
  reproduces.
- The schema is a property of the build, so the embedded resource stays and only a test's demo SHA would change.

**UTF-8 name: unresolved.** The bytes `D0 9F D1 91 D1 82 D1 80` (`Пётр`) are present in the 2011, 2013, 1.0.9.5 and
live POV demos. They may be only in the recorded `dem_consolecmd`, though.
- The summary prints 14 `�` for 2011 and `empty` for 2013.
- Whether the userinfo string carries the UTF-8, which would make the display our decode's fault, or something else
  (the cfg read or the engine) is not yet read.
- **Until it is, the old 2013 POV (the UTF-8 bug finder) stays in gcor.**

**Voice codec per era** (`svc_voiceinit`): `vaudio_speex` on 2010 ×2, 2011 and 2013; `steam` on live. The Speex
carriers for `CorpusVoiceCodecEraTests` are therefore the 2010 and 2013 pairs, not 2011.

**The owner-click eras were not run**: no click arrived within 27 minutes of the ready message.
- Everything placed for them was put back:
  - `Start Team Fortress 2.lnk` was restored to `D8D5057B…0373` (match).
  - The 3862 `tf2.lnk` was restored to `2CFA0465…2D88` (match).
  - The 2007, 2008 and 3862 folders' diffs against their before-listings are empty.
- The scratchpad `TF2-2007-specimen.lnk` remains outside every game folder.
- To run them later, repeat for each: `prep.sh`, then `lnk-args.ps1 -Add` (2008, 3862), then the click, then
  `drive.ps1`, then `restore.sh` plus `lnk-args.ps1 -Restore`.

### Owner-click eras, third attempt (2026-10-29, 09:20–10:23): nothing captured

Each era was prepared (`prep.sh`, hashed shortcut, `lnk-args.ps1 -Add`), then the lead got `CLICK NOW: <path>` via
`SendMessage to main` (the session name is not addressable from a subagent; `main` delivered), and RCON 27025 was polled
for 9.5 min. The desktop lock was held throughout (`hold-lock.ps1` under `run-exclusive.ps1`).

- **2007, via the scratchpad shortcut to `RUN_TF2.exe`.** The owner clicked, and `hl2.exe` PID 44188 came up
  `Responding=False`, with nothing listening on 27025.
  - **Finding: `RUN_TF2.exe` drops its own command line.** The client's command line was exactly `"…\hl2.exe" -game tf
    -steam -console` (`Win32_Process`): the string baked into `RUN_TF2.exe`, and none of the shortcut's arguments. So no
    RCON and no `-condebug` (no log line was written). A shortcut to `RUN_TF2.exe` cannot carry arguments.
  - The client froze anyway, as it did on the hl2-direct re-record. The launcher and the arguments are not the cause.
  - **Where the freeze happens:** both earlier hang logs (Steam online; raw re-record) end on the owner's autoexec banner
    `Comanglia' frames config loaded`, before that file's final `exec pinconfig.cfg`. The one boot that worked goes on
    to `execing pinconfig.cfg`, `maxplayers set to 24`.
  - The `execing pinconfig.cfg` line read during the session was the restored pre-session `console.log` (it always ends
    there), and not this run.
  - **Hypothesis, unconfirmed:** `mat_queue_mode 2`, set by the owner's autoexec and archived in `config.cfg`. The
    autoexec's own first line reads "Unexplained crashes? Try changing mat_queue_mode to -1".
  - PID 44188 was ended (the lead agreed); `restore.sh` diff empty.
- **2007, autoexec route** (owner-approved, because `RUN_TF2.exe` drops arguments). The owner's `tf/cfg/autoexec.cfg`
  (sha256 `aa71bd57…3827`) was copied aside and replaced by a self-driving one, in this order:
  1. `mat_queue_mode 0`, `con_logfile tf2ds_2007.log`, `version`, `fps_max 66`, `sv_lan 1`, `sv_cheats 1`,
     `tv_enable 1`, `tv_delay 10`, `name tf2ds_specimen`;
  2. exec of the specimen and `era_2007` cfgs;
  3. `ds_finish` redefined to end in `quit`;
  4. `map cp_granary`, then `wait 1320; ds_start`.

  No click arrived in 9.5 min. The autoexec was restored (hash match) and the diff is empty. **Not run, so the
  mat_queue_mode hypothesis is untested.**
- **2008, build 3420.** The owner's first click worked: RCON answered, `Protocol version 14`, `Exe version 1.0.2.2`,
  `Exe build: 20:17:35 Mar 19 2008 (3420)`, and `cp_granary` loaded with `SourceTV broadcast active`.
  - **Finding: Steam offline drops the local player**: `Dropped PinKushin from server (No Steam logon)`, before the
    chain started. This is the same failure as live run 1; `sv_lan 1` is needed on every Steam-backed era.
  - The chain then ran disconnected, with every `joinclass` `Unknown command` and `SourceTV not active`. No demo.
  - Also: `name tf2ds_specimen` set before `map` did not take (`PinKushin connected`).
  - **The client hung on `quit`**: it wrote `config.cfg`, printed `Unable to remove …\tf\textwindow_temp.html!`, then
    spun the CPU, `Responding=True`, for 2.5 min. Ended.
  - The shortcut was re-edited with `+sv_lan 1`, but the second click did not come in 9.5 min.
  - Shortcut restored (`D8D5057B…0373`); diff empty.
- **2009, build 3862.** `tf2.lnk` (`2CFA0465…2D88`, the same as the aside copy; it targets this folder's `hl2.exe`
  with `-game tf -appid 440 -steam -windowed -w 800 -h 600`) was given the RCON args plus `+sv_lan 1 +fps_max 66
  +tv_enable 1`. No click arrived in 9.5 min. Shortcut restored (hash match); diff empty.

After every era, no `hl2`/`RUN_TF2` process was left, and `SteamClientDll` read
`C:\Program Files (x86)\Steam\steamclient.dll`. The 2007 `spec0` pair stays the only scripted 2007 recording; 2008
and 3862 have none. To retry: `prep.sh <era-rN> <folder> <era cfgs>`, then:
- for 2008/3862, `lnk-args.ps1 -Add -Extra "+sv_lan 1 +fps_max 66 +tv_enable 1"`;
- for 2007, the session autoexec above.

Then the click, `drive.ps1` (2008/3862 only), collect the demos first, then `restore.sh`.

### 2008, build 3420, run 3 (2026-10-29): recorded, POV + SourceTV pair in lcor

- **Prep:** `prep.sh 2008-r3` (104 files listed and copied aside), then the owner's modern `tf/cfg/autoexec.cfg`
  (`aa71bd57…3827`, a 2014 fps config: `mat_queue_mode 2`, `r_threaded_*`, `exec pinconfig.cfg`) and `config.cfg`
  (`89f8d443…334d`, `mat_queue_mode "2"`) moved out, so the client started on `config_default.cfg`. Shortcut
  `D8D5057B…0373` given the RCON args plus `+sv_lan 1 +fps_max 66 +tv_enable 1`.
- **Clean config, measured with `find` before applying anything:** the build knows `mat_queue_mode` (default -1),
  `host_thread_mode` 0, `snd_mix_async` 0, `cl_threaded_*` 0, `r_threaded_client_shadow_manager` 0,
  `r_threaded_renderables` 0, and `r_threaded_particles` **1 by default**. Set over RCON: `mat_queue_mode 0`,
  `r_threaded_particles 0`, `mat_picmip -1`; `mat_forceaniso 16`, `mat_trilinear 1`, `r_rootlod 0`, `r_lod -1`,
  `mat_hdr_level 2` were already the max. `mat_antialias` (1) was left alone (interpolated, not measured: it is believed to apply only through a video mode
  reset).
- **The owner's click came within the 30 min poll.** `version`: `Protocol version 14`, `Exe version 1.0.2.2`,
  `Exe build: 20:17:35 Mar 19 2008 (3420)`. With `sv_lan 1` the player **stayed** (`status`: `PinKushin … active
  loopback`), fixing run 2's `No Steam logon` drop.
- **Finding: `name` does not take in this build, before or after the map.** `name tf2ds_specimen` after the map loaded
  (twice, quoted and unquoted) left `"name" = "PinKushin"` (that the Steam persona overrides the cvar is interpolated, not observed). The chain's UTF-8
  name and its rename are therefore absent from both demos.
- **Finding: the owner's modern class cfgs run on `joinclass`.** `scout.cfg` etc. in `tf/cfg` (crosshairswitcher)
  printed `Unknown command: cl_crosshair_*` / `tf_remember_activeweapon` and `Can't use cheat cvar viewmodel_fov`;
  harmless here, but they should be moved aside too for 2007/2009.
- **Chain:** every mark start…finish, `PinKushin captured the Central Control Point for team #2`, `Completed demo,
  recording time 312.0, game frames 5283`, `Completed SourceTV demo … 311.6`. `tf_bot_add`, `addcond`, `removecond`
  are unknown commands in 3420.
- **Quit:** `disconnect` then `quit`; the client again spun in shutdown (alive at 30 s, `Responding`), and was then
  ended. No `hl2` left; `SteamClientDll` read `C:\Program Files (x86)\Steam\steamclient.dll`.
- **lcor:** `tf2-2008-build3420-spec-pov-cp_granary.dem` (1,814,563 bytes, `753278df…f9ed`) and
  `tf2-2008-build3420-spec-stv-cp_granary.dem` (786,149 bytes, `c5859cf8…342d`).
- **Read back (Cli `-s`/`-t`, Probe `buildings`), POV / STV:** network protocol 14 both, `svc_serverinfo protocol 14
  map "cp_granary"`; 5,283 / 3,985 packets, frame check ok; first `svc_packetentities` updated 227 / 399; 124 / 125
  game events with `teamplay_point_captured` 1, `controlpoint_starttouch` 1, 13 deaths, 14 class changes; 2 chat
  lines; `svc_voiceinit vaudio_speex quality 5` and 44 `svc_VoiceData` each. Sentry (entity 477, type 3) reached
  150/150 and finished building; dispenser (entity 486, type 0) reached 66/150, 44 % built, when the chain's `kill`
  came — the dispenser is placed and building, not finished.
- **Restore:** `restore.sh 2008-r3` diff against the before-listing **empty** (autoexec/config restored from the aside
  copies); shortcut restored, hash match `D8D5057B…0373`.

### Build times: the old builds were not slower (2026-10-29)

The owner thought Engineer built more slowly in old builds. The evidence says no, and the 44 % dispenser was the
script's timing:
- **Read from the game's shipped data:** `tf/scripts/objects.txt` gives `BuildTime 10` for `obj_sentrygun` and `20`
  for `obj_dispenser` (and 20 for both teleporters) in 3258 (2007), 3420 (2008) and 3862 (2009) alike. The 2013 and
  live folders ship it inside VPKs, and those were not read.
- **Measured on the 3862 pair (Probe `buildings`, POV):** from the first tick each object exists to the first tick it
  reads `building False pct 1`, the sentry took 749 ticks (11.2 s) and the dispenser 1,442 ticks (21.6 s). That is
  the shipped time plus the placement.
- **Secondary:** the TF2 wiki's Buildings page gives 10.5 s (sentry) and 21.0 s (dispenser, twice the sentry) as the
  default construction speed (https://wiki.teamfortress.com/wiki/Buildings, read 2026-10-29). Its update history has
  no change to construction time before the July 2, 2015 Gun Mettle redeploy change. The Dispenser page's update
  history has none either (https://wiki.teamfortress.com/wiki/Dispenser, read 2026-10-29).
- A web search of 2008–2009 patch notes found no build-time change. **No source dates a change**, so "old builds were
  slower" is unsupported, not disproven.
- **The 44 % has a different cause, measured on two eras:** the dispenser was killed at health 66/150, pct 0.44, on
  both 3420 (2008) and 4231 (2010). The fixed `wait 198` after placement is frames, and at the ~16 fps these clients
  run, 198 frames is well short of 20 s.

### The fix: the kill waits until both buildings read finished

- `tools/specimen/tf2ds_era_pre2011.cfg` (2007, 2008, 2009 only): `ds_engineer_build` now ends on `echo tf2ds_mark
  engineer_wait`, then `wait 3960; ds_engineer_cont`.
  - The session's driver reads the POV demo as it is being recorded and sends `ds_engineer_go` once both buildings
    read finished. That runs the rest (`kill; wait 99; ds_medic`) and disarms the fallback.
  - With no driver (2007's self-driving autoexec), the fallback runs the rest after 3,960 waits.
- The driver is scratchpad `drive-build.ps1`, which calls `pollbuild.ps1`. The poll reads a copy of the open demo with
  Probe `buildings` and needs the last state of each object to be `building False placing False pct 1`. It is capped at
  60 s, three times the dispenser's 20 s.
- **The controls, run before the session:**
  - On the finished 3420 POV, the poll read sentry done and dispenser 0.44, so `BOTH_BUILT False`.
  - The same file cut to 1.2 MB and to 1.4 MB read the same states, so a partial demo decodes.
  - The 4231 POV read `False` for the same reason. That run is how the 2010 defect was found.
- **Instrument note:** the client flushes the demo in chunks of about 196 KB. During the run, the copy stayed at
  913,408 B for 33 s, then jumped to 1,110,016 B and read both finished. So the poll lags the game by up to a flush.
  That is safe, because it only lengthens the wait.
- **Closed by the third pass (below):** the wait/go/fallback mechanism moved into the shared `ds_engineer_build`, and
  `tf2ds_era_pre2011.cfg` now redefines only the two `build` lines (`ds_build_sentry`, `ds_build_dispenser`). The
  fallback became `wait 7920`. 2008 can be redone with it as it stands.

### 2009, build 3862 (1.0.5.9), run 3 (2026-10-29): recorded, POV + SourceTV pair in lcor

- **Prep:** `prep.sh 2009-3862-r3` (61 files listed and copied aside). No `autoexec.cfg` exists.
  - `config.cfg` (from the extract: `mat_queue_mode "-2"`, `name "MikeD"`) and the nine class cfgs were moved out.
  - `tf2.lnk` (`2CFA0465…2D88`) was given the RCON args plus `+sv_lan 1 +fps_max 66 +tv_enable 1`.
  - The desktop lock was held throughout.
- **The owner's click came within the poll.** `version`: `Protocol version 15`, `Exe version 1.0.5.9 (tf)`,
  `Exe build: 13:52:56 Jun  4 2009 (3862)`. `sv_lan 1`, `sv_allow_wait_command 1`, `fps_max 66`. `status` showed
  `insecure` and the player `active loopback`.
- **`find` before setting anything:**
  - The build knows `mat_queue_mode` (-2), `r_threaded_particles` (**1**), `host_thread_mode` 0, and `cl_threaded_*`
    and `r_threaded_*` (0).
  - Set: `mat_queue_mode 0`, `r_threaded_particles 0`, `mat_picmip -1`. Nothing else threading-related was touched.
  - `mat_forceaniso 16`, `mat_trilinear 1`, `r_rootlod 0`, `r_lod -1` and `mat_hdr_level 2` were already at the max.
  - `find addcond` and `find tf_bot` printed nothing.
- **`name` does not take**, as on 3420: `name tf2ds_specimen` left `"name" = "PinKushin"`.
- **Chain:** every mark ran, from start to finish. `PinKushin captured the Central Control Point for team #2`.
  - The engineer wait read both buildings finished 38 s after `engineer_wait`; `ds_engineer_go` was sent, and the
    fallback later printed `engineer_fallback_disarmed`.
  - `Completed demo, recording time 339.6, game frames 5897`; `Completed SourceTV demo … 348.3`.
  - **Refusals:** `cl_disablehtmlmotd`, `tf_bot_add` ×4, `tf_bot_quota`, the two deliberate unknowns, and one
    `Can't use cheat cvar cl_phys_timescale`. `addcond`/`removecond` printed no refusal.
- **Quit:** `disconnect`, then `quit`. The client exited by itself within 30 s, unlike 3420.
- **lcor:**
  - `tf2-2009-build3862-spec-pov-cp_granary.dem`: 1,865,596 B, sha256 `a635be1e…5e45`.
  - `tf2-2009-build3862-spec-stv-cp_granary.dem`: 1,084,769 B, `9b0294ef…3c05d`.
- **Read back (Cli `-s` and `-t -e`, Probe `buildings`), POV / STV:**
  - Network protocol 15 on both; `svc_serverinfo protocol 15 map "cp_granary"`.
  - 5,897 / 4,840 packets, frame check ok.
  - `teamplay_point_captured` 1, `controlpoint_starttouch` 1, 13 `player_death`, 2 `svc_SetPause`.
  - `svc_voiceinit vaudio_speex quality 5` and 43 `svc_VoiceData` on each.
  - **Both buildings finished on both demos.** Sentry (entity 476, type 3) reached `building False pct 1` at tick 9562
    (POV). The dispenser (entity 485, type 0) reached 150/150 and `building False` at tick 10627.
  - The builder control resolved on 6,647 of 6,647 ticks (POV) and 6,531 of 6,531 (STV).
  - `m_nPlayerCond` takes values beyond 0/1 (16, 32, 128, 2048, 262144 among them). That these are the forced
    conditions is **interpolated**: `find addcond` printed nothing (and `find` does list commands in this build:
    `find thread` showed `thread_test_tslist`), yet no `Unknown command: addcond` was printed. The chain's own steps
    (spin-up, zoom, disguise, cloak, taunt) can set such bits. Unresolved; see the differences table.
- **Restore:** `restore.sh 2009-3862-r3` diff against `before.txt` **empty**. `config.cfg` and the class cfgs came back
  from the aside copies. `tf2.lnk` was restored, hash match `2CFA0465…2D88`. No `hl2` was left, and `SteamClientDll`
  reads `C:\Program Files (x86)\Steam\steamclient.dll`.

### The 2013 and live sentry: the same cause as the 2010 dispenser (2026-10-29)

The `spec2` 1729296 and live POVs left the sentry at pct 0.58 while 4231 finished it. Read with Probe `buildings`
(arithmetic over its ticks, at 66.67 ticks/s, assuming construction is linear):
- **Construction ran at the normal rate.** 1729296: the sentry was `building True` from tick 5976 and at 0.58 by the
  kill at 6381, 405 ticks (6.1 s), so a full build is 10.5 s. The dispenser went from 6211 to 0.13 at 6381, so 20 s.
  Live: 400 ticks to 0.58, 162 to 0.12, the same. 4231: the sentry took 698 ticks (10.5 s) to finish.
- **The kill came sooner.** From the sentry's build start to the kill: 405 ticks (6.1 s) on 1729296 and 400 on live,
  against 970 ticks (14.5 s) on 4231, for the same ~660 waits. So the faster clients killed both buildings, and the
  slower one only the dispenser: the same frame-counted `wait 198` the 2009 fix replaced.
- **Refuted:** spawn placement (both were `placing False`, `building True` at the mid position); an upgrade (level 1
  everywhere, `mode 0`).
- The re-recordings below, with the wait read from the demo, finish both buildings in every era. That is the
  manipulation that confirms the cause.

### Third pass (2026-10-29, 12:37–13:51): the five self-startable eras re-recorded with the shared engineer wait

The shared `ds_engineer_build` now ends on `tf2ds_mark engineer_wait` and the wait/go/fallback aliases
(`tf2ds_specimen.cfg`); scratchpad `era-f/run-era.ps1` drives it (reads the build, loads the map with `fps_max 66;
sv_lan 1; tv_delay 10; tv_enable 1`, runs the chain, polls with `era-f/pollbuild.ps1` and sends `ds_engineer_go`).
The `spec2` pairs (the second pass) were renamed apart in lcor first; `spec-pov` is not a substring of `spec2-pov`.
One client at a time, each session under the desktop lock (`hold-lock.ps1` under `run-exclusive.ps1`), quit before
the next. Every placed cfg's sha256 matched the worktree's (`94b5607f…c14a` for the shared one). After the runs,
`tf2ds_era_2011.cfg` (placed `b15e5d51…`) and `tf2ds_era_live.cfg` (placed `f221abe5…`) gained comments only, and
the shared cfg's engineer comment was reworded, so the committed hashes differ from the placed ones. The new
`tf2ds_era_pre2011.cfg` (only the two `ds_build_*` aliases) had not run on a pre-2011 client at this point (3862 was
recorded with the old wholesale redefinition); its first run is 3420 run 4, below.

| era | launch | POV s / frames | lcor `…-spec-{pov,stv}-cp_granary.dem` (sha256) | captured | sentry / dispenser | svc_VoiceData, codec | engineer step |
|---|---|---|---|---|---|---|---|
| 1729296 (2013) | `mcp__tf2__launch … hl2.exe "+fps_max 66 +sv_lan 1 +tv_enable 1"` | 238.2 / 14,384 | `tf2-2013-build1729296`: POV 3,135,865 B `03162bfb…9e130`; STV 1,114,516 B `29029c3d…8f6` | 1 | done / done | 28, `vaudio_speex` | go at +32 s |
| live 11138802 | `mcp__tf2__launch "+fps_max 66 +tv_enable 1"`; `sv_voicecodec vaudio_celt` before `map` | 262.4 / 16,214 | `tf2-live-build11138802`: 3,754,805 B `040a9f49…ffd4`; 1,719,222 B `4e51fe40…75bc` | 1 | done / done | 79, `vaudio_celt` | fallback, then go (both already done) |
| 4604 (2011) | `SteamClientDll` blanked as the `.bat` does; `hl2.exe -game tf -steam -appid 440` + RCON args (the first start shut down, the second ran, as recorded before); `sv_use_steam_voice 0` before `map` | 264.9 / 13,530 | `tf2-2011-build4604`: 3,119,444 B `a133bb4f…c341`; 1,043,987 B `1f180a17…2979` | 1 | done / done | 88, `vaudio_speex` | go at +40 s |
| 4231 (1.0.9.5) | `hl2.exe -game tf -steam` + RCON args | 280.6 / 11,969 | `tf2-2010-build4231`: 2,590,482 B `6487f0d2…a409`; 1,000,815 B `086a5bd8…b563` | 1 | done / done | 44, `vaudio_speex` | go at +44 s |
| 4269 (1.1.0.0) | `SmartSteamEmu.ini` `CommandLine` extended (`ini-args.ps1`), `SmartSteamLoader.exe`, ini restored once RCON answered | 277.0 / 5,065 | `tf2-2010-build4269`: 1,649,103 B `1fbaa6e7…aa25`; 1,056,173 B `35903ed0…e076` | 1 | done (a mini-sentry) / done | 28, `vaudio_speex` | fallback, then go (both already done) |

**Read back** (Cli `-s` and `-t -e`, Probe `buildings`; scratchpad `era-f/verify/`), POV and SourceTV alike: network
protocol 24 / 24 / 16 / 15 / 15 in table order, `svc_serverinfo … map "cp_granary"`, `teamplay_point_captured` 1, 13
`player_death` (14 on live), 2 `svc_SetPause`. Each demo's builder control resolved on every tick with a builder.
Refusals: only the two deliberate unknowns, plus the owner's `cl_hud_playerclass_use_playermodel` on 1729296 and 4604.
The capture line printed the UTF-8 name as `empty` (1729296) or 14 `�` (4604, 4231, 4269); not chased.

**Voice, settled per era:**
- **4604:** `sv_use_steam_voice` read `1` (`replicated`, def. 1), with the help text "if enabled, voice_inputfromfile
  will no longer function". Set to 0 before `map`: 88 packets. Set back to 1 and read back before quit.
- **Live:** `sv_voicecodec` read `steam` (no flags, so not archived; `sv_use_steam_voice` is a deprecated placeholder
  there, 0). Two 5 s voice-only test demos (scratchpad `era-f/voicetest/`, not lcor): `vaudio_speex` → `svc_voiceinit
  codec "vaudio_speex" quality 11025`, 0 packets, and the console printed "Unable to load voice codec 'vaudio_speex'.
  Voice disabled."; `vaudio_celt` → `quality 22050`, 171 packets. The real run used `vaudio_celt`. Restored to
  `steam`, and `sv_lan` to `0`, both read back before quit.
- 4231 and 4269 have no `sv_use_steam_voice` (`Unknown command`).

**Restore:** every folder's diff against its before-listing is **empty** (`era-f-<era>/after-restore.*.txt`).
- Live's 2026-10-02 leftovers (`tf/cfg/tf2ds_specimen.cfg`, `tf/tf2ds_specimen.dem`) are back byte-identical from the
  aside copies, and `config.cfg` too.
- The root `rev-client.log` the 4231 and 4269 emulators append to was copied before and restored after (hash match),
  which closes the residue the earlier passes left.
- `SteamClientDll` read the 4604 path, the 4231 path, and `…\steamclient.dll ` with a trailing space after the three
  emulated sessions. Each time it was set back to `C:\Program Files (x86)\Steam\steamclient.dll` and read back.
- No `hl2`, `tf_win64` or `SmartSteamLoader` was left running.
- **Residue outside the listed directories** (`era-f/residue.sh`: every file newer than the before-listing, outside
  the dirs `listing.sh` covers), with no pre-session copy, so not restorable here; for the lead:
  - 1729296 and live: `steam_appid.txt`, rewritten as `440\n\0` (5 B);
  - 4604: `bin/stats.bin` (26,116 B), `config/serverbrowser.vdf`;
  - 4231: `stats.bin` (20,172 B), `config/serverbrowser.vdf`;
  - 4269: `config/serverbrowser.vdf`, `tf/maps/soundcache/_other.cache` (538,614 B).
  - Live's `tf/downloadlists/cp_granary.lst` was rewritten with the same bytes (`b0e28087…`, in the listing).
- The 1.1.0.0 `RemStorage/` folder is empty and has no session writes, and `%APPDATA%\SmartSteamEmu` does not exist.
- The live prep's listing hashes about 22 GB of VPKs and outlasted a 2-minute tool call. The aside copy and placement
  were finished by `era-f/prep-rest-live.sh`, after checking that the listing was complete (291 entries, as in
  `era-live-r2`).

### 2008, build 3420, run 4 (2026-10-29, 14:30–14:45): re-recorded with the shared engineer wait

The first run of the new `tf2ds_era_pre2011.cfg` (only the two `ds_build_*` aliases) on a pre-2011 client.
- **lcor first:** the run-3 pair renamed to `tf2-2008-build3420-spec2-{pov,stv}-cp_granary.dem` (hashes unchanged,
  `753278df…f9ed` / `c5859cf8…342d`), nothing overwritten.
- **Prep:** `prep.sh 2008-r4` (104 files listed and copied aside). Placed sha256: `tf2ds_specimen.cfg` `7d454642…7b92`,
  `tf2ds_era_pre2011.cfg` `ea4e6f2a…a1cd`, `tf2ds_era_2008.cfg` `041abf99…e772`, matching the worktree. The owner's
  `autoexec.cfg`, `config.cfg` and all nine class cfgs moved out (run 3 left the class cfgs in place). Shortcut
  `D8D5057B…0373` given the RCON args plus `+sv_lan 1 +fps_max 66 +tv_enable 1`. Desktop lock held throughout.
- **Control before the run:** `pollbuild.ps1` on the `spec2` POV read sentry done, dispenser `health 66/150 … pct
  0.44`, `BOTH_BUILT False`.
- **The owner's click; RCON answered.** `version`: `Protocol version 14`, `Exe build: … Mar 19 2008 (3420)`;
  `sv_lan 1`, `sv_allow_wait_command 1`, `fps_max 66`, `tv_enable 1`; `status`: `insecure`, `PinKushin … active
  loopback`.
- **`find` before setting anything:** as in run 3 (`mat_queue_mode` -1, `r_threaded_particles` 1, every other
  `*thread*` cvar 0 except `net_queued_packet_thread` 1 by default, untouched). Set: `mat_queue_mode 0`,
  `r_threaded_particles 0`, `mat_picmip -1`; aniso 16, trilinear 1, `r_rootlod 0`, `r_lod -1`, `mat_hdr_level 2`
  already at the max.
- **Chain:** every mark start…finish. The engineer wait read both finished **38 s** after `engineer_wait` (the copy
  sat at 684,032 B for ~10 s, then 782,336 B read the dispenser at `pct 1`); `ds_engineer_go` sent; the fallback later
  printed `engineer_fallback_disarmed`. `PinKushin captured the Central Control Point for team #2`. `Completed demo,
  recording time 237.8, game frames 4525`; `Completed SourceTV demo … 239.5`. Refusals: `addcond`/`removecond` ×14
  each, `tf_bot_add` ×4, `tf_bot_quota`, `cl_disablehtmlmotd`, the two deliberate unknowns.
- **Quit:** `disconnect`, `quit`; alive and `Responding` at 30 s, as in run 3, then ended. No `hl2` left.
- **lcor:** `tf2-2008-build3420-spec-pov-cp_granary.dem` 1,330,346 B `c3ca1ded…4467`;
  `tf2-2008-build3420-spec-stv-cp_granary.dem` 803,852 B `202ab655…bbda`.
- **Read back (Cli `-s` and `-t -e`, Probe `buildings`), POV / STV:** network protocol 14 both, `svc_serverinfo
  protocol 14 map "cp_granary"`; 4,525 / 3,874 packets, frame check ok; first `svc_packetentities` updated 227 / 399;
  90 / 91 game events, `teamplay_point_captured` 1, `controlpoint_starttouch` 1, 13 `player_death`, 14
  `player_changeclass`; 2 chat lines; 2 `svc_SetPause`; `svc_voiceinit vaudio_speex quality 5` and **167**
  `svc_VoiceData` each. **Both buildings finished on both demos:** POV sentry (entity 483, type 3) from tick 5763 to
  `building False pct 1` 150/150 at 6513 (750 ticks, 11.3 s), dispenser (entity 476, type 0) from 6011 to 150/150 at
  7468 (1,457 ticks, 21.9 s); STV 5732→6430 and 5965→7359 (10.5 s / 20.9 s). Builder control resolved on 6,544 of
  6,544 (POV) and 6,287 of 6,287 (STV) ticks.
- **Restore:** `restore.sh 2008-r4` diff against `before.txt` **empty** (`after-restore.144348.txt`); the owner's
  cfgs came back from the aside copies. Shortcut restored, hash match `D8D5057B…0373`. `SteamClientDll` reads
  `C:\Program Files (x86)\Steam\steamclient.dll`.

### 2007, build 3258, run 6 (2026-10-29, 14:55–15:01): the autoexec route ran, and froze; nothing captured

- **Fallback sized first, from the `spec0` POV** (Cli `-t`, `dem_consolecmd` ticks; arithmetic): 4,764 frames over
  18,390 ticks is ~17 fps, but `wait` advanced about once a TICK, not once a frame: `+forward` `wait 99` spanned 99
  ticks, `+attack` 99 → 78, `+reload` 66 → 55, the voice `wait 330` → 269 (1.0–1.25 waits/tick). So the shared
  `wait 7920` is ~6,300–7,900 ticks (95–119 s) on this build, over 4× the dispenser's ~22 s; even at 2013's 1.9
  waits/tick it is 62 s. Left unchanged.
- **Prep:** `prep.sh 2007-r6` (109 files listed and copied aside; placed hashes as 3420 run 4: shared `7d454642…7b92`,
  `tf2ds_era_pre2011.cfg` `ea4e6f2a…a1cd`). The uploader's performance-pack `autoexec.cfg` (`aa71bd57…3827`),
  `config.cfg` (`89f8d443…334d`, `mat_queue_mode "2"`) and the nine class cfgs moved out, so the client started on
  `config_default.cfg`. Session `autoexec.cfg`: `con_logfile tf2ds_2007.log` first; then `mat_queue_mode 0`,
  `r_threaded_particles 0`, `host_thread_mode 0`, `snd_mix_async 0`, `version`, `mat_picmip -1`, aniso 16,
  trilinear, `r_rootlod 0`, `r_lod -1`, `mat_hdr_level 2`, `fps_max 66`, `sv_lan 1`, `sv_cheats 1`,
  `sv_allow_wait_command 1`, `tv_enable 1`, `tv_delay 10`; exec of the specimen and `era_2007` cfgs; `ds_finish` ending
  in `quit`; `map cp_granary`; `wait 1320; ds_start`. Desktop lock held throughout. Steam offline.
- **The owner clicked `RUN_TF2.exe`;** `hl2.exe` PID 20756 came up at 14:57:44 with `-game tf -steam -console` and was
  `Responding=False` from the first poll, spinning ~0.86 of a core (8.6 s CPU per 10 s), 190 MB, title
  `Team Fortress 2`, for 2 min. Ended at 14:59:45.
- **The con_logfile's last line:** `Unknown command "tf_bot_quota"` (288 B in all). It carries `version` (`Protocol
  version 11`, `Exe build: 18:14:51 Oct  9 2007 (3258)`, `Not playing a local game.`) and the specimen cfg's top-level
  refusals through its line 35. **No `Unknown command` for any of the four threading cvars**, so build 3258 knows
  `mat_queue_mode`, `r_threaded_particles`, `host_thread_mode` and `snd_mix_async` (measured: an unknown prints).
  Nothing after line 35 of the specimen cfg prints (aliases, the era exec, `map`), so the log cannot place the freeze
  more exactly than "at or after the end of autoexec.cfg".
- **The mat_queue_mode hypothesis is refuted** (measured, run 7 below confirms over 15 min): with the pack autoexec
  and `config.cfg` out and `mat_queue_mode 0` set first, the client stalled at the same place: the end of autoexec.cfg. What follows autoexec in
  `valve.rc` is `stuffcmds` and `startupmenu`, and here `map cp_granary`. The one 2007 boot that worked (`spec0`) had
  its map loaded over RCON, not from the autoexec. **Open; not chased** (the brief: no loop).
- **Restore:** `restore.sh 2007-r6` diff against `before.txt` **empty**; no file outside the listed dirs is newer than
  the listing. No `hl2`/`RUN_TF2` left; `SteamClientDll` reads `C:\Program Files (x86)\Steam\steamclient.dll`.
- **Instrument error, corrected by the lead:** run 6 was ended after 2 min on `Responding=False` alone, which a 2007
  client also shows while it loads a map (it does not pump its window). Ending it looked to the owner like the client
  quitting by itself; he clicked again at 15:02:32, after the restore, so that second instance (PID 17060) booted on
  the pack config, spun the same way, and was ended (lead's instruction). Rule from run 7 on: a freeze is a
  con_logfile that has not grown AND CPU time that has not advanced for 5 min; otherwise wait, to a 15-min cap.

### 2007, build 3258, run 7 (2026-10-29, 15:05–15:23): the same autoexec route, waited 15 min; nothing captured

- **Prep:** `prep.sh 2007-r7` (before-listing identical to run 6's); the same cfgs moved out. The session autoexec
  differs from run 6 in two ways: `alias <name> ""` for the four top-level settings 3258 lacks (`cl_disablehtmlmotd`,
  `mp_respawnwavetime`, `mp_disable_respawn_times`, `tf_bot_quota`), so the log tail means something, and
  `tf2ds_mark specimen_loaded` / `era_loaded` / `map` / `map_loaded` echoes around each exec and the `map`.
- **The owner's one click after READY:** `hl2.exe` PID 24540 at 15:06:49. For 15 min it stayed `Responding=False`,
  CPU time advancing ~57 s per minute (one core: 888 s at the end), working set 191 MB. **No demo file appeared**,
  and the con_logfile stayed at 136 B from 15:07 on: `tf2ds_mark autoexec`, the three `version` lines and `Not
  playing a local game.` Not a freeze by the run-7 rule (CPU moved), so it ran to the cap and was then ended.
- **What the two logs together say** (measured; the reading is interpolated): both stop at the last line that prints
  before the `exec tf2ds_specimen` output ends. Run 6 printed the specimen cfg's line-35 refusal; run 7, with those
  refusals silenced, printed nothing after `version`, not even `tf2ds_mark specimen_loaded`, which follows the
  specimen exec. So either the client stopped inside or right after the specimen cfg, before the `map`, or this
  build's con_logfile holds its later lines in a buffer that a forced end discards. Either way, no `map` progress
  was ever seen in 15 min, and cp_granary loaded far faster when the map came over RCON (`spec0`). **The longest
  line in the specimen cfg is 396 characters** (`ds_engineer_build`), so an over-long alias is not obviously the
  cause; not chased (one re-prepare only).
- **Restore:** `restore.sh 2007-r7` diff **empty**; nothing outside the listed dirs newer than the listing; no `hl2`
  left; `SteamClientDll` reads `C:\Program Files (x86)\Steam\steamclient.dll`. lcor unchanged: the `spec0` pair
  stays the only scripted 2007 recording.

### 2007, build 3258, run 8 (2026-10-29, 15:55–16:14): the 255-character hypothesis, killed

- **The hypothesis (the lead's, unmeasured):** the 2007 command buffer limits a cfg line to about 255 characters, and
  the specimen cfg has two lines over that: line 65 (`ds_extras`, 344) and line 95 (`ds_engineer_build`, 396).
- **Already against it before the run (measured):** the `spec0` cfg's `ds_extras` was also 344 characters. Over RCON
  it ran to its last command, `ds_scout` (`tf2ds_mark scout` in `tf2ds_rec_2007.log`).
- **Engine limit: not read.** The 3258 `engine.dll` strings hold the old engine's own `Cbuf_AddText: buffer overflow`
  and `Alias name is too long`. They do not hold tier1's `Command too long` or `CCommand::Tokenize` warnings, which the
  2013 SDK has (`tier1/commandbuffer.cpp:124`, `tier1/convar.cpp:407`). The Ghidra project `tf2engine2007` opened,
  but its analysis tools were not reachable from this session, so no buffer size is cited.
- **The test:** a session-only variant of the specimen cfg (`9dfad82b…3704`), scratchpad `era-2007-r8-src/`. It drops
  the comments and splits the two long aliases at a `wait`, the second half called last
  (`ds_extras2`, `ds_engineer_build2`). Its longest line is 241 bytes. Joining the halves back reproduces the shared
  cfg's command lines exactly (`diff`, empty). Every ~10 lines it switches `con_logfile` to a new file
  (`tf2ds_2007_L00.log`, `_L10`, …) and echoes a marker. The autoexec does the same around each exec. The shared
  `tools/specimen/tf2ds_specimen.cfg` is **unchanged**.
- **Result (measured):** `hl2.exe` PID 14248 started at 16:02:15, `Responding=False` from the first poll. At 16:02:42
  it wrote `tf2ds_2007.log`, `_A1`, `_L00`, `_L10` and `_L20`, each holding its marker, and **never `_L30`**. CPU ran at
  about one core and then slowed (455 s by 16:13:58). It was ended at the lead's instruction.
- **The log is not buffered** (measured): each stage file held its line within the same second.
- **So the exec stopped between `L20` and `L30`, with no line over 250 bytes. The 255-character hypothesis is
  killed.** That block holds `ds_class`, `ds_class2`, `ds_class3`, `ds_start`, `ds_extras` and `ds_extras2`.
  `ds_extras` is the cfg's only non-ASCII line (`name Пётр大将`). Run 9 says what that line does.
- **Restore:** `restore.sh 2007-r8` diff **empty**.

### 2007, build 3258, run 9 (2026-10-29, 16:20–16:42): menu reached, chain stepped over RCON, POV + SourceTV pair in lcor

- **Experiment A, the bare client:** the folder was prepared as in run 8 (`prep.sh 2007-r9`, before-listing identical
  to run 8's). The pack cfgs were moved aside, and the autoexec held only `con_logfile`, two echoes and
  `mat_queue_mode 0`. The lead's static read of `RUN_TF2.exe` (a shortcut that runs `hl2.exe -game tf -steam
  -console`) let a new shortcut, scratchpad `era-2007-r9/TF2-2007-run9.lnk`, start `hl2.exe` directly with the same
  arguments plus the RCON ones (`make-lnk.ps1`, which also appends `+fps_max 66 +tv_enable 1`). The owner
  double-clicked it.
- **Result A (measured):** PID 9240 started at 16:28:14 and was **`Responding=True` 20 s later**. Both autoexec
  markers printed, RCON answered `version` (`Protocol version 11`, `Exe build: 18:14:51 Oct  9 2007 (3258)`). **The
  launcher and the bare client do not hang.** Runs 6–8 differed in two ways (from my own logs): those clients were
  `Responding=False` from the first poll, and their logs stopped inside the specimen exec. This one ran a minimal
  autoexec.
- **The chain, stepped over RCON one command at a time** (`era-2007-r9/step.ps1` over `manual-commands.txt`, the
  variant's lines plus the 2007 overrides). The 15 pre-map settings, then `map cp_granary`: `status` showed the map
  loaded 5 s later, `PinKushin … active loopback`. Then the 44 alias definitions, and every one returned with the
  client `Responding=True`. **No command hung.** Line 31 (`ds_extras`) was sent with the ASCII name
  `Petr_tf2ds` instead of the UTF-8 one.
  - **Instrument note:** output from a command sent over RCON goes back over RCON and not into the con_logfile, and
    `-condebug`'s `console.log` stopped at 7,174 B once `con_logfile` was set. The chain's later output (run from the
    command buffer) did land in `tf2ds_2007.log`.
- **Recording:** `ds_start` at 16:32:37 printed every mark from start to finish. `PinKushin captured the Central
  Control Point for team #2`. `Completed demo, recording time 427.4, game frames 6165`, then `Completed SourceTV demo
  … 393.8`, then `tf2ds_specimen done`.
  - `drive-build.ps1` (given a `-LogName` parameter so it reads `tf2ds_2007.log`) saw `engineer_wait` at 16:35:13.
  - Its one poll took 93 s. The copy sat at the 782,336 B flush and read sentry `pct 0.44`, dispenser not yet seen.
  - It then sent `ds_engineer_go`; the fallback printed `engineer_fallback_disarmed`. The demo shows both buildings
    finished before the kill (below).
- **The UTF-8 line, tested on its own at the menu after `disconnect`** (the not-connected state of an autoexec). Each
  was a 4-line cfg: an echo, `alias ds_utf8probe "name <X>; echo tf2ds_mark probe_ran"`, an echo.
  - Control (`X` = `Petr_ascii`): `utf8ctl_before`, `utf8ctl_after`, and no `probe_ran`.
  - Test (`X` = `Пётр大将`): `utf8test_before`, **`probe_ran`**, `utf8test_after`; the client stayed `Responding=True`.
  - **Measured: in 3258, the UTF-8 name ends the alias's quoted body, and the rest of the line runs at once as
    top-level commands.**
  - The `spec0` log shows the same thing from a different run. `*DEAD* tf2ds_renamed : hi` and `Unknown command:
    tf2ds_unknown_one` / `_two` come at lines 1220–1228, BEFORE `tf2ds_mark extras` at 1272. That is the tail of
    `ds_extras` (`… say hi; … tf2ds_unknown_one; … wait 330; … ds_scout`) running when the cfg was exec'd. It also
    explains the "UTF-8 name dropped from a cfg" row below.
- **Startup hang: the cause is unconfirmed (interpolated):** the same tail exec'd from the autoexec runs ~495 waits
  and `ds_scout` (a `joinclass` and the whole class chain) during startup with no map loaded. Runs 6–8 stalled
  exactly in that block. That this is what spins the client was not observed, because the UTF-8 line was not exec'd
  from an autoexec again.
- **The owner's observation, as he reported it:** on this 2007 client the screen sat at the MOTD and he heard nothing.
  On the newer clients the RCON scripts get past the MOTD and he hears the game run. **The demo disagrees about the
  game itself** (below): the player joined team 2, spawned 17 times, died 13 times and captured the point. Why the
  window and sound did not show it is **not known**.
- **lcor:**
  - `tf2-2007-build3258-spec-pov-cp_granary.dem`: 1,749,267 B, `bcc8fabe…a287`.
  - `tf2-2007-build3258-spec-stv-cp_granary.dem`: 872,785 B, `ac7d119e…62a3`.
  - `spec0` untouched.
- **Read back (Cli `-s` and `-t`, Probe `buildings`), POV / STV:**
  - Network protocol 11 on both; `svc_serverinfo protocol 11 map "cp_granary"`.
  - 6,165 / 5,075 packets, frame check ok.
  - `dem_datatables` 85,063 / **65,536** B: the SourceTV 64 KiB cut (B24), predicted.
  - `teamplay_point_captured` 1, `controlpoint_starttouch` 1, 13 `player_death`, 2 `svc_SetPause`, 2 chat lines.
  - `svc_voiceinit vaudio_speex quality 5` and **74 `svc_VoiceData`** on each.
  - **Both buildings finished on both demos.** POV: the sentry (entity 476, type 3) from tick 9977 to `building False
    pct 1` 150/150 at 10799 (822 ticks, 12.3 s). The dispenser (entity 485, type 0) from 10401 to 150/150 at 12098
    (1,697 ticks, 25.5 s). STV: 9248→9946 and 9625→11021.
  - The builder control resolved on 15,530 of 15,530 ticks (POV) and 13,275 of 13,275 (STV).
  - The dispenser stays in the POV until tick 17487, after the Engineer's kill and class change; not chased.
- **Quit:** `quit` over RCON; the client exited within 10 s. **Restore:** `restore.sh 2007-r9` diff **empty**.
  `console.log`, `demoheader.tmp`, `tf_gamestats.dat`, `config.cfg` and `platform/config/ingamedialogconfig.vdf` came
  back from the aside copies. No `hl2` left; `SteamClientDll` reads `C:\Program Files (x86)\Steam\steamclient.dll`.

### Differences observed, per era (D240: fixed in the script where the build allows it, documented where inherent)

The owner, 2026-10-29: *"any differences like that should be fixed, it lets us obviously see and notice changes too,
which should be documented"*.

Evidence classes: **read** (published source or shipped game data), **measured** (a session's console or demo),
**interpolated** (inferred, not observed).

| what differs | builds | evidence | status |
|---|---|---|---|
| Buildings not finished when the chain's `kill` came. Last state, sentry / dispenser, before (`spec2`) → after (`spec`): 4231 done / 0.44 → done / done; 4269 done / 0.44 → done / done; 4604 done / 0.39 → done / done; 1729296 0.58 / 0.13 → done / done; live 0.58 / 0.12 → done / done; 3420 done / 0.44 → done / done (run 4) | 3420, 4231, 4269, 4604, 1729296, live | measured (Probe `buildings` on each POV and SourceTV) | **fixed** in the shared cfg (third pass; 3420 by run 4, the first pre-2011 run of `tf2ds_era_pre2011.cfg`): one cause in every era, see "the 2013 and live sentry", above |
| No building at all | 3258 `spec0` | measured | **fixed** by run 9 (`spec`): sentry and dispenser both finished, POV and STV |
| The client freezes during `exec tf2ds_specimen` from the autoexec (`hl2.exe` direct and `RUN_TF2.exe` alike) | 3258 | measured (both Steam-online starts, the raw re-record, the `RUN_TF2.exe` click, runs 6–8; run 8's stage files place it between the variant's `L20` and `L30`). Run 9's minimal autoexec reached the menu, `Responding=True`, in 20 s, and every specimen command stepped over RCON returned | **worked around**: 2007 is recorded over RCON from the menu, as 2008/2009 are. Not `mat_queue_mode` (refuted, runs 6–7), not `RUN_TF2.exe` (run 9 A, plus the lead's static read), not line length (run 8: no line over 241 B). That the UTF-8 tail of `ds_extras` (next row), run at startup with no map, is the cause is **interpolated** |
| A UTF-8 byte ends an alias's quoted body; the rest of the line runs immediately at top level | 3258 | measured (run 9: the UTF-8 probe alias printed its own `probe_ran` at definition, while the ASCII control did not; `spec0` log: `ds_extras`'s tail ran before `tf2ds_mark extras`) | **inherent**; the shared cfg's `ds_extras` defines a broken alias on 3258, and the 2007 RCON list uses an ASCII name. Not checked on other builds |
| The owner saw the screen stay on the MOTD and heard nothing during the RCON-driven chain (newer clients: past the MOTD, sound heard) | 3258 (run 9) | observed by the owner; the demo meanwhile records a team join, 17 spawns, 13 deaths, a capture and both buildings | **open**; cause unknown, not guessed |
| `wait` runs about once a tick at ~17 fps (1.0–1.25 waits/tick) | 3258 | measured (`spec0` `dem_consolecmd` ticks) | inherent; the 7920-wait fallback is 95–119 s here |
| `addcond` absent from `find` yet printing no `Unknown command` | 3862 | measured | **unresolved**: whether 3862 has `addcond` is not known; the non-zero `m_nPlayerCond` values may come from the chain's natural steps |
| Construction time: sentry 10 s, dispenser 20 s | every build | read (`objects.txt`, 3258/3420/3862); measured (first `building True` → `building False`, SourceTV `spec` demos): sentry 10.4 s and dispenser 20.9 s on 4231, 4269 (dispenser), 4604, 1729296 and live; 3862 11.2 / 21.6 s from first existence | **the same in every era**; no change to explain |
| The Engineer builds a **mini-sentry** (`m_bMiniBuilding 1`, health 100, built in 2.7 s) | 4269 only (spec1, spec2 and spec alike) | measured (Probe `buildings`: `mini True`) | **open**: a loadout difference, consistent across three runs. That the 1.1.0.0 emulator equips the Gunslinger is interpolated, not read. Checked and ruled out as the store: `RemStorage/` (empty, untouched), `%APPDATA%\SmartSteamEmu` (absent). `SmartSteamEmu.ini` has `RandomItemAwards = 1` and `DisableGC = 1`, untested. The step is the same; the object is not |
| The kill's timing is the poll's, not the build's: from `engineer_wait`, the driver read both finished after 32 s (1729296), 40 s (4604), 44 s (4231); on live (55 s) and 4269 (64 s) the 7920-wait fallback fired first | 4269, live | measured (console marks, poll timestamps) | harmless as recorded: on both, the dispenser had already finished (live: tick 7615, buildings removed at 10223), and the fallback fired within 55 s (live) and 64 s (4269) of `engineer_wait`, well past the dispenser's 21 s. Slow polls are the cause: the demo flushes in ~196 KB chunks, so at 4269's ~6 KB/s the copy did not change for about 60 s (12 polls). How many waits a second a client runs is not settled: 7920 took about a minute on both an 18 fps (4269) and a 62 fps (live) client, while the same ~660 build waits took 14.5 s on 4231 `spec2` and 6.1 s on 1729296 `spec2` |
| `build` takes one argument | 3258, 3420, 3862 | read (`client.dll` format string `build %d`) | fixed (`tf2ds_era_pre2011.cfg`) |
| Object type of the sentry: 3 (old enum), not 2 | 3420, 3862 vs 4231 | measured | inherent; the poll accepts any non-zero type |
| `name` does not take (the start name, the UTF-8 name and the rename are absent) | 3420, 3862 | measured; that the Steam persona overrides it is interpolated | inherent so far |
| UTF-8 name dropped from a cfg (the ASCII rename works) | 3258 | measured | inherent; the mechanism is the alias-quote row above (run 9) |
| `tf_bot_add`, `tf_bot_quota` unknown | 3258, 3420, 3862 | measured | inherent (no bots before the bot update) |
| `tf_bot_add` accepted but adds nothing | 5252 | measured (`status`) | not chased |
| `addcond`/`removecond` unknown | 3258, 3420 | measured | inherent |
| `cl_disablehtmlmotd` unknown | 3258, 3862 | measured | inherent |
| `mp_respawnwavetime`, `mp_disable_respawn_times` unknown | 3258 | measured | inherent |
| `cl_savescreenshotstosteam` unknown | 4604 | measured | inherent |
| `tv_delay` clamped to at least 10 s | 3258 | measured | fixed for every era (SourceTV stops 726 frames after the POV) |
| Steam offline drops the local player unless `sv_lan 1` | 3420, live | measured | fixed at launch (`+sv_lan 1`) |
| No `svc_VoiceData` (`voice_inputfromfile` silently accepted) | 4604 | measured: `sv_use_steam_voice` defaults to 1 in 4604, and its help says "voice_inputfromfile will no longer function" | **fixed**: `sv_use_steam_voice 0` before `map` (`tf2ds_era_2011.cfg` comment); 0 → 88 packets, `vaudio_speex` |
| No `svc_VoiceData`, codec `steam` | live | measured: `sv_voicecodec steam` (default) carries none; `vaudio_speex` prints "Unable to load voice codec 'vaudio_speex'. Voice disabled." (0 packets in a 5 s test demo); `vaudio_celt` 171 in the same test. `bin/x64` ships `vaudio_celt.dll` and no `vaudio_speex.dll` (32-bit `bin/` has both) | **fixed** for voice: `sv_voicecodec vaudio_celt` before `map` (`tf2ds_era_live.cfg` comment); 0 → 79 packets. **Inherent**: live's codec is CELT where 2010–2013 are Speex, and this pair is the controlled CELT specimen gcor lacked |
| `svc_VoiceData` count from the same 330-wait step: 1729296 81 → 28; 4231 266 → 44; 4269 44 → 28; 4604 0 → 88; live 0 → 79; 3420 44 → 167 (spec2 → spec) | 3420, 2010+ | measured | not chased; that the count follows how long 330 waits take on that run is interpolated |
| `quit` hangs in shutdown | 3420 (runs 3 and 4) | measured | worked around (disconnect, quit, end after 30 s); 3862 exited cleanly |

**Every era override:** 2010 gets `tf2ds_era_2010.cfg`, which redefines nothing. Both 2010 `client.dll` files carry
the two-argument `build %d %d` format string, as 2011 does; 3862 carries the one-argument `build %d`.
