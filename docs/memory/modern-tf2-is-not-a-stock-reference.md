---
name: modern-tf2-is-not-a-stock-reference
description: The owner's modern TF2 install has a custom HUD and custom binds/settings (viewmodels and tracers off) — not a stock reference; era clients are
metadata:
  type: project
---

The owner, 2026-09-25: *"my in game tf2 hud cant be used for parity, the era games can be, becuase they dont have a custom
hud, but to use the modern one for parity, you need to remove my hud from my tf folder and my custom binds/settings since
i get rid of viewmodels and tracers basically everywhere"*.

**Why:** a golden shot from the modern install shows HIS HUD and HIS settings, not TF2's defaults. A parity comparison
against it would "fix" the viewer toward his customisation.

**How to apply:**
- HUD parity references: the era clients (stock HUD), or the modern install only after his `tf/custom` HUD and his
  config are moved aside — ask him before moving his files, and restore them after.
- Any golden shot from the modern install: remember viewmodels and tracers are off there by his config; an absent
  tracer or viewmodel in that capture is his setting, not TF2's behaviour.
- Lighting/geometry comparisons (world, props, sky) are unaffected.
- Related: [[custom-folder-and-choosable-huds]] — the VIEWER loading his HUD from `custom/` is intended (D91); the
  REFERENCE for parity is the stock HUD.
