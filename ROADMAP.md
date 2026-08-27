# TF2 Demo Parser — Architecture & Roadmap

Status: implementation under way (updated 2026-08-09). **Phase 1 is substantially built** — the
container, the text dump, the Quake-style trace, JSON Lines, the CLI, the network message layer,
the embedded entity schema, schema flattening, `svc_PacketEntities` decoding and cross-tick
entity state all work against real demos, across two eras (network protocol 15 and 24).
What remains for Phase 1: a compiler from the text output back to a `.dem`, which is what would
actually prove the decode is lossless. Every message body the corpus contains is now decoded.
`README.md` has the per-layer status and is kept current; this file remains the plan rather than
the report.

Originally locked for initial implementation (2026-08-07). See `docs/DECISIONS.md` in the repo for the ADR-style record of every choice below.

Goal: recover data from TF2 `.dem` files of any age — including demos Valve's own updates have broken — and eventually view them, in the spirit of the Quake community's demo tools (parse → readable text/data → 2D playback → full 3D playback), without depending on Valve's live client.

## 1. Why old demos break, and why that doesn't actually block us

A `.dem` file has three layers:

1. **Container envelope** — `HL2DEMO` header + a stream of commands (`dem_signon`, `dem_packet`, `dem_synctick`, `dem_consolecmd`, `dem_usercmd`, `dem_datatables`, `dem_stringtables`, `dem_stop`, and `dem_customdata` in newer protocol versions). This has been very stable across TF2's history — only a handful of "demo protocol" version bumps in 18 years.
2. **Network protocol** — the actual messages inside `dem_signon`/`dem_packet` chunks (`svc_ServerInfo`, `svc_SendTable`, `svc_PacketEntities`, `svc_GameEvent`, `svc_UserMessage`, `svc_StringTable`, etc). This is what changes almost every major update — message IDs shift, bit layouts change, new message types appear.
3. **Entity schema (SendTables/DataTables)** — the actual field layout for every networked entity (players, weapons, objects, etc). Critically, **this is embedded inside every demo file itself** (`dem_datatables`). A demo is self-describing: it doesn't need to agree with the *current* game's entity layout, because it carries the layout that was active when it was recorded.

The July 25, 2023 break (`RecvProp type doesn't match server type for DT_ObjectDispenser/healing_array`) happened because the live **client** validates incoming SendTables against its own compiled-in class definitions. A standalone parser that reads *only* what the demo provides never hits that check — it just needs a decoder that's schema-driven (reads whatever SendTables the demo carries) rather than hardcoded to one era's field layout, plus correct per-version handling of the lower-level bit-packing/message-ID quirks in layer 2.

That's the actual engineering problem: not "know every version of TF2," but "build one generic SendTable-driven decoder + a small table of documented quirks per demo/network protocol version range."

