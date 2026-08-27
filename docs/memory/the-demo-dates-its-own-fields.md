---
name: the-demo-dates-its-own-fields
description: Whether an old build sent a property is answerable from that demo's own schema, not from the SDK or a decompiler.
metadata:
  type: project
---

**"Did the 2009 engine send this field?" is answered by the 2009 demo, not by the 2013 SDK and not
by a decompiler.** Every demo embeds the SendTables that describe it, so each file carries the
schema of the build that wrote it — including property widths and flags.

Raised on 2026-08-20 as possibly needing a decompiler, because the viewmodel-slot defect appeared
only on older demos and `source-sdk-2013` is one era's snapshot. It needed nothing: asserting
`DT_BaseViewModel.m_nViewModelIndex` present at 1 bit unsigned against each corpus demo's own schema
covered 2007 through modern in a 150 ms test.

**Why to apply:** the SDK proves what one build did. A demo proves what the build that recorded it
did, which is the actual question whenever a defect is era-shaped. Reaching past that for
[[nothing-is-closed]] or a decompiler is work for an answer already on disk.

**How to apply:** when a property's existence, width or flags is in doubt for an era, write a
conformance test that reads `Corpus.Schema(path)` for every demo and asserts on the `SendProperty`.
It is schema-only, cached and cheap — no entity decode — so it does not offend the rule that corpus
tests are slow ([[real-data-hides-bugs-small-inputs-expose]] still governs behaviour tests, which
stay synthetic). Related: [[hl2sdk-branches-are-per-era-headers]] for headers the demo cannot carry,
[[era-axis-is-measured]] for which builds exist.
