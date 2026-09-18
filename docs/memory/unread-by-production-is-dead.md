---
name: unread-by-production-is-dead
description: "When production stops reading a component, delete it with its tests and probes; a test or probe is not a reader. D180."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-18T19:53:42.416Z
---

**Code that production does not read is dead, and goes with the tests and probes that call it** (D180, 2026-09-18). After the corpse
switch-over I kept the old solver's map world (`IvpWorldCollision`, `MapPropCollision`, `Gjk`) alive for one probe and some tests.
The owner: *"If production doesn't read it, it's dead code and can be removed with the tests that call it can't it?"*

**Why:** a test of code nothing runs proves nothing about the product, and a probe of a world nothing collides with measures the wrong
world. Keeping it also kept a cost in production: the old map world was still being built at every map load.

**How to apply:** after replacing a component, find its production readers (LSP `find_references`, restart the LSP after deletions,
because its index goes stale). None means delete it, with its tests and probes. Carry across only what production still uses, such
as constants or a conversion, to the component that owns it now. Port any test whose ENGINE rule still holds onto the new
implementation first; don't delete the rule with the old code. Related: [[a-test-can-outlive-its-design]], [[valve-parity-is-the-first-principle]].
