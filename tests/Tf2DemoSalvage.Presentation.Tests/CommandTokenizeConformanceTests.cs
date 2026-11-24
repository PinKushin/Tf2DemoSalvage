using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>One console command into its arguments, as `CCommand::Tokenize` does it (B532).</summary>
/// <remarks>
/// <code>
/// // tier1/convar.cpp:326, :389-478 — the break set is "{}()':", and each argument is one CUtlBuffer::ParseToken
/// // tier1/utlbuffer.cpp:1242-1326
/// EatWhiteSpace (isspace); a "//" here eats the line
/// '"' → everything to the next '"' or the end, EMPTY INCLUDED, the quotes dropped
/// a break character → itself, alone
/// else a word, up to a break character, a '"' or anything &lt;= ' '
/// </code>
/// A config is not KeyValues: no escapes, no braces as structure, no conditionals.
/// </remarks>
public sealed class CommandTokenizeConformanceTests
{
    [TestCase("bind w +forward", new[] { "bind", "w", "+forward" })]
    [TestCase("  bind\t\"w\"   \"+forward\"  ", new[] { "bind", "w", "+forward" })]
    [TestCase("bind \"MOUSE1\" \"+attack; spec_next\"", new[] { "bind", "MOUSE1", "+attack; spec_next" })]
    public void Tokens_AnOrdinaryCommand_SplitsOnWhiteSpaceAndKeepsAQuotedStringWhole(string command, string[] expected) =>
        SourceConfig.Tokens(command).ShouldBe(expected);

    /// <remarks>`alias noop ""` defines an alias that does nothing, and `bind k ""` clears a key: both are three arguments.</remarks>
    [TestCase("alias noop \"\"", new[] { "alias", "noop", "" })]
    [TestCase("bind k \"\"", new[] { "bind", "k", "" })]
    public void Tokens_AnEmptyQuotedString_IsAnArgument(string command, string[] expected) =>
        SourceConfig.Tokens(command).ShouldBe(expected);

    [TestCase("say a:b", new[] { "say", "a", ":", "b" })]
    [TestCase("bind ' +use", new[] { "bind", "'", "+use" })]
    [TestCase("echo (x){y}", new[] { "echo", "(", "x", ")", "{", "y", "}" })]
    public void Tokens_ABreakCharacter_IsAnArgumentOfItsOwnAndEndsAWord(string command, string[] expected) =>
        SourceConfig.Tokens(command).ShouldBe(expected);

    /// <remarks>The case convar.cpp:450 names: `"foo"bar` parses into two arguments.</remarks>
    [TestCase("\"foo\"bar", new[] { "foo", "bar" })]
    [TestCase("a\"b c\"d", new[] { "a", "b c", "d" })]
    public void Tokens_AQuoteAgainstAWord_SplitsThem(string command, string[] expected) =>
        SourceConfig.Tokens(command).ShouldBe(expected);

    [TestCase("bind w +forward // moves", new[] { "bind", "w", "+forward" })]
    [TestCase("echo \"a // b\"", new[] { "echo", "a // b" })]
    [TestCase("echo a//b", new[] { "echo", "a//b" })]
    public void Tokens_AComment_IsEatenOnlyWhereAnArgumentWouldStart(string command, string[] expected) =>
        SourceConfig.Tokens(command).ShouldBe(expected);

    [TestCase("echo \"unterminated text", new[] { "echo", "unterminated text" })]
    [TestCase("echo a\vb\fc", new[] { "echo", "a", "b", "c" })]
    [TestCase("", new string[0])]
    public void Tokens_TheEdges_AreTheBuffers(string command, string[] expected) =>
        SourceConfig.Tokens(command).ShouldBe(expected);

