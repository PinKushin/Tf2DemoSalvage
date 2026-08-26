---
name: a-walking-test-cannot-see-a-deletion
description: A test that iterates whatever exists passes when something is missing; generate the denominator instead.
metadata:
  type: reference
---

**A test that walks a collection and checks a property of each member cannot detect a member that is
gone.** One fewer item is one fewer to check, and the assertion still holds.

Measured 2026-08-26, moving 363 lines of menu construction out of `MainForm`. Three tests covered
that menu and none of them could have seen an item fail to arrive:

- `ShortcutCollisionTests` — walks every menu item, asserts no two claim the same key. Deleting an
  item removes a *potential collision*, so it passes more easily.
- `DebugMenuWiringTests` — addresses six items by name. Silent about a seventh.
- The UI suite — presses F11 and invokes the screenshot item. Two of twenty.

**Confirmed by manipulation rather than argued:** with one item dropped from the View menu,
`ShortcutCollisionTests` **passed**.

**The fix is a generated denominator.** The new test reads every `*ItemId`/`*MenuId` constant off the
type by reflection and requires each to name something reachable in the strip; a second uses the
`bool` property count of the `DebugModes` record as the expected submenu size. A constant added
without an item fails; an item deleted fails; nobody maintains a number, so it cannot go stale.

This is the same split `SdkCoverageTests` uses in this repo — **the generated instrument catches what
is MISSING, the hand-written one catches what is WRONG** — and only the first kind survives someone
forgetting to update it.

**How to apply:** when a test iterates a collection, ask what it would do if the collection were
empty. If the answer is "pass", it is measuring the wrong thing, and the missing half is a count or a
set comparison against a denominator that comes from somewhere else. Prefer somewhere the compiler
already knows about — constants, record fields, an enum — over a literal.

**A hand-written exact count is still worth having beside it.** `view.DropDownItems.Count.ShouldBe(12)`
goes stale by design: it fails when the menu changes, which is the moment a person should look.

Related: [[a-count-cannot-see-past-a-pruner]], [[the-denominator-is-already-written-down]],
[[an-empty-search-needs-a-control]].
