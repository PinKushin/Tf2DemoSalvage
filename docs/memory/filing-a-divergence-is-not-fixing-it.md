---
name: filing-a-divergence-is-not-fixing-it
description: Measuring a divergence as rare decides its PRIORITY, never whether parity is owed — the owner's standing rule is fix it, and a well-written OPEN entry is the most convincing way to not do the work; the same shape covers a "still to read" note that already diagnoses the live bug, a stale "not implemented" comment, a measurement written down as a ranking that expires when the numbers move, and an impossibility claim nobody re-reads once it is disproved.
metadata:
  type: feedback
---

**A measurement that a divergence is rare decides what to do FIRST. It never decides whether to do
it.** The standing rule is explicit: *"If a divergence is found, FIX IT. Report what was done, not a
menu of what could be."*

The owner, 2026-09-04, on finding five items filed as OPEN in one session: *"OMG did you take more
shortcuts instead of just getting 100% valve parity?"* and *"seriously if you are not going to do it
all, at least give it to a subagent"*. Both were fair. The items were:

- the death animation branch, measured at ~1 corpse in 100 and left open
- two wearable skips, dismissed as needing "the item schema at a point that has neither" — the schema
  was one accessor away on a class the same layer already held
- the gold, ice and zombie overrides, not even read

**The trap is that a good OPEN entry looks like diligence.** Each had a citation, a measurement and a
statement of what was not established. That is exactly what makes it persuasive, and it is still the
work not being done. A divergence with a beautiful writeup is a divergence.

**Delegate rather than defer.** The agent cap is one at a time, so run one and keep working on
something else in the meantime; split by FILE OWNERSHIP so the concurrent edits cannot collide, and
say in the prompt which files are off limits. A subagent finished the item-schema half — with its own
tests and sabotage rounds — in the time it took to do the death animation by hand.

**And the rarity measurements are still worth taking**, just not as an exit. They caught that two of
the three taunt-kill exclusions in `CreateTFRagdoll` are unreachable, and that 0 of 11,497 items can
resolve to `LOADOUT_POSITION_HEAD`. Both are real findings about the engine that only came out of
implementing the thing.

Related: [[an-unrecoverable-input-is-not-an-open-choice]] (the same shape: a hard input is not a
licence to skip the logic), [[valve-parity-is-the-first-principle]],
[[parity-is-the-search-not-the-defence]].

**Five more memories were folded into this one on 2026-09-04**, all the same failure at a different
stage of the record: a diagnosis filed under a heading that reads as optional background, a
"not implemented" comment that outlives the feature it describes, a measurement written down as a
ranking that goes stale the moment either number moves, an impossibility claim nobody re-reads once
it is disproved, and a famous branch implemented before anyone counted how rarely this project's own
inputs reach it. Their names are kept as headings below.

---

## `an-open-item-is-a-defect-report`

`docs/RISKS.md` carried this for months, first on a list headed **Still to read**:

> the `update_baseline` flag and the two baseline slots. This parser ignores both, and **a baseline
> swap that changes how a later delta is interpreted would look exactly like this.**

It was a correct diagnosis. In the meantime the same symptom — spawn props drawing in the wrong
place or not at all — was blamed on entity parenting, on render mode, and on PVS. Three
investigations, two merges reverted.

**Why:** the note was filed under a heading that reads like optional background, inside the write-up
of a bug that had already been fixed. Nothing said *this is broken now*. Every wrong theory was also
a real Valve mechanism we had half of, so each produced a plausible story and some genuine fixes.

**How to apply:** when a symptom matches the TEXT of an open item, read that item before forming a
new theory — not after the new one fails. Grep `docs/RISKS.md`, the "still to read" tails, and
`docs/findings/` for the symptom's own words. And when filing: a mechanism we do not implement is an
open defect and belongs in a numbered entry a symptom search will surface, not in the tail of a
closed investigation.

More measurement would not have helped: all three wrong theories rested on correct measurements of
correctly decoded values. See [[nothing-is-closed]], [[parity-is-the-search-not-the-defence]] —
`baseline` and `update_baseline` were decoded and round-tripped for months with no consumer.

