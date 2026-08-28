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

## The old-era CRC: two faults at once

**Solved.** All four era demos match their own client's map exactly — 2007 granary, 2008 granary,
2009 badlands, 2011 viaduct. Two things were wrong simultaneously, which is why changing one variable
at a time never produced a hit.

### Fault one: the wrong field

On old protocols `svc_ServerInfo` carries BOTH a 32-bit field and a four-byte one. This project
called the first `mapCrc` and the second `mapHash`, and chased the first for a day.

**The map checksum is the second.** The 2007 granary demo's four bytes are `BC0A65F1`; the 32-bit
field is `534EEB7C` and remains unidentified. In modern demos that same 32-bit field is consistently
`0xFFFFFFFF` while the checksum field grows to sixteen bytes of MD5.

The decoder said so itself, in a comment nobody re-read:

> *"the older branch is written from the reference implementation and has no specimen to verify it
> against, so it is flagged rather than trusted"*

It was flagged as unverified and then treated as fact. **The alignment is not in doubt** — every era
demo decodes `IntervalPerTick` as exactly 0.015, and that field sits after both reads.

### Fault two: no CRC32_Final

`CRC32_Init` sets `0xFFFFFFFF` and `CRC32_ProcessBuffer` accumulates, but the engine never calls
`CRC32_Final`. The accumulator goes straight into the comparison:

```
mov ecx, [server crc from svc_ServerInfo]
cmp ecx, dword [our accumulator]
je  ok
```

A standard CRC-32 closes with `^= 0xFFFFFFFF`. The engine's number is therefore the **complement**
of a conventional CRC32 — `~0E9AF543 = F1650ABC`, which is exactly what the 2007 demo carries.

### What was NOT wrong

The byte selection, from the first attempt: every lump but the entities, index order, raw bytes,
skip empty. Two wrong write-ups were committed before this was found — that the archived clients'
maps had been repacked, and that the selection must be wrong. The owner killed the first
(*"i recorded it with that client so it has to be the same map"*) and the decompilation killed the
second.

**The lesson is about search shape rather than about CRCs.** Every hypothesis tested one variable
while holding the others; with two faults present, no single-variable test could ever succeed, and
each failure was read as evidence against the variable being tested. What broke it was widening the
target — searching for the *other field* as well — which cost one line and had been available from
the start.

## Superseded: what this section said while it was UNSOLVED

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

### The algorithm, from the 2007 engine itself

Decompiled from `F:\tf2-builds\tf2-2007\Team Fortress 2\bin\engine.dll` (October 2007), anchored on
the strings the client prints when the check fails. Ghidra's headless scripting is broken on this
JDK — Felix aborts in `handleJavaVersionChange` — so this was done with radare2.

The trail: `"Couldn't CRC map %s, disconnecting"` is referenced by `fcn.100c5cc0`, which calls
`fcn.10012c60` for the real check. `fcn.100217c0`, which looks like a CRC from the error message, is
not one — it is `IBaseFileSystem::Open` (vtable index 2) followed by `Size(FileHandle_t)` (index 7),
returning −1 when the open fails.

`fcn.10012c60` reads `0x40C` = 1036 bytes — exactly `sizeof(dheader_t)` — refuses any BSP version
outside 19–20, and then:

```
xor ebx, ebx                  ; lump index 0
lea edi, [header.lumps]
loop:
  test ebx, ebx
  je   next                   ; skip index 0 — ENTITIES EXCLUDED
  mov  edx, [edi]             ; lump.fileofs
  add  edx, [base]            ; + Tell(handle), taken at open
  Seek(handle, edx, FILESYSTEM_SEEK_HEAD)
  mov  esi, [edi+4]           ; lump.filelen
  test esi, esi / jle next    ; skip empty lumps
  read in 0x400 chunks -> CRC32_ProcessBuffer(&crc, buf, n)
next:
  add ebx, 1 ; add edi, 0x10  ; 16 bytes per lump_t
  cmp ebx, 0x40               ; ALL 64 LUMPS
```

**This is exactly what this project implements**, and the published prose description is right after
all: every lump but the entities, index order, raw bytes, skip-empty. The chunking into 1024-byte
reads cannot change a CRC.

**One term is new and does not apply here:** `add edx, [base]`, where `base` is `Tell(handle)` taken
immediately after opening and before the header read. That is non-zero only when the map is being
read from inside an archive — a `.gcf` or `.vpk` — where the handle starts partway into a larger
file. Every `cp_granary.bsp` under `F:\tf2-builds` is loose, so `Tell` is 0.

*Evidence class: decompiled from the era binary. Carried back as this description; no decompiler
output was placed in the repository.*

**So the implementation is confirmed and the mismatch is about WHICH FILE**, not how it is hashed.
That is a different open question from the one this section originally recorded, and a narrower one.

### A note on the citation

`CRC_MapFile` (`utils/common/bsplib.cpp:3774`) has exactly one caller in the published tree —
`SwapBSPFile`, the Xbox 360 conversion tool. It is never shown being used by a server, so citing it
for the network checksum was a guess that happened to describe the right algorithm. The engine's own
`checksum_engine.cpp` is not in `source-sdk-2013`; the leaked 2007 tree carrying it is DMCA'd off
GitHub (HTTP 451) and was not pursued — which is a second, independent reason decompiler output
stays out of this repository, beyond the size rule that is the main one.

### Where this leaves the feature

**The modern path is done and verified.** The old-era algorithm is now confirmed correct against the
era binary, and the open question is narrower than it was: which file a 2007 demo was actually
recorded against, given that none of the three `cp_granary.bsp` copies on disk reproduces its number.

The failure is systematic — all four era demos, against every `.bsp` in every build — which argues
against a one-off map swap and for something shared: the archives having been repacked after
recording, or the client having loaded the map from somewhere other than `tf/maps` at the time.

## Why this matters beyond the check

The checksum turns "mysterious rendering defect on an old demo" into "wrong map, known". Three
defects were investigated as regressions in one evening; two were real bugs in unrelated work and one
was never code at all, and nothing separated them except the owner's memory of a different map.
