# 43 — What identifies a map, and when Valve stopped saying

*Measured 2026-08-28, on gcor and the period builds.*

A demo's map NAME does not identify the map. `cp_badlands` in 2017 is not `cp_badlands` in 2026, and
a viewer that loads by name draws an old demo against geometry it was never recorded on — which
produces defects indistinguishable from rendering bugs (finding 41, D113).

`svc_ServerInfo` carries two fields that could settle it. Both were decoded here since the container
work and neither was ever compared to anything.

## The era split

| demo | `mapCRC` | map hash |
|---|---|---|
| 2007 granary (build 3258) | `534EEB7C` | 4 bytes |
| 2008 granary POV (build 3420) | `3B81F90A` | 4 bytes |
| 2008 granary **STV**, same session | `3B81F90A` | 4 bytes |
| 2009 badlands (build 3862) | `22CF9CB6` | 4 bytes |
| 2011 viaduct POV (build 4604) | `9C749032` | 4 bytes |
| 2011 viaduct **STV**, same session | `9C749032` | 4 bytes |
| 2013 badlands (build 1729296) | `FFFFFFFF` | `20A3CEA9…` (16 bytes) |
| 2013 foundry STV | `FFFFFFFF` | `D33C79E4…` |
| z1800 | `FFFFFFFF` | `D17F3DC7…` |

**Valve stopped computing the CRC between 2011 and 2013 and started sending an MD5 instead.**
`0xFFFFFFFF` is the CRC32 *init* value — the field is present and never filled in, which is a
different thing from absent and is why a naive comparison against it would report every modern map
as mismatched.

**Both POV/STV pairs agree with themselves**, which is what establishes the old field as genuine
rather than as noise: two independent recordings of one session, made by different clients, carrying
the same number.

*Evidence class: measured on the corpus.*

## What each is computed over

**The same lump selection, in both eras.** `CRC_MapFile` (`utils/common/bsplib.cpp:3774`):

```c
// CRC across all lumps except for the Entities lump
for ( int l = 0; l < HEADER_LUMPS; ++l )
{
    if (l == LUMP_ENTITIES) continue;
    curLump = &g_pBSPHeader->lumps[l];
    ... seek to fileofs, read filelen bytes, CRC32_ProcessBuffer ...
}
```

The entity lump is excluded deliberately, so a server that edits its entity list still matches its
clients — which is most competitive servers. Lumps are read by their own `fileofs`/`filelen` in
header order, not sequentially through the file, and the bytes are raw: a Source BSP may hold LZMA
compressed lumps and `CRC_MapFile` neither knows nor cares.

**The MD5 covers the same walk.** Not published anywhere, and established by measurement: the MD5 of
`cp_process_f12.bsp` *as a file* is `B18E4159…`, while its demo says `DF0D50EF…`. Over the
lumps-except-entities walk it is `DF0D50EF…` exactly.

*Evidence class: read from published source for the CRC; measured differentially for the MD5.*

**The CRC variant is ordinary reflected CRC-32/ISO-HDLC**, not a Valve invention — established from
three facts in `tier1/checksum_crc.cpp`: init and xor are both `0xFFFFFFFF`, the step is
`table[b ^ (byte)crc] ^ (crc >> 8)`, and `pulCRCTable[1]` is `0x77073096`, the reflected polynomial
`0xEDB88320`. So a standard library computes it, and the check value for `123456789` is `0xCBF43926`.

## The old-era CRC is UNSOLVED

The obvious next test — a 2007 demo against the 2007 client's own `cp_granary.bsp` — fails. So does
every other pairing: across every `.bsp` under `F:\tf2-builds`, **zero of four** era checksums
matched.

**This was first written up as the archived clients' maps having been repacked**, on the reasoning
that the lump walk is shared with the MD5 path and the MD5 matches `cp_process_f12` exactly, so a
wrong byte selection would have broken that too. The owner killed it in one sentence:

> *"if the demo you are using to compare is the era specimine in the gcor, i recorded it with that
> client so it has to be the same map"*

The era specimens were recorded ON these clients. The map is not in question. **Both halves of that
reasoning were sound and the conclusion was still wrong** — the MD5 match proves the *modern*
selection and says nothing about what a 2007 engine did.

### What has been ruled out, measured

Against a known answer (`0x534EEB7C`) and a known input:

- the whole file; the file after the header; the header alone
- every lump in index order, entities included and excluded
- every lump in FILE order, entities included and excluded
- lump ceilings of 35, 40, 48, 56 and 64, with and without entities
- **every lump but the entities AND one other — exhaustively, all 63**

None reproduces it.

### Why the published description is not enough

*"A concatenation of all lumps in the BSP except for the entities lump"* is what the community
libraries implement and what this project implemented. It is confirmed correct for the MODERN hash.
It does not reproduce a 2007 CRC.

**And the citation this project used may be the wrong function entirely.** `CRC_MapFile`
(`utils/common/bsplib.cpp:3774`) has exactly one caller in the published tree — `SwapBSPFile`, the
Xbox 360 conversion tool. It is not shown being used by a server. The engine's own checksum lives in
`engine/checksum_engine.cpp`, which is not in `source-sdk-2013`; the leaked 2007 tree that carries it
is DMCA'd off GitHub (HTTP 451) and was not pursued.

*Evidence class: measured exhaustively over a bounded hypothesis space; the remaining space is
unbounded without the engine's own code.*

### Where this leaves the feature

**The modern path is done and verified** — which is the era where a mismatch actually bit (a 2017
badlands demo on a 2026 map). The old era already pairs with its maps by construction: the era
specimens have their own client beside them, so the map to load is known without a checksum.

So the CRC is a confirmation this project would like and does not need, and the MD5 is the one that
does the work.

## Why this matters beyond the check

The checksum turns "mysterious rendering defect on an old demo" into "wrong map, known". Three
defects were investigated as regressions in one evening; two were real bugs in unrelated work and one
was never code at all, and nothing separated them except the owner's memory of a different map.
