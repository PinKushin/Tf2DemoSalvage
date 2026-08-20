using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// Assembly text that stops in the middle of an entity or effect block.
/// </summary>
/// <remarks>
/// **A truncated file is the realistic failure, not a mistyped keyword.** An edit interrupted, a
/// copy that ran out of disk, a paste that dropped the tail — all of them produce text whose last
/// block never closes, and all of them look identical to well-formed text until the reader runs
/// out of lines.
///
/// The input here is genuine rather than authored: a written demo is compiled to assembly and then
/// cut, so every line before the cut is exactly what this project emits. Hand-writing the same
/// text would test the parser against a fixture built from the same belief as the writer, which is
/// the failure <c>docs/memory/put-the-real-file-in-the-fixture.md</c> records.
///
/// Each cut is made at a different nesting depth, because the blocks are closed by three separate
/// loops with three separate messages, and a reader who is told "a block was not closed" without
/// being told which has been told very little.
/// </remarks>
public sealed class EntityAssemblyRefusalTests
{
    [Test]
    public void Parse_TextCutInsideAnEntityBlock_SaysTheEntityBlockWasNotClosed()
    {
        // Cut after the first `entity` line, so a property list is open and its packet is open
        // above it. The innermost unclosed block is the one worth naming.
        Refuse(CutAfter(PlayerAssembly(), "entity "))
            .ShouldContain("An entity was not closed");
    }

    [Test]
    public void Parse_TextCutInsideAnEffectList_SaysTheEffectBlockWasNotClosed()
    {
        // A temp entity list nests the same way and has its own message. Sharing one message
        // across both would make a truncation inside an effect read as a truncation inside a
        // snapshot, which sends the reader to the wrong line.
        Refuse(CutAfter(EffectAssembly(), "svc_tempentities"))
            .ShouldContain("effect");
    }

    [Test]
    public void Parse_TextCutInsideOneEffect_SaysTheEffectWasNotClosed()
    {
        // One level deeper: inside a single effect's property list rather than inside the list of
        // effects. Two loops, two messages.
        Refuse(CutAfter(EffectAssembly(), "effect class="))
            .ShouldContain("effect was not closed");
    }

    [Test]
    public void Parse_TheUncutText_StillCompilesToTheSameBytes()
    {
        // **The sensitivity control, and it is stronger than a "does not throw".** Every cut above
        // is one line from text that must compile — so this asserts that the uncut form not only
        // parses but produces the original demo byte for byte. Without it, a Parse that refused
        // everything would satisfy all three refusals.
        foreach (byte[] demo in new[] { PlayerDemo(), EffectDemo() })
        {
            (DemoHeader header, IReadOnlyList<DemoCommand> commands) = Read(demo);

            StringWriter text = new() { NewLine = "\n" };
            DemoAssembly.Write(text, header, commands);

            using StringReader reader = new(text.ToString());
            (DemoHeader compiledHeader, IReadOnlyList<DemoCommand> compiled) =
                DemoAssembly.Parse(reader);

            DemoWriter.Write(compiledHeader, compiled).ShouldBe(demo);
        }
    }

    /// <summary>Everything up to and including the first line containing <paramref name="marker"/>.</summary>
    private static string CutAfter(string assembly, string marker)
    {
        string[] lines = assembly.Split('\n');

        int index = Array.FindIndex(
            lines, line => line.Contains(marker, StringComparison.Ordinal));

        // **The cut has to have happened.** A marker that matches nothing leaves the text whole,
        // and the test then measures a complete file while claiming to measure a truncated one.
        index.ShouldBeGreaterThanOrEqualTo(0, $"the assembly has no line containing '{marker}'");

        return string.Join('\n', lines.Take(index + 1)) + "\n";
    }

    private static string Refuse(string text)
    {
        using StringReader reader = new(text);

        return Should.Throw<InvalidDataException>(() => DemoAssembly.Parse(reader)).Message;
    }

    /// <summary>A demo with one positioned player, as assembly text.</summary>
    private static string PlayerAssembly() => Assemble(PlayerDemo());

    /// <summary>A demo carrying temp entities, as assembly text.</summary>
    private static string EffectAssembly() => Assemble(EffectDemo());

    private static byte[] PlayerDemo() =>
        SyntheticPlayer.Demo(new Dictionary<string, Tf2DemoSalvage.Core.Schema.PropertyValue>
        {
            ["m_vecOrigin"] = Tf2DemoSalvage.Core.Schema.PropertyValue.FromVectorXY(64f, -64f),
            ["m_lifeState"] = Tf2DemoSalvage.Core.Schema.PropertyValue.FromInt(0),
        });

    private static byte[] EffectDemo() => SyntheticPlayer.DemoWithTempEntities();

    private static string Assemble(byte[] demo)
    {
        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = Read(demo);

        StringWriter text = new() { NewLine = "\n" };
        DemoAssembly.Write(text, header, commands);
        return text.ToString();
    }

    private static (DemoHeader Header, IReadOnlyList<DemoCommand> Commands) Read(byte[] demo) =>
        (DemoHeader.Parse(demo.AsSpan(0, DemoHeader.SizeBytes)),
            [.. DemoCommandReader.Read(demo.AsMemory(DemoHeader.SizeBytes))]);
}
