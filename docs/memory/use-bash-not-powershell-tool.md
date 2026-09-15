---
name: use-bash-not-powershell-tool
description: Use the Bash tool (Git Bash) for commands, not the PowerShell tool — its encoded parse wrapper trips Defender.
metadata:
  type: feedback
---

Run commands through the **Bash tool** (Git Bash, present on this machine), not the PowerShell tool.

**Why:** the owner asked for it on 2026-09-15 after Defender flagged `Trojan:Win32/Commando.A!ml`. The
flagged command line was Claude Code's PowerShell-tool pre-parse: `pwsh -NoProfile -NonInteractive
-EncodedCommand <base64>`, which parses a command's AST for the permission check. The inner command
was harmless float arithmetic. Encoded pwsh command lines look like attacker obfuscation to Defender's
ML heuristic, so every PowerShell tool call risks another detection. The owner said they have no bash;
they do — Git Bash is what the Bash tool runs.

**How to apply:** default to Bash. Needing PowerShell-only cmdlets (`Get-CimInstance`, etc.) is rare;
find a bash/CLI equivalent (`tasklist`, `git`, `dotnet`) first. The `run-exclusive.ps1` viewer
launches can go through `pwsh -NoProfile -File ...` from Bash, which is a plain script path rather
than an encoded command. Related: [[a-gui-exe-does-not-hold-the-lock]].
