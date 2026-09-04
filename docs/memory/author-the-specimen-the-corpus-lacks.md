---
name: author-the-specimen-the-corpus-lacks
description: The writer is a test instrument, not just a product feature — a case no demo contains can be authored rather than hunted for; covers what makes a field truly untestable on ordinary play (a default that is the operation's identity), why a feature scoring 159 of 159 on one demo needs a second era before it counts as done, the standing rule to check backwards compatibility the moment an old demo is in play, and why a POV recording's missing props are the PVS rather than a decode gap.
metadata:
  type: project
---

**When the corpus does not contain a case, write a demo that does.** This project can emit `.dem`
files the engine accepts ([[engine-accepts-authored-demos]]), and that makes the writer a *testing*
capability as much as a product one. A case no recording happens to contain is not automatically a
case that cannot be tested.

**This is the inference that gets forgotten**, including by the assistant, on 2026-08-19: a test
skipped because cp_process_final's own materials run no time-driven proxy, and the response was to
hunt for a map that had one rather than to consider authoring the input. The owner had to point it
out, and said explicitly that they will not always think of it at the right moment. So it is written
down here rather than left to be re-derived.

Where it applies, in rough order of value:

- **The era gaps.** Protocols 12–13 and 17–23 have no specimen and community demos are genuinely
  rare (`docs/DECISIONS.md` D5). Anything currently flagged **interpolated** in `docs/findings/`
  is a candidate.
- **Messages the corpus never carries.** A decoder branch real demos never take is most of a
  decoder ([[most-of-a-decoder-is-untested]]); an authored file can take it deliberately.
- **Edge values and malformed input.** Adversarial bytes with a known intended meaning, which is
  stronger than fuzzing alone because the expected result is known.
- **Anything whose absence would otherwise be an eternal skip.** A test that can only ever skip is
  a test that can never be wrong.

**Two distinctions the owner has drawn, and they are not the same.** *Cutting up* an existing demo
was called "a little cheaty" as a test, and the truncation code written for it was deleted the same
day. *Authoring* a specimen to exercise a specific case was endorsed outright. The difference is
between trimming someone else's recording and constructing an input whose contents you chose and can
therefore predict.

**And check whether a demo is needed at all first.** The 2026-08-19 case did not need one:
`MapAssets.Load` takes the entity model list as a parameter, so naming the capture point models
exercised the path directly. Reach for the writer when the thing under test is the *demo stream*,
not when it is something a demo merely happens to supply.

Related: [[round-trip-needs-the-encoding-shape]] for what an authored file has to record beyond the
values, [[fixtures-are-the-weak-point]] for why an authored input is still weaker evidence than
a real one where a real one exists.

**Four more memories were folded into this one on 2026-09-04**, all about the same boundary: what a
corpus of ordinary play structurally cannot show, and the standing habits that follow from knowing
it. Their names are kept as headings below.

---

## `a-default-valued-field-is-untestable-on-the-corpus`

**A field whose default makes it a no-op cannot be tested on a corpus of ordinary play, ever.**
Not "we have not found a demo yet" — the observation is impossible in principle, because the correct
implementation and the missing one produce identical output at that value.

`m_flHeadScale`, `m_flTorsoScale`, `m_flHandScale` (B312): all three multiply a scale and default to
**1**. Every recording in the corpus reports 1 — 440 of 440 on `z1800` — so every rendering
comparison agreed and every count matched while nothing read the fields at all. The same shape as
`m_flPlaybackRate`, decoded and retained and unit-tested while every animation played at rate 1.

**The tell: a default that is the IDENTITY of whatever operation the field feeds.** 1 for a
multiplier, 0 for an offset, empty for a list that is concatenated. Grep for the field, then ask what
the engine's own initialiser sets it to (`c_tf_player.cpp:577` here) — if that value is the identity,
no measurement of ordinary content can ever find the gap.

**So author the specimen**, per the discussion above. `SyntheticPlayer.Demo` takes a property
dictionary and writes a real demo through the real container and schema; the value comes back out of
`DemoTimeline` having been through production's decode. That converts "correct by construction and
citation" into "observed".

**Use values that are distinct from each other AND from the default.** Equal values let a carry into
the wrong field pass; the default is what every lost hop falls back to. Three fields, three numbers,
none of them 1.

**And test the DEFAULT's own claim separately.** A control asserting "nothing sent leaves it at 1"
stays green through every sabotage of the carry, because its input is null on both sides — only a
sabotage of the coalesce itself (`?? 0f`) reddens it. Two claims, two inputs.

---

## `measure-a-new-feature-on-a-second-demo`

**Before calling a decode feature done, run its probe on a demo from another era.** One extra
command, and it is the difference between "159 of 159" and finding that the same feature scores zero
on everything older.

Measured (B319). A corpse's orientation is reached through the player it was, since `DT_TFRagdoll`
sends no angles. Built and measured against a 2026 SourceTV demo: **159 of 159**. Run on `z1800`,
which is a committed era specimen: **0 of 407**. The two demos name the field differently, and the
values are not even the same kind:

```
DT_TFRagdoll.m_hPlayer        24587, 174093, 311301, …   packed ehandles, need Resolve
DT_TFRagdoll.m_iPlayerIndex   2, 3, 4, 5, 6, …           entity indices, used as they stand
```

**`m_iPlayerIndex` is not in the published SDK at all** — not even kept as a `RECVINFO_NAME` alias.
Reading the SDK could not have found it, and no amount of care about the modern name would have
helped. Only a demo carries it. That is the whole premise of decoding off the embedded schema
([[the-demo-dates-its-own-fields]], [[wire-names-are-strings]]).

**The tell that a feature is under-measured is a perfect score on one file.** 159 of 159 reads as
proof and is really a statement about one recording. Two demos of different eras cost one more
command, and this project keeps era specimens precisely so that command exists.

**And prefer a committed era specimen over another modern demo.** The gcor corpus is one file per
era for this reason; picking a second 2026 match would have scored 159 of 159 again and taught
nothing.

Related: [[era-axis-is-measured]], [[an-empty-search-needs-a-control]], [[record-both-points-of-view]].

---

## `check-backwards-compat-on-old-demos`

Owner, after the doubled-viewmodel bug: *"you know the demos have to be backwards compat to 07, we
should probably check the 07 demo after this... thats why we should looks towards backwards compat
immediately whenever we are using an old demo."*

**Why:** the bug was a modern assumption — that a first-person weapon is always hands plus a
separate gun — applied to a 2011 recording where it was one combined `v_` model. It survived because
nothing exercises the era specimens end to end. The owner named the gap precisely: *"we dont ui test
every demo we have, and i dont look at every one before we commit"*, so a rendering regression on an
old file is invisible to both the suite and the eye.

**How to apply:** when a change touches how something is drawn or resolved, ask what the oldest
supported demo does with it, and open one. The era axis is measured — protocols 11, 14, 15, 16 and
24, with matched POV/STV pairs — so the specimen exists. Related:
[[a-player-has-two-viewmodels]], [[era-axis-is-measured]], [[record-both-points-of-view]],
[[the-demo-dates-its-own-fields]].

**A constraint on verifying era behaviour:** the period clients have **no internet connection**, so
a modern item cannot be loaded in them to compare. Whether a modern-only symptom also occurs on an
era demo often cannot be checked in the original client at all, and the answer has to come from the
shipped data and the SDK instead.

---

## `pov-demos-are-pvs-limited`

A POV `.dem` is one client's **received** packet stream. The server transmits an entity to a client
only when it passes the PVS check, so a POV recording physically cannot contain entities the
recorder could not see. Fly the free camera to the other end of the map and the medkits, ammo packs
and control points there were never in the file.

Measured 2026-08-16 on `tf2-2013-build1729296-pov-cp_badlands.dem` (the UI-test demo) against
`demostf-cp_process_f12-2026-08-08-2207.dem` (SourceTV), same build of the viewer:

| | badlands POV | process STV |
|---|---|---|
| studio props drawn, peak | **16** | **94** |
| `cap_point_base` in one frame | never above **2** | **5** |
| `medkit_small` in one frame | up to 4 | 7 |

The badlands timeline holds 5 cap points, 20 `ammopack_small` and 14 `medkit_small` **over the whole
recording** — they exist, just never at once. That is the shape of a PVS-limited stream, not a
decode gap.

Valve's side: `FL_EDICT_PVSCHECK` is the default transmit state — `CBaseEntity::SetTransmitState`
returns it at `game/server/baseentity.cpp:4025` and `UpdateTransmitState` falls through to it at
`:4096`. Entities opt **out** of PVS (always-transmit); they do not opt in.

**Why this is worth a memory: it imitates a regression perfectly.** It presented as "all the props
went away — the cap point, the health packs, the ammo packs" and consumed a session of bisecting
skin retention, track identity and the draw-loop skip counters, all of which were healthy. The one
question that would have ended it immediately is *which demo*, because every earlier screenshot had
been of a SourceTV recording.

**So: verify rendering on an STV demo.** Use a POV demo only when the point is the recorder's own
view. [[record-both-points-of-view]] is the same distinction from the writer's side.

Related: [[an-empty-search-needs-a-control]] — "no props here" was a fact about the input, not about
the code, and a second demo was the control that showed it.
