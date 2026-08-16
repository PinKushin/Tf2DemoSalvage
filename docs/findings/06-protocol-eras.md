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
| **13** | — | **≤ 15 November 2007** | that day's patch note, below — no client, no demo |
| **14** | 3420 | **19 March 2008** | client `version` |
| **15** | 3862 | **4 June 2009** | client `version` |
| **16** | 4604 | **15 June 2011** | client `version` |
| **24** | 5252 / 1729296 | **25 March 2013** | client `version` |

Gaps, still unmeasured: **12–13**, now squeezed into the five weeks after launch, and **17–23**,
between June 2011 and March 2013. Nine protocol numbers, and one of the two windows is much
tighter than it was.

## The one boundary Valve wrote down, and why a patch note is a ceiling

Everything above came from running a period client. Protocols 12 and 13 did not, because no
client for either survives — they come from the TF2 patch notes for **15 November 2007**:

> Added backward compatibility code to allow demos recorded with protocol 12 to continue to be
> playable under protocol version 13

Two protocol numbers in one sentence, which is more than the other eighteen years of notes
contain put together. The build shipped that day *is* protocol 13, so both 12 and 13 exist by
15 November — five weeks after a launch build measured at protocol 11.

**But the date belongs to the fix, not to the bump, and the first version of this section
conflated them.** Read what the sentence actually describes: *adding backward compatibility
code*. That is a repair, and a repair lags the break. For it to be written, protocol 13 had to
have shipped, players had to have discovered their protocol-12 recordings no longer played, and
somebody had to have reported it. None of that happens on the morning of the bump. So 13 was
live for some unknown number of days before 15 November, and 12 sits further back still — toward
the 25 October patch, the largest of the four candidates.

The corroboration is in the same patch. It carries a **second and separate** demo repair, *"Fix
for broken .dem file playback"*. Two independent demo fixes shipping together describes a demo
system that had been broken in the field for a while, not one that broke that morning.

So the row is graded **bounded**, not sourced: `13 ≤ 15 Nov 2007`, with the true date earlier by
an unknown margin. Calling it "dated exactly" would have been the single most confident wrong
thing in this document.

**The general lesson, and it needs stating precisely, because the first version of it was too
broad.** Valve publishes TF2's notes on the day the build ships, so **the note's date is exact for
the build it describes**. Nothing is lost there. What can lag is the *subject* of the note, and
only for one kind of note:

| the note says | the date is | because |
|---|---|---|
| "Added *X*" | **exact** for when *X* went live | the build shipping is the event |
| "Fixed *X*" / "Added compatibility for *X*" | an **upper bound** on *X* | the break, the report and the fix are three different days |

This entry is the second kind. A repair cannot precede the thing it repairs, so `13 ≤ 15 Nov 2007`
and the slack is whatever Valve's report-and-fix cycle was in 2007. A *feature* note carries no
such slack — which is what makes the message-table dating in [05](05-user-messages.md) much
stronger evidence than this row, despite both coming from the same changelog.

The trap is not "changelogs are unreliable". It is reading a **repair** as if it were an
**announcement**, which produces a date that looks measured, reads as measured, and is wrong by
weeks in a direction nobody checked.

That distinction is exactly why the evidence grades at the top of `TIMELINE.md` exist. **Measured**
rows come from a client that printed its own build string; this row came from Valve reacting to
users, which is a different and weaker kind of fact wearing the same clothes.

## Valve documents effects, never mechanisms

The above is the *only* protocol bump documented anywhere in TF2's patch notes. Not 11→12, not
13→14, not 15→16, not any of the eight bumps between 2011 and 2013. Nineteen years, one hit — and
finding it required searching for the demo breakage rather than for the word "protocol".

The reason is structural. A protocol change is a mechanism, so it is invisible to a changelog
written for players. A protocol change that breaks something a player notices becomes an *effect*,
and only then does it get written down. The protocol numbers in that sentence are incidental
detail that leaked into a note written about something else entirely.

Which is also why this route dated one boundary and none of the others, and why it cannot answer
whether 11→14 carried breaking format changes: those bumps broke nothing a player complained
about, so nothing was written.

It says one more thing, about the demo format's standing inside Valve. They *did* care that old
recordings kept playing — enough to write compatibility code five weeks after launch — and then
never did it again for any later bump. Which is precisely the gap this project exists in.

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

