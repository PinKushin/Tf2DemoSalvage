---
name: engine-accepts-authored-demos
description: "The 2007 TF2 client plays demos this project generated, which is a stronger result than the byte-identical round trip."
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-09-09T03:54:15.157Z
---

**The byte-identical round trip was a GATE, passed before anything else began.** Owner: *"the core
parser got to 100% demo decode before i even started anythign else, I required a real demo to be
parsed to our quake code then recompiled byte identical into a new demo file."* Decode side is
complete; draw side is the backlog — a decoded value with no renderer is expected state, not a no-op
([[output-level-assertion-or-it-is-not-done]]).

**A round trip proves fidelity, not understanding** — copying bytes back achieves that too. The real
test: does the engine accept a file this project INVENTED? Confirmed 2026-08-11 in the March 2007
client (build 3258, protocol 11), against cut-down demos: 1/20/70/300 frames all played correctly,
nothing crashed, behaviour tracked length.

Two format facts: **`dem_synctick` is tick zero** — before it, ticks are the SERVER's (2083-2153),
after, the packet stream restarts at 0; taking the largest tick reports the connect phase as the
demo's length (turned a 1-frame cut into 32 seconds). SourceTV files carry no `dem_synctick` at all.
**Length is stated three ways, all read**: `playbackframes` (packets), `playbackticks` (last tick),
`playbacktime` (ticks × interval).

**Size is dominated by the signon, not length** — a 1-frame cut of a 460KB demo is still 160KB
(schema + string tables). A short demo isn't a cheap demo.

**Not a product feature** — owner: this isn't a TAS tool, cutting an existing demo is "a little
cheaty" as a test (code deleted same day). Keep probes in a scratchpad; keep only the finding here.
Related: [[measure-the-output-not-the-capability]], [[fixtures-are-the-weak-point]].
