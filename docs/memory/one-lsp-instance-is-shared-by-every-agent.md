---
name: one-lsp-instance-is-shared-by-every-agent
description: agent-lsp and clangd are single shared instances; start_lsp from one agent repoints it for all, so symbol answers in a worktree can be about another tree - read known files directly
metadata:
  type: feedback
---

Owner, 2026-10-20, after a subagent logged "The language server is pointed at another worktree. Reading the known region directly.": *"we can have lsp's step on each other because theres only one"*.

**Why:** the hook forces C#/C++ symbol questions onto `agent-lsp` / `clangd`, but there is ONE server of each for the whole machine. `start_lsp(root_dir=...)` from any agent repoints it, so a concurrent agent in a different worktree gets symbols (and empty `find_references`) from the wrong tree, silently. Seen: `find_symbol` timing out, `find_references` returning empty on a symbol with ten callers.

**Owner, same day: he would still rather LSPs were used, just carefully.** Scope is uncertain: several `agent-lsp` wrapper processes exist (one set per session), so the sharing is at least between the lead and its subagents; machine-wide sharing is NOT verified. Fix in use: `add_workspace_folder` for each worktree an agent is working in (and `remove_workspace_folder` after), never `start_lsp` on a second root. Cost: a symbol/reference query then returns a hit per tree (about double the tokens) and each root loads its own project model (csharp-ls was 395-620 MB for one root). Separate per-agent instances would avoid both but are MCP config entries - the owner's call. **Every agent brief that may use LSP says: add your worktree with add_workspace_folder, read only results whose path is inside it, remove it when done.**

**How to apply:** an agent in a worktree treats an LSP answer as unverified until the `file_path` it returns is inside its own worktree; an empty reference list is not an absence (control: ask for a symbol that must exist). Reading a known file at a known line (Read) is always fine and is the fallback. Do not call `start_lsp` with another tree's root while another agent may be using it; for the Valve SDK, `clangd` rooted at `F:/src/source-sdk-2013` is shared, so leave it there. Related: [[clangd-on-the-sdk]], [[instrument-bugs-outnumber-decoder-bugs]].
