---
name: driving-tf2-demo-playback
description: "How real TF2 behaves when driven through the tf2 MCP server (F:\\source\\repos\\Tf2Mcp) - seeking back replays from the start, a paused demo stops answering RCON, jpeg needs a rendered frame."
metadata:
  node_type: memory
  type: reference
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-09-23T09:07:00.631Z
---

Measured 2026-09-23 driving `f12.dem` through the `tf2` MCP server:

- **TF2 cannot rewind** — `demo_gototick` to an earlier tick reloads and replays up to it. Seek
  forward only, in order.
- **A paused demo stops answering RCON**, even fresh login — `demo_gototick <t> 0 1` wedged the
  server until `demo_resume`. Use `demo_gototick <t> 0 0` with a low `demo_timescale` instead. A
  paused demo also blocks free-cam movement for a human; camera mode changes still work.
- **`jpeg` writes only on a rendered frame** — worked at the main menu, wrote nothing during the demo
  (minimized window suspected).
- Demo must be under `tf/` (`playdemo f12` for `tf/f12.dem`).

Measured 2026-10-09 for the golden comparison (B161, findings 76):

- **`demoui` prints the tick** (`Tick: n / total`, the viewer's numbering). Leave it open in the capture;
  `demo_gototick` lands within a few ticks, so follow TF2's tick rather than forcing one.
- **Slow with `host_timescale`, not `demo_timescale`** (a seek resets the latter) — and it slows RCON
  too: 0.002 is ~15 s a call, 0.0001 times out.
- **`spec_goto` is a SERVER command** — nothing in a demo. An exact camera is a listen server:
  `map <name>`, `jointeam spectator`, `spec_mode 7`, `spec_goto x y z pitch yaw`; `spec_pos` reads it.
  Roaming is mode 7 (6 is POI and follows the player).
- **Exposure**: read `mat_hdr_tonemapscale`, pin with `mat_force_tonemap_scale` (cheat), bloom off with
  `mat_disable_bloom`. Read every cvar before changing it and read all back after restoring.
- Never play a 2009 demo in live TF2 — it wedges the client.

Measured 2026-10-11 (B378 pause-glow):

- **Capturing while PAUSED works in one chained line**: RCON dies on pause, so send
  `demo_pause; wait 20; spec_next; wait 15; devshots_screenshot; wait 300; devshots_screenshot; demo_resume`
  and read the TGAs from `tf/screenshots/` yourself.
- **`wait` is a no-op when the demo's server set `sv_allow_wait_command 0`** (competitive configs do): every
  command then runs in one frame and screenshots collapse into one. Set it to 1 (it sticks during playback),
  restore after.
- **Enable `wait` in an EARLIER call and read it back**: in the same line it is not yet in effect. Without it
  `demo_pause; ...; demo_resume` lands in one frame and leaves the demo PAUSED, RCON dead; only ending the
  process recovers (2026-10-12, an instance this session launched).
- `wait N` counts FRAMES: an unfocused TF2 runs ~37 fps, so 900 is ~24 s, not 3.
- `spec_player <dead player>` is refused silently; the HUD target id is not proof of the camera's target.
  Check the reference's subject (class, weapon, health) before blaming our camera.
- `tf/f12.dem` is NOT lcor's f12 (34.5 MB, another match); give the viewer TF2's own file, via a space-free copy.
- Never grab the screen from outside the game (PIL `ImageGrab`): it captures the owner's whole desktop.
- Tf2Mcp `3a695ba`: a game that exited is forgotten, so `launch` works again without restarting the server.

**How to apply:** use the MCP tools, never manual RCON scripts — built for token savings.
