---
name: predict-a-translucent-pixel-before-calling-it-wrong
description: Before calling a translucent HUD pixel wrong, predict it from the texture of the state drawn, the background behind it, and a linear blend.
metadata:
  type: feedback
---

Before calling a translucent pixel wrong, predict it from three things: the texture of the exact state on screen, the actual background, and a linear-space blend (pow 2.2).

**Why:** B709's control-point icons were filed as "too pale, matches two paints". The alpha came from `icon_obj_red` while the pixels were `icon_obj_red_locked`, and the backgrounds differed. The prediction matched TF2 exactly, and a live breakpoint showed one paint.

**How to apply:** Sample a body pixel away from glyphs in both captures. Do the arithmetic first; a matching number can still support the wrong theory, so name the material and state it came from. See [[split-a-composite-by-fitting-its-alpha]].
