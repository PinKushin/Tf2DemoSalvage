# PreToolUse guard: a text search for a C# SYMBOL is refused, naming the agent-lsp tools that answer it.
#
# WHY A HOOK. The owner, 2026-09-16: "Remember the lsp and mcp servers. Idk if you have been using them but I
# just saw grep" and "you've wasted so many tokens not using that today. There needs to be a hook that
# reminds you." A whole session of porting went by on grep and sed over .cs files while agent-lsp was
# connected: every "where is X defined", "who calls X" and "show me X" was a grep that printed dozens of
# matching lines, then a sed that printed a hundred more, where find_symbol / find_references /
# get_symbol_source answer with the one declaration or the exact call sites. A rule in CLAUDE.md was not
# enough - the habit won every time - so it is enforced at the tool call.
#
# WHY DENY AND NOT A SILENT REMINDER. An allow-with-context note is read after the search has already
# printed its output, so the tokens are spent either way. Refusing costs one short round trip and the
# next call is the right tool.
#
# WHAT COUNTS AS A SYMBOL SEARCH (all must hold):
#   - the search targets C#: the Grep tool's glob/type/path names .cs, or a shell grep/rg names .cs
#     (a file, a --include, or -g glob);
#   - the pattern is symbol-shaped: a bare PascalCase identifier (`IvpFrictionSystem`), a member access
#     (`Foo.Bar`), a declaration (`class X`, `record X`, `void X(`, `public ... X`), or a call (`X(`),
#     alone or as an alternation of those.
# WHAT IS LEFT ALONE: patterns with prose (spaces between plain words), engine addresses and offsets
# (`FUN_`, `DAT_`, `0x`, `+0x`), quoted strings, test-output and log greps, and searches over docs, json,
# txt and anything not C#. Those are text questions, which is what grep is for.
#
# FIRST USE IN A SESSION: agent-lsp needs start_lsp(root_dir=<repo root>, language_id=csharp) once.
#
# C AND C++ TOO (owner, 2026-09-28: "we need a hook that forces you to use the lsp for c++ code, I have one for
# C# already ... but you are constantly going back to grep for fucking c++"). The C# rule never fired on the
# Source SDK because its trigger is `.cs`: a `grep -n "CanPickupBuilding\|m_iStunFlags" *.cpp` walked straight
# through. The C/C++ trigger is a grep naming .cpp/.cc/.cxx/.c/.h/.hpp/.inl, or one run inside the SDK tree
# (a path containing source-sdk-2013). C++ symbols do not share C#'s PascalCase shape - `m_iStunFlags`,
# `CTFPlayer::CanPickupBuilding`, `GetActiveStunInfo(`, `^[A-Za-z].*CTFHudPlayerClass::` - so any code-shaped
# token counts: `Class::Member`, `m_` fields, a `C`-prefixed class, a call, a CamelCase identifier, or a bare
# identifier alternation. Prose, addresses and quoted string literals stay allowed, as for C#. Reading a known
# file at a known line with the Read tool is not a search and is untouched.
$ErrorActionPreference = 'Stop'
try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $toolInput = ($raw | ConvertFrom-Json).tool_input
    if ($null -eq $toolInput) { exit 0 }
} catch { exit 0 }

$identifier = '[A-Z][A-Za-z0-9_]*'
# PowerShell strings keep backslashes literally, so each `\` below is one regex backslash.
$symbolAtom = "(?:(?:class|record|struct|interface|enum|void|static|public|internal|private|protected)\s+[^|]*$identifier|$identifier(?:\.$identifier)*\s*(?:\\?\()?)"
$symbolPattern = "^\s*(?:\\b)?(?:$symbolAtom)(?:\s*\|\s*(?:\\b)?(?:$symbolAtom))*(?:\\b)?\s*$"

function Test-SymbolPattern([string]$pattern) {
    if ([string]::IsNullOrWhiteSpace($pattern)) { return $false }
    if ($pattern -match '(FUN_|DAT_|0x[0-9a-fA-F]|\+0x|Passed!|Failed!|error |warning |TRACE|"|'')') { return $false }
    # Drop grep's own escaped alternation so `A\|B` reads as `A|B`.
    $normalised = $pattern -replace '\\\|', '|'
    return $normalised -cmatch $symbolPattern
}

$cppWhere = '(?i)(\.(cpp|cc|cxx|c|h|hpp|hxx|inl)\b|\b(cpp|c)\b(?=\s*$)|source-sdk-2013)'
$cppToken = '(\w+::~?\w+|\bm_\w+|\bC[A-Z][A-Za-z0-9_]+|\b[A-Za-z_]\w*\s*\\?\(|\b[a-z]+[A-Z]\w*|\b[A-Z][a-z0-9]+[A-Z]\w*)'

function Test-CppSymbolPattern([string]$pattern) {
    if ([string]::IsNullOrWhiteSpace($pattern)) { return $false }
    if ($pattern -match '(FUN_|DAT_|0x[0-9a-fA-F]|\+0x)') { return $false }
    $normalised = $pattern -replace '\\\|', '|'
    # Prose: three or more plain lowercase words in a row, with no code token anywhere.
    if ($normalised -cnotmatch $cppToken) {
        # A bare alternation of identifiers (`minigame|premade`) is still a symbol hunt.
        return $normalised -match '^\s*\w{3,}(\s*\|\s*\w{3,})+\s*$'
    }
    return $true
}

