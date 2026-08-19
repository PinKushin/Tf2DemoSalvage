---
name: put-the-real-file-in-the-fixture
description: Three bugs in one session survived their own tests because the fixture was authored from the same belief as the code
metadata:
  type: project
---

**When a real example of a format is available, put the real bytes in the fixture.** A fixture the
same person authored from the same belief as the reader cannot falsify that belief, however many
cases are written on top of it. Three bugs in one session (2026-08-18) survived their tests exactly
this way:

| Bug | The fixture that confirmed it |
|---|---|
| `dcubemapsample_t` read at 13 bytes instead of 16 | builder emitted 13-byte records |
| `Patch` VMTs were a total no-op — `replace` block keys dropped | test put the keys at the patch's **top level**, a shape real VMTs never use |
| `SdkCoverageTests` missing the standard-var axis | denominator scraped only the two axes already known |

Each had *more* tests than average. The patch one had a test named
`Patch_TakesTheIncludedTextureAndTheOverriddenKeys` that passed while no patch anywhere applied.

**Why:** the failure is not a careless fixture, it is a faithful one — a correct expression of the
wrong model. Adding cases multiplies the same hypothesis; it never tests it.

**How to apply:**

- **Real bytes where a real file exists.** cp_process_final's pakfile, the corpus demos, an SDK
  header. `VmtPatchBlockTests` embeds a patch VMT byte for byte.
- **Where synthetic data is unavoidable, assert a property of REAL data that the wrong reading
  cannot satisfy** — not a count, which is as plausible either way. For cubemaps that was the
  ±16384 world bound; a stride error lands outside it and a correct one cannot.
- **A fix whose failure mode is "works on everything I tried" needs its negative case written
  down.** Flattening `replace` had to keep NOT flattening `Proxies`, and a `replace` nested inside
  `Proxies` is still a proxy's.

Related: [[fixtures-are-the-weak-point]], [[struct-padding-is-on-disk]],
[[output-level-assertion-or-it-is-not-done]], [[differential-beats-fixtures]]. Story:
`docs/findings/27-cubemap-placement.md`.
