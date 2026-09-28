---
name: use-bash-not-powershell-tool
description: Use the Bash tool (Git Bash) for commands, not the PowerShell tool — its encoded parse wrapper trips Defender.
metadata:
  type: feedback
---

Run commands through the Bash tool (Git Bash), not the PowerShell tool.

**Why:** owner asked after Defender flagged a false-positive trojan detection. The flagged line was
Claude Code's PowerShell-tool pre-parse wrapper (`pwsh -EncodedCommand <base64>`), which parses a
command's AST for the permission check — encoded pwsh command lines look like attacker obfuscation to
Defender's ML heuristic regardless of the harmless inner command.

**How to apply:** default to Bash. Needing PowerShell-only cmdlets is rare — find a bash/CLI
equivalent first (`tasklist`, `git`, `dotnet`). Viewer launches via `run-exclusive.ps1` can go through
`pwsh -NoProfile -File ...` from Bash, a plain script path rather than an encoded command. Related:
[[a-gui-exe-does-not-hold-the-lock]].
