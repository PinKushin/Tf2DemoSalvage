---
name: log-the-engine-when-reading-stops
description: How to log a light-cache build from the running game with Cheat Engine, and why it beat reading; ghidra's engine.dll is stale, rebase by signature
---

B669: reading found no cause; a log found it in one load. Set was right, values wrong: B122's cosine.

**Why:** a read says what a function does; only a log says which values reached it. Every subset fit failed since the question was wrong.

**How (owner approves each use; `-insecure`, local map, lock with its own timeout, read only):**
- Start `C:/Program Files/Cheat Engine/cheatengine-x86_64.exe` so the MCP bridge loads, `open_process`, `debugProcess(2)`: Windows debugger, no injected DLL.
- `debug_setBreakpoint(addr,1,bptExecute,bpmDebugRegister,fn)`. **`fn` must call `debug_continueFromBreakpoint(co_run)` then return 1**, or the game stops at the first hit.
- Addresses: `getAddress("engine.dll")` + RVA found by AOBScan of the prologue. Ghidra's copy is older, +0x1940 at the functions checked (B684).
- Filter at `FUN_1801b8270` entry on RDX's floats, arm the inner breakpoints only for that state. 1366 states build in about 40 s this way.
- XMM: `debug_getContext(true)`, then `readFloatLocal(debug_getXMMPointer(n))`.
- `r_visualizelighttraces 2` with a mass rebuild crashes the game. `debug_detach` over the bridge reports false: clear breakpoints, `quit` the game, close CE.
- A hung `mcp__tf2__launch` with `+map` outlived the lock. Launch with no map, then `map` over rcon.
