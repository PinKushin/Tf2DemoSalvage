---
name: clangd-on-the-sdk
description: The clangd MCP indexes source-sdk-2013 fully, but only after a file is opened; query a method if a class name misses
metadata:
  type: reference
---

The `mcp__clangd__*` tools (agent-lsp over `C:\Program Files\LLVM\bin\clangd.exe`) cover `F:/src/source-sdk-2013/src`:
`compile_commands.json` there has 2,604 entries — all 276 `game/client/tf/*.cpp` included — and the background index
(`src/.cache/clangd/index`, ~4,900 shards) is already built.

**Workspace search returns nothing until a file is opened.** clangd discovers its compilation database on the first
`didOpen`, and only then loads the index. So: `start_lsp(root_dir=F:/src/source-sdk-2013/src)`, then any
`list_symbols`/`get_symbol_source` on an SDK file, then `find_symbol`. Measured 2026-09-25: before opening, every query
returned 0; after, `ComputeWide` and `CTFHudPlayerHealth` resolved.

**A class name can miss while its methods resolve** — `CHudBaseDeathNotice` returned 0 while
`RetireExpiredDeathNotices` found both the TF and HL2MP versions. Query a distinctive method name when a class misses.

**Closed code is not there, and an empty answer for it is correct**: `CScheme`/`CSchemeManager` (vgui2.dll),
vguimatsurface, engine and materialsystem are Ghidra work — projects under `D:\ghidra-proj` (`tf2vgui2`, `tf2enginex64`,
`tf2materialsystem`), launched by the `ghidra-mcp-*.bat` files there.
