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
    /// **`GetNextCommandLength`** (tier1/commandbuffer.cpp:163-203): a `;` outside quotes ends a command, and a `//` outside
    /// quotes hides the rest of the line — the `;` in it included.
    /// </remarks>
    [Test]
    public void Clauses_SemicolonsAndAComment_SplitOutsideQuotesOnly() =>
        SourceConfig.Clauses("bind a \"+x; +y\"; alias b \"\" // c; d").ShouldBe(["bind a \"+x; +y\"", "alias b \"\""]);
}
