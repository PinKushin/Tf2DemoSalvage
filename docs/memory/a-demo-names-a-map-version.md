---
name: a-demo-names-a-map-version
description: A demo's map name does not identify the map — svc_ServerInfo carries a CRC32 over every lump but entities, and a mismatched .bsp produces defects that look exactly like rendering bugs.
metadata:
  type: project
---

`cp_badlands` in 2017 is not `cp_badlands` in 2026. The viewer loads the map by NAME out of the
current TF2 install, so an old demo is rendered against geometry it was never recorded on — and every
consequence looks like a rendering defect.

Measured 2026-08-27/28. Three separate "regressions" were reported and investigated against a 2017
badlands demo: roller doors drawing as grey rock, players appearing out of nowhere, doors flickering
between grate and stone. **None was caused by the work under suspicion.** The owner: *"the bugs are
probably from a mismatched map version… not a regression, just something to document and fix."*

**The check needs nothing invented.** `CRC_MapFile` (`utils/common/bsplib.cpp:3774`) is published:
CRC32 over every lump **except `LUMP_ENTITIES`**, in header order, over the raw on-disk bytes — no
decompression. Entities are excluded so a server editing them still matches its clients. And the
expected value arrives on the wire: `svc_ServerInfo`'s `mapCRC`, decoded here since the container
work as `ServerInfoMessage.MapCrc` and never once compared to anything.

**Why:** without the comparison, no visual report on an old demo can be trusted to be about the code.
The only instrument that resolves the ambiguity is the owner's familiarity with one map, which is
what [[the-f12-demo-is-the-parity-reference]] is for — and leaning on it is expensive in his evenings.

**How to apply:** on any visual oddity from a demo that is not f12, the FIRST question is whether the
map matches, not what the renderer did. Until the CRC check exists (D113), treat a non-f12 map as an
unverified subject: reproduce on f12 before calling anything a regression. The plan is to pre-pack
period maps from a client per year rather than patch modern ones — the owner's call, on the grounds
that it is the easier of the two.
