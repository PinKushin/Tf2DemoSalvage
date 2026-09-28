---
name: test-naming-convention
description: "Tests here use {Subject}_{Scenario}_{Expected}; the old prose names are being converted, and the convention is written down in CLAUDE.md so it cannot drift again."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T02:37:35.954Z
---

Test methods use `{Subject}_{Scenario}_{Expected}`. Classes use `{TypeUnderTest}Tests`; a class named
`Conformance` must keep that (docs select suites by filtering the name).

- **Subject** — the method under test (`Decode`) or the operation for cross-layer tests (`RoundTrip`).
- **Scenario** — the condition (`AtProtocol23`).
- **Expected** — the predicted observation (`Is14Bits`).

**Why:** ~2,132 prose-named tests across 371 files drifted from no recorded decision — one early file
set the style, later files matched neighbours. Owner's reason for converting: prose names make
hand-debugging harder — a failing test naming the CLAIM but not the SUBJECT forces opening the file
to learn what it touches; the new form says where to look on every red run.

**How to apply:** write new tests in this form; convert a file's names when already editing it. Bulk
conversion is safe because nothing outside test assemblies references a method name by string, and
`build/gate.sh`'s floors are floors, not equalities — a rename that drops a test reddens the gate, but
adding tests always passes. Do not attempt bulk conversion with a regex — choosing subject/scenario/
expectation requires reading the assertion; a mechanical transform produces names nobody will fix.
See [[edit-files-with-the-file-tools]].
