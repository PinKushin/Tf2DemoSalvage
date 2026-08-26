---
name: a-divergence-is-asked-not-documented
description: "Departing from Valve's implementation requires ASKING the owner first; writing the reason into a doc comment does not discharge the obligation."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-26T03:54:19.430Z
---

**Any departure from what Valve's code does is a QUESTION for the owner, not a decision to record.**
The owner, 2026-08-25, after catching the third one in a session:

> "if you diverge i need to be asked"

**The owner's framing, which says WHY the rule has no exceptions:**

> "Valve can be thought of as god in this project, and this project has to follows gods rules because
> it exists in gods universe lol"

That is not reverence, it is scope. A demo is a recording made BY the engine, of a world defined by
the engine, in a format the engine wrote — so every question about what a value means has an answer
that already exists, and any answer of ours that differs is simply wrong about the universe it is
in. There is no design space to have an opinion in. Reasoning toward what a thing "should" do is
therefore not analysis, it is guessing at something already written down.

**Why:** parity is the project's first principle (D89), and every measured win on the viewer has been
a move TOWARD the engine. A divergence chosen unilaterally and explained in a comment reads as
settled to the next person — mine or anyone's — so the wrong turn survives precisely because it was
written up well. The owner is the one who decides what the program is allowed to differ on.

**The failure mode is specific and I did it three times in one session, each time in the same
words.** The doc comments said "stated rather than dropped" and "a divergence stated rather than
hidden", which sounds like diligence and is the tell: writing it down felt like discharging it.

The three, for shape rather than for the detail:

- `MapOverview` — `CanPlayerBeSeen` rejects a player at exactly the origin (`// Invalid guy`); ours
  does not. I reasoned that demo entities are read rather than networked and `Drawn` already covers
  it. Plausible, mine, unasked.
- `LeafVis` — the leaf box is projected to clip space and ignores depth rather than being drawn in
  world space. Partly inherited from an existing overlay pass, which is not a reason to keep quiet.
- `LevelSystems` — explicit wiring instead of Valve's `IGameSystem` list-walk.

**The third one is the argument for asking, because my reason was simply WRONG.** I claimed a shared
`ILevelSystem` was impossible: it would need `LoadedMap` (Scene) visible to `SoundscapeSystem`
(Audio), and Audio does not reference Scene. One grep of `igamesystem.h` killed it —
**`virtual void LevelInitPreEntity() = 0;` takes NO parameters.** Valve's systems pull what they need
from globals, so the interface carries no payload and the boundary was never in the way. The owner:

> "there we go, i knew there was no reason to drift away from valves decisions"

**How to apply:** when a departure looks necessary, stop and ask before writing the code — and
before asking, go and read the actual declaration rather than reconstructing it from what the
divergence would need. Most "we cannot do what Valve does" claims are claims about a reconstruction.
Present the cost of both sides; the owner has said they can be influenced, so a recommendation is
wanted, but the choice is not mine.

Related: [[valve-parity-is-the-first-principle]], [[name-the-reading-you-picked]],
[[never-revert-without-asking]], [[an-optimisation-is-not-a-skippable-departure]],
[[name-the-trade-before-fixing-valve]], [[nothing-is-closed]].
