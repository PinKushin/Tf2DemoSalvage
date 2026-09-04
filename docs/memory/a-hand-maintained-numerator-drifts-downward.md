---
name: a-hand-maintained-numerator-drifts-downward
description: A coverage report with a generated denominator and a hand-listed numerator understates, silently and flatteringly-in-reverse; and the obvious source-text check to stop it cannot work here.
metadata:
  type: project
---

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

## The obvious fix cannot work, and this is the part worth keeping

The natural ratchet is: every name claimed must appear somewhere in `managed/`. That catches the
dangerous direction — a typo or an aspiration reading as coverage for ever, invisible by hand.

**Its positive control failed on the first run.** `LUMP_FACES` appears nowhere in this project; the
lumps are `BspLumpIndex.Faces`. Neither does `mstudioanimvalue_t`, though `ExtractAnimValue` is
fully implemented. The SDK's spelling reaches this codebase only where somebody happened to cite it
— most places, not all — so the check accuses correct entries.

An assembly search is wrong the other way: these names live in comments, so
[[an-empty-search-needs-a-control]]'s usual tool, `SchemaGap.AnyProductionAssemblyMentions`, reports
every one of them absent.

**So the check was written, measured, and deleted, with the reason recorded next to the list.** A
test with false accusations is worse than no test; the note stops it being built a second time.

Related: [[a-stale-not-implemented-is-a-todo-list]] — same disease, opposite document.
