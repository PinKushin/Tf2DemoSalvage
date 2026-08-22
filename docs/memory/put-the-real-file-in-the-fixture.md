---
name: put-the-real-file-in-the-fixture
description: A fixture must come from a real specimen, the SDK or a decompilation — never from our own code's belief; synthetic is fine and often required, sourcing it from ourselves is not
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

## The title overstates it. The axis is not synthetic-vs-real — it is WHICH REFERENCE

**Owner's correction, 2026-08-21**, when this entry was read as "prefer real files":

> *"what im telling you to do is use the actual read demos to make the synthetic tests, you dont
> assume our code when doing that … plus the sythetic fixtures can actually test things we should
> never see in a real demo, so we can catch edge cases we wouldnt otherwise be able to with real
> demos. So tension, sort of, but when you use the correct reference for the fixture, either a real
> demo or the sdk, or the decompiler, and not our code, then you avoid it"*

**The defect in every row of the table above is not that the fixture was authored. It is that it was
authored from THIS PROJECT'S BELIEF.** A 13-byte cubemap record and a 13-byte reader agree because
one person wrote both; a fixture built by reading the real file, or `bspfile.h`, would have been 16
and the reader would have gone red on its first run.

So the question to ask is never "is this fixture synthetic". It is **"where did its bytes come
from?"** — and there are exactly four acceptable answers: a real specimen, the SDK, a decompilation,
or arithmetic on one of those. Our own code is not on the list.

**Synthetic fixtures are required here, for two reasons neither of which is convenience:**

- **Mutation runs cannot use real demos.** Stryker re-runs the suite per mutant, and a suite that
  opens 774 MB of demos slows to a crawl. Synthetic inputs are what make mutation testing possible
  at all — and mutation testing is a standing requirement (`CLAUDE.md`).
- **They reach cases no real demo contains.** A malformed length, a field at its maximum, a
  combination the engine never writes. Those are exactly the inputs a decoder gets wrong, and no
  corpus will ever supply them. [[author-the-specimen-the-corpus-lacks]] is the same point from the
  writer's side.

**And diagnosis still wants the real file.** When something is actually broken, open a real demo —
that is where a wrong belief gets falsified, and it is the step that did not happen in any of the
three cases above.

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
