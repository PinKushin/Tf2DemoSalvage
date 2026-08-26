---
name: the-game-folder-is-the-users-to-provide
description: TF2's location is not known until the user points at it; a missing install must error clearly, never crash.
metadata:
  type: project
---

**The owner, 2026-08-26:**

> *"the user has to point us to their tf2 folder before we can do anything, and the program cant
> crash because its missing it must just error and mention it"*

Two requirements, and the second is the one that gets broken quietly.

**Nothing may throw on a missing install.** `GameContent.Open(null, …)` is a normal answer, not a
failure: it yields empty archives and logs `game folder: not found`. A demo still plays without a
map — that is the point of the program.

**And the message must name the right thing.** B211 was the failure of the second half: with no TF2
present the viewer said *"cp_badlands is not installed; fetching it"* and started a download, because
`MapProvider.Locate` returned null both for "the map is absent from an install we found" and for "we
found no install". One sentinel, two facts. `Find` now answers `Found` / `NotInstalled` / `NoGame`.

**Why:** telling someone the wrong cause is worse than telling them nothing. They go and look for the
map.

**This is why `_game` is opened lazily, and the code said otherwise for months.** The comment claimed
the archives are slow to open — true, and not the reason. The reason is that *the location does not
exist yet*. That matters because **lazy initialisation is otherwise a shape this codebase distrusts**:
D86 records the engine precaching at level load precisely so nothing is decoded mid-game, and ours
cost 385 ms in one frame when it packed on sight. Read as lazy-because-slow, `_game` looks like the
next thing to make eager. Read correctly, there is nothing to hurry.

**How to apply:** before "fixing" a deferred initialisation here, ask whether the thing is *expensive*
or *not yet knowable*. Only the first is laziness. And when a message reports something absent, check
which of the possible causes produced it — [[sentinels-conflate-unknown-with-answer]] is the general
form and this was an instance nobody had looked for.

Related: [[a-neutral-default-must-be-neutral]], [[name-the-reading-you-picked]].
