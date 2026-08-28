---
name: run-the-control-before-arguing
description: When a symptom appears right after a change, build the pre-change tree and run it — one launch settles authorship that hours of correct reasoning cannot.
metadata:
  type: feedback
---

**When a defect surfaces right after a change, run the PRE-CHANGE build on the same input before
reasoning about whether the change caused it.** `git worktree add <tmp> <commit>` and build — the
tree does not have to be clean, and nothing in the working copy is disturbed.

Measured 2026-08-28. A viewmodel dropped out during a session that had just landed two-pass drawing.
An evening went into arguing authorship from evidence:

- the commit touched no bone, animation, merge or pose file
- both candidate failure modes make a model invisible under any pass or material
- the model's only material was opaque, so the change was provably a no-op for it

**All true, all correct, and none of it was evidence about the symptom.** The owner eventually said
*"lets run the control"*. One launch: the dropout still happened on the pre-change build. Question
closed.

**Why:** an argument that a change *cannot* have caused something is reasoning about a mechanism you
have already assumed. The control tests the claim itself, needs no mechanism, and cannot be wrong
about authorship. It is also cheap — the build was already sitting in a worktree from earlier in the
same session and went unused for hours while better arguments were made.

**How to apply:** the moment "is this mine?" is asked out loud, build the control. Do it before
instrumenting, before reading the SDK, and certainly before explaining why it cannot be yours. If the
symptom is visual, the control needs the owner's eyes for one playthrough — cheaper than any of the
alternatives.

**The corollary, and it cost more than the control did:** the owner then observed *"the arms are not
being drawn either during the dropout, its not just the weapon"* — which meant every measurement of
the weapon had been aimed at the wrong subject, and the clean results were clean because that model
was fine. **Ask what ELSE is missing before instrumenting the thing that was reported.** A symptom is
reported as the part that was noticed, not as its full extent.

Related: [[ask-which-input-differs-before-bisecting]], [[the-f12-demo-is-the-parity-reference]],
[[log-the-event-not-a-sample-of-it]], [[suspect-the-input-not-the-algorithm]].
