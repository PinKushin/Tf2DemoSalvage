---
name: z1800-is-modern-not-2015
description: "The corpus demo is from 2020+, not 2015; protocol numbers never date a TF2 demo, and the file is truncated by one byte"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-07T23:13:19.361Z
---

Established 2026-08-07 by reading `z1800.dem`'s actual bytes (reproduce with
`python tools/inspect_demo.py walk|date tools/corpus/demos/z1800.dem`).

**Never infer a demo's age from its protocol numbers.** The demboyz writeup documents
demo protocol 3 / network protocol 24 as current in July 2015, and it was — but TF2 kept
that pair for years. `z1800.dem` carries protocol 3/24 *and* `sum20_fire_fighter_style1`
and `@20_handsome_devil` (Summer 2020), `rglgg_medal`, `etf2l_2018_bronze`, and
Competitive Mode voice lines. It is from **mid-2020 or later**, not 2015–2016 as earlier
docs claimed. Date demos from seasonal asset names in the string tables — Valve names
event items after the year they shipped, so they are self-dating. Protocol numbers tell
you which decode quirks apply and nothing about age.

**The file is truncated by exactly one byte.** Its final `dem_stop` command header has 3
of its 4 tick bytes. Harmless — `dem_stop` has no payload and its tick is unused — but
the parser must treat EOF there as a normal end, not corruption. Earlier docs called the
file "structurally intact"; that was too strong.

**Server identity is self-declared.** `Server Name` is the `hostname` cvar: free text the
operator picks, often an advert. The bytes support "the server called itself FACEIT", not
who actually ran it.

**Why it matters:** the corpus was believed to be a rare mid-2010s specimen. It is
modern-era, so there are currently *zero* pre-2020 demos — and modern ones are freely
obtainable from demos.tf. **D5 was rewritten 2026-08-07** to split the problem: modern demos
are abundant, and self-recorded ones are the only source of *correctness* ground truth
available (fuzzing proves liveness only; nobody knows what is in `z1800.dem`). Historical
demos are genuinely scarce and currently number zero — the parser has no historical coverage
and no test that would reveal its absence.

See [[fuzzing-belongs-here]] and `docs/SPEC.md`, which carries the full consolidated
format spec with per-claim confidence tags.

**A demo names its own map file.** Added 2026-08-08: the `downloadables` string table contains
`maps\<name>.bsp` outright, and `svc_ServerInfo` carries a 16-byte map hash alongside the map
name. D9's resolver can work from what the demo states rather than inferring from the map name,
and the hash catches the real hazard — a community map with the right name but the wrong
version.