---

## `a-stale-not-implemented-is-a-todo-list`

**Grep the source for "not implemented", "not reproduced" and "is a gap" before planning parity
work.** They read as a to-do list, and a stale one costs twice: the work looks undone, and the
reader who checks discovers the DOCUMENT was wrong rather than the code.

Found false in one pass on 2026-09-03, all four in doc comments:

- `StudioSequences.cs` — *"`AddSequenceLayers` is not implemented"*. Implemented in `EntityModels`,
  both passes, and B307 had just fixed a branch of it.
- `StudioLayout.cs` — *"B82 is open … a halo or a canteen sits at the wearer's feet."* Attachment
  parenting reads, carries and applies `m_iParentAttachment`.
- `SkeletonPose.cs` — *"the flag is not read by this project's `.mdl` parser yet"*, about
  `BONE_FIXED_ALIGNMENT`, in a file that branches on that flag eighty lines below.
- `EntityTracker.cs` — *"instance baselines are not implemented"*, while `BaselineBuilder` does
  them. That grep also turned up the bigger fact: **the type has no production caller at all.**

**Why it happens: a comment is written when the gap is real and nothing re-reads it when the gap
closes.** The implementer works in a different file. So the claim ages in place, sounding
authoritative.

**Search for them in the same session you plan from.** The mirror is the entry below on an
impossibility claim expiring — a claim that something CANNOT be known, never re-read once it can.
Same failure, opposite sign.

---

## `a-measurement-recorded-as-a-conclusion-expires`

**Three OPEN entries in `docs/RISKS.md` were stale in one session (2026-09-03), all the same way.**
B157 described a substitution that had already been built. B254 said *"every prop the tick carries
is posed"* when nine of 567 are. B258 quoted `sample 2.0 ms` against a measured 0.3.

**None was wrong when written.** What they share is that a MEASUREMENT was written down as a
CONCLUSION. "Sample is 2.0 ms" is a fact about one build on one day. "Sample is the same size as
pose, so it is the next thing to fix" is a RANKING, and it expires the moment either number moves —
silently, because nothing re-runs it.

**Why it costs more than a wrong note.** These entries are the work queue. A stale one sends the
next session to re-derive a fixed problem, and it does so with the authority of a written record and
a citation. Two of tonight's hours went into confirming that two entries were describing a state the
code had left.

**How to apply:**

- **Put the command beside the number.** One line, runnable. `TF2VIEW_AUTOPLAY=1 tf2demoview <demo>
  --tick 14000 --first-person --measure 12 +fps_max 0` is cheaper to re-run than to argue with.
- **Re-measure before believing a ranking**, especially one that says "this is where the frame is".
- **Separate the reading of the engine from the ranking of the work.** B258's reading of
  `ProcessInterpolatedList` is still correct and still worth having; only its "therefore this is
  next" died. Kept apart, half the entry survives.