Prior art worth studying (not depending on, given license/language mismatch, but useful as a reference to cross-check against): [demostf/parser](https://github.com/demostf/parser) (powers demos.tf, handles the full multi-year corpus), `tf-demo-parser` (the crate behind it), and the format writeup in [demboyz's DemFormat.md](https://git.botox.bz/CSSZombieEscape/demboyz/src/commit/3858162c9c0fb0988e30f61de526ebfe85eb1e2f/docs/DemFormat.md).

## 2. Language architecture

Per your call: no Rust, no C++, no Python — and per further discussion, **no native C either, until/unless Phase 3 actually proves it necessary.** Modern C# (`unsafe`, `Span<T>`, `stackalloc`, `MemoryMarshal`, `System.Numerics` SIMD) is fast enough for bit-level demo decoding and bulk corpus processing. Pure C# for Phase 1 and 2.

- **`managed/Tf2DemoSalvage.Core` (C#)** — the actual decode engine lives here now, not in a separate native library: container parser, bit-reader/varint primitives, SendTable-driven entity delta decoder, string table decoder, plus the object model (ticks, entities, game events, chat) built on top of it.
- **`managed/Tf2DemoSalvage.Cli`, `Tf2DemoSalvage.Viewer2D`** — batch/parallel processing of a demo backlog, the Quake-style trace and JSON Lines output, and the 2D viewer, all consuming `Tf2DemoSalvage.Core`.
- **`native/libtf2dem`** — kept as a placeholder folder only. Not started for Phase 1/2. Revisit only if Phase 3 profiling shows a specific piece (most likely a per-frame render-loop step, not demo decoding) genuinely needs native code after `unsafe` C# has actually been tried and measured — a last resort, not a default. If that trigger ever fires: default to C, with Zig as an open long-shot alternative (not C++) — it exports a plain C ABI natively, so it's the same P/Invoke story as C with real memory-safety improvements and none of C++'s naming-convention/template baggage, at the cost of its own build step outside the main `.sln`. See `docs/DECISIONS.md` D2 for the full reasoning.

**Native build, if it's ever needed: MSBuild/vcxproj**, not CMake — would live as a native project in the same Visual Studio solution as the C# projects. Trade-off already accepted for that hypothetical: Windows/MSVC-only, no cheap Linux ASan/UBSan fuzzing CI. This only matters if D2's "revisit if Phase 3 proves it necessary" ever actually triggers.

## 3. Phased roadmap

**Phase 0 — Corpus & spec-mining.** *Largely answered, and not the way this paragraph expected.*
The original plan was to collect old demos from community archives. Old demos turned out to be
genuinely scarce (D5), and the route that worked instead was **acquiring old clients and recording
new demos on them** — archive.org carries period TF2 builds, each one dates itself from
`bin/engine.dll` before download (D30), and a listen server with `tv_enable 1` produces a matched
POV and SourceTV pair.

That gives dated specimens rather than undated ones, which is strictly better ground truth: the
recorder knows what happened. Five protocols are now measured — 11 (launch, Oct 2007), 14, 15, 16
and 24 — with gaps at 12–13 and 17–23. See `docs/TIMELINE.md` for the era table and
`docs/RECORDING_CHECKLIST.md` for what to do while recording, so each era exercises the same
things and differences between specimens are era differences.

Community archives remain the only route for demos of *real matches* played at the time, which
self-recording cannot reproduce — 12 players, real network conditions, real STV delay.

**Phase 1 — Core parser (C#, `Tf2DemoSalvage.Core`), text/structured output.** *Substantially
complete as of 2026-08-10.* Envelope + `dem_datatables`/`dem_stringtables` parsing, generic
SendTable-driven entity decode, normalized event stream (entity spawn/update/delete, game events,
chat, user messages, tick timing). Output: **a Quake-style readable trace** — the demo decompiled
to text, message by message, in stream order — plus a summary dump and JSON Lines for tools that
want a machine format. **This alone delivers the core "recover lost demos" goal**, independent of
any viewer.

**Status.** Container, text dump, trace, JSON Lines, CLI, every net message the corpus contains,
`dem_datatables`, string tables, entity decode with baselines, and the normalized event stream are
done. Every demo in the corpus decodes end to end across five protocols — 11, 14, 15, 16, 24 —
with the one documented exception of a SourceTV recording whose schema the *writer* truncated
(`RISKS.md` B24).

`svc_Sounds` and `svc_TempEntities` are decoded as of 2026-08-10, which was the last of the
message bodies that could be. The share of payload bits consumed without being understood is
0.00–0.19% per demo, the remainder being `svc_EntityMessage` bodies (laid out by the receiving
class, so there is no generic reading) and voice payloads (a codec, and a different project).
`CorpusCodecCoverageTests` reports that number per demo and does not gate on it.

Known remaining work, none of it blocking:

- The voice client-to-player mapping is unresolved and needs a demo with two speakers
  (`docs/RECORDING_CHECKLIST.md`).
- **The text output cannot be compiled back into a demo.** The pieces exist as of 2026-08-11 — a
  bit writer, a message writer covering every type the corpus contains, an entity encoder — and
  they are exercised the other way round: every message re-encodes bit for bit, and entity
  snapshots rebuilt from decoded values are exact on 99.78%. What is missing is the parser that
  turns the trace back into those objects. That is the last Phase 1 item.
- **RISKS B25**, a UBitVar written one selector step wider than canonical on 0.16% of modern
  snapshots. Found by the entity round trip and by nothing else, because both forms decode to the
  same number.

*Ordering note learned in practice: layer 2 messages carry no length prefix, so they must be
implemented in the order the stream blocks on, not in order of apparent usefulness —
`svc_ServerInfo` appears once per demo and gated the entire signon stream.*

**Phase 2 — 2D top-down viewer (C#).** Player positions/orientation/deaths/objective state scrubbed over time on a top-down map projection. Use TF2's shipped overview/radar images where they exist, fall back to a wireframe top-down projected from BSP world geometry where they don't. **Correction (2026-08-07): that geometry is the FACES lump, not brushes** — brushes are collision volumes. See `docs/RENDERING_NOTES.md` §2.

**Phase 3 — Full 3D native-quality viewer (long-term stretch).** Honest framing: matching the actual TF2 client's visual fidelity means writing a lightweight Source-engine renderer — BSP world geometry + lightmaps, MDL/VVD/VTX skeletal player & weapon models, VTF/VMT materials, animation from the demo's bone/pose data, particles, HUD. That's an engine-team-sized undertaking, not a side feature.

- **Phase 3.0 / v0.1 (locked scope):** everyone rendered as a sphere or capsule/pill — literally reusing the same primitive shapes TF2's own hitboxes already use internally — team-colored, positioned/oriented from the demo's entity data, over simplified flat-shaded world geometry (no lightmaps/materials). **Correction (2026-08-07): draw the FACES lump, not brushes** — brushes are collision volumes, and displacement terrain (533 of them in `koth_harvest_final`) is separate geometry again. See `docs/RENDERING_NOTES.md`. Tractable multi-month goal, not multi-year, and immediately useful for reviewing a match.
- **Phase 3.x (later, unscoped):** real player/weapon models, materials, animation, particles, HUD — fidelity work, only after v0.1 exists and proves the pipeline (entity → transform → render loop) end to end.

**Multi-view for SourceTV demos — a security-monitor layout (owner-stated, 2026-08-11).** A SourceTV recording carries the whole server's entity state, not one player's view, so it can be rendered from *any* player's eyes — and there is no reason to pick one. The viewer should show **at least six simultaneous views, a full team, each in its own partition**, and clicking a partition promotes it to full screen, the way a bank of security monitors works.

This is a genuine advantage over Valve's own client rather than a convenience feature: the live client plays a SourceTV demo through a single camera and makes you scrub back to see what another player was doing at the same moment. Six views make a team fight legible in one pass.

It is also mostly a *scene-graph and camera* problem rather than a rendering one — six viewports over one shared world and entity state, so the cost is roughly six camera transforms and six draw passes, not six parsers. Worth designing the Phase 3.0 render loop with multiple viewports from the start rather than retrofitting a single-camera design, since the retrofit is the expensive version.

Rendering backend, when we get there: **Vortice.Windows** (actively maintained, modern successor to SharpDX, thin managed wrapper over real D3D11/12) fits your Windows/DirectX + C# preference directly — no need to drop into C for the renderer itself, only asset-format parsing if you want that shared with the C core for consistency.

**Phase 4 — Demo repair (replay compatibility with the live client): parked, essentially indefinitely.** Feasibility is genuinely uncertain — it would mean rewriting a demo's embedded SendTables/entity data to match whatever schema the *current* client expects, for every historical schema shape, which is a moving target Valve keeps changing. And if Phase 3 exists, the actual user need ("see what happened in this old match") is already met without fighting the client's validation at all. Keeping it noted here only so it isn't forgotten, not because it's expected to happen.

**Phase 5 — The write-up (project website, owner-stated 2026-08-18).** When the app is done, the closing deliverable is a long-form history of TF2's Source engine and its demo system — how the format works, how each part was reverse-engineered, what was believed first and what corrected it, and the era/date research that is this project's original contribution. It lives on the project's website, not in the repo. **The raw material already exists and is written for exactly this:** `docs/findings/` is a narrative record kept quotable end to end (its README says so in as many words), `docs/TIMELINE.md` holds the measured era axis, and `docs/findings/README.md`'s "Which of this is original" section already separates the transcribed-from-Valve parts from the genuinely new findings — the protocol-to-build-date table, the writer's 64 KiB schema cap, `PlayerAnimEvent_t`'s append-only history, and the rest. So Phase 5 is editing and assembling an existing corpus into a public piece, not researching one from scratch. This is *why* the findings discipline (evidence class on every claim, wrong turns kept rather than tidied) is enforced during development rather than reconstructed at the end: a finding written up weeks late loses the measurement that killed the wrong version, which is the part worth reading.

### On the Source SDK — options weighed

One important correction to flag: Source SDK 2013 does **not** actually contain the demo/netcode parser or the renderer (`engine.dll`, `materialsystem`, the actual .dem reader) — those stay proprietary and closed. What it *does* contain is the mod-side game code (client/server DLLs), `tier0`/`tier1` utility libs, mathlib, and the map/model compiler tools (`vbsp`, `vrad`, `studiomdl`) with their format headers (`bspfile.h`, `studio.h`). So it's only relevant to Phase 3 (asset parsing for the 3D viewer), never to Phase 1/2 demo parsing itself.

| | Clean-room C/C# (VDC docs + prior art as reference) | Source SDK 2013 headers/utils (C++) |
|---|---|---|
| **Wins** | Stays in your chosen stack end-to-end; no license entanglement — you own the whole codebase outright and can license/distribute however you want; forces you to actually understand the formats, which pays off when something inevitably doesn't match docs | Authoritative, exact struct layouts and constants straight from Valve — removes a whole category of "reverse-engineering drift" bugs; battle-tested math/BSP utility code you don't have to rewrite; if you ever want to lean on `studiomdl`/`vbsp` themselves rather than just their headers, that code already exists |
| **Drawbacks** | Real risk of subtle field/offset bugs versus community docs that occasionally lag or disagree; more up-front effort per format | SDK license (Source 1 SDK License) is written around "non-commercial mods that require the base game to run" — using it in a standalone public tool is a legal gray area, not a clean fit; pulls C++ into the codebase; ties that component's build to the SDK's own build assumptions (older MSVC toolchain conventions, etc.) |

Given you're fine with C++ *if* it's genuinely needed: recommend staying clean-room C/C# for Phase 1/2 (SDK has nothing to offer there — it doesn't contain demo parsing at all), and treating SDK-vs-clean-room as a per-format decision *within* Phase 3, made only if a specific format (most likely MDL/VVD/VTX skeletal animation, which is the gnarliest one) proves too error-prone to reverse-engineer cleanly. If we do reach for it, the same ABI-boundary pattern as the C core applies: wrap the SDK-dependent piece behind a narrow C-callable interface so it's an isolated, swappable native component rather than something that spreads C++ through the rest of the codebase.

## 3b. Viewer requirements (owner-stated, 2026-08-12)

The shape of the application, decided while building the shell. Recorded here because most of it
is not derivable from the code and was settled in conversation.

**WinForms owns the user interface; Direct3D owns one rectangle.** The viewport is the centrepiece
and everything around it is ordinary controls. The reason is testability: anything drawn inside
the D3D surface is invisible to UIA, so a UI built there would be hard to test and in places
impossible. Accessibility metadata exists for the same reason — automation ids are what the UI
tests address — rather than for screen-reader support, which this application is unlikely to need.

**Opening, never importing.** Nothing is copied and nothing is written into the user's folders. A
demo stays where it is and the library remembers where to find it.

| Requirement | Notes |
|---|---|
| Open a **file or a folder** | A folder is a playlist |
| Folders walk **subfolders** | Except the game's asset directories, matched by name |
| **Several roots open at once** | Choose what to play across all of them |
| **Multi-select like a file browser** | Ctrl/shift click, not checkboxes |
| **File association** | Double-click a `.dem` in Explorer and it opens here — something TF2 itself cannot do |
| Playlist side panel | Lists demos and their folders. **Not** entities or classes: that is parser working state, and anyone who wants it can export the assembly script |
| Video-style transport | Play/pause, scrub, current tick |
| **Full screen** | F11 or Escape to leave; the transport moves onto a floating overlay |
| Import/export/compile row | Sits **under** the play bar — operations on the demo as a whole, where the transport is about the moment being watched |

**The file association and the in-application browser must be the same code path.** Two loaders
would drift — disagreeing about folders, multi-select, or what counts as a demo — and the
divergence would only surface for whichever is used less.

### Maps

Use the user's own TF2 maps when the map is installed; **read their game folder, never write to
it.** When a map is missing, fetch it the way the game does: from fastdl, the HTTP mirror a server
hands a joining client, which serves `maps/<name>.bsp.bz2`. Downloads land in this application's
own maps directory.

A BSP obtained that way is **hostile input** — supplied by whoever runs the server, reviewed by
nobody — and `docs/DECISIONS.md` D32 has the rules the reader must follow before it exists.

### One viewport, and what that buys (owner-stated, 2026-08-12)

The overhead view and the eventual 3D camera live in the **same viewport**, not in separate modes.
That is not only tidiness — it is what makes the interesting features possible:

- **Click a player, get their point of view.** The overhead view becomes a picker: the thing you
  click is an entity whose position and angles the demo already carries, so the camera can move to
  their eyes. **This works for SourceTV demos too**, where the recording belongs to nobody in
  particular — every player's position is in the stream, so every player's POV is reconstructable.
  The live game cannot do this with an STV demo.
- **Several players at once, like a security feed.** Six views on one screen, possibly twelve, each
  following a player; click one to take it full size. A 6v6 match is twelve feeds, which is the
  whole server.

Neither needs anything the parser does not already produce. They need the camera to be a value
rather than a mode, which is why the split is being avoided now rather than undone later.

### Settings policy (owner-stated, 2026-08-12)

**If TF2 lets the user change it, this should too** — with two deliberate exceptions:

| | |
|---|---|
| **DirectX level** | Excluded. We are on D3D11 against a DX9-era asset set; there is nothing to trade. |
| **Interpolation and network rates** | Excluded, and this one is a *category* error rather than a preference. `cl_interp` and the rate cvars shape what a client **records**; a demo already contains the interpolated result. There is nothing left for them to act on at playback, and TF2 itself looks jittery with interp set too low — that jitter is baked into any demo recorded that way. |

Settings live in a Source-style `.cfg`, not JSON: one command per line, value after a space, `//`
for comments, unknown commands ignored. Anyone who has edited `config.cfg` can edit ours.

**A user's real TF2 config must be usable wholesale, and the earlier assessment of this was wrong**
(owner-restated 2026-08-22, correcting the paragraph that stood here):

> i want someone like myself to be able to just copy and paste there tf2 configs over wholesale, in
> .cfg or vpk form like comfig's configs

The text this replaces said the return was "small" because "a personal cfg is mostly movement
scripts, which a viewer has no use for", and concluded that copying TF2's *default* bindings gets
almost all of the benefit. **That reasoning governed the work and produced exactly that** — a
defaults table, built on 2026-08-22 and mistaken for the finished feature.

Why it is wrong: the value is not in which commands transfer, it is in the user not having to set
anything up. Someone who runs mastercomfig has already made every one of these decisions once, and
being asked to make them again in a second, different settings file is the friction the requirement
exists to remove. "Mostly movement scripts" is also true of the lines and false of the *file* — the
binds are what matter and they are all there.

**What it requires**, and none of it is exotic:

- **Source `.cfg` syntax**, quoted or bare, `//` comments, `unbindall`, later-wins ordering.
- **Ignoring almost everything.** A real config is hundreds of `mat_*`, `cl_*`, `alias` and `exec`
  lines this viewer does not implement; a parser that objected to unknown commands would reject
  every real file. Ignoring is the primary feature.
- **Our vocabulary must BE Source's** — keys named `SPACE`, `CTRL`, `MOUSE1`, `'`, `/`, and actions
  named `+forward`, `+jump`, `+moveup`. A translation layer would mean the paste does not work,
  which is the whole requirement.
- **VPK form**, since mastercomfig ships as `.vpk` under `tf/custom/`. This project already reads
  one: `docs/findings/24-reference-capture.md` pins the reference capture state against
  `mastercomfig-base.vpk`, and `VpkArchive` is the tool.

**And it requires running the config rather than reading it** (owner, same day, D70):

> since we are going to take valve cfgs, we have to allow scripting or it wont work. valve configs
> are little state machines themselves

`alias` is a *runtime* command that redefines other aliases as it runs, which is how null-cancelling
movement scripts work — and those are what most competitive configs are. `ConfigConsole` is the
interpreter: a mutable alias table, a bind table, and one `kbutton_t` per action, all read from
`in_main.cpp` and `kbutton.h` and pinned by conformance tests written before the code.

**Done:** full screen mode (borderless/exclusive), texture detail, rebindable actions with TF2's
default bindings (D68), **reading and executing a real `.cfg` (D70)**, **loading the player's own
configs at startup including from a VPK (D71)** — `Tf2ConfigFiles` goes through `GameArchives`,
which already mounts `tf/custom/*`, so a mastercomfig pack is read without anything knowing a VPK is
involved.

**Measured on the owner's install:** three configs, 12 of 95 binds applied, no control left
unreachable. Pressing W flies forward through his null-cancel script; holding S overrides it;
releasing S resumes forward.

**Wanted:** viewer resolution, export format, mouse sensitivity, and a settings screen that shows
the bindings the console resolved.

**One rule worth carrying to any other config import (D71):** a key the config binds to a command
this viewer does not implement keeps whatever this viewer had on it. `resetcamera` and `playpause`
are names this project invented because TF2 has no equivalent, so no TF2 config can ever bind them —
it just uses `f` and `k` for its own purposes and those controls vanish. **A config cannot express a
preference about a feature the game does not have.**

### Options, eventually

Full screen **mode is done** — borderless or exclusive, chosen in View > Full screen mode and
remembered in `settings.json` under LocalApplicationData. Borderless is the default because DXGI is
allowed to refuse exclusive; exclusive is the lower-latency path and the owner's preference.

Still wanted: viewer resolution, and export format (JSON or the Quake-style assembly script). No
options dialog yet; the list is here so the shell keeps room for one.

### Open a compressed demo without the user unpacking it first (owner-stated, 2026-08-21)

> *"we need to add a decompression algo to the roadmap too, so we can decompress demos, and play
> them, or add them to a playlist, without the user having to decompress themselves. this will
> probably require a library or using 7zip or somthing."*

**Recorded as a roadmap item, not started.** Applies to opening a file, to adding one to a playlist,
and to anything that walks a folder — an archive should behave like a demo everywhere a demo is
accepted, rather than at one entry point.

**Why it matters more here than it looks.** Demos are shared as archives almost universally: a
match's POV recordings arrive as one zip, community collections as `.7z` or `.rar`, and the older
the material the more likely it is packed. This project's whole reason for existing is the demos
nobody can play any more, and those are exactly the ones sitting in an archive somebody made in 2011.
Requiring a manual unpack puts a step between the user and the file for the entire long tail.

#### What .NET already has, and what it does not

`System.IO.Compression` ships Deflate, GZip, ZLib, Brotli and `ZipArchive`. So **zip and gzip need no
dependency at all**. It has no bzip2, no 7z, and no rar.

#### The likely answer is one managed library, not 7-Zip

**SharpCompress** (MIT) reads zip, 7z, rar, tar, gzip and bzip2 through one API and is pure managed
code — no native binary, no P/Invoke, nothing to ship per architecture. That last point is what
decides it against shelling out to `7z.exe`:

- **An external process is a dependency the user has to install**, and a missing one fails at the
  moment they try to open a file rather than at build time.
- **It is a command-injection surface**, and the input is a path the user supplies. `CLAUDE.md`'s
  security section is explicit about never passing user input into a process argument string.
- **It cannot be tested the way everything else here is.** A managed reader can be handed bytes in a
  unit test; a subprocess needs the tool installed on whatever machine runs the suite, including CI.

Not decided yet — it wants its own `docs/DECISIONS.md` entry when it is, per the standing rule that a
new package is flagged rather than added quietly.

#### Things to settle before writing any of it

- **Solid archives and seeking.** 7z and rar are often solid, so extracting one member can mean
  decompressing everything before it. A 2 GB collection cannot be held in memory, so the reader
  wants a spill-to-temp path and a cleanup that survives a crash.
- **A demo is identified by content, not extension.** An archive may hold `.dem` files beside
  screenshots, configs and a readme. The `HL2DEMO` stamp is the test, and this project already reads
  it — see `docs/SPEC.md`.
- **Nested archives and zip-bombs.** A depth limit and a total-bytes limit, both stated, because the
  input is a file from the internet. Same class of untrusted input as everything in `CLAUDE.md`'s
  security list.
- **Path traversal on extraction.** `..` in an archive entry name is the classic one; extraction must
  resolve and prefix-check against the temp root, and `CLAUDE.md` requires exactly that for any path
  derived from user input.
- **Does anything need extracting at all?** For a single-member gzip or a stored (uncompressed) zip
  entry the reader can be handed a stream and never touch the disk. Worth doing, because it makes the
  common case free — and this project's parser already reads from a stream.

### Beyond TF2's own quality (owner-stated, 2026-08-16)

**Upsample the textures and models so the viewer can look better than the game.** Recorded as a
roadmap item, not started.

**It does not begin until parity is reached — owner-stated, and it is a gate rather than a
preference.** The reason is measurement, not discipline: this project checks itself by comparing its
output against captures of the real game, and every difference is currently a defect to explain.
Turn on a feature that changes every pixel by design and that instrument stops working — a wrong
shader and an intentional improvement become the same observation. Parity first buys a baseline that
says the remaining differences are ours.

Two further things worth stating now rather than later:

- **The ceiling this clears is real.** `mat_picmip -1` is TF2's maximum and going below it does
  nothing — a VTF's mip 0 is a hard ceiling, so no setting recovers detail the file does not contain.
  Upsampling is the only route past that, because it manufactures detail rather than requesting it.
- **It must be switchable, and that rule already exists.** `findings/13-settings-parity.md` says any
  "better than TF2" feature needs a switch the parity path turns off, because this project validates
  itself against screenshots of the real game. An upsampled render compared against a TF2 capture
  would differ everywhere, and the difference would be the feature rather than a defect.

Nearest thing already done: anisotropic filtering, added 2026-08-16 — but that is **parity, not
improvement**. The sampler had none while the reference config sets `mat_forceaniso 16`, so it was
closing a gap in our own disfavour rather than exceeding the game.

### The 3D skybox, and rendering settings as settings (owner-stated, 2026-08-23)

**The 3D skybox gets implemented properly, with `sky_camera`.** A TF2 map keeps a miniature copy of
its surroundings far outside the level; the engine draws it as a separate view, scaled and offset by
the map's `sky_camera` entity. Until 2026-08-23 this project deleted it as a side effect of a
play-area cull built for the overhead camera. The cull is gone (D76) and the skybox is now drawn
raw — at its literal size and position, which is wrong and is deliberately visible. **Tracked as
B152.** It matters because it is what makes a map look right from a free camera and from a
point-of-view demo, which is most of what this viewer is for.

**Whether it draws is the user's setting, not ours**, and Valve's own declaration settles the shape:
`r_3dsky` defaults to `"1"` with no flags, `r_skybox` is `FCVAR_CHEAT`. Competitive players run
without the skybox and expect to be able to; video makers need it on. Both audiences are served by
the same cvar the game already uses.

### Valve's debug draws (owner-stated, 2026-08-23)

**Implement Valve's debug visualisations under Valve's names**, as development instruments rather
than as features for the end user. **Done, 2026-08-24 — B153 is closed.** `mat_wireframe`,
`mat_specular`, `mat_fullbright` (all three states), `mat_drawflat`, `mat_luxels`,
`mat_normalmaps`, `mat_bumpbasis`, `mat_leafvis`, `mat_showlowresimage`, `r_drawworld` and
`r_drawentities` all exist, alongside the category view and B156's FGD entity colours. **Decided as
D75.**

**They are retail cvars rather than a Hammer facility, which was checked rather than assumed** — the
strings are in the shipped `materialsystem.dll`, `engine.dll` and `client.dll`. That result became
the general rule in **D79**: every setting is a cvar, Valve's name where one exists and Valve's style
where none does.

`mat_showlowresimage` was last because it was the only one needing an ASSET rather than a shader
branch — every VTF's thumbnail had been skipped on the way past. Retaining it also pinned a
load-bearing assumption nothing had checked: the skip is sized as DXT1 unconditionally, so a
thumbnail in any other format would misplace every mip in the file.

**Two defects came out of the work itself**, which is the argument for having done it: the leaf-box
view was bound to `Keys.F11`, which full screen already had, and had silently broken full screen for
days with three UI tests red and no single failure able to say why (B165, and D78 on making every
control bindable); and the category view drew overlays white — the absence of a colour rather than a
colour — which cost two turns of misreading during the B154 hunt.

The justification is a measurement rather than a preference: the B154 blend-state leak took two days
and four cleared hypotheses, each costing a rebuild and a manual flight to the same spot, and was
then found immediately by drawing one view two different ways. See
`findings/32-the-opaque-pass-blend-leak.md`.

### Paying off the orthographic camera (2026-08-23)

The viewer began with a top-down orthographic camera, and several decisions taken then were correct
*for that camera* and are now wrong. Three were removed on 2026-08-23 — the decal depth bias, the
brush-outline overlay, and the play-area cull — and at least two remain live. **Tracked as B155**,
which proposes an audit against a specific tell rather than a rewrite: a comment asserting "X is Y"
where X and Y are different quantities that merely coincided under an overhead projection.

The owner raised restarting the WinForms project outright. The recommendation was against it, on the
grounds that the MVP extraction (D60–D63) already confines this damage to `Render` and `Scene` and
that the list is enumerable — but the audit is what converts that from an assertion into a number,
and if the number is large the rewrite argument wins.

### HDR lighting and tone mapping (owner-stated, 2026-08-27)

**This viewer is an LDR renderer and TF2 ships HDR on by default.** The owner, on being shown that:
*"i think most comp players run ldr so i didnt know that tf2 shipped with it on be default, we will
need to implement that, but its roadmap so write it down"*. Recorded as **D103**.

Every compiled map carries both compiles — measured across the era axis, with `cp_granary`'s LDR and
HDR lumps byte-identical — and this project reads the LDR pair unconditionally: `LUMP_LIGHTING` (8)
and `LUMP_LEAF_AMBIENT_LIGHTING` (56) with its index (52). Lumps 53, 55 and 51 are never touched.

**The work is a fork, not a switch**, and half of it is worse than none:

1. Read the HDR lumps when a map carries them.
2. Tone map, because HDR light routinely exceeds white and nothing currently scales it —
   `FinalOutput` (`common_ps_fxc.h:345`) multiplies by `LINEAR_LIGHT_SCALE` before `SRGBOutput`.
3. Autoexposure, which is what that scale actually holds: `viewrender.cpp:2214` sets it only under
   `HDR_TYPE_INTEGER`. Without it, walking from shade into sun stays blown out instead of settling.

**The current state is not a bug and should not be "fixed" piecemeal.** Under `HDR_TYPE_NONE` Source
does no tone mapping and lets overbright light clip, which is exactly what this renderer does — so
as an LDR renderer it is already at parity, and adding a tone mapper to the LDR path would match
neither engine mode. The gap is against Valve's *default*, not against what these demos were watched
in, which is the owner's point about the competitive audience.

**Not a candidate fix for B170.** It was briefly treated as one and is not: only the weapon washes
out, while the arms beside it take the same cube, the same sun and the same tone-map-less output.

## 4. Repo scaffold (once we lock the plan)

```
Tf2DemoSalvage/
  native/libtf2dem/      placeholder only — not used unless Phase 3 proves it necessary
  managed/
    Tf2DemoSalvage.Core/        the actual decode engine (Phase 1) + object model
    Tf2DemoSalvage.Cli/         batch parse, Quake-style trace, text dump, JSON Lines
    Tf2DemoSalvage.Viewer2D/    phase 2
    Tf2DemoSalvage.Viewer3D/    phase 3 (v0.1 primitives, then fidelity work)
  tools/corpus/          manifest + demo files
  docs/                  per-era format notes, ADRs
  tests/                 golden-output regression tests, one per corpus demo
```

CI: build the C core + run the C# test suite against the full corpus on Windows, fail on any output regression. License: MIT (locked), with a clear note that it's an independent/clean-room project unaffiliated with Valve. Map assets are resolved at runtime rather than bundled — see `docs/DECISIONS.md` D9, which supersedes the earlier blanket "ships no game assets" wording.

## 5. Corpus status

Only confirmed specimen so far: `z1800.dem` — FACEIT SourceTV demo, `koth_harvest_final`, demo protocol 3 / network protocol 24 (matches the documented July 2015 TF2 build), ~14.4 min / 57,551 ticks at the standard 66.67 tick rate, header internally consistent, file structurally intact. It fails to play in the current client for the same class of reason as the July 2023 break (client-side SendTable validation against the live schema) — the file itself isn't damaged, it's a compatibility problem a standalone parser sidesteps by design.

Reality check on going further back: TF2's early competitive era (~2008–2010, ETF2L S2–S10-ish) ran mostly on Mumble for casting rather than recorded STV, and no centralized demo archive existed before demos.tf. Recovering anything from that era depends entirely on an individual having personally kept a local `.dem` for 15+ years — plausible but low-probability at any real scale. **Decision: corpus growth is opportunistic and non-blocking, not a gate.** Phase 1 builds and validates against `z1800.dem` now; a community ask (r/tf2, TF2 Discords, ETF2L/teamfortress.tv forums) runs in parallel as a cheap side effort, not a dependency.
