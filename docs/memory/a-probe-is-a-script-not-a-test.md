---
name: a-probe-is-a-script-not-a-test
description: Probes belong in tools/Tf2DemoSalvage.Probe, not in a test assembly; [Explicit] hides a probe from the run but not from the build, the discovery, or the floor.
metadata:
  type: feedback
---

**The owner, 2026-08-30:** *"btw, you can script a probe outside the test suite, having a bunch of
probe tests just slows the suite down and putting in a suite and running the whole damn thing takes
forever"*.

**Why:** two costs, and the second is the one that hides.

- **The suite pays for every probe it holds.** About sixty `*Probe` files sit across the test
  projects. `[Explicit]` keeps a probe out of the RUN and out of nothing else — it is still
  compiled, still discovered by the adapter on every `dotnet test`, and still counted in the
  `.trx` total that `build/assert-test-count.sh` gates on.
- **Asking a probe a question cost a test run.** `dotnet test --filter` builds an assembly
  referencing NUnit, the adapter and Shouldly, starts a VSTest host to execute one case, and buries
  the answer in `TestContext.Out`. Worse, the parameters were `const`, so the owner naming a second
  tick window meant editing a constant and paying it all again.

**How to apply:** a probe is a console program in `tools/Tf2DemoSalvage.Probe`, discovered by
reflection from `IProbe` — adding one is adding a file.

```bash
dotnet run --project tools/Tf2DemoSalvage.Probe
```

Its parameters are command-line arguments, not constants; that is most of the point. `DemoCorpus`
lives in the tool and `Tf2DemoSalvage.Corpus.Tests` consumes it, so the suite and the probes locate
demos through one implementation — see [[one-place-or-it-drifts]].

**This does not replace [[measure-the-output-not-the-capability]] or D38's rule that a measurement
is not a test.** D38 already said a harness worth keeping asserts nothing and is `[Explicit]`; what
it never said was where such a harness should live, so the answer defaulted to "in the suite" and
sixty accumulated there while each one followed the rule. Anything with a right answer — decode,
arithmetic, a rule read from the SDK — is still a synthetic test in `Core.Tests` or a layer's own
suite.

**Do not port sixty probes in one pass.** Several carry findings in their prose and several answer
questions that are now closed; those should be deleted with the finding promoted to
`docs/findings/`. A bulk move relocates prose without reading it, which is worse than leaving it.

D126.