---

## The voice axis: Speex for six years, and a codec the modern x64 client cannot load

Evidence class: **measured on the corpus** for the demos, **measured on one machine** for the
installs. Both dated 2026-08-16.

Every committed era specimen declares the same codec, POV and SourceTV alike:

| Era | `svc_VoiceInit` codec | quality field |
|---|---|---|
| 2007 build 3258 | `vaudio_speex` | 5 |
| 2008 build 3420 | `vaudio_speex` | 5 |
| 2009 build 3862 | `vaudio_speex` | 5 |
| 2011 build 4604 | `vaudio_speex` | 5 |
| 2013 build 1729296 | `vaudio_speex` | 5 |
| modern (`z1800.dem`) | `vaudio_celt` | 22050 |

**Speex holds across the whole 2007–2013 range without wavering.** The quality field is 5 every time;
the modern demo reads 22050 because at quality 255 the message carries a 16-bit sample rate instead —
**the same field changes meaning rather than changing value**, which is the same shape as the
observer-enum hazard below and just as quiet.

### The part that matters, and it is the project's thesis in a new place

The period clients each ship `vaudio_miles.dll` and `vaudio_speex.dll`, and no CELT — consistent with
every demo they produced. The live install ships more:

| Client | `bin` | `bin/x64` |
|---|---|---|
| 2007 / 2008 / 2011 / 2013 | miles, speex | *(no x64 at all)* |
| modern | celt, miles, minimp3, speex | **celt, minimp3** |

**The 64-bit client ships no `vaudio_speex.dll`.** Its `x64/engine.dll` still contains the string
`vaudio_speex` — twice — so it can still be *asked* for a codec it has no implementation of.

So every demo in the table above, 2007 through 2013, requests a codec the modern 64-bit client cannot
load. This project decodes Speex, which means **it reads voice the live game no longer can** — the
founding argument for this parser, arrived at from the audio side rather than the schema side, and
measured rather than assumed.

### What the corpus cannot say

The change is **bracketed and not dated**: after the 2013 build, at or before `z1800`. Nothing in the
corpus sits between them, and that gap overlaps the protocol gap at 17–23. Closing it is a recording
problem — a specimen from 2014–2019 — not a parsing one. `CorpusVoiceCodecEraTests` asserts both ends
and skips on the undated middle rather than interpolating a date.

## An enum value inserted into the MIDDLE, and Valve says why

Evidence class: **read from published source**, `game/shared/shareddefs.h:499`.

```c
OBS_MODE_POI,   // PASSTIME point of interest - game objective, big fight, anything
                // interesting; added in the middle of the enum due to tons of
                // hard-coded "<ROAMING" enum compares
```

**A value was added to the middle of a networked enumeration on purpose**, because the surrounding
code compares `< OBS_MODE_ROAMING` and appending would have broken every one of those comparisons.
The cost did not disappear — it moved onto the wire. `OBS_MODE_ROAMING`, and everything else at or
after `POI`, is a **different integer before and after PASSTIME shipped**.

**This is the era axis appearing somewhere other than the protocol number.** Everything else on this
page is about the container and message layer changing between builds. This is a *value's meaning*
changing while the protocol version, the message id, the field width and the send table all stay
exactly the same. Nothing about the demo announces it.

The failure it produces is the worst-behaved kind: a decoder with a hardcoded `7` for roaming reports
an ordinary, legal camera mode on the wrong era. No exception, no impossible value, nothing to
notice — and for a POV or SourceTV demo the observer mode is precisely what the recording *was*.

**Two things follow for this project.**

1. **An enum is not automatically era-stable, and appending is not the only thing that can happen to
   one.** The working assumption had been that values are appended, so old values keep their meaning.
   That holds until a maintainer values source compatibility over wire compatibility, and here one
   did — and documented it in a comment rather than anywhere a parser author would look.
2. **The comment is the only evidence.** Nothing in `proto_version.h` marks this, because the
   protocol did not change. That makes it a class of era hazard that the boundary list cannot
   enumerate: it has to be found by reading the code that uses the value.

Worth a sweep of the other networked enums for the same shape before anything depends on their
numbering. `UnimplementedGameplayEntityConformanceTests` pins this one, including Valve's comment —
if it ever disappears, the fact it describes does not.
