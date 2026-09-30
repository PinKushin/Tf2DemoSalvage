---
name: a-corpus-selection-outlives-its-corpus
description: lcor grows, so a demo picked by fragment, prefix or a closed list silently changes subject; name demos in full, select by the property meant, and know that a per-demo assertion loop hides every bad demo after the first.
metadata:
  type: project
---

`Corpus.Demo(fragment)` returns the first ordinal match, and lcor keeps growing. The first full superset
(2026-09-30) found seven failures that were selections outliving the corpus they were written over:

- `"cp_process"` meant the f12 demo until `20150119_2240_cp_process_final_(ovo)_blu` arrived (digits sort
  first) — WearableTrackTests changed subject silently on 2026-08-18.
- prefix `tf2-` meant era specimens until `tf2-2026-pub-pov-*` joined lcor.
- closed lists (`[11, 14, 15, 16, 24]`) and constants (fog's 15 properties) written over gcor.

And the masking half: a test that asserts demo by demo stops at the first bad one, so `esea_match_2184869`
hid behind `auto-20101109…` (B440); `FilesWithSchema` drops an undecodable demo from every sweep silently.

**Why:** "Passed" over gcor said nothing about lcor; the superset had not run in weeks.

**How to apply:**
- Name a specific demo in full; select a population by the property the claim is about (header, schema).
- A list of known values cites where it was measured, and TIMELINE/RISKS updates it when the corpus grows.
- A corpus sweep that finds a bad demo should say which demos it never reached.
- After lcor grows, expect claims last run over gcor to break on the next superset; these waited weeks.
- Related: [[a-corpus-sweep-holds-one-demo-at-a-time]], [[a-test-can-outlive-its-design]], [[era-axis-is-measured]].
