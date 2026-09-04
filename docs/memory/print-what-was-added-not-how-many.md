---
name: print-what-was-added-not-how-many
description: When a change makes a count go up, the count reads as success — name the things it added instead, because the identities carry the bug and the quantity cannot.
metadata:
  type: feedback
---

**When new work makes a number go up, print the NAMES of what it added, not the number.** A count
rising is exactly what a working feature and a wrong one both look like.

Measured (B320). Hanging a corpse's cosmetics on it took the drawn count at one tick from 4 to 24.
That is the right shape — four corpses, twenty items, five each — and it is what a success looks
like. Printing the model names took one more line and said:

```
c_grenadelauncher.mdl   c_stickybomb_launcher.mdl   c_bottle.mdl   c_pickaxe.mdl
```

All four of a demoman's WEAPONS, holstered ones included, hung on his corpse. The scan had walked
every bone-merged child of the dead player; the engine walks the econ wearable list, and a weapon is
not in it. Twenty items was never going to reveal that. Four names did, instantly.

**The general rule: a count answers "did something happen", and the thing that goes wrong is usually
WHICH something.** Wrong set, wrong owner, wrong era's field, the same item twice. None of those
changes the magnitude in a way anyone would notice, and several make it look better.

**Cheap enough that there is no trade.** Cap the list at a handful and print it beside the count —
the count still shows the scale, the names still show the identity. Every probe in this repository
that earns its keep does both.

Related: [[it-ran-and-it-mattered-are-two-claims]] (execution against magnitude — this is magnitude
against identity), [[log-the-event-not-a-sample-of-it]], [[instrument-bugs-outnumber-decoder-bugs]],
[[measure-the-output-not-the-capability]].
