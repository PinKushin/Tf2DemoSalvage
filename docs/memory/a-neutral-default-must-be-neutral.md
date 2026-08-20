---
name: a-neutral-default-must-be-neutral
description: WorldRenderer._white is the magenta chequer, and binding it as a neutral detail texture chequered every model.
metadata:
  type: project
---

**`WorldRenderer._white` is not white — it is the missing-material chequer**, built by `Missing()`.
It serves two roles that want different values: the fallback for a **base** texture that did not
upload (right — Source's own convention, a missing texture should look like a fault), and the
neutral default for the **detail** and **bump** slots (wrong).

The model and decal draw paths bound it to slot 3 unconditionally while the other three paths looked
up `_details[material]`. The shader combines a detail whenever the material's mode is not −1, so
every model material declaring `$detail` had a magenta chequer multiplied into its albedo. Players
came out in purple and grey squares while the map and the static props in the same frame were
correct — because those are drawn by the paths that do the lookup.

**Why:** the fault was invisible to the whole suite and confined to characters, so it read as a
player-specific problem. Four candidates were eliminated first — the chequer being bound at all (0
materials had a null handle), the material name, the `--colours` debug view, and the VTF decode
(`medic_red` decodes perfectly) — before anyone read the draw call.

**How to apply:** when adding a draw path, copy the detail and bump lookup rather than binding a
default; and treat "this is missing, look at it" and "nothing here, carry on" as two values, never
one. The probe that cracked it wrote a PNG instead of reporting an average — a checkerboard of
magenta and grey averages to an unremarkable brown, which is how its first version reported four
healthy textures against a chequered screen. Related:
[[output-level-assertion-or-it-is-not-done]], [[measure-the-output-not-the-capability]],
[[one-place-or-it-drifts]].
