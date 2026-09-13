---
name: no-task-cards
description: Main session is the first mate - side work goes to reviewed sonnet subagents, never cards or sibling sessions, and the main session works every merge
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

**2026-09-13, he widened it to sibling sessions and named the role**, while the main session merged that card's
branch to main: *"and this is why i like subagents over chips/sibling agents, I need a "first mate" to oversee
agents, so the work gets merged and done right. thats why i want you running subagents over chips, you get to
see where the work is at and work the merges so nothing gets weird."*

**What "this" was:** the card's branch, `fix/vphysics-solid-load-table`, had merged the in-progress B369
branch into itself, so a branch named for a loader fix held forty-four commits, most of them another port. It
had never been pushed or merged, its session raised the scope only when the merge was announced, and the
owner had to be asked what main should get (D170).

**How to apply, added:**

- **The main session is the first mate.** It runs the subagents, knows what every branch holds, and does the
  merges itself — gate, merge, push — so nothing is left for the owner to reconcile.
- **Prefer a subagent over a sibling session as well as over a card.** Where sibling sessions already exist
  (`git worktree list`, `ListAgents`), check what their branches hold against main before merging anything of
  theirs, and coordinate through `SendMessage` rather than working in their worktrees unannounced.
- **Once a sibling's work is merged, archive its session** (*"the siblling session is done after the merge it
  can be deleted and archived"*). Archiving is reversible and the main session does it; deleting is permanent
  and stays the owner's own click.

Recorded as D171.
