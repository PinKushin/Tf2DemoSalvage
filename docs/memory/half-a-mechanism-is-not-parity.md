---
name: half-a-mechanism-is-not-parity
description: "Porting one half of a behaviour Valve split across two systems breaks the invariant the other half relies on, silently."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-29T02:24:14.945Z
---

When Valve splits a behaviour across two systems, port BOTH or neither. Implementing one and
open-coding the other's symptom is not parity, however good the citation on the half that landed.

The worked example (B222, D116): a dead spectated player. `C_HLTVCamera::CalcInEyeCamView`
(`hltvcamera.cpp:307`) switches the CAMERA to third person. This project instead emptied the
viewmodel's hands and left the first-person camera in the dead player's skull — a state the engine
cannot produce. It took the viewmodel off screen for the whole of every death and was reported for
days as "the viewmodel is missing".

**The tell was a silence in the SDK read as an omission.** `C_BaseViewModel::ShouldDraw`
(`c_baseviewmodel.cpp:277`) asks only "is the camera in-eye" and "is this the target's viewmodel" —
no liveness term. That is not an oversight: the camera guarantees in-eye is never held on a dead
target, so the draw test can afford not to ask. **One system's invariant is another system's
unstated precondition**, and a check that looks missing is often a check something upstream already
made impossible to need.

The owner's framing, which is what identified it: *"i dont think you can force tf2 to spectate a
dead player in 1st person like we can force this viewer to do by fucking up and not having
everything implemented"*. Ask whether the state you are guarding against is reachable in the engine
at all. If it is not, the guard is the bug — you have broken an invariant and are patching its
symptom.

**Why:** he had already said death was not the cause and it was kept anyway, because it carried a
citation. A citation on half a mechanism proves the half, not the port.

**How to apply:** before adding a guard that Valve does not have, find which system maintains the
condition that makes Valve's version safe, and implement that instead. State any deliberate
narrowing so it can be falsified — here liveness was applied to the spectated path only, since
`C_HLTVCamera` never runs on a POV demo.

Related: [[a-divergence-is-asked-not-documented]], [[valve-parity-is-the-first-principle]],
[[read-the-sdk-for-the-whole-mechanism]], [[name-the-trade-before-fixing-valve]].
