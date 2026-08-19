---
name: test-naming-convention
description: Tests here use {Subject}_{Scenario}_{Expected}; the old prose names are being converted, and the convention is written down in CLAUDE.md so it cannot drift again.
metadata:
  type: feedback
---

Test methods use **`{Subject}_{Scenario}_{Expected}`**. Classes use `{TypeUnderTest}Tests`, and any
class whose name contains `Conformance` must keep it — `docs/CONFORMANCE.md` selects those suites
with `--filter 'FullyQualifiedName~Conformance'`.

- **Subject** — the method under test when there is one (`Decode`, `Write`, `Parse`); otherwise the
  operation, for tests that span layers (`RoundTrip`, `Trace`, `Dump`).
- **Scenario** — the condition (`AtProtocol23`, `AfterAStopWithoutFlags`, `WithNoStopCommand`).
- **Expected** — the predicted observation (`Is14Bits`, `InheritsSndStop`, `ReproducesBytes`).

**Why:** the repo had grown ~2,132 prose-named tests (`ASoundAfterAStopInheritsSndStopUnlessItSays
Otherwise`) across 371 files, and no decision was ever recorded for it — checked 2026-08-19 against
`docs/DECISIONS.md`, `CLAUDE.md` and every memory entry. It drifted: one early file used prose, each
later file matched its neighbours because matching surrounding style is the default, and nobody
compared the result against the written standard.

The owner's reason for converting is the deciding one and it is not aesthetic: **prose names make
hand-debugging harder.** A failure reading `Failed TheTraceNamesEveryKindItWalksPast` names the
CLAIM but not the SUBJECT, so the reader has to open the file to learn what it even touches.
`Trace_EveryMessageKind_IsNamed` says where to look. That cost is paid on every red run.

**How to apply:** write every new test in this form. Convert an existing file's names when you are
already editing it. Two things make bulk conversion safe: nothing outside the test assemblies
references a test method name (no `--filter` pins one, no Stryker config filters by test), and the
COUNT must not change — `build/gate.sh`'s floors are exact, so a rename that drops or merges a test
fails the gate immediately.

Do not attempt this with a regex. Choosing the subject, scenario and expectation requires reading
what the test asserts; a mechanical transform produces names that are wrong in a way nobody will
ever go back and fix. See [[edit-files-with-the-file-tools]].
