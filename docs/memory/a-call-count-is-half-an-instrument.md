---
name: a call count is half an instrument
description: Counting how often a hot function runs does not say it is cheap; time the calls where they run before closing a profile lead
type: feedback
---

**A census of calls is half an instrument: time the calls too, inside the callee, before calling a lead closed.**

B594 (2026-10-24) counted `LevelLighting.SunFrom` at "at most 2 a frame" and filed the profile's 5.8% as likely
sampler bias. Timed inside the call on 2026-10-26 (`LevelLighting.SunWork`, `SunMeter`), the same 1.5 calls a frame cost
0.46 ms, 4.9% of the frame: each was a 57,016-unit sky trace whose terrain half swept most of the map's displacements.
Bounding it by the brush trace's fraction took it to 0.012 ms and the frame down 0.6 ms
(`docs/measurements/2026-10-26-threaded-particles-and-sun.md`).

**How to apply:** when a profile share and a call count disagree, the next instrument is a stopwatch carried from inside
the callee (B243), differenced over the WHOLE frame across every caller — not a second count over a narrower window.
Low frequency with a long argument (a ray, a range, a box) is exactly the shape that hides a per-call cost.
