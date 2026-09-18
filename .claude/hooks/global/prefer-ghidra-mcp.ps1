# PreToolUse guard: a one-question analyzeHeadless read of vphysics.dll is refused, naming the headless GhidraMCP server.
#
# WHY A HOOK. The owner, 2026-09-16, after a session of reading the binary: "Did you make a hook to remind you to use the mcp
# and lsp servers?" docs/memory/spend-fewer-tokens.md already said the binary goes through GhidraMCP's headless server; the
# session still ran analyzeHeadless for every question - a JVM start, a project open and a log full of per-line prefixes each
# time - where one curl to the running server returns the one routine.
#
# WHAT IT REFUSES: an analyzeHeadless command against the analysed vphysics projects (tf2vphysics, tf2vphysics-collide) that
# runs one of the read scripts (DecompAt, DisasmAt, DisasmWithData, DumpFloats, DumpDoubles, DumpInts, DumpPointers).
# WHAT IT LEAVES: -import runs, analysis, renames and every other project (engine.dll, materialsystem, ...), which the
# server does not load.
$ErrorActionPreference = 'Stop'
try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $command = [string](($raw | ConvertFrom-Json).tool_input.command)
    if ([string]::IsNullOrWhiteSpace($command)) { exit 0 }
} catch { exit 0 }

if ($command -notmatch '(?i)analyzeHeadless') { exit 0 }
if ($command -notmatch '(?i)\btf2vphysics(-collide)?\b') { exit 0 }
if ($command -match '(?i)\s-import\b') { exit 0 }
if ($command -notmatch '(?i)-postScript\s+(DecompAt|DisasmAt|DisasmWithData|DumpFloats|DumpDoubles|DumpInts|DumpPointers)\.java') { exit 0 }

$reason = "vphysics.dll reads go through the headless GhidraMCP server, not analyzeHeadless (owner, 2026-09-16). " +
          "Start once per session: pmux new-session -d -s ghidra-mcp, then pmux send-keys -t ghidra-mcp " +
          "'D:\ghidra-proj\ghidra-mcp-headless.bat' Enter (check: curl http://127.0.0.1:8089/mcp/schema). Then " +
          "curl 'http://127.0.0.1:8089/disassemble_function?address=0x1800...' (constants: read_memory?address=..&length=..; " +
          "decompile_function, get_function_callers, get_function_callees). Redirect to `$TEMP and grep the lines needed."

@{ hookSpecificOutput = @{
      hookEventName = 'PreToolUse'
      permissionDecision = 'deny'
      permissionDecisionReason = $reason } } | ConvertTo-Json -Depth 5 -Compress
