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

- **TF2 cannot rewind.** `demo_gototick` to an earlier tick reloads the demo and replays up to it (the owner: *"tf2 cannot actually rewind if you seek earlier it replays up to that point"*). Seek forward only, in order - check tick 13944 before 14252.
- **A paused demo stops answering RCON**, even a fresh login. `demo_gototick <t> 0 1` (pause) wedged the server until the owner typed `demo_resume`. Use `demo_gototick <t> 0 0` with a low `demo_timescale` instead. The owner adds that a paused demo also blocks free-cam movement for a human, while camera mode changes still work.
- **`jpeg` writes only on a rendered frame.** It worked at the main menu and wrote nothing during the demo; a minimized window is the suspect.
- The demo must be under `tf/` (`playdemo f12` for `tf/f12.dem`).

**How to apply:** use the MCP tools, never manual RCON scripts - the owner built it for token savings (*"mcp means token saving"*).
