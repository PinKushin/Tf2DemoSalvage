---
name: unread-by-production-is-dead
description: "When production stops reading a component, delete it with its tests and probes; a test or probe is not a reader. D180."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-18T19:53:42.416Z
---

**Code production doesn't read is dead, and goes with the tests and probes that call it (D180).**
After a solver switch-over, the old solver's world was kept alive for one probe and some tests. Owner:
*"If production doesn't read it, it's dead code and can be removed with the tests that call it can't
it?"*

**Why:** a test of code nothing runs proves nothing about the product, and a probe of a world nothing
collides with measures the wrong world. Keeping it also kept a production cost — the old world was
still being BUILT at every map load.

**How to apply:** after replacing a component, find its production readers (LSP `find_references`,
restart the LSP after deletions since its index goes stale). None means delete it with its tests and
probes. Carry across only what production still uses (constants, conversions) to the new owner. Port
any test whose ENGINE rule still holds onto the new implementation FIRST — don't delete the rule with
the old code. Related: [[a-test-can-outlive-its-design]], [[valve-parity-is-the-first-principle]].
