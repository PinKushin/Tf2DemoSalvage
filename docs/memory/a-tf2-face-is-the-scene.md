---
name: a-tf2-face-is-the-scene
description: "TF2 players network no flex weight, blink or view target; faces move only through scene EXPRESSION events and .vfe settings (B513)."
metadata:
  type: project
---

**A TF2 player's face is driven by the scene it is in, never by the wire.** `DT_TFPlayer` excludes
`m_flexWeight`, `m_blinktoggle` and `m_viewtarget` (`tf_player.cpp:782-784`), so the networked weights are zero
(rescaled to each controller's MIN) and no blink ever starts. Even a blink would do nothing: TF2's DME eyelid rules
stack the blink controller and never read it (`studio.cpp:1584`).

**Why it matters:** "port blink first as the cheap driver" was the plan handed over, and it is dead code. Check the
send table's exclusions before porting any client effect fed by a networked prop.

**How to apply:** faces = `EXPRESSION` events (9,222 in scenes.image) → `.vfe` setting → `AddFlexSetting` blend →
`RunFlexRules` → vertex deltas (`FaceFlex`). `FLEXANIMATION` tracks mostly name HL2 controllers and depend on a
per-frame decay; lip sync needs `.wav` phonemes. Both open under B513. Findings 75.