    /// <remarks>
    /// **A byte of 0x80 or more ends a word and starts the next** (B823): `ParseToken` stores a word's first `char` unchecked
    /// and ends it at `c &lt;= ' '` with `c` a signed `char` (tier1/utlbuffer.cpp:1303-1320), and `EatWhiteSpace` tests
    /// `isspace` on the unsigned byte (:404-415). So TF2 tokenizes a config's UTF-8 BYTES, and a non-ASCII character
    /// unquoted is one argument per byte. A lone byte is spelled as the lone surrogate U+DC00 + byte (see
    /// <see cref="SourceConfig.Tokens"/>). `EF BB BF` running straight into `echo x` is the B822 measurement's line. A
    /// source, not attributes: an attribute's string is stored as UTF-8 and loses a lone surrogate to U+FFFD, and a lone
    /// surrogate in a test's name drops the test from discovery, so each case names itself in ASCII.
    /// </remarks>
    [TestCaseSource(nameof(UnquotedNonAscii))]
    public void Tokens_AnUnquotedNonAsciiWord_IsOneArgumentPerByte(string command, string[] expected) =>
        SourceConfig.Tokens(command).ShouldBe(expected);

    private static readonly TestCaseData[] UnquotedNonAscii =
    [
        new TestCaseData("﻿echo x", new[] { "\uDCEF", "\uDCBB", "\uDCBFecho", "x" }).SetArgDisplayNames("EF BB BF echo x"),
        new TestCaseData("alias café x", new[] { "alias", "caf", "\uDCC3", "\uDCA9", "x" }).SetArgDisplayNames("alias caf C3 A9 x"),
        new TestCaseData("bind k éa", new[] { "bind", "k", "\uDCC3", "\uDCA9a" }).SetArgDisplayNames("bind k C3 A9 a"),
    ];

    /// <remarks>A quoted string runs to the next `"` whatever its bytes (utlbuffer.cpp:1268-1287), so its text stays whole.</remarks>
    [TestCase("echo \"café\"", new[] { "echo", "café" })]
    [TestCase("echo \"﻿ €\"", new[] { "echo", "﻿ €" })]
    public void Tokens_AQuotedNonAsciiString_StaysWhole(string command, string[] expected) =>
        SourceConfig.Tokens(command).ShouldBe(expected);

    /// <remarks>
    /// **A ConVar's value is `ArgS`, not `Arg(1)`** (B823): `engine.dll` FUN_180176c50 (x64), the console's ConVar set — the
    /// function holding "Can't use cheat cvar %s in multiplayer" — copies `ArgS()` (convar.h:232-235, the text from argument
    /// 1 on), drops a leading `"`, strips trailing bytes `&lt;= ' '` by a SIGNED compare (`CMP byte ptr [RCX],0x20; JG`), and
    /// then one trailing `"` if it began with one. So the bytes of a trailing non-ASCII character are stripped as if they were
    /// spaces, and a leading one is kept whole, where `Arg(1)` would be its first byte alone.
    /// </remarks>
    [TestCase("cl_voice_filter café", "caf")]
    [TestCase("cl_voice_filter éa", "éa")]
    [TestCase("cl_voice_filter \"café\"", "café")]
    [TestCase("cl_voice_filter a b \t", "a b")]
    [TestCase("cl_voice_filter \"a\" b", "a\" b")]
    [TestCase("cl_voice_filter", "")]
    public void ConVarValue_ACommand_IsArgSWithTheEnginesTrim(string command, string expected) =>
        SourceConfig.ConVarValue(command).ShouldBe(expected);

    /// <remarks>
    /// **`GetNextCommandLength`** (tier1/commandbuffer.cpp:163-203): a `;` outside quotes ends a command, and a `//` outside
    /// quotes hides the rest of the line — the `;` in it included.
    /// </remarks>
    [Test]
    public void Clauses_SemicolonsAndAComment_SplitOutsideQuotesOnly() =>
        SourceConfig.Clauses("bind a \"+x; +y\"; alias b \"\" // c; d").ShouldBe(["bind a \"+x; +y\"", "alias b \"\""]);
}
