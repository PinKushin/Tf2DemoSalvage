---
name: measure-every-hop-before-blaming-one
description: A feature can be correct at every measured hop and still not work; measure the hops you have not, rather than re-reasoning about the ones you have.
metadata:
  type: feedback
---

**When a change does not take effect, enumerate the hops it travels and measure each one. The bug is
always in the hop nobody measured.**

**Why:** bodygroups on the capture point hologram. Three hops were measured and all correct — the
model offers 4 alternatives, the demo carries bodies 0/2/3, the packer produced "9 batches spanning
4 alternatives" — and the picture still showed one sign on every point. Three correct measurements
proved only that the fault was in the fourth hop, which was the one never looked at: the value
arriving at `DrawModel`.

The same shape recurred all session:

- The overlay stripes: four renderer theories, all killed by measurement, and the answer was in the
  BSP's face list the whole time.
- The player pose: decode, matrix maths and Euler conversion all verified against the SDK, and the
  fault was a substring lookup one layer above them.
- Doors: a submodel geometry reader was nearly built before counting showed the faces were already
  in the world buffer.

**One symptom can have several INDEPENDENT causes, and fixing one proves nothing about the
diagnosis.** "The viewmodel does not appear" had five: not loaded, not uploaded, wrong sequence,
wrong owner, wrong posing mechanism. Each was real, each was fixed correctly, and after each fix the
screen looked exactly the same as before — so every fix read as a failed hypothesis when it was not.
A pipeline with N stages can be broken at N of them at once, and it usually is when the whole
pipeline is new. **Verify a fix at its own stage** (was the model in the packed set? did the upload
happen?) rather than at the far end, or a run of correct work looks like a run of wrong guesses.

**How to apply:** write the chain down — file, decode, pack, instance, draw — and put a number on
each link before touching code. A hop that "obviously works" is exactly the one to instrument,
because the hops that obviously work are the ones nobody instrumented. And prefer measuring the
LAST hop first: it is closest to the symptom and cheapest to read.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[read-the-map-before-the-renderer]].
