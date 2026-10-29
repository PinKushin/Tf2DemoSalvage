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
  - POV: `teamplay_point_captured` 1, `m_bBuilding 1` twice (CObjectSentrygun, CObjectDispenser), `svc_VoiceData` 81,
    `svc_SetPause` 2, 13 deaths.
  - SourceTV: the same.

### Second pass, every self-startable era on the verified script

The first-pass pairs are kept in lcor as `…-spec1-…`; the 2007 first pair is `…-spec0-…`.

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

**Every era override:** 2010 gets `tf2ds_era_2010.cfg`, which redefines nothing. Both 2010 `client.dll` files carry
the two-argument `build %d %d` format string, as 2011 does; 3862 carries the one-argument `build %d`.
