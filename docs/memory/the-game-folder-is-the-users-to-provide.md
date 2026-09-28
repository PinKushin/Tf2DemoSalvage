---
name: the-game-folder-is-the-users-to-provide
description: TF2's location is not known until the user points at it; a missing install must error clearly, never crash.
metadata:
  type: project
---

Owner: *"the user has to point us to their tf2 folder before we can do anything, and the program cant
crash because its missing it must just error and mention it."*

**Nothing may throw on a missing install** — an empty archive with a logged reason is a normal answer,
not a failure. A demo still plays without a map.

**And the message must name the right thing** — B211 was the failure of the second half: with no TF2
present the viewer said *"cp_badlands is not installed; fetching it"* and started a download, because
`MapProvider.Locate` returned null both for "map absent from a found install" and "no install found".
`Find` now answers `Found` / `NotInstalled` / `NoGame`, three distinct states.

**Why:** telling someone the wrong cause is worse than telling them nothing — they go look for the
wrong thing.

**This is why the game archive is opened lazily, and the code said otherwise for months** — a comment
claimed it's slow to open (true, not the reason); the real reason is the location doesn't exist YET.
Lazy initialisation is otherwise distrusted here (D86: the engine precaches at level load so nothing
decodes mid-game) — read as lazy-because-slow, this looked like the next thing to make eager. Read
correctly, there's nothing to hurry.

**How to apply:** before "fixing" a deferred initialisation, ask whether the thing is EXPENSIVE or NOT
YET KNOWABLE — only the first is laziness. Check which of several possible causes produced an absence
message — [[sentinels-conflate-unknown-with-answer]] is the general form.

Related: [[a-neutral-default-must-be-neutral]], [[name-the-reading-you-picked]].
