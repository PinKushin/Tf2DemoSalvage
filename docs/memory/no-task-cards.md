---
name: no-task-cards
description: Do not offer spawn_task cards for side work; do it in the main loop or hand it to a reviewed sonnet subagent
metadata:
  type: feedback
---

**The owner, 2026-09-12**, after a card was offered for a `PhysicsHull` loader difference found while
porting the vertex-face search: *"you should probably not use cards, either take care of it yourself or
subagent it, those cards end up needing help being merged a lot of the time, because you end up hitting
the same files"*. He had already started that card, so it runs; the rule governs the next one.

**Why:** a card starts a separate session in its own worktree, and the work it is spun off from is
usually in the same files — here the card's `PhysicsHull.cs` had just gained `EdgeOffsets` on the
branch. Two sessions editing one file independently is a merge the owner then has to help with.

**Minutes later he added the other reason and softened it to a preference:** *"it just seems easier for
you all to pass messages back and forth, and for you to review if its a subagent, when its a peer agent,
you have to look into the worktree itself and only talk through that filesystem mcp server i think, if its
all the same really then i guess chips are fine, but i still would rather you subagent so they auto start"*.
So a subagent is the default because it starts on its own, can be messaged, and hands its work back for
review in place.

**How to apply:**

- **Do not call `spawn_task`.** Side work found in passing is done in the main loop, or delegated.
- **Delegate to one reviewed subagent on `sonnet`** ([[one-subagent-and-prefer-cheap-models]], D168),
  scoped to files the main loop is not touching, and review its diff before believing or committing it.
- **When a card is already running, stay out of its files** until it lands, then merge it — do not
  race it.

Recorded as D169.

**It recurred the same day**, in the B404 session: a card was offered for `PhysicsModel.Read` throwing
`InvalidOperationException` past Scene callers that catch `InvalidDataException`. The owner: *"told you not
to do those, so you get to do it here or call a subagent"*. That session had started before this entry was
written, and a running session's memory index is the snapshot from its own start — a rule another session
records mid-flight never reaches it. **Before offering side work anywhere, grep the memory directory for the
rule itself, not only the index that loaded at the start.** The work was then done in that session (B405).
