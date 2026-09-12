---
name: the-denominator-decides-what-can-be-lost
description: "A coverage test can only find things missing from the set it enumerates; pick the denominator from what must be produced, never from the mechanism producing it."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T03:56:13.679Z
---

A test that asks "was everything covered?" is only as good as the set it walks. Walk the
**mechanism's** input and anything the mechanism cannot see is outside the question — the test passes
while the output has a hole in it.

Measured 2026-08-28. A world cull drew surfaces from the BSP leaves it could see, and the coverage
test asked: for each face **named by a visible leaf**, is it drawn? Displacements are named by no
leaf at all — `vbsp` builds a leaf's face list from its portals and detail faces, and a displacement
is neither — so the ground could vanish entirely with the test green. The owner found it by looking
at the screen.

The test even had a control, `checkedFaces > 0`, and it passed on twelve thousand brush faces while
sixty terrain faces went uncounted. **A control on the total does not control for a missing
category.**

**Why:** the correct denominator is what must be PRODUCED, not what the producer consulted. Here that
is every surface the uncalled renderer draws; each one dropped must then be justified — outside the
frustum, or excluded by a filter that can be checked independently. Stated that way the test cannot
be satisfied vacuously.

**How to apply:** when writing a coverage or completeness test, ask what set the implementation
iterates and deliberately choose a different one. Then add a control for each CATEGORY the output can
contain, not just for the total — and verify the control fires: the first corrected version of this
test still passed with the camera indoors, where every displacement was legitimately off screen, so
"at least one orphan was drawn" had to become its own assertion. See
[[instrument-bugs-outnumber-decoder-bugs]].

**A denominator built by grepping SOURCE TEXT misses whatever a macro generates.** Measured
2026-09-03. A conformance test enumerated every TF2 weapon by regex over `LINK_ENTITY_TO_CLASS(...)`
and asserted each resolved to its script name. Sixteen weapons never write that text — they are
registered by `CREATE_SIMPLE_WEAPON_TABLE`, which expands to it — so the pair exists in the built
game and nowhere in the source. Two of the sixteen resolved to nothing, one of them the stock
engineer shotgun, and the owner found it by watching a gun fail to draw.

Widening it needed care in the same direction: the macro's argument order is REVERSED from the call
it generates, and it spells the class without its leading `C`. A scan widened carelessly is a
silently inverted denominator rather than a bigger one.

Then the question asked properly instead of once: enumerate every macro whose body contains the
declaration, rather than reading the one file the failures happened to live in.

**"What a demo draws" and "what the game contains" are two denominators, and a parity question needs
the second.** Measured 2026-09-04 on the procedural bone rules. One tick of one demo covers 44
models and reported `AXISINTERP`, `AIMATBONE` and `AIMATATTACH` on no bone at all — which is a fact
about that demo, not about TF2, and would have left "some weapon somewhere might use one" open for
ever. Counting every `.mdl` in `tf2_misc_dir.vpk` — 14,109 models — closed it: those three rules are
asked for by NO model in the game, so implementing them would be writing code for content that does
not exist.

Both denominators are needed and they answer different questions: a rule can be common in the
content and absent from every demo, or the reverse. Keep both probes rather than replacing one with
the other, and say which you ran.

---

## `a-hand-maintained-numerator-drifts-downward` — and the obvious ratchet cannot work

**`SdkCoverageTests` generates its denominator from the SDK and cannot go stale. Its NUMERATOR is a
hand-written list, and that half drifts every time somebody adds a reader without adding a name.**

Measured 2026-09-04. The published report said **29 of 66** BSP lumps and **10 of 54** studio
structures. The real figures were **31 and 18**:

- `LUMP_CUBEMAPS` — `BspCubemaps` reads lump 42.
- `LUMP_VISIBILITY` — `BspVisibility` reads lump 4.
- `mstudioquatinterpbone_t`, `mstudioquatinterpinfo_t`, `mstudiojigglebone_t`,
  `mstudioattachment_t`, `mstudioevent_t`, `mstudioikchain_t`, `mstudiovertex_t`,
  `mstudioautolayer_t` — each with a dedicated reader.

