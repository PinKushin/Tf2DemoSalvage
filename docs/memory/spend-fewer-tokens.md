---
name: spend-fewer-tokens
description: The owner runs ponytail ultra for token savings and expects drift back to verbosity; the big costs are essay remarks and wide reads.
metadata:
  type: feedback
---

Owner, switching to ponytail ultra: *"i want token savings, so im running it hard, i expect you will
stat semi ignoring it over time the same way you did caveman, but hopefully not as bad."*

**Why:** the real spend was never chat prose — it was multi-paragraph remarks on every member,
findings sections restating code, and reading whole disassembly logs when 60 lines would do.

**How to apply:**
- Code remarks: one line of engine citation plus only the surprise; full derivation lives once in
  findings docs.
- **Code reads go through the LSP** (`get_symbol_source`, `find_references`, `blast_radius` before an
  edit), not grep/sed on `.cs` files — enforced by `~/.claude/hooks/prefer-lsp-for-symbols.ps1` (D178)
  after it didn't stick once.
- **Engine C++ through `clangd`**, not raw file reads (275 tokens vs. 8,055 for one function).
- **MCP over raw for everything** — GitHub through its MCP, not `gh` output.
- **The binary through GhidraMCP's headless server** (`curl` to a running instance), not
  `analyzeHeadless` (a JVM start per question).
- Disassembly logs: grep the address, then offset/limit to that routine only.
- Workflows/subagents only where they save main-context reads, not by reflex.
- Never trade parity for brevity ([[valve-parity-is-the-first-principle]]).
