# 06 — Protocol eras: what changed, and when

**This is the chapter with the least published prior art.** Valve ships `proto_version.h`, which
says *what* each protocol change was, in order. It does not say **when**, and no public source
gives dates. The table below was measured by running period clients, and as far as this project can
tell it does not exist anywhere else.

## The dating method

Three techniques, in increasing order of confidence.

**1. Asset dating (weak, but needs nothing).** A demo's string tables name cosmetics, and cosmetics
have release dates. `sum20_fire_fighter_style1` means Summer 2020 or later. This gives a *lower
bound* only, and it fails completely on a freshly recorded listen-server demo, which carries no
cosmetics at all.

**2. Reading `engine.dll` before downloading (cheap, exact).** Old client packages on archive.org
can be range-requested: pull `bin/engine.dll` out of the ZIP — about 4 MB — and read its embedded
build string. This dates a candidate build *before* committing to a multi-gigabyte download, which
is what made surveying the era axis practical at all.

**3. Running the client (exact, and the standard used here).** The `version` console command
reports protocol, exe version and build date together:

```
Protocol version 11
Exe version 1.0.0.5 (tf)
Exe build: 18:14:51 Oct  9 2007 (3258)
```

Then record a demo on that client and confirm the demo header states the same protocol. That
closes the loop: the file, the binary, and the date all agree, and none of it depends on inference.

## The measured table

| Protocol | Build | Date | Evidence |
|---|---|---|---|
| **11** | 3258 | **9 October 2007** | client `version`; TF2 shipped 10 Oct 2007, so this is the launch build |
| **14** | 3420 | **19 March 2008** | client `version` |
| **15** | 3862 | **4 June 2009** | client `version` |
| **16** | 4604 | **15 June 2011** | client `version` |
| **24** | 5252 / 1729296 | **25 March 2013** | client `version` |

Gaps, still unmeasured: **12–13**, somewhere between October 2007 and March 2008 — a five-month
window — and **17–23**, between June 2011 and March 2013. Nine protocol numbers across two narrow
windows.

## The cadence of change, which is not uniform at all

The dates above are only half the result. Put the intervals side by side and the wire format has a
clearly periodised history:

| Span | Elapsed | Bumps | Rate |
|---|---|---|---|
| 11 → 14 | 9 Oct 2007 → 19 Mar 2008, **162 days** | 3 | ~54 days per protocol |
| 14 → 15 | 19 Mar 2008 → 4 Jun 2009, 443 days | 1 | — |
| 15 → 16 | 4 Jun 2009 → 15 Jun 2011, **741 days** | 1 | the quietest stretch |
| 16 → 24 | 15 Jun 2011 → 25 Mar 2013, 649 days | 8 | ~81 days per protocol |
| 24 → now | 25 Mar 2013 → 2026, **13+ years** | 0 | frozen |

**Three protocols in the first five months, then two in the next three years.** That is a launch
period doing what launch periods do — the network format was still being worked out in public —
followed by a long stable stretch, then a second burst through 2011–2013, and then nothing at all
for thirteen years and counting.

Two things follow that are directly useful rather than merely interesting.

**Protocols 12 and 13 may have been live for only weeks each.** They sit in the fastest-churning
window the format ever had, averaging under two months per bump. That is a strong explanation for
why no demo from them has ever surfaced: it is not only that early TF2 demos are rare, it is that
the *window in which one could have been recorded* was tiny. Anyone hunting for them should be
looking at builds from roughly November 2007 to February 2008 and expecting a narrow target.

**Protocol 24's freeze is why this project is possible.** A parser aimed at a format that bumped
every two months would be chasing it forever. Instead the overwhelming majority of demos in
existence — everything from 2013 to now — share one container revision, and the historical work is
a bounded set of five known-and-four-unknown earlier revisions rather than an open-ended one.

The freeze also explains the trap in the previous section: thirteen years of demos all reporting
network protocol 24 is exactly what makes the number useless for dating, and what made "~2015"
look like a reasonable guess when the file was 2020 or later.

