---
name: custom-folder-and-choosable-huds
description: A `custom/` folder like modern TF2, several huds in it at once, and one chosen at runtime — a deliberate step BEYOND the game, not a parity gap.
metadata:
  type: project
---

Owner, 2026-08-25: *"our program is suppose to/going to be able to import a users config, or allow a
user to paste their config into our folder structure somewhere, likely a custom folder like modern
tf2... im going to go for being able to choose huds on demand, meaning more than one hud can be in
custom and you can choose which one to use, which is something tf2 doesnt do... the user will be
allowed to just import huds too."* Recorded D91.

**D193 (2026-09-26) sets the default: TF2's stock HUD.** A custom HUD is chosen (imported, from
`tf/custom`, or our own custom folder) — never picked up by being installed; once chosen it wins.
Imported/copied huds and configs go in OUR `custom/` folder; configs looked for in both `custom/` and
`cfg/`. HUD tests read `GameArchives.WithoutCustom()`, so every test runs against Valve's defaults.
**This is an AFTER-PARITY goal** — *"it might effect some earlier design decisions, so it needs to be
kept in mind"* — not work in flight; settings/asset code being built now must not make it impossible.

**The part needing guarding: choosing among several huds is a DEPARTURE and it's intended.** TF2
can't do it. D89 makes Valve parity the first principle, but governs reproducing the ENGINE's
behaviour — it doesn't forbid the viewer offering what the game doesn't, since this is a recording
viewer, not a client that must behave like one.

**How to apply, to settings work now:**
- Keep ignoring unknown commands (D69) — a real config has hundreds of unimplemented lines.
- Use Valve's own cvar names so a pasted config works unchanged.
- Never assume one config file — a `custom/` tree is several.
- Every player-changeable setting is settable here — "it makes changing defaults free" (owner).

Related: [[a-config-is-a-program]], [[valve-parity-is-the-first-principle]],
[[a-default-is-not-a-constant]], [[silence-about-a-missing-feature-is-not-a-preference]].
