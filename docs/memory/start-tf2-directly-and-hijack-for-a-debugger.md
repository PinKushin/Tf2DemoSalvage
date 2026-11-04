---
name: start-tf2-directly-and-hijack-for-a-debugger
description: "To debug TF2's map load: start tf_win64.exe directly, attach, set breakpoints, then -hijack +map; the tf2 MCP launch hung three times."
metadata:
  node_type: memory
  type: feedback
---

`mcp__tf2__launch` hung for 30 minutes three times (2026-11-03). Measured working, first try, Steam already running:

1. Lock with a self-timed holder.
2. `tf_win64.exe -game tf -insecure -vulkan -novid -sw -w 1280 -h 720 -nosound +sv_lan 1`, working directory the game root.
3. Cheat Engine: `open_process`, `debugProcess(2)`, `debug_setBreakpoint`.
4. `tf_win64.exe -game tf -hijack +map <map>`: the running game takes it, so a load-time function is logged from its first call.
5. `-hijack +quit`, inside the lock.

**Why:** step 4 is what a `+map` on the first launch cannot do, as the load races the attach. No RCON is needed.