## The protocol number dates nothing by itself

The correction that motivated all of the above. An early demo (`z1800.dem`) was estimated at
"~2015" from its protocol pair. It is **2020 or later** — protocol 3 / network 24 spans at least
2013 through 2026, thirteen years and counting, because Valve stopped bumping the number.

So: **date the client, never the protocol.** A protocol number is an upper bound on age and
nothing more. This is recorded in `docs/memory/z1800-is-modern-not-2015.md` because it is the kind
of wrong conclusion that gets repeated confidently.

## What changes at each boundary

From `proto_version.h` where it says so, from measurement where it does not:

| Change | Boundary | Source |
|---|---|---|
| String table compression flag | above 14 | `proto_version.h` |
| Message type field 5 bits → 6 bits | between 15 and 16 | **measured** |
| `SendPropType` renumbering | above 15 | measured + differential |
| `svc_ServerInfo` replay flag | above 15 | measured at 16, the first value that carries it |
| MD5 replaces 4-byte map CRC | above 17 | `proto_version.h` |
| Sound index width | above 22 | `proto_version.h` |
| `svc_Prefetch` index 13 → 14 bits | above 22 | `proto_version.h` |
| Varint string table lengths | above 23 | `proto_version.h` |
| Temp entity varint lengths | above 23 | `proto_version.h` |
| **`Damage` user message layout** | between 14 and 15 | **measured, unpublished** |
| **`dem_stringtables` command absent** | 14 and below | **measured, unpublished** |

The last two are worth separating out: neither appears in `proto_version.h`, and the second was
not predicted by anything. Protocol 14 demos carry **no `dem_stringtables` command at all** — the
tables arrive only as `svc_CreateStringTable` during signon. That was confirmed rather than assumed
because the corpus holds a POV *and* a SourceTV recording at protocol 14, and both lack it, which
makes it an era property rather than a recording-mode one.

## Boundaries are only really tested at the value where they change

Protocol 16 was the single most valuable specimen acquired, and the reason is methodological. The
replay flag is read as `protocol > 15`, so **16 is the first protocol that carries it**. Every
other corpus entry tests that rule from a distance — 14 and 15 from below, 24 from well above. A
rule can be wrong in a way that only the adjacent value reveals.

Protocol 16 also holds a combination nothing else does: new `SendPropType` numbering and a six-bit
message type *together with* a 13-bit prefetch index and fixed temp-entity lengths. That is exactly
the interpolation the 15-to-24 jump had forced the parser to guess at.

## Numbers that move, and numbers that do not

Useful because a fingerprint that does not vary is worthless as one:

| Quantity | 11 | 14 | 15 | 16 | 24 |
|---|---|---|---|---|---|
| String table **count** | 16 | 16 | 16 | 16 | 16 |
| `max_classes` | 216 | 216 | 232 | 256 | 275 |

The table count never moves across eighteen years, so it dates nothing — the table *names* are the
discriminator. `max_classes` grows monotonically but is **non-decreasing rather than strictly
increasing** (216 at both 11 and 14), so it bounds an era rather than identifying one.

## Manufacturing evidence instead of waiting for it

The corpus cannot be extended backwards by searching — pre-2013 TF2 demos barely exist. ETF2L's
archive has rotted below id ~12010, ESEA's SourceTV demos shipped with expiry dates, and the
community collections catalogued in 2013 are dead. See `docs/DECISIONS.md` D5.

But **an era's client can be made to emit whatever you need**, and that changes the shape of the
problem. Protocol 11's `Damage` rule rested on nothing, because the committed protocol-11 demos
contain no `Damage` message — nobody was hurt in them. Fix: play a soldier, stand next to a
resupply cabinet, rocket-jump into yourself for 52 seconds. 43 damage messages, 460 KB, boundary
closed.

For any era whose client runs, **a missing message is a recording task rather than a search.**
That is the cheapest evidence on this axis and it generalises to every message type. What it
cannot do is fill protocols 12–13 and 17–23, where the problem is finding the *build*, not finding
the gameplay.
