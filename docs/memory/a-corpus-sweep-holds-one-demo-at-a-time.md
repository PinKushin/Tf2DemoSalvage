---
name: a-corpus-sweep-holds-one-demo-at-a-time
description: A timeline is 40-84x its demo (lcor's largest 4.7 GB); TimelineCache keeps two, so every corpus test gets timelines from it, sweeps ask in WarmFirst order, and nothing heavy outlives one demo (B439).
metadata:
  type: project
---

A built `DemoTimeline` holds 40–84x its demo: z1800 635 MB, lcor's snakewater 4,692 MB, the local corpus
~80 GB in all (`docs/verification/README.md`). `TimelineCache` keeps two built timelines beyond those tests
hold (`LruCache`, SdkReference).

**Why:** 2026-09-30, the unbounded cache took the superset's corpus host to 39 GB on a 32 GB machine (B438).
The bound was first to be sized on z1800 — 7x too small; size on lcor's largest, never a gcor specimen. A
bound alone multiplies builds: a dozen sweeps start minutes apart, and in `FilesWithSchema` order each late
one rebuilds what the first ones passed. After the cache, two more holders surfaced: the round-trip test's
all-demos `StringBuilder` (16 → 23 GB) and eight `DemoTimeline.Build` calls outside the cache.

**How to apply:**
- Timelines only through `TimelineCache.For(path)`; never a private `DemoTimeline.Build` in Corpus.Tests.
- Many demos: `foreach (string path in TimelineCache.WarmFirst(paths))`, `For(path)` inside.
- Keep a demo's numbers, never its timeline, text or bytes, past its iteration; no `Split` of a demo's text.
- `TIMELINE built <demo> in <s> s` lines in the trx are the builds; a demo named twice was built twice.
- Related: [[a-cached-timeline-samples-for-everyone]], [[lcor-is-not-in-a-worktree]].
