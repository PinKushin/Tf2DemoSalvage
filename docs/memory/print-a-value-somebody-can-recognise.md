---
name: print-a-value-somebody-can-recognise
description: A count cannot say whether a decode is right; a value a human can name can — TF2's paints came back as Pink as Hell and Noble Hatter's Violet, and a wrong read of the same bits is still "a colour".
metadata:
  type: project
---

**When a decode produces a value the world has a NAME for, print the value.** A count says the code
ran; a recognisable value says it ran correctly, and nothing else available does.

Measured 2026-09-04 implementing TF2's paint (`ItemTintColor`, B330). The `paint` probe prints hex
rather than a total, and what came back was:

| colour | paint |
|---|---|
| `#FF69B4` | Pink as Hell |
| `#E6E6E6` | An Extraordinary Abundance of Tinge |
| `#7D4071` | Noble Hatter's Violet |
| `#141414` | A Distinctive Lack of Hue |
| `#694D3A` | Radigan Conagher Brown |

Every one is a paint somebody has equipped. **"12 painted of 51 econ items" would have been equally
true of a wrong implementation** — the attribute's 32 bits are a float whose VALUE is the packed
colour, and reinterpreting the bits instead of truncating gives `0x4B67B53B` for `0xE7B53B`. That is
still "a colour", still non-zero, still counts as 12.

**The technique generalises to anything with a vocabulary outside this project**: a material name, a
map name, a model path, a class name, a hex colour, a known constant. Print it and read it. Where a
value has no such vocabulary — a bit offset, a float from an interpolation — this does not help and
a control has to come from somewhere else.

## The corollary: a rare branch shows up in real data or not at all

Two of the twelve came back `#B8383B / BLU #5885A2`, which is `RGB_INT_RED` 12073019 and
`RGB_INT_BLUE` 5801378 — **Valve's old team-colour sentinel, live in a 2026 match.** The attribute's
value 1 is not a colour; it selects two constants
(`GetModifiedRGBValue`, `econ_item_view.cpp:1612-1615`).

A synthetic test covers that branch only if somebody thought of it. Running the probe over real
demos is what proved the branch is REACHED — and reading its 1 as a colour would have painted two
hats near-black in every demo containing them, which is exactly the kind of defect that gets
reported as "that hat looks wrong" years later.

Related: [[measure-a-new-feature-on-a-second-demo]] — this was checked on two, 12 of 51 and 10 of
102. [[print-what-was-added-not-how-many]] — the same rule, one step less specific.
