---
name: clangd-on-the-sdk
description: The clangd MCP indexes source-sdk-2013 fully, but only after a file is opened; query a method if a class name misses
metadata:
  type: reference
---

**SDK `.cpp`/`.h` lookups go through clangd, not grep/sed** — owner: *".cpp files should be going
through clang not grep btw"*. Symbol source, callers, overrides and references come from clangd; grep
is for prose, string literals and ConVar names only.

`mcp__clangd__*` covers `F:/src/source-sdk-2013/src` (2,604 compile-db entries, all 276
`game/client/tf/*.cpp`; background index already built at `src/.cache/clangd/index`).

**Workspace search returns nothing until a file is opened** — clangd discovers its compilation
database on first `didOpen`. So: `start_lsp(root_dir=F:/src/source-sdk-2013/src)`, open a file, then
`find_symbol`.

**A class name can miss while its methods resolve** — `CHudBaseDeathNotice` returned 0 while
`RetireExpiredDeathNotices` found both TF and HL2MP versions. Query a distinctive method when a class
misses.

**References come back as a count, not as places** (2026-09-29, B105): `find_references` prints `ref_N` nodes
with no file or line, `callers` answers only from OPEN files whose AST builds (tf_weaponbase.cpp's does not:
"Unknown type name 'CEconItemView'"), and `rename_symbol` dry-run lists only the declaration and definition.
`find_symbol` with `detail_level: "hover"` DOES print each definition's file and 0-based line — so trace a
call path top-down from its entry point by definitions, and say which call sites stayed unlocated.

**Closed code isn't there, correctly empty**: `CScheme`/`CSchemeManager`, vguimatsurface, engine and
materialsystem are Ghidra work under `D:\ghidra-proj` (`tf2vgui2`, `tf2enginex64`,
`tf2materialsystem`).
