---
name: era-axis-is-measured
description: Five TF2 protocols dated exactly by running period clients — and the rule that a protocol number dates nothing, with the corpus itself as the counterexample.
metadata:
  type: project
---

**Two memories were merged into this one on 2026-08-27** — `z1800-is-modern-not-2015` and
`a-client-dates-a-protocol-a-demo-does-not`. They are the negative half of this entry: what the era
axis does *not* let you conclude. Having the table in one file and its limits in two others is how
"we have a protocol 11 specimen" turns back into "so it is from 2007".

The era axis is no longer inferred. Five protocols, each dated by running the period client,
reading `version`, recording a demo, and checking the demo header agrees:

| Date | Protocol | Build |
|---|---|---|
| 2007-10-09 | **11** | 3258, `PatchVersion=1.0.0.5` — TF2's launch build |
| 2008-03-19 | **14** | 3420, `1.0.2.2` |
| 2009-06-04 | **15** | 3862 |
| 2011-06-15 | **16** | 4604, `1.1.5.8` |
| 2013-03-25 | **24** | 1729296 |

**Gaps: 12–13** (five months, Oct 2007 → Mar 2008) and **17–23** (Jun 2011 → Mar 2013).
Protocol 11 at launch was a surprise — 14 was expected, so three versions came and went in TF2's
first five months, a faster cadence than any later period.

## Date a candidate build before downloading it

`bin/engine.dll` carries the build date as a plain string (`Exe build: 18:14:51 Oct  9 2007`), so
a candidate is datable **statically** — no launching, no Steam, no unpacking. Archive.org serves a
single member out of a **ZIP** (4 MB instead of 3 GB) and **not** out of a 7z: a solid archive
cannot be partially decompressed and the request fails as **HTTP 200 with a zero-byte body**.
Check the size, not the status. Full detail in `DECISIONS.md` D30.

The `Exe build` trailing numbers are `(build) (appid)` — one number in 2007/2008, two from 2011.
A logging change, not a structural one. Worth knowing precisely because it looked like a
structural fingerprint first.

## What each era actually changes

Every protocol-conditional rule in the parser is keyed at 14, 15, 22 or 23. So **protocol 11
needed no new rules at all** — it exercises every branch on its old side simultaneously. The
boundaries that matter:

- **≤14**: no string table compression flag, **6-bit** schema bit-count field (B23), no
  `dem_stringtables` command in the container at all
- **≤15**: 5-bit message type, old `SendPropType` numbering, no `svc_ServerInfo` replay flag
- **16**: first protocol *with* the replay flag — which is why 16 was the single most valuable
  value in the 17–23 gap to obtain
- **≤22**: 13-bit `svc_Prefetch` index; **≤23**: fixed rather than varint lengths

## Fingerprints: one works, one is a trap

`max_classes` is **non-decreasing**: 216, 216, 232, 256, 362, 363. It bounds a demo's age from
below. Note 2007 and 2008 **tie**, so it ranges rather than separates.

**The string table COUNT dates nothing** — 16 at protocols 11, 14, 15, 16 and 24; 20 in 2020+.
Five eras across six years with an identical number. It was briefly treated as a discriminator and
would have given a confident wrong answer for anything in that span. The table *names* differ; the
count does not. A measure that fails to move across five samples is not a fingerprint, and only
the samples showed that.

---

## `a-client-dates-a-protocol-a-demo-does-not`

Owner, correcting the assistant three times in one exchange, each time toward claiming less:

1. *"even running the period client doesnt actually date the demo, i dont know why you think it
   does"*
2. *"thats only true for the vast vast majority too, because if someone like me uses a old client you
   can make ned demos on old protocols, just like we did, so you cant date the demo at all, you can
   only guestimate where the protocol updates landed within small windows, normally windows we
   figured out by following forum posts and change logs"*

