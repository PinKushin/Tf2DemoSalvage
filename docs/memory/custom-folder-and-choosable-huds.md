---
name: custom-folder-and-choosable-huds
description: A `custom/` folder like modern TF2, several huds in it at once, and one chosen at runtime — a deliberate step BEYOND the game, not a parity gap.
metadata:
  type: project
---

The owner, 2026-08-25, while the field of view was being made config-settable:

> "our program is suppose to/going to be able to import a users config, or allow a user to paste
> their config into our folder structure somewhere, likely a custom folder like modern tf2, along
> with a hud or huds, im going to go for being able to choose huds on demand, meaning more than one
> hud can be in custom and you can choose which one to use, which is something tf2 doesnt do, the
> user will be allowed to just import huds too, so they dont have to find our folders to put it in
> the right place."

Recorded as **D91**. **It is an AFTER-PARITY goal** — *"its a after parity goal, but it might effect
some earlier design decisions, so it needs to be kept in mind"* — so it is not work in flight. It is
written down because the settings and asset code being built now must not make it impossible.

**The part that needs guarding: choosing among several huds is a DEPARTURE and it is intended.** TF2
cannot do it — a hud is installed by being the one present in `custom/`. D89 makes Valve parity the
first principle, so without this note the next person to audit for divergence finds a real one and
"fixes" it. D89 governs reproducing the ENGINE's behaviour; it does not forbid the viewer offering
what the game does not, and this is a tool for looking at recordings rather than a client that must
behave like one.

The other two commitments are parity and cost nothing: the `custom/` layout matches modern TF2, so a
config or hud that works in the game works here unchanged; and an importer exists so nobody has to
find the folder.

**How to apply, to settings work happening now rather than when the feature lands:**

- Keep **ignoring unknown commands** (D69). A real config is hundreds of `mat_*`/`cl_*`/`alias`/
  `exec` lines this viewer does not implement, and a parser that objected would reject every real
  file.
- Keep using **Valve's own cvar names** — `fov_desired`, `viewmodel_fov`, `demo_fov_override` — so a
  pasted config works without translation.
- **Never assume one config file.** A `custom/` tree is several, and a hud carries its own.
- **Every setting a player can change in the game is settable here.** The reason is the owner's: "it
  makes changing them and changing defaults free" — a compiled-in value has to be argued about, a
  config value gets tried.

Related: [[a-config-is-a-program]], [[valve-parity-is-the-first-principle]],
[[a-default-is-not-a-constant]], [[silence-about-a-missing-feature-is-not-a-preference]].
