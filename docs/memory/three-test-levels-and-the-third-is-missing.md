---
name: three-test-levels-and-the-third-is-missing
description: Conformance and real-data tests both pass on a feature nothing calls; only a test that drives the real UI can fail when the wiring is absent.
metadata:
  type: project
---

**A feature can have eleven conformance tests and three real-data tests and still do nothing.** That
was B145: spectator target cycling was declared, bound to `MOUSE1`/`MOUSE2`, given the Source command
names, and covered by three tests — with no production code reading it. Clicking cycled nothing.

**The tests were not wrong.** They asserted that a binding table held what it should, and it did.
*Nothing about a binding table can tell you whether anything consults it*, and a unit test of the
search cannot either, because the search is fine — it is never called.

Three levels are needed, and it is always the third that is skipped:

1. **Conformance**, from the source with citations, written before the code. Says what the engine
   does.
2. **Real data** — a corpus demo, real bytes. Says the rules meet reality. Both of these pass on a
   feature nothing invokes.
3. **The wiring**: drive the real thing. Click the real button in the real window and ask whether
   the code ran. **This is the only level that can fail when the wiring is removed.**

**Verify level 3 by removing the wiring and watching it, and only it, go red.** Done here by
deleting the `CycleTarget` call from the action switch: one UI test failed, thirteen passed. Without
that check, a level-3 test can be as decorative as the others.

**Choosing the level-2 specimen is where this goes quietly wrong.** A POV demo would have passed the
cycling tests while measuring nothing — the committed era POVs are solo recordings, so a cycle finds
one target and stops, which is indistinguishable from a broken search. See
[[pov-demos-are-pvs-limited]]. The claim about *which* player got cycled to belongs to z1800, a
nine-versus-nine match; the UI test only claims the code ran, because the demo it opens cannot show
more.

Related: [[output-level-assertion-or-it-is-not-done]], which is this seen from one feature;
[[measure-the-output-not-the-capability]].
