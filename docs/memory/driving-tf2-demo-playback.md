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

**How to apply:** use the MCP tools, never manual RCON scripts — built for token savings.