**A demo's protocol says which protocol it speaks. That is the entire content of the fact.** It does
not bound when the demo was recorded, even loosely, because an old client still runs and still
records.

**The counterexample is this corpus itself.** Every gcor era specimen was recorded on a period client
in 2026: `tf2-2007-build3258-pov-cp_granary.dem` speaks protocol 11 and is weeks old. The method the
corpus was built with is the thing that breaks the inference.

Four separate questions, and only the first is answered by the file:

| Question | Answered by |
|---|---|
| Which protocol does this file speak? | the demo's header |
| Do we hold a specimen of protocol N? | the corpus |
| When did protocol N land? | changelogs, forum posts, build stamps — **estimated windows** |
| When was THIS demo recorded? | its own content — seasonal assets, map versions, a dated filename |

**The protocol windows above are estimates and always were.** They come from following changelogs
and forum posts, not from measurement, and they are narrow rather than exact. See
[[a-changelog-dates-the-complaint]], which is about the same evidence class.

**Dating serves the write-up, not the parser, and it is never going to be precise.** The decoder is
schema-driven and blocks on none of this. And TF2 shipped weekly builds while protocols changed
rarely, so a protocol spans many builds and its edges cannot be pinned to a week — the owner:
*"trying to date it to the week is going to be practically impossible"*. The target is a narrow
window with its evidence attached. A gap that stays a window is the honest answer, not a failure.

**Never infer a recording date from a protocol number, in either direction.** Do not let "we have a
protocol 21 specimen now" become "we know when protocol 21 was", and do not treat an open dating gap
as blocked work; it is a note on the write-up.

---

## `z1800-is-modern-not-2015` — the mis-dating that made the rule

Established 2026-08-07 by reading `z1800.dem`'s actual bytes. The demboyz writeup documents demo
protocol 3 / network protocol 24 as current in July 2015, and it was — but TF2 kept that pair for
years. `z1800.dem` carries protocol 3/24 *and* `sum20_fire_fighter_style1` and `@20_handsome_devil`
(Summer 2020), `rglgg_medal`, `etf2l_2018_bronze`, and Competitive Mode voice lines. It is from
**mid-2020 or later**, not 2015–2016 as earlier docs claimed.

**Date demos from seasonal asset names in the string tables** — Valve names event items after the
year they shipped, so they are self-dating. Protocol numbers tell you which decode quirks apply and
nothing about age.

**The file is truncated by exactly one byte.** Its final `dem_stop` command header has 3 of its 4
tick bytes. Harmless — `dem_stop` has no payload and its tick is unused — but the parser must treat
EOF there as a normal end, not corruption. Earlier docs called the file "structurally intact"; that
was too strong.

**Server identity is self-declared.** `Server Name` is the `hostname` cvar: free text the operator
picks, often an advert. The bytes support "the server called itself FACEIT", not who actually ran it.

**Why it mattered:** the corpus was believed to be a rare mid-2010s specimen. It is modern-era, so at
that point there were *zero* pre-2020 demos — and modern ones are freely obtainable from demos.tf.
**D5 was rewritten 2026-08-07** to split the problem: modern demos are abundant, and self-recorded
ones are the only source of *correctness* ground truth available (fuzzing proves liveness only).
Historical demos are genuinely scarce — which is what the period-client recordings above were built
to fix.

**A demo names its own map file.** Added 2026-08-08: the `downloadables` string table contains
`maps\<name>.bsp` outright, and `svc_ServerInfo` carries a 16-byte map hash alongside the map name.
D9's resolver can work from what the demo states rather than inferring from the map name, and the
hash catches the real hazard — a community map with the right name but the wrong version.

---

Related: [[proto-version-h-enumerates-the-boundaries]] for the boundaries Valve did write down —
which notably excludes B23 and the missing `dem_stringtables` — plus
[[where-the-game-and-clients-live]] for where the period clients are, and
[[record-both-points-of-view]] for how each specimen is recorded.
