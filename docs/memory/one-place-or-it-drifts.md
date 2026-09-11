---
name: one-place-or-it-drifts
description: A fix belongs in exactly one place; anything copied or kept in step between files goes out of sync.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:37:54.835Z
---

Every fix should land in a **single place**. If a change needs the same information copied into two
files, or two sites kept in step by hand, they will go out of sync.

**Why:** stated by the owner directly — "pretty much every fix should be in a single place, if we
run into a place we are having to copy or synchronize the information between files, they are going
to get out of sync." Said after watching exactly that failure: players all faced north because
`m_angRotation` was being read for them in `RecordProp`, while the comment naming
`m_angEyeAngles` as TF2's real facing property sat in a different method of the same file. Two
places, one of them right, and the wrong one was the one that ran.

**How to apply:** put the fix at the point the data is produced, not at each point it is consumed.
The eye-angle fix is one line in `RecordProp` because the pose it writes already feeds the
interpolator, `ScenePlayer` and the renderer — so position and angle cannot drift apart. Setting a
`Yaw` field on `ScenePlayer` instead would have been the same behaviour with two sources of truth,
and would have needed a second edit every time the angle logic changed.

The corollary for design: when a feature will be reached from two paths (a POV camera and a free
camera, say), build one thing that both call with a flag, not two implementations that agree today.

Related: [[logs-are-the-debugger]] is how the drift gets *found*, and
[[fixtures-are-the-weak-point]] is why a second implementation cannot check the first.

---

## `police-the-document-not-just-the-test` — the audit's last instruction is the one skipped

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
that will not get updated. Point the check at the prose. A stale "not implemented" list is a to-do
list somebody will work from — that is the disease this treats, and
[[the-denominator-decides-what-can-be-lost]] carries the same shape in the coverage report.

### The loop closed on 2026-09-04, and both halves fired on the same run

`$basetexturetransform` (B332) is the first gap where the audit's whole instruction was carried out.
The gate reddened on `GapMarkers_WhoseFeatureNowWorks_AreReported` naming
`TextureTransforms_AreNotParsed`, and the marker, its row and the `CONFORMANCE.md` section all went
in that change.

**Both checks covered the same feature from opposite ends.** The audit names the TEST to delete; the
document police would have caught the SECTION had it been left. Before the second check existed,
four firings in a row deleted the test and the row and left the prose — which is the failure the
police was written for, and it is now redundant in the good way rather than the unused way.

**And the pinned marker count came down 2 → 1 WITH the deletion**, not to make a run pass. That
distinction is the entire reason the number is pinned, and it is worth stating in the comment beside
it every time it moves.
