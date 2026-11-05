---
name: mcp-tools-first
description: Reach for an installed MCP tool before scripting the same job by hand.
metadata:
  type: feedback
---

An MCP tool for the job is the first choice, always; a hand-written script is the fallback when it fails.

**Why:** owner, 2026-11-04: *"mcp tools should ALWAYS be your first round pick"*, after hand-rolled `-hijack` scripts with sleeps failed three times and hung TF2, and `mcp__tf2__*` got the frame first try.

**How to apply:** TF2 frames and console: `mcp__tf2__*`. Live engine values: `mcp__cheatengine__*`. Binaries: `mcp__ghidra__*`. C# and C++ symbols: `mcp__agent-lsp__*`, `mcp__clangd__*`. See [[cheat-engine-is-for-live-tf2]], [[driving-tf2-demo-playback]].
