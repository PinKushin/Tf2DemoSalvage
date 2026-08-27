---
name: ask-which-input-differs-before-bisecting
description: "When a defect appears on one input and not another, find what differs about the INPUT before diffing code; a check that never existed cannot be found by comparing two versions."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-26T03:18:27.597Z
---

**Before bisecting code, ask which INPUT changed.** A defect reported on one file and not another is
a statement about the files first, and about the code second.

**Measured 2026-08-25, and it cost an evening.** Five visual defects were reported at once — door
grates piled in one place or missing, trigger volumes visible, a missing model, two animation
symptoms — right after a large refactor, and the owner's initial read was "a massive regression". It
was not. The demo I had picked for the check was a different one from the one they normally use.

**The cause was a map-version mismatch.** Competitive maps are recompiled repeatedly — `cp_process_f9`,
`f10`, `f11`, `f12` — and the viewer loads the map BY NAME from the local install. A demo drawn
against a different compile of the same name has every `*N` brush submodel index pointing into
somebody else's BSP: doors take another door's geometry, an entity lands on a trigger's submodel and
becomes visible, a model vanishes because that index is now something else. One cause, five symptoms.

**Six hypotheses died first, and every instrument said the code was innocent — correctly.** The world
build, brush-entity counts and faces-held-back were identical between the two versions; the packer
and the instancer were byte-identical; the moved pipeline matched the original step for step; the
decode project was untouched.

**That is the general lesson: a check that has NEVER EXISTED cannot be found by diffing two
versions**, because it is absent from both. Bisection can only locate something that changed. When
every diff comes back clean and the symptom is real, stop diffing — the fault is in something
missing, and the way in is the input that provokes it.

**How to apply, in order:**

1. **Ask what is different about the input** that fails, versus one that works. One question, asked
   at the start, would have replaced the whole hunt.
2. **Reproduce on the KNOWN-GOOD input** before believing a regression. Running the old build was
   what settled it, and it should have been the first move rather than the last.
3. **If the diffs are clean and the symptom is real, look for an absent guard** rather than a
   changed line — especially one the engine has and we do not.

**The specific gap, worth knowing on its own:** `svc_ServerInfo` carries a map CRC. This project
decodes it, keeps it on `ServerInfoMessage.MapCrc`, writes it back and prints it in the trace — and
compares it to nothing. The engine refuses a mismatched map; we draw one silently. Filed as B200.

Related: [[logs-are-the-debugger]], [[instrument-bugs-outnumber-decoder-bugs]],
[[read-the-spec-before-measuring-our-data]], [[pov-demos-are-pvs-limited]],
[[fallbacks-do-not-make-guesses-safe]].
