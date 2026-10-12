---
name: a-directorless-stv-chase-does-not-turn
description: "spec_autodirector 0 chase in a SourceTV demo looks along the demo's recorded view angles, which are zero: world +X, level. TF2 references taken after a hand-typed spec_mode are this camera (B535)."
metadata:
  type: project
---

**A TF2 reference of a SourceTV demo taken after `spec_mode` was typed is NOT the camera `--spectate --third-person` draws.**

- `spec_mode` by hand calls `SetAutoDirector( false )` (SpectatorGUI.cpp:869). `spec_player` alone does not.
- Director off = `bManual` (hltvcamera.cpp:109, :174-178): `engine->GetViewAngles`, no offsets, no dead-target pitch.
- In demo playback those view angles are the demo's recorded ones. A SourceTV recording's are ZERO.
  Camera: pitch 0, yaw 0, behind the target along world −X, whatever he does. *Interpolated* (closed engine), confirmed
  on three references at eye yaws −23, +4, −1.
- Reproduce with `+spec_autodirector 0` (config cvar, default 1). Viewer default unchanged: target's eye yaw, level.

**Wrong turn:** `SetPrimaryTarget` sets view angles to the target's eye angles at the choice (:583-590). Porting THAT held
his eye pitch (8 down); TF2's is level. The demo packet overwrites it. Read the picture's horizon before the code path.

**Before comparing any pixel of a golden pair, check all four:** tick (HUD tick minus interp when the reference plays),
camera mode (director on or off), camera distance history (`m_flLastDistance`, B528/B533), and the pins
(`mat_force_tonemap_scale 1`, `mat_disable_bloom 1`, `hud_saytext_time 0` — `golden-viewer-shot.sh` sets them).
Each of the four was once read as a rendering fault: beam direction, hidden glow, dark wall, chat panel.

`player-track <demo> <entity> <from> <to>` prints recorded origin, eye angles, health and the director's shot per tick
from one timeline build. `f12`'s director column is empty: that demo has no `hltv_chase` at all.
