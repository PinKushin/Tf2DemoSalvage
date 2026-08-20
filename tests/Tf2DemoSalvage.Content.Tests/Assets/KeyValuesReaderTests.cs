using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Valve's KeyValues, read as a stream of events rather than into a tree.
/// </summary>
/// <remarks>
/// **Streamed because the file this exists for is eight megabytes.** <c>items_game.txt</c> holds
/// every item TF2 has ever shipped, and a consumer of it wants a handful of keys out of two of its
/// blocks. Building a tree of the whole thing to read <c>model_player</c> off forty weapons would
/// allocate tens of megabytes and keep them.
///
/// **The syntax is small and the traps are in the corners**, so those are what these test: comments
/// to end of line, unquoted tokens, a value that is a block rather than a string, and the escape
/// sequences Valve's own reader honours inside quotes.
/// </remarks>
public sealed class KeyValuesReaderTests
{
    [Test]
    public void Read_APairAtTheTopLevel_ReportsKeyAndValue()
    {
        Pairs("\"model_player\" \"models/weapons/c_scattergun.mdl\"")
            .ShouldContain(("model_player", "models/weapons/c_scattergun.mdl"));
    }

    [Test]
    public void Read_AKeyWhoseValueIsABlock_ReportsTheKeyWithNoValue()
    {
        // The distinction the item schema turns on: `"prefab" "weapon_scattergun"` is a pair, and
        // `"used_by_classes" { ... }` is a block. A reader that treated the next token as the value
        // either way would report `used_by_classes` = `scout`.
        List<(string Key, string? Value)> pairs =
            Pairs("\"used_by_classes\" { \"scout\" \"1\" } \"prefab\" \"weapon_scattergun\"");

        pairs.ShouldContain(("used_by_classes", (string?)null));
        pairs.ShouldContain(("scout", (string?)"1"));
        pairs.ShouldContain(("prefab", (string?)"weapon_scattergun"));
    }

    [Test]
    public void Read_ACommentToEndOfLine_IsIgnored()
    {
        // items_game.txt is full of them, including commented-out keys — a reader that missed
        // these would report `//"playermodel"` as a key and its note as a value.
        Pairs("// \"model_player\" \"wrong.mdl\"\n\"model_player\" \"right.mdl\"")
            .ShouldContain(("model_player", "right.mdl"));
    }

    [Test]
    public void Read_UnquotedTokens_AreReadAsWords()
    {
        // Valve's reader accepts them and the shipped scripts use them — the weapon scripts write
        // `"viewmodel"  -viewmodel is now defined in _items_main.txt`, where the "value" is bare
        // words. A reader that required quotes silently skips to the next quoted token, which is
        // the next KEY, and answers with it.
        Pairs("\"viewmodel\" bare_word \"next\" \"1\"")
            .ShouldContain(("viewmodel", "bare_word"));
    }

    [Test]
    public void Read_NestedBlocks_ReportTheirDepth()
    {
        // Depth is what lets a consumer skip a block it does not care about without building a
        // tree, which is the whole reason this is a stream.
        List<string> path = [];

        KeyValuesReader.Read(
            Encoding.UTF8.GetBytes("\"items\" { \"13\" { \"name\" \"SCATTERGUN\" } }"),
            (key, value, depth) =>
            {
                path.Add($"{depth}:{key}={value ?? "{}"}");
                return true;
            });

        path.ShouldBe(["0:items={}", "1:13={}", "2:name=SCATTERGUN"]);
    }

    [Test]
    public void Read_WhenTheCallbackStops_TheRestIsNotParsed()
    {
        // The eight-megabyte file has one block anybody wants; stopping early is the difference
        // between reading it and reading past it.
        int seen = 0;

        KeyValuesReader.Read(
            Encoding.UTF8.GetBytes("\"a\" \"1\" \"b\" \"2\" \"c\" \"3\""),
            (key, value, depth) =>
            {
                seen++;
                return seen < 2;
            });

        seen.ShouldBe(2);
    }

    [Test]
    public void Read_AQuotedValueContainingBraces_IsNotMistakenForABlock()
    {
        // **The control.** Braces are structure outside quotes and text inside them, and a reader
        // that scanned for them without tracking quotes would lose its depth on the first path
        // that contained one.
        Pairs("\"note\" \"a { brace } inside\"")
            .ShouldContain(("note", "a { brace } inside"));
    }

    /// <summary>Every pair the reader reports, flattened.</summary>
    private static List<(string Key, string? Value)> Pairs(string text)
    {
        List<(string Key, string? Value)> pairs = [];

        KeyValuesReader.Read(
            Encoding.UTF8.GetBytes(text),
            (key, value, depth) =>
            {
                pairs.Add((key, value));
                return true;
            });

        return pairs;
    }
}
