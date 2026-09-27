# PreToolUse guard: reading a decompiled binary by shell is refused, naming the Ghidra MCP tools that answer it.
#
# WHY A HOOK. The owner, 2026-09-16, after a session of reading the binary: "Did you make a hook to remind you to use the mcp
# and lsp servers?" docs/memory/spend-fewer-tokens.md already said the binary goes through GhidraMCP's headless server; the
# session still ran analyzeHeadless for every question - a JVM start, a project open and a log full of per-line prefixes each
# time - where one call to the running server returns the one routine.
#
# WIDENED 2026-09-28. The owner: "it should also force the use of the mcp/lsp for ghidra and the stuff that helps keep
# tokens low during reversing too, thats the whole point in the lsps and MCPs". The first version covered only the two
# vphysics projects and pointed at raw curl; the `mcp__ghidra__*` tools are now connected and answer for any program.
#
# WHAT IT REFUSES:
#   - analyzeHeadless running a read script (DecompAt, DisasmAt, DisasmWithData, Dump*) against ANY project;
#   - curl to the GhidraMCP HTTP server (127.0.0.1:8089) - the MCP tools wrap it without the JSON/log noise;
#   - grep/cat/sed/head/tail/awk over Ghidra output under D:\ghidra-proj (decompile and disassembly dumps).
# WHAT IT LEAVES: -import runs, analysis, and scripts that WRITE (renames, labels) - the MCP does not replace those; starting
# the headless server itself (ghidra-mcp-headless.bat); and every read of anything that is not decompiler output.
$ErrorActionPreference = 'Stop'
try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $command = [string](($raw | ConvertFrom-Json).tool_input.command)
    if ([string]::IsNullOrWhiteSpace($command)) { exit 0 }
} catch { exit 0 }

$refuse = $false

if ($command -match '(?i)analyzeHeadless' -and $command -notmatch '(?i)\s-import\b' -and
    $command -match '(?i)-postScript\s+(DecompAt|DisasmAt|DisasmWithData|Dump\w+)\.java') {
    $refuse = $true
}

# Any GhidraMCP server port, not only 8089: a second headless server on 8090 was read by curl on 2026-09-27.
if ($command -match '(?i)\bcurl\b[^|;&]*(127\.0\.0\.1|localhost):80[89]\d\b' -and $command -notmatch '(?i)/mcp/schema') {
    $refuse = $true
}

if ($command -match '(?i)\b(grep|rg|cat|sed|head|tail|awk|less)\b[^|;&]*[/\\]ghidra-proj[/\\]' -and
    $command -notmatch '(?i)ghidra-mcp-headless\.bat') {
    $refuse = $true
}

if (-not $refuse) { exit 0 }

$reason = "Binary reads go through the Ghidra MCP, not analyzeHeadless, curl or grep over dumps (owner, 2026-09-16 and " +
          "2026-09-28). Load the tools with mcp__ghidra__search_tools / load_tool_group, then list_instances and " +
          "connect_instance to the program; ask for ONE function or ONE constant (decompile, disassemble, read memory, " +
          "callers, callees). If no instance is up, start the headless server once: pmux new-session -d -s ghidra-mcp, then " +
          "pmux send-keys -t ghidra-mcp 'D:\ghidra-proj\ghidra-mcp-headless.bat' Enter. Imports, analysis and rename " +
          "scripts stay on analyzeHeadless."

@{ hookSpecificOutput = @{
      hookEventName = 'PreToolUse'
      permissionDecision = 'deny'
      permissionDecisionReason = $reason } } | ConvertTo-Json -Depth 5 -Compress
