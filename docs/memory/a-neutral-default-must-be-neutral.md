---
name: a-neutral-default-must-be-neutral
description: WorldRenderer._white is the magenta chequer, and binding it as a neutral detail texture chequered every model.
metadata:
  type: project
---

**`WorldRenderer._white` is not white — it's the missing-material chequer** from `Missing()`. It
serves two roles wanting different values: fallback for a base texture that failed to upload
(correct, Source's own convention) and neutral default for detail/bump slots (wrong).

Model and decal draw paths bound it to slot 3 unconditionally while the other three paths looked up
`_details[material]`. The shader combines a detail whenever mode ≠ −1, so every model material
declaring `$detail` got a magenta chequer multiplied into its albedo — players came out purple and
grey while map and static props (drawn by the lookup paths) were correct.

**Why:** invisible to the whole suite, confined to characters, so it read as player-specific. Four
candidates were eliminated first (null chequer handle, material name, `--colours` debug view, VTF
decode) before the draw call was read.

**How to apply:** copy the detail/bump lookup when adding a draw path, never bind a default; treat
"missing, show a fault" and "nothing here, carry on" as two distinct values. The probe that cracked it
wrote a PNG rather than an average — magenta+grey averages to unremarkable brown, which is how the
first probe reported four healthy textures against a chequered screen. Related:
[[output-level-assertion-or-it-is-not-done]], [[measure-the-output-not-the-capability]],
[[one-place-or-it-drifts]].
