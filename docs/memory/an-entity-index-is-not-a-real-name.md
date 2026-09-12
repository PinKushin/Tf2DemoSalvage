---
name: an-entity-index-is-not-a-real-name
description: "Who entity N is comes from the roster, never from a nameplate in a screenshot — and the roster itself was one entity short (B398); a userinfo entry is a client slot, entity = slot + 1"
metadata:
  type: project
---

**A nameplate in a capture names whoever is in frame, not whoever holds the camera.** B397 compared
the wrong players' views several times because the camera was guessed from what was visible.

**And the roster that should have settled it was wrong too.** `RosterBuilder` took the `userinfo`
entry index as the entity index; the entry index is the CLIENT SLOT, and entity = slot + 1, because
entity 0 is the world (`UTIL_PlayerByIndex`, `game/server/util.cpp:565`). Every name sat one entity
short, so `--spectate <name>` landed on the neighbour — on an STV demo, on the SourceTV bot — and a
"resolved" verdict naming entity 7 "nezay" was drafted off it. Entity 7 was abelll. Fixed in B398.

**How to apply:** confirm an index with the `roster` probe, then `--spectate <name>` or the user id.
Before trusting ANY index-to-name mapping, check one against something that must hold: on a POV demo
the header's client name must be the roster name at `RecorderEntityIndex`; on the f12 demo, entity 2
must be Beleleu. Never declare a divergence resolved off a capture the owner has not confirmed. Same
family as [[an-entity-index-does-not-name-a-track]] and [[instrument-bugs-outnumber-decoder-bugs]].
