---
name: a-sabotage-must-compile
description: "A build failure is not a red test; a sabotage the analyzers reject proves nothing about a test's sensitivity, and a fixture must set the field production actually reads."
metadata:
  node_type: memory
  type: feedback
---

Verification by sabotage is the house rule: break the code on purpose, watch the RIGHT test fail,
restore with a precise inverse edit. Two ways it silently fails to verify anything, both measured
2026-09-03.

**A sabotage that does not compile is not a red test.** Deleting an expression stranded two
parameters, and SonarAnalyzer promoted "unused parameter" to a build error, so no assembly was
produced and no test ran. The result reads as failure and is not evidence: nothing was measured. The
delegated agent reported it honestly as inconclusive rather than counting it. Rewrite the sabotage to
keep every symbol used — invert the value, clamp it to a constant, swap an index — so the code still
builds and only the BEHAVIOUR changes.

**A fixture must set the field production reads, not the one that looks canonical.** Writing
`STUDIO_AUTOPLAY` into a hand-built `.mdl` body was the faithful-looking choice and set a field
nothing on that path reads: the model's sequence flags reach the draw path through hand-built
`StudioSequence` records, not through the bytes. The test stayed red with the implementation
correct — which is indistinguishable from a wrong fix, and is the failure mode that sends you back
to rewrite working code.

**Why:** both turn "no evidence" into something that looks like evidence. A red test is a claim about
behaviour; a build failure and a mis-fed fixture are claims about the toolchain and the harness, and
neither says whether the test can detect the thing it names.

**How to apply:** before believing a sabotage, confirm the run produced a PASS/FAIL summary and that
the failing test names are the ones predicted — a compile error in the output means start over. When
a test stays red after a fix you believe in, check the fixture reaches the field under test before
suspecting the code. See [[instrument-bugs-outnumber-decoder-bugs]] and
[[output-level-assertion-or-it-is-not-done]].