The list's own comment had predicted exactly this: *"Adding a reader without adding its name here
does not fail anything — it makes the generated report understate coverage."* A prediction sitting
in a comment is not a check.

**How to find it: grep the project for every name the report calls UNHANDLED, then open what turns
up.** A name in a comment is not a reader — `LUMP_BRUSHES` appears here only in a note saying
Source's collision is brush-based and this tree is not — so each has to be looked at rather than
counted.

### The obvious fix cannot work, and this is the part worth keeping

The natural ratchet is: every name claimed must appear somewhere in `managed/`. That catches the
dangerous direction — a typo or an aspiration reading as coverage for ever, invisible by hand.

**Its positive control failed on the first run.** `LUMP_FACES` appears nowhere in this project; the
lumps are `BspLumpIndex.Faces`. Neither does `mstudioanimvalue_t`, though `ExtractAnimValue` is
fully implemented. The SDK's spelling reaches this codebase only where somebody happened to cite it
— most places, not all — so the check accuses correct entries.

An assembly search is wrong the other way: these names live in comments, so
[[instrument-bugs-outnumber-decoder-bugs]]'s usual tool,
`SchemaGap.AnyProductionAssemblyMentions`, reports every one of them absent.

**So the check was written, measured, and deleted, with the reason recorded next to the list.** A
test with false accusations is worse than no test; the note stops it being built a second time.
See [[one-place-or-it-drifts]] for the same disease in the opposite document.

---

## `a-census-of-requests-beats-a-list-of-features` — only it catches an unreachable parameter

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

Related: [[measure-the-output-not-the-capability]],
[[material-variables-split-three-ways#a-parameter-can-be-gated-by-a-sub-block]].

---

## `an-uncoverable-gap-is-usually-your-reader` — an exclusion describes your parser, not the format

**When a conformance suite records something as "not coverable", re-read the reason before believing
it.** Three such notes were written during the SDK-derived work on 2026-08-16 and two were wrong:

- `ddispinfo_t` was excluded because it "embeds `CDispNeighbor`, a class rather than a struct". C++
  makes `class` and `struct` identical for layout — they differ only in default access. The obstacle
  was one keyword missing from a regex. Adding it derived the whole chain, ending at 176.
- The static prop lump was excluded for having "a per-version layout". It has four versions, all
  declared, and they only append — so the property the reader actually relies on (origin, angles and
  prop type at fixed offsets in every version) was checkable all along, and is a *better* test than
  a stride.
- Only the VTX topology fields were genuinely uncoverable: added under a define the published SDK
  does not carry.

**Why it costs:** the exclusion is written in the same confident tone as the rest of the file, sits
in the file it excludes, and reads as a fact about Valve's format rather than about the tool. Nothing
ever fails to make it re-examined. It is the same shape as
[[a-test-can-outlive-its-design]] — a correct statement that stops being one silently.

**The general move: separate "the format does not permit this" from "my reader does not do this
yet".** The first is a finding; the second is a to-do wearing a finding's clothes.

**Related failures of the same kind, same session:**

- A constant extractor rejected lowercase, silently dropping
  `TCOMBINE_RGB_EQUALS_BASE_x_DETAILx2`. A constant missing from the reference makes whatever asked
  for it look *checked*.
- A control asserted that defining `REPLAY_ENABLED` changed a struct's SIZE. It does not — the extra
  byte lands in padding the struct already had. The member list was the sensitive measurement.
- Caching a file crawl to speed up a suite: measured 553 ms before, 532–648 ms after. Noise. The real
  cost was elsewhere entirely (a missing `[assembly: Parallelizable]`, worth 3x).

Related: [[conformance-test-before-implementation]], [[instrument-bugs-outnumber-decoder-bugs]],
[[measure-the-output-not-the-capability]].
