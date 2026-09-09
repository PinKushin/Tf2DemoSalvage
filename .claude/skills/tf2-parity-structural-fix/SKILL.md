---
name: tf2-parity-structural-fix
description: Use when working on a filed parity divergence (a B### in docs/RISKS.md whose finding is about STRUCTURE — two things the engine keeps apart that we keep together, a condition we reach by a different route, a history or list shaped unlike the engine's). Forces the replacement rather than a patch on one of its symptoms.
---

# Fixing a structural divergence

**A structural divergence's symptoms are not separate tasks.** If a `B###` says the engine keeps two
histories and we keep one, then every wrong pose, every stutter and every wrong spline that follows from
that is the SAME defect. Patching one of them is forbidden here, and D155 is why:

> *"i dont care if your hack worked, you shouldnt even have done the hack actually, because that was a
> waste when it needs to be ripped out and replaced"*

Three costs, and the third is the one that lasts:

1. **The work is spent twice** — the patch lives in the code the real fix deletes.
2. **The evidence goes away.** A green test and a working door make the structural entry look academic.
3. **The log claims a fix.** Somebody later reads "FIXED" and does not look again.

## The order, and it is not negotiable

1. **Quote the engine's structure** into the risk entry: the append conditions, the containers, the
   search key, the prune rule. Every one with `file:line`. If any is still unread, read it — a structure
   half-known produces a replacement that is a third thing, neither ours nor Valve's.
2. **Write the conformance tests from that quote, and let them be RED.** They are red because the
   structure is wrong; that is the correct state and it is the proof the tests can fail.
3. **Replace the structure.** Not alongside — the old shape comes out. If it cannot come out in one
   change, the intermediate states must each be honest: no branch that says "if the old shape, do the old
   thing" left behind as a permanent fallback.
4. **The symptom test going green is the evidence.** Do not add a special case to make it green sooner.
5. **Then check the neighbours.** A structure that was wrong here is usually wrong in its siblings —
   the animation clock's copy of the simulation clock's bug, the second pass that never got the first
   pass's fix.

## What counts as the forbidden patch

Anything that reads the collapsed, merged or otherwise wrong-shaped data and *infers* what the engine's
shape would have contained. Recognisable by its comment: *"the engine would have a second entry here, so
use this"*. That sentence is the tell — it is a reconstruction of a structure instead of the structure.

**A reconstruction is allowed only when it is proved equivalent for every input**, and "equivalent for the
case I tested" is not that. Where equivalence is claimed, the risk entry states what would falsify it.

## Where our storage differs from the engine's on purpose, and how to keep parity anyway

We hold a whole recording and can seek; a live client cannot. So the engine's **prune** —
`RemoveEntriesPreviousTo( currentTime - interpolation_amount - EXTRA_INTERPOLATION_HISTORY_STORED )` at
the end of `Interpolate()`, keeping `Truncate( i + 3 )` — has no equivalent for us, and must not be copied
as a deletion.

**Bound the REACH, not the storage.** Keep every entry so a scrub backwards still has data, and make the
neighbour search consider only what the engine's pruned history would have held at that moment. The owner
set this requirement directly: *"we should be able to get valve parity there and still scrub and rewind
the demo, we just have to make it work in both directions."*

That is the one licensed difference. It is a difference in what is RETAINED, never in what is ANSWERED.

## Before claiming it is done

- The old structure is gone from the file, not disabled.
- The conformance tests that were red are green, and none of them was edited to get there.
- One test was sabotaged and reddened, so the suite can still fail (`sabotage-verifier`).
- The risk entry says what is still not established. A structural fix nearly always leaves a sibling
  unchecked, and naming it is the difference between finished and abandoned.
