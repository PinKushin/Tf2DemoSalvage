---
name: police-the-document-not-just-the-test
description: A gap audit that fires correctly still rots the prose it points at — the last step of its own instruction is the one that gets skipped, four times here.
metadata:
  type: project
---

**An audit that names three things to do gets two of them done.** Measured 2026-09-04.

`ConformanceGapAuditTests` exists to stop `docs/CONFORMANCE.md` claiming a feature is missing after
it lands. It works: it went red the session `$normalmapalphaenvmapmask` was implemented, and its
message says what to do — *"delete the test, its row here, **and its section in
docs/CONFORMANCE.md**"*.

The test went. The row went. **The section stayed. Four times** — `$phong`,
`$normalmapalphaenvmapmask`, `$lightwarptexture` and `$rimlight`, all still filed under *"Not
implemented, ordered by what it costs"* while all four are in the shader. A reader planning work off
that list would have built one of them twice, which the file's own header records having already
happened once.

**The fix is to police the artefact rather than the reminder.** A heading under that section naming a
parameter that `MaterialCensus.ImplementedParameters` contains is now a red test: two documents
contradicting each other, one of them enforced for its own reasons. A heading may say IMPLEMENTED in
as many words, which keeps the history without keeping the lie.

**Two details that made it work rather than nag:**

- **Headings only, never prose.** A section may discuss an implemented parameter — the
  `$normalmapalphaenvmapmask` entry explains the mask it is mutually exclusive with — and that is
  not a claim. What a heading says IS the claim.
- **A structural control.** The section is located by its `## Not implemented` heading and the test
  asserts it found more than two `###` entries, so a renamed heading fails loudly instead of
  checking an empty list.

**It immediately caught one nobody had noticed:** `$phong`'s section carried "Implemented
2026-08-21, B128" in its BODY and "every model is dull" in its HEADING. The body was right and
nobody reads it.

**Generalises past this file.** Wherever a check tells a human to update prose, the prose is the part
that will not get updated. Point the check at the prose.

Related: [[a-stale-not-implemented-is-a-todo-list]] — the disease.
[[a-hand-maintained-numerator-drifts-downward]] — the same week, the same shape, a different
document.
