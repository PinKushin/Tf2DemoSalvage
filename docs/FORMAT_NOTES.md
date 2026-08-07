# Format notes

Working notes on the `.dem` format and specific corpus findings, kept separate from the roadmap so it can grow without cluttering the plan.

## Container structure (stable across eras)

Confirmed via the community DEM format writeup ([demboyz DemFormat.md](https://git.botox.bz/CSSZombieEscape/demboyz/src/commit/3858162c9c0fb0988e30f61de526ebfe85eb1e2f/docs/DemFormat.md), documenting the format as of the TF2 build active July 2015, demo protocol 3 / network protocol 24) and cross-checked against `z1800.dem`'s actual bytes.

**Demo header** (all little-endian):

| Offset | Type | Field |
|---|---|---|
| 0 | char[8] | Stamp, `"HL2DEMO\0"` |
| 8 | int32 | Demo Protocol |
| 12 | int32 | Network Protocol |
| 16 | char[260] | Server Name |
| 276 | char[260] | Client Name |
| 536 | char[260] | Map Name |
| 796 | char[260] | Game Directory |
| 1056 | float32 | Playback Time (seconds) |
| 1060 | int32 | Playback Ticks |
| 1064 | int32 | Playback Frames |
| 1068 | int32 | Signon Length |

Header is 1072 bytes total, followed immediately by the command stream: repeating `[int8 CommandType][int32 Tick][command-specific payload]`, terminated by `dem_stop` (type 7).

Command types: `dem_signon`=1, `dem_packet`=2, `dem_synctick`=3, `dem_consolecmd`=4, `dem_usercmd`=5, `dem_datatables`=6, `dem_stop`=7, `dem_stringtables`=8. (Newer demo protocol versions add `dem_customdata` — confirm the exact value when we have a specimen new enough to check; don't assume it matches this list until verified against real bytes.)

**Open verification items for Phase 1** (don't trust these without checking against real files):
- Exact set of demo protocol version values TF2 has used across its life (this doc only confirms protocol 3 was in use mid-2015; earlier/later values unconfirmed here).
- Whether/when `dem_customdata` was introduced and its payload shape.
- Bit-packing/varint helper differences, if any, tied to demo protocol bumps.
  - Sharpened 2026-08-07: **whether protocol 24 uses varints at all on the paths we decode is
    unconfirmed.** The encoding is not original to Source — GoldSrc (1998) and Source (2004)
    both predate protobuf's 2008 open-sourcing, and the original netcode used hand-rolled bit
    packing. `bf_read::ReadVarInt32` appears in the protobuf-adoption era (~2011–12) and is
    present in the Source 2013 SDK, but TF2's `NET_Messages` at this vintage are still the
    older `bf_read` style. `Tf2DemoSalvage.Core.Primitives.VarInt` exists and is tested, but
    its necessity here is a question to settle against real packet bytes, not an assumption.
    (The encoding itself long predates protobuf too — LEB128 in DWARF, and MIDI's big-endian
    cousin from 1983 — so "protobuf-style" names the popular user, not the inventor.)

## Corpus: `z1800.dem`

Parsed directly from the file (not assumed) on 2026-08-07:

- Stamp: `HL2DEMO`
- Demo Protocol: **3**
- Network Protocol: **24** — matches the exact pair documented as active in TF2 as of July 2015 in the demboyz writeup, so this file most likely dates to roughly 2015–2016, not earlier.
- Server Name: `FACEIT.com register to play here`
- Client Name: `SourceTV Demo` (this is an STV/observer demo, not a player POV demo)
- Map: `koth_harvest_final`
- Game Dir: `tf`
- Playback Time: 863.26s (~14.4 min)
- Playback Ticks: 57,551
- Playback Frames: 14,386
- Signon Length: 912,640 bytes
- File size: 8,964,241 bytes

Sanity check: ticks / time = 57551 / 863.265 ≈ 66.67, matching TF2's standard tick rate exactly — header fields are internally consistent, file is structurally intact, not corrupted or truncated.

**Why it fails to play in the current client:** almost certainly the same class of issue as the well-documented July 25, 2023 break (`RecvProp type doesn't match server type for DT_ObjectDispenser/healing_array`) — the live client validates incoming SendTables against its own current compiled entity layout and rejects anything that doesn't match. This is a client-side compatibility check, not file damage. Since SendTables are embedded in the demo itself (`dem_datatables`), a standalone schema-driven parser reading only what the file provides should be unaffected by this class of failure entirely. This makes `z1800.dem` a good first real Phase 1 target: known-good structurally, known-broken in the official client for a well-understood reason unrelated to file integrity.

Location in repo: `tools/corpus/demos/z1800.dem` (tracked via Git LFS, see `.gitattributes`). Metadata mirrored in `tools/corpus/manifest.json`.
