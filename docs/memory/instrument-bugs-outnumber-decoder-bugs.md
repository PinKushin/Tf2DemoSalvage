---
name: instrument-bugs-outnumber-decoder-bugs
description: On this project the tests have been wrong far more often than the readers they test
metadata:
  type: project
---

**Across the map-rendering work the decoders were right almost every time and the measurements
were wrong repeatedly.** Recorded because the instinct on a bad number is to suspect the code
under test, and here that instinct has been wrong more often than not.

The cases, all from 2026-08-12/13:

- **A control that could not fail.** "The flat lightmap set must read byte-identically" is blind
  to the set count, because the sets term cancels when the style is zero. Forcing every face to
  four sets passed it. Length arithmetic — each face's span must reach the next face's offset —
  was what could fail.
- **A picture test that passed on the headline defect.** It survived a shader reading lightmap
  set 0 three times instead of sets 1, 2 and 3, because the twelve ssbump materials still changed
  under that sabotage. Threshold recalibrated between two measured values, 9,502 and 24,096.
- **A winding assumption.** A point-in-polygon test assumed a fixed winding, reporting 0% decal
  coverage with a bimodal 56/162 split that read exactly like a placement defect. `BspSurface.Normal`
  is corrected for the face's side and the vertex order is not corrected with it.
- **An overstated verification.** "Placement verified" covered origin-on-plane and normal
  alignment, neither of which constrains the quad's *extent*.
- **A distinguishing case absent from a fixture.** A `.mdl` byte-versus-vertex divisor sabotage
  passed because props-only fixtures all had `vertexindex == 0`.

**How to apply:** when a measurement comes out wrong, check what the measurement is actually
sensitive to before touching the reader. Ask whether an input exists where correct and broken
differ, and whether this input is one. Prefer checks that cannot be satisfied by accident —
lengths that must tile exactly, vectors that must be unit length, bases that must be orthonormal.
Those have caught real defects here; "the number looks plausible" has not.

Related: [[fixtures-are-the-weak-point]], [[differential-beats-fixtures]],
[[measure-the-output-not-the-capability]], [[a-test-can-outlive-its-design]].