function Deny([string]$reason) {
    @{ hookSpecificOutput = @{
          hookEventName = 'PreToolUse'
          permissionDecision = 'deny'
          permissionDecisionReason = $reason } } | ConvertTo-Json -Depth 5 -Compress
    exit 0
}

$cppReason = "This is a C/C++ symbol lookup - use the clangd MCP, not grep (owner, 2026-09-28). Open the file first " +
             "(mcp__clangd__open_document), then mcp__clangd__find_symbol for a declaration, go_to_definition / " +
             "find_references / get_symbol_source for bodies and callers, list_symbols(format='outline') for a file's " +
             "shape; if a class misses, query the method. For the Valve SDK, start_lsp(root_dir='F:/src/source-sdk-2013', " +
             "language_id='cpp') once. Reading a KNOWN file at a KNOWN line is Read, not a search. Text questions - " +
             "quoted strings, .res/.txt/.vmt content, prose, addresses - stay on grep."

# C/C++ first: the same tool call can never be both.
if ($null -ne $toolInput.pattern -and $null -eq $toolInput.command) {
    $where = "$($toolInput.glob) $($toolInput.type) $($toolInput.path)"
    if ($where -match $cppWhere -and (Test-CppSymbolPattern ([string]$toolInput.pattern))) { Deny $cppReason }
} elseif ($null -ne $toolInput.command) {
    $cmd = [string]$toolInput.command
    $m = [regex]::Match($cmd, '(?:^|[;&|(]\s*|\s)(?:grep|rg)\b(?<args>(?:"[^"]*"|''[^'']*''|[^|;&"''])*)')
    if ($m.Success) {
        $gArgs = $m.Groups['args'].Value
        $inSdk = $cmd -match '(?i)source-sdk-2013'
        if ($gArgs -match $cppWhere -or $inSdk) {
            $q = [regex]::Match($gArgs, '"(?<p>[^"]*)"|''(?<p>[^'']*)''')
            if ($q.Success -and (Test-CppSymbolPattern $q.Groups['p'].Value)) { Deny $cppReason }
        }
    }
}

$pattern = $null
$targetsCs = $false

if ($null -ne $toolInput.pattern -and $null -eq $toolInput.command) {
    # The Grep tool.
    $pattern = [string]$toolInput.pattern
    $where = "$($toolInput.glob) $($toolInput.type) $($toolInput.path)"
    $targetsCs = $where -match '(?i)(\.cs\b|\bcs\b|\*\.\{[^}]*\bcs\b)'
} elseif ($null -ne $toolInput.command) {
    $command = [string]$toolInput.command
    # Only the first grep/rg of a pipeline is judged; output-filtering greps over a file of results are text.
    $match = [regex]::Match($command, '(?:^|[;&|(]\s*|\s)(?:grep|rg)\b(?<args>(?:"[^"]*"|''[^'']*''|[^|;&"''])*)')
    if (-not $match.Success) { exit 0 }
    $grepArgs = $match.Groups['args'].Value
    if ($grepArgs -notmatch '(?i)\.cs\b') { exit 0 }
    $targetsCs = $true
    $quoted = [regex]::Match($grepArgs, '"(?<p>[^"]*)"|''(?<p>[^'']*)''')
    if (-not $quoted.Success) { exit 0 }
    $pattern = $quoted.Groups['p'].Value
    # A quoted pattern is not a string literal search here, so judge it without its own quotes.
    if ($pattern -match '(FUN_|DAT_|0x[0-9a-fA-F]|\+0x)') { exit 0 }
    $normalised = $pattern -replace '\\\|', '|'
    if ($normalised -cnotmatch $symbolPattern) { exit 0 }
    $pattern = $null
} else {
    exit 0
}

if (-not $targetsCs) { exit 0 }
if ($null -ne $pattern -and -not (Test-SymbolPattern $pattern)) { exit 0 }

$reason = "This is a C# symbol lookup - use agent-lsp, not a text search (owner, 2026-09-16: grep over symbols wasted " +
          "the session's tokens). find_symbol(query) for where it is declared; find_references(file_path, " +
          "position_pattern) for who uses it; get_symbol_source(file_path, line) for its body; list_symbols(file_path, " +
          "format='outline') for a file's shape; blast_radius before an edit. Load them with ToolSearch " +
          "'select:mcp__agent-lsp__find_symbol,...', and call mcp__agent-lsp__start_lsp(root_dir=<repo root>, " +
          "language_id='csharp') once per session first. Text questions - FUN_/DAT_ addresses, offsets, prose in " +
          "comments, docs - stay on grep."

@{ hookSpecificOutput = @{
      hookEventName = 'PreToolUse'
      permissionDecision = 'deny'
      permissionDecisionReason = $reason } } | ConvertTo-Json -Depth 5 -Compress
