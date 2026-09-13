---
name: spend-fewer-tokens
description: The owner runs ponytail ultra for token savings and expects drift back to verbosity; the big costs are essay remarks and wide reads.
metadata:
  type: feedback
---

The owner, 2026-09-13, switching from caveman to ponytail ultra: *"i want token savings, so im running it hard, i expect
you will stat semi ignoring it over time the same way you did caveman, but hopefully not as bad"*.

**Why:** the style skills were drifting out within a session, and the real spend was never the chat prose — it was
multi-paragraph XML remarks on every member, findings sections restating the code, and reading whole disassembly logs
when a function's 60 lines would do.

**How to apply:**

- Code remarks: one line of engine citation plus only the surprise; the full derivation lives once, in
  `docs/findings/51` ([[one-place-or-it-drifts]]).
- **Code reads go through the LSP** (`agent-lsp`: `get_symbol_source`, `list_symbols`, `find_symbol`, `find_references`,
  `blast_radius` before an edit) — the owner, same day: *"reads can be done with the LSP which is what you were suppose
  to be using already"*. Whole-file `Read` of a C# file is the exception, not the habit.
- **The engine's C++ goes through `clangd`**: `start_lsp` with `root_dir` `F:\src\source-sdk-2013\src` (it has a
  `compile_commands.json`), then `get_symbol_source` — `PhysicsLevelInit` came back in 275 tokens against 8,055 for the
  file. No Java LSP is needed: vphysics has no source.
- **MCP over raw for everything else too** — owner: *"LSP/MCP reads and use over raw, it saves context and tokens"*.
  GitHub through the `github` MCP, not `gh` output. Raw only when no server covers it.
- **The binary through GhidraMCP's headless server**, not `analyzeHeadless` plus a log: `D:\ghidra-proj\ghidra-mcp-headless.bat`
  in a `pmux` session `ghidra-mcp`, then `curl "http://127.0.0.1:8089/disassemble_function?address=0x…"` — one routine,
  no per-line timestamps, no JVM start per question; `/mcp/schema` lists the 245 endpoints. The `ghidra` MCP bridge
  only discovers the GUI plugin's sockets, so it does not see this server; a constant's value needs its own read, which
  `DisasmWithData` printed inline.
- Disassembly logs, when a headless run is still needed: grep the address, then `offset`/`limit` to that routine only.
- Workflows and subagents only where they save main-context reads, not by reflex — even with ultracode on. A three-agent
  verification run cost 683,000 subagent tokens.
- The quiet-output hook appends `--verbosity minimal` to a `dotnet` command's LAST pipe segment when none is given, which
  breaks `| head`; pass `--verbosity minimal` explicitly.
- Never trade parity for brevity ([[valve-parity-is-the-first-principle]], `ponytail-works-inside-parity`).
