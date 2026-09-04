---
name: a-census-of-requests-beats-a-list-of-features
description: A coverage list of what the SDK declares cannot flag an unreachable parameter; a census of what real data ASKS FOR can, and did, in the same run that made it reachable.
metadata:
  type: project
---

**Two instruments measure conformance here and only one can find a parameter that was invisible.**

- `SdkCoverageTests` generates the denominator from the SDK — 489 shader parameters, 66 lumps, 54
  studio structures — and can never go stale.
- The **parameter census** asks a different question: of everything a REAL MAP requests, is each one
  implemented or written down in `docs/RISKS.md`?

Measured 2026-09-04. Reading VMT DirectX-level sub-blocks (B326) made `$selfillummask` reachable for
the first time, and the census went red on it **in the same gate run**. The SDK list could never
have flagged it: `$selfillummask` was already in its denominator, already counted as declared-and-
unimplemented, indistinguishable from the hundreds nobody has needed. What made it a finding was a
map asking and nothing accounting for the request.

**So the rule when adding any reader: ask what the new reach makes VISIBLE, and expect the census to
speak.** A change that widens what you can see is exactly when a request-based instrument earns its
keep, and a red census in that run is the feature working rather than a regression.

**And keep the two instruments apart when reporting.** "489 parameters, N implemented" is a
denominator; "a real map asks for X and nothing accounts for it" is a defect report. Only the second
has a subject.

Related: [[measure-the-output-not-the-capability]], [[the-denominator-decides-what-can-be-lost]],
[[a-parameter-can-be-gated-by-a-sub-block]].
