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

## Session log (Phase B)

Empty: no client launched, nothing copied into any game folder.
