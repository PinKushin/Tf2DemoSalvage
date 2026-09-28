---
name: the-denominator-decides-what-can-be-lost
description: "A coverage test can only find things missing from the set it enumerates; pick the denominator from what must be produced, never from the mechanism producing it."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T03:56:13.679Z
---

A coverage test is only as good as the set it walks. Walk the MECHANISM's input, and anything the
mechanism can't see is outside the question — the test passes while the output has a hole.

A world cull's coverage test asked "for each face named by a visible leaf, is it drawn?" —
displacements are named by NO leaf at all, so the ground could vanish entirely with the test green.
Even a control on the total (`checkedFaces > 0`) passed on 12,000 brush faces while 60 terrain faces
went uncounted. **A control on the total does not control for a missing category.**

**Why:** the correct denominator is what must be PRODUCED, not what the producer consulted.

**How to apply:** ask what set the implementation iterates, deliberately choose a DIFFERENT one. Add a
control per CATEGORY the output can contain, not just the total — and verify the control actually
fires.

**A denominator built by grepping source text misses what a macro generates** — sixteen weapons were
registered via a macro that never writes the literal text a regex searched for; two resolved to
nothing including a stock weapon, found by watching a gun fail to draw. Widening carelessly can invert
the denominator (macro argument order reversed from the generated call).

**"What a demo draws" and "what the game contains" are different denominators, and parity needs the
second** — a rule reported on zero of 44 bones in one demo (a fact about that demo) was closed by
counting all 14,109 shipped models: the rule is asked for by NO model in the game.

---

## `a-hand-maintained-numerator-drifts-downward` — and the obvious ratchet cannot work

**A generated DENOMINATOR can't go stale; a hand-written NUMERATOR (list of what's implemented)
drifts every time someone adds a reader without adding a name.** A coverage report understated
implemented lumps/structures by several each, because "adding a reader without adding its name here
does not fail anything" was true and sat unenforced in a comment.

**The obvious ratchet (every claimed name must appear in the codebase) fails its own positive
control** — some correctly-implemented readers are cited under a different spelling than the SDK
uses, so an assembly search accuses correct entries.

**So the check was written, measured, and DELETED, with the reason recorded next to the list.** A
test with false accusations is worse than no test. See [[one-place-or-it-drifts]].

---

## `a-census-of-requests-beats-a-list-of-features` — only it catches an unreachable parameter

Two conformance instruments: an SDK-generated denominator (never stale, but blind to WHICH declared
parameters are unimplementable-vs-just-unneeded), and a census of what a REAL MAP requests. A newly
reachable material parameter went red on the census in the SAME gate run it became reachable — the
SDK list could never flag it, since it was already counted as "declared, unimplemented" identically to
hundreds nobody needs.

**Rule when adding any reader: ask what the new reach makes VISIBLE** — a red census in that run is
the feature working, not a regression. Keep denominator and census reports separate when reporting;
only the census names an actual defect.

Related: [[measure-the-output-not-the-capability]],
[[material-variables-split-three-ways]]#a-parameter-can-be-gated-by-a-sub-block.

---

## `an-uncoverable-gap-is-usually-your-reader` — an exclusion describes your parser, not the format

**When a conformance suite records something "not coverable", re-read the reason before believing
it.** Two of three such notes were wrong: a struct was excluded as embedding "a class, not a struct" —
C++ makes them layout-identical, the obstacle was one keyword missing from a regex; a lump was
excluded for "a per-version layout" that turns out fully declared and append-only, so the fields
actually used were checkable all along. Only the third exclusion was genuinely uncoverable.

**Why it costs:** the exclusion sits in the file it excludes, reads as confident as everything else,
and nothing ever fails to trigger re-examination. Same shape as [[a-test-can-outlive-its-design]].

**Separate "the format doesn't permit this" from "my reader doesn't do this yet"** — the first is a
finding, the second a to-do wearing a finding's clothes.

Related: [[conformance-test-before-implementation]], [[instrument-bugs-outnumber-decoder-bugs]],
[[measure-the-output-not-the-capability]].
