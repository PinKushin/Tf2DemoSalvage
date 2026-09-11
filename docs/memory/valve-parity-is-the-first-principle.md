---
name: valve-parity-is-the-first-principle
description: Performance never buys a departure from Valve — matching the engine is where every measured win came from.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T03:30:26.470Z
---

The owner, 2026-08-25, mid-optimisation:

> "this isnt messing up the matching valve rule is it? we gained a lot of performance my matching
> valve, but weve seemed to lose part of them"

> "ok well dont change things that are valve parity, keep valve parity as first principal"

Recorded as **D89** in `docs/DECISIONS.md`.

**Why:** D82 bounds departures and D86 requires them declared where made, but neither says what
happens when a departure would be *faster*. This does: it doesn't happen. Parity is the constraint
the performance work happens inside, not one factor weighed against speed.

And the owner's observation is the empirical case for it — **every measured win on this viewer has
been a move toward the engine**:

| Change | Result | Valve's shape |
|---|---|---|
| one static mesh per model (B163) | 193–231 ms × 25 → gone | `CreateStaticMesh` |
| precache models at load (B163) | 385–425 ms in-frame → 515 ms once | `IsPrecacheAllowed()` |
| precache sounds at load | 27–91 ms every few seconds → 2,261 ms once | `Assert( "PrecacheSound: too late" )` |
| reuse the skinning buffer | ~20,000 arrays a frame → none | `m_CachedBoneData.SetSize` on count change |

An assistant proposal to keep the packed vertex buffer and append to it was overruled during B163
with *"so we switch to valves, which is what we should have been using in the first place, becasue
valves imp is blazingly fast"*. Same argument, and it was right then too.

**How to apply:**

- Before proposing a performance change, **find the engine's arrangement for the same problem**. If
  ours already matches it, the cost is elsewhere and the change is the wrong one.
- A candidate optimisation that departs from Valve is **first evidence the engine's arrangement is
  not understood yet**, not a trade to evaluate. Ask "what does Valve do here, and why is it fast".
- If the engine's arrangement genuinely IS the cost — profiled, not assumed — D86 applies: declare
  the departure where it is made, with the measurement that forced it.
- **A change moving toward Valve needs no such justification**, even when its purpose is speed.
  Parity restorations that happen to be faster are the ordinary case, not a lucky one.

Related: [[name-the-trade-before-fixing-valve]],
[[instrument-bugs-outnumber-decoder-bugs]], [[parity-is-the-search-not-the-defence]].

---

## `parity-is-the-first-hypothesis` — D148, and it governs DIAGNOSIS rather than design

The owner, 2026-09-07: *"basically any time theres anything wrong, look at valve parity first"* —
and on a hang: *"if this is partiy it shouldnt be doing this"*.

**Why:** the rule above governs design and the performance clause governs trades. Neither covers
DIAGNOSIS, and that is where the defects are. Four for four in one session: a contact solve invented
because its engine function was unread; static props missing from the physics world; a ragdoll
colliding with playerclip that `MASK_SOLID` excludes; and a hundred-pass loop transcribed at the
wrong scope. None was a coding mistake in the ordinary sense.

**The speed half is the one that keeps getting missed.** The engine runs a server full of ragdolls
at 66 ticks a second, so when our transcription cannot keep up the likely cause is a DIFFERENT
algorithm, not the same one slowly. Treating cost as cost sent an afternoon the wrong way; the
`an-optimisation-is-not-a-skippable-departure` section below is the trade version of this.

**How to apply:** before proposing any cause for a defect, name the engine function or SDK file the
code corresponds to and check it. When the symptom is cost, ask specifically whether one iteration
of our loop covers the same thing as one iteration of the engine's — the failure that day was a
bound read correctly (`iVar15 < 100`, per mindist pair) and applied to the wrong set (every contact
of a ragdoll). `~/.claude/hooks/block-fix-without-parity.ps1` refuses a fix commit that cites no
engine; `not-parity:` is the audited escape. Recorded as D148.

---

## `an-optimisation-is-not-a-skippable-departure` — Valve's optimisations earn their place

