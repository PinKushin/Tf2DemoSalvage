---
name: modern-tf2-is-not-a-stock-reference
description: The owner's modern TF2 install has a custom HUD and custom binds/settings (viewmodels and tracers off) — not a stock reference; HUD goes back in tf/custom when a TF2 task ends (D254)
metadata:
  type: project
---

Owner: *"my in game tf2 hud cant be used for parity, the era games can be, becuase they dont have a
custom hud, but to use the modern one for parity, you need to remove my hud from my tf folder and my
custom binds/settings since i get rid of viewmodels and tracers basically everywhere."*

**Why:** a golden shot from the modern install shows HIS HUD and settings, not TF2's defaults —
comparing against it would "fix" the viewer toward his customisation.

**How to apply:**
- HUD parity references: era clients (stock HUD), or the modern install only after his `tf/custom`
  HUD and config are moved aside (ask first, restore after).
- Any golden shot from the modern install: remember viewmodels/tracers are off there by his config.
- Lighting/geometry comparisons (world, props, sky) are unaffected.

**2026-11-10 (D254, reverses D222's "stays out"):** his HUD lives in `tf/custom/Garm3n-VIP-Quad` (a junction to his
repo). Move it to `F:\tf2-hud-backup\` only for a task needing the stock HUD; move it back when that task ends, with
the TF2 quit. Owner: *"you havent reset my tf2 install with my hud and stuff so im over it already"* — a lost play
session. **Before ending any TF2 task: HUD back in `tf/custom`, capture ConVars back.** His autoexec and class configs still
run: set `crosshair`, `viewmodel_fov`, `tf_use_min_viewmodels`, `r_drawviewmodel`, `mat_hdr_level` to defaults for a
capture and put them back; `cl_savescreenshotstosteam` is his setting at 0 — set it to 0 and `host_writeconfig`,
never "restore" it to 1. TF2 also needs Steam running or it exits right after boot.

Related: [[custom-folder-and-choosable-huds]] — loading his HUD from `custom/` is intended (D91); the
parity REFERENCE is the stock HUD.
