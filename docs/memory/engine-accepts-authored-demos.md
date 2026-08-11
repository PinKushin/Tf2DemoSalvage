---
name: engine-accepts-authored-demos
description: "The 2007 TF2 client plays demos this project generated, which is a stronger result than the byte-identical round trip."
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-11T11:22:45.536Z
---

**A round trip proves fidelity, not understanding.** Writing back the bytes you were handed can be
achieved by copying them. The real test is whether a file this project *invented* is one the engine
accepts — and it is.

Confirmed 2026-08-11 in the March 2007 client (build 3258, protocol 11), against demos cut from a
recording it made itself:

| frames | length | result |
|---|---|---|
| 1 | 0.000 s | renders a still, does nothing — correct for one frame |
| 20 | 0.900 s | never leaves the startup pause every demo has |
| 70 | 3.4 s | plays normally |
| 300 | 14.9 s | plays normally |

Nothing crashed, and **the behaviour tracks the length** — which is what separates a correct file
from one the engine merely tolerates.

Two format facts that came out of it:

- **`dem_synctick` is tick zero.** Everything before it carries the SERVER's tick (2083–2153 in
  that recording) while the packet stream restarts at 0. Taking the largest tick in a file
  therefore reports the connect phase as the demo's length, which turned a one-frame cut into a
  32-second one. SourceTV files carry no `dem_synctick` at all, so a fallback has to count packets
  rather than scan the whole file.
- **Length is stated three ways and all three are read**: `playbackframes` counts packets,
  `playbackticks` is the last tick, `playbacktime` is ticks × the interval `svc_ServerInfo`
  carries. The header states the interval itself, as `playbacktime / playbackticks`.

**Size is dominated by the signon, not the length.** A one-frame cut of a 460 KB demo is still
160 KB, essentially all schema and string tables. A short demo is not a cheap demo — worth knowing
before assuming a corpus of tiny specimens would be cheap.

**Why this is worth keeping:** it is the strongest available evidence that the container and the
message framing are understood, and it costs one edit plus one `playdemo`. Cheaper than any
differential. See [[differential-beats-fixtures]] for the other direction, which tests the
decoder's reading rather than the writer's output.

**Not a product feature.** The owner was explicit: this project is not a TAS tool, and cutting an
existing demo up is "a little cheaty" as a test. Truncation code was written and deleted the same
day. Keep the probes in a scratchpad; keep only the finding here. Related:
[[measure-the-output-not-the-capability]].