Proposing to match Valve's bone pipeline, the assistant offered one departure: keep `SetupBones` as
the entry point but **skip Valve's threaded bone setup**, on the grounds that it is "an optimisation
rather than semantics". The owner rejected it:

> *"go for the threading too, full parity thats an optimization valve did for vary good reason, the
> bones are heavy, they need speed."*

**Why:** an optimisation in shipping engine code was written against a frame budget by people
measuring it. The default assumption is that it earns its place. And in this specific case the
evidence was already in the repository and went unchecked — B99 records posing at **~420 ms of every
second**, which is precisely the cost Valve's pre-pass targets. The proposal to skip it was made
without looking at the number that had already been measured.

**How to apply:** when tempted to file something as "merely an optimisation", first find the
measurement that would decide it — this project usually has one already. Then treat a departure from
an optimisation as needing the same evidence as a departure from a behaviour
([[name-the-trade-before-fixing-valve]], and D86 in `docs/DECISIONS.md`).

Note also what the threading actually is, because the name misleads and that was part of the
misjudgement: it is a **speculative prefetch of last frame's expensive roots**, run between simulate
and render, not a parallel-for over the draw loop. See `docs/findings/35-the-bone-pipeline-audit.md`
§8 and D88. Related: [[parity-is-the-search-not-the-defence]],
[[measure-the-output-not-the-capability]].

---

## `baking-yields-to-parity` — D143, ours is the side that changes

The owner, 2026-09-06, while B363 (detail props that are MODELS) was being designed: *"valve doesnt
have baking, soo, their version isnt going to bake, and we might have to change our baking if valve
does something that is imcompatable"*. Recorded as D143.

**Why:** a detail model's alpha is the per-view distance fade (`CDetailModel::GetFxBlend` returns
`m_Alpha`), so it cannot live in a baked vertex buffer whose colours are fixed at load. The design
question "which side gives" has one answer: the baking. It is ours, no engine behaviour depends on
it, and parity is not tradeable for it — D89 applied to a structure instead of a branch.

**How to apply:** when a piece of engine behaviour will not fit the baked static-prop path, do not
look for the version of the behaviour that fits. Change the path. The trap is that a baked buffer
reads as architecture rather than as an optimisation, so it quietly acquires a veto it was never
given — and the symptom is a design discussion about what to implement rather than about what the
engine does.

Does not mean the baking is wrong: it stays wherever it reproduces what the engine draws, which is
most of a map.

---

## `refactors-are-when-to-check-parity` — the check is nearly free while the shape is being decided

The owner, 2026-08-25, during the `ShowMoment` extraction:

> "the refactors are perfect times to double check stuff like that"

**Why:** the code is being moved anyway, so reading Valve's arrangement for the same job costs one
grep at exactly the moment the new shape is being decided. And the failure mode is worse than
skipping the check elsewhere — **a divergence written into a NEW type is harder to spot afterwards
than one left in an old method**, because a freshly extracted class reads as deliberate.

It also works in reverse: an extraction is a chance to find a divergence nobody was hunting, because
the boundary being drawn has an equivalent in the engine, and comparing the two asks "why is ours
shaped differently" while changing the answer is still free.

**Found this way within minutes of starting**, and it decided the design:

| ours | Valve |
|---|---|
| `ShowMoment(double tick)` reads the camera off the form | `BuildRenderablesList( const SetupRenderInfo_t &info )` is TOLD the camera |
| — | `SetupRenderInfo_t` carries the output list, render origin, render forward, render frame |

`clientleafsystem.h:75` and `:169`. That ambient-state coupling is precisely what makes `ShowMoment`
untestable without a window, which is B188.

**How to apply:**

- Before choosing the shape of an extracted type, find the engine's equivalent and read what it is
  PASSED versus what it reaches for. Parameters versus ambient state is usually the whole difference
  between testable and not.
- Do it during the extraction, not after. Afterwards it is a rewrite instead of a naming decision.
- Record what the comparison found even when ours turns out to match — the check having been done is
  part of what a later reader needs.

Related: [[conformance-test-before-implementation]], [[nothing-is-closed]].