- **A counter that reports one of two exits reads as a failure of the whole.** B254's `0.3 hidden by
  pvs` looked like an idle cull; the frustum half simply returns first without counting. See
  [[instrument-bugs-outnumber-decoder-bugs]].

See [[read-the-trx-total-not-the-console]] and [[instrument-bugs-outnumber-decoder-bugs]].

---

## `an-impossibility-claim-expires`

`ViewerSettings.DefaultFrameRateLimit` said `fps_max`'s default *"could not be recovered from the
binary"*. `docs/findings/37-the-engines-demo-vocabulary.md` had already recovered it — **400**, with
flags — by reconstructing the pooled numeric block instead of reading string adjacency. Both were
written in this repository, weeks apart, and the two sat contradicting each other until a parity
audit on 2026-08-26 read them on the same day.

**Why this shape survives when a wrong positive claim does not.** A positive claim is load-bearing:
something calls it, a test pins it, changing the code forces a re-read. An impossibility claim is
inert. Nothing depends on it, so nothing drags it back into view — and the later finding that
disproves it has no reason to look for it, because it is off doing the thing the claim said could
not be done.

The reasoning is usually *correct*, which is what makes it stick. The string-pool argument here is
exactly right: the pooled layout really does put `engine_no_focus_sleep` beside `fps_max`'s help
text, and defaults really are single-character literals shared by hundreds of registrations. What
does not follow is "therefore unknowable" — that promotes a fact about **one instrument** into a
fact about the world.

**How to apply:** when a finding establishes something, grep for prior claims that it cannot be
established — `cannot`, `impossible`, `no way to`, `not recoverable` — and retire them in the same
commit. And when writing one, scope it to the instrument: *"not recoverable from the string pool"*,
never *"not recoverable"*. Related: [[nothing-is-closed]], [[a-filed-design-choice-may-not-be-one]].

---

## `measure-the-gate-before-building-the-branch`

**Before implementing a branch, count how many of this project's own inputs actually reach it.** A
memorable engine mechanism can sit behind a gate that almost nothing passes, and nothing in the code
says so — the gate is usually a `switch` or an early `return -1` several functions away.

TF2's corpse death animation was the case. The famous line is the coin flip:

```cpp
if ( !m_bIceRagdoll && !tf_always_deathanim.GetBool() && (RandomFloat( 0, 1 ) > 0.25f) )
    iDeathSeq = -1;
```

which reads as "a quarter of deaths play an animation" and got recorded that way. The gate in front
of it is `GetSequenceForDeath` — a `switch` on `m_iDamageCustom` with two cases and **no default**
(`tf_player_shared.cpp:13441-13455`). Only headshots, decapitations and backstabs are eligible;
everything else returns -1. Counted on real demos:

| demo | corpses | eligible |
|---|---|---|
| comp 6v6 | 159 | **0** |
| koth pub | 457 | 22 |
| koth pub | 147 | 5 |

**About one corpse in a hundred.** A day spent on it would have changed nothing anybody could see,
and the audit's own entry — which said "three quarters of ELIGIBLE deaths" — was literally true and
read as though eligible meant most of them.

**The measurement is cheap and it is the same probe you already have.** Decode the field the gate
switches on and count. Here it also produced the prioritisation: what actually lays a corpse down is
the physics, which every corpse takes.

**And the zero needs a control**, because a field decoding to its default looks identical to a real
absence ([[an-empty-search-needs-a-control]]). The spread settles it: the comp match's values were
`NONE`, `STANDARD_STICKY`, `ROCKET_DIRECTHIT`, `AIR_STICKY_BURST` — a soldier-and-demo match exactly,
with no sniper or spy to produce an eligible death. The pub demos, which field both, are where the
eligible ordinals appear at all.

**The same question with the answer the other way, an hour later.** `STUDIO_PROC_QUATINTERP` is
declared, named and computed by nothing — four bones on the player models in one demo. Four out of
540 sounds like nothing. The question that settles it is not the count but **whether any vertex is
weighted to those bones**, because a procedural bone nothing is skinned to computes a transform that
reaches no mesh. The `bone-flags` probe was taught to say `SKINNED` or `no-verts` for exactly this,
and all four came back SKINNED — a forearm that does not twist with the wrist. Worth building.

**And the follow-up caught an overclaim in that same sentence.** It first read "on every class
model", which came from a single tick containing a demoman and a scout — the smallest sample that
could produce it. Counted over all 14,109 models the game ships, the rule is on **three** classes,
scout, heavy and demoman. "Every X" from one observation is a guess wearing a quantifier, and it
survived being written into three documents before anyone counted.

So the rule is not "small counts are not worth it". It is **find the quantity that decides whether
anybody can see it, and measure that one** — eligible inputs for a branch, weighted vertices for a
bone, drawn instances for a prop. The raw count answers neither way.

Related: [[parity-is-the-search-not-the-defence]] (the branch you found is the one you do not
take), [[measure-the-route-before-building-on-it]], [[the-denominator-decides-what-can-be-lost]].
