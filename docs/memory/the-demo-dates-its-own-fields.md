---
name: the-demo-dates-its-own-fields
description: "Whether an old build sent a property is answerable from that demo's own schema, not from the SDK or a decompiler."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:54:50.586Z
---

**"Did the 2009 engine send this field?" is answered by the 2009 demo, not the SDK (one era's
snapshot) and not a decompiler.** Every demo embeds the SendTables that describe it. A viewmodel-slot
defect appearing only on older demos was raised as possibly needing a decompiler — it needed nothing:
asserting the property's presence/width against each corpus demo's OWN schema covered 2007 through
modern in a 150ms test.

**Why:** the SDK proves what one build did; a demo proves what the build that recorded it did, which
is the actual question whenever a defect is era-shaped.

**How to apply:** when a property's existence/width/flags is in doubt for an era, write a conformance
test reading the schema for every demo and asserting on it. It's schema-only, cached, cheap — no
entity decode. Related: [[hl2sdk-branches-are-per-era-headers]], [[era-axis-is-measured]].
