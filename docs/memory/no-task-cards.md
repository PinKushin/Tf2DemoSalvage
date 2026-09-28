---
name: no-task-cards
description: Main session is the first mate - side work goes to reviewed sonnet subagents, never cards or sibling sessions, and the main session works every merge
metadata:
  type: feedback
---

Owner, after a card was offered for a loader difference found mid-work: *"you should probably not use
cards, either take care of it yourself or subagent it, those cards end up needing help being merged a
lot of the time, because you end up hitting the same files."*

**Why:** a card starts a separate session in its own worktree, usually on the same files the current
work touches — two independent editors of one file means a merge the owner has to help with.

**Softened to a preference, same day:** *"if its all the same really then i guess chips are fine, but
i still would rather you subagent so they auto start"* — a subagent is the default since it starts on
its own, can be messaged, and hands work back for review in place.

**How to apply:**
- Do not call `spawn_task` — side work found in passing is done in the main loop or delegated.
- Delegate to one reviewed subagent on `sonnet` ([[one-subagent-and-prefer-cheap-models]], D168),
  scoped to files the main loop isn't touching; review its diff before believing/committing.
- When a card is already running, stay out of its files until it lands, then merge — don't race it.

Recorded as D169.

**Recurred the same day, in the B404 session:** a card was offered for `PhysicsModel.Read`
throwing past callers that catch a different exception type; owner: *"told you not to do those, so
you get to do it here or call a subagent"* — a rule recorded mid-flight in another session doesn't
reach a session already running (its memory index is a snapshot from its own start). **Before
offering side work, grep the memory directory for the rule itself, not only the index that loaded at
start.** The work was then done in that session (B405).

**Widened to sibling sessions, 2026-09-13, while the main session merged a card's branch to main:**
*"I need a 'first mate' to oversee agents, so the work gets merged and done right... you get to see
where the work is at and work the merges so nothing gets weird."* What prompted it: a card's branch
had merged the in-progress B369 branch into itself, so a branch named for a loader fix held
forty-four commits, most of them another port, never pushed or merged, and the owner had to be asked
what main should get (D170). The main session runs subagents, knows what every branch holds, and does merges
itself. Prefer a subagent over a sibling session too; where siblings already exist, check their
branches against main before merging, coordinate through `SendMessage`. Once merged, archive (not
delete) a sibling's session — archiving is reversible, deleting stays the owner's own click.

Recorded as D171.

**A subagent must not be exempt from this rule** — one called `spawn_task` itself, prompting the
owner to start the chip by mistake: *"you are not suppose to be doing those though asshole, that
takes more tokens."* **Every subagent prompt says "never call spawn_task; report anything out of
scope in your final answer instead."**
