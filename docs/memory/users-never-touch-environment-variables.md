---
name: users-never-touch-environment-variables
description: A user-facing setting is a cfg line plus UI, never an environment variable; env vars are for scripts, CI and debugging.
metadata:
  node_type: memory
  type: feedback
---

A setting a user would reasonably change lives in the viewer's own `settings.cfg` (TF2 config syntax) with a UI to
change it; setup docs never tell a user to set an environment variable. Owner, 2026-10-04: *"users should never have
to fuck with env variables, they should have a UI to change it and it should probably live in a cfg file."*

**How to apply:** when adding a launch option or env var, sort it as user-facing (cfg + UI) or dev/debug (`--help`
lists it under a "nothing a user needs" heading). The sort and the precedence for the TF2 folder
(`TF2_FOLDER` > `cl_game_folder` > Steam) are in `docs/DECISIONS.md` D210; the test seams `TF2VIEW_SETTINGS` and
`TF2VIEW_STEAM_ROOT` keep UI tests off the owner's real settings (B497).

**Docs trap:** README and RELEASE-NOTES said "set `TF2_FOLDER`" for weeks. Any user-facing doc that names an env var
is a bug unless it says "scripts only".
