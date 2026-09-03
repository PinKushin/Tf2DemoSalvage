---
name: a-defect-that-survives-its-cause-is-in-the-instrument
description: "If disabling the suspected cause leaves the measurement unchanged AND the number is total, suspect the instrument before the code."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-03T19:18:02.230Z
---

**A control that removes the cause and does not change the reading is evidence about the
INSTRUMENT.** Hunting upside-down players (B298), a census reported *every* skeleton on the map
collapsed — and still reported it with every animation layer disabled. That total, surviving its
own cause, was the tell. The census was reading `ModelInstance.Bones`, which is the SKINNING
palette: `Concatenate(boneToWorld, poseToBone)`, whose translation column is a mixture of placement
and bind offset and is not a bone's position.

**Why:** a real defect has a cause; remove it and the number moves. A number that will not move, or
that indicts 100% of a population, is usually measuring something that was never the variable.
B222 had already recorded this exact mistake on the viewmodel size check, in a doc comment in the
same file, and it was not read first.

**How to apply:**

- **Pick the variable the symptom is about.** "Upside down" is not size — an inverted skeleton has
  the same bone spread as an upright one. Spread found nothing; head-above-foot found seven of
  fifteen.
- **Print a control the instrument cannot fake**, next to the measurement, every run. Here: the
  BIND-pose rise beside the posed rise. It immediately caught a second error — bind space is Y-up
  and world space is Z-up, so the first version read the wrong axis and reported a bind rise of 4
  on a model whose bind rise is 71.
- **A denominator of ALL is a warning, not a finding.**

See [[instrument-bugs-outnumber-decoder-bugs]], [[an-empty-search-needs-a-control]],
[[measure-the-output-not-the-capability]].
