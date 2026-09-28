---
name: ask-which-input-differs-before-bisecting
description: "When a defect appears on one input and not another, find what differs about the INPUT before diffing code; a check that never existed cannot be found by comparing two versions."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-26T03:18:27.597Z
---

**Before bisecting code, ask which INPUT changed.** Five visual defects reported at once right after a
large refactor read as "a massive regression" (2026-08-25) — it wasn't. The demo picked for the check
differed from the one normally used.

**Cause: a map-version mismatch.** Competitive maps recompile repeatedly (`cp_process_f9`...`f12`);
the viewer loads by NAME from the local install, so a demo against a different compile has every `*N`
brush submodel index pointing into someone else's BSP — doors take another door's geometry, entities
land on trigger submodels and become visible, models vanish. One cause, five symptoms.

**Six hypotheses died first, correctly clearing the code:** world build, brush-entity counts, faces
held back, packer, instancer, pipeline all identical/untouched between versions.

**General lesson: a check that never existed cannot be found by diffing two versions**, since it's
absent from both. When every diff is clean and the symptom is real, stop diffing — look for an absent
guard, not a changed line.

**How to apply, in order:**
1. Ask what's different about the failing input vs. a working one.
2. Reproduce on the KNOWN-GOOD input before believing a regression.
3. If diffs are clean and the symptom is real, look for an absent guard the engine has and we don't.

**Specific gap found:** `svc_ServerInfo` carries a map CRC, decoded, kept, printed — and compared to
nothing. The engine refuses a mismatched map; we draw one silently (B200).

Related: [[logs-are-the-debugger]], [[instrument-bugs-outnumber-decoder-bugs]],
[[nothing-is-closed]], [[author-the-specimen-the-corpus-lacks]], [[fallbacks-do-not-make-guesses-safe]].
