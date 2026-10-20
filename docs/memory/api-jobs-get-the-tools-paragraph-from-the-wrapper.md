---
name: api-jobs-get-the-tools-paragraph-from-the-wrapper
description: Queue (API) jobs only know what their prompt says; the wrapper prepends F:\credits-queue\prompts\_tools.md so they use the LSP MCP tools instead of blocked symbol greps. Local agents already see the hooks.
metadata:
  type: feedback
---

Owner, 2026-10-20, seeing a queue reviewer log "Hook blocked grep for symbols. Reading files directly instead": *"the hook blocks ... and the subagents are not getting told to use the mcp"*, then *"at least not the api ones"*.

**Why:** a hook blocks text search (grep/rg) for C# and C++ SYMBOLS and points to `agent-lsp` (C#) and `clangd` (the Valve SDK). A local subagent sees that hook message and the tool list; an API job (`claude -p` through the credits queue) starts with only the prompt file, so it fell back to Read of whole files (slower, more tokens). MCP tools DO work in queue jobs (a clangd job ran `find_symbol` fine), they just were never mentioned.

**How to apply:** the standing paragraph lives in ONE file, `F:\credits-queue\prompts\_tools.md` (C#: `start_lsp` once with `root_dir` and `language_id` `csharp`, then `find_symbol`/`find_references`/`get_symbol_source`/`list_symbols`; SDK: clangd `start_lsp` on `F:/src/source-sdk-2013` `cpp`, `open_document` first; Grep stays for strings, prose, cfg/res/txt, addresses; report a failed LSP call instead of silently falling back). `~/.local/bin/claude-credits.ps1` prepends it to every job, so a new prompt needs nothing extra; edit that file to change the guidance. A Ghidra job still needs `"ghidra": true` in its JSON. Related: [[credits-queue]], [[use-bash-not-powershell-tool]].
