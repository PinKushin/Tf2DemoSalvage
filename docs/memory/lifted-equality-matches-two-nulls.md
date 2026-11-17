---
name: lifted-equality-matches-two-nulls
description: `int? a == int? b` is true when both are null; comparing an entity's optional owner to an optional "recorder" matches every ownerless entity in a demo that has none.
metadata:
  type: feedback
---

C#'s lifted `==` on two `int?` values is **true when both are null**. B746 first wrote
`corpse.PlayerIndex == recorder` to hide the recorder's own decapitated head; in a SourceTV demo the recorder is null,
so every corpse that named no player matched and had its head hidden. The synthetic test for "another player's head is
drawn" caught it only because its corpse had no `PlayerIndex`.

**Why:** the comparison reads as "is this the recorder's", and the both-null case is invisible at the call site.

**How to apply:** when one side means "the one we are looking for" and may be absent, unwrap it first —
`recorder is { } local && corpse.PlayerIndex == local`. Give the test a subject whose optional field is null, as the
control that catches it. Related: [[nullable-pattern-on-a-struct-is-dead-code]].
