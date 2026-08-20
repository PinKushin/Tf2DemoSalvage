using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// A string table message with no wire form, and a table block that never closes.
/// </summary>
/// <remarks>
/// **A message this project constructed and a message it read are different things**, and only the
/// second can be written back. The wire form records the bits exactly as they arrived — the entry
/// count the header declared, the body's length, whether it was compressed — and none of that is
/// recoverable from the decoded entries: the same entries can be encoded several ways, and a
/// writer that picked one would produce a demo that decodes identically and does not match.
///
/// So a message with no wire form declines to render as assembly rather than inventing one, and
/// the caller falls back to raw bits. Returning <c>null</c> is what says "I cannot reproduce this",
/// and it is the branch nothing reaches when every fixture comes from a real or written demo.
///
/// <c>docs/memory/round-trip-needs-the-encoding-shape.md</c> is the general statement: which
/// optional fields were sent is not recoverable from the values.
/// </remarks>
public sealed class StringTableAssemblyWireTests
{
    [Test]
    public void WriteCreate_ATableWithNoWireForm_DeclinesRatherThanInventingOne()
    {
        // The entries are perfectly good; what is missing is how they were framed. A writer that
        // guessed would emit a table that decodes to the same entries from different bits.
        StringTableAssembly.WriteCreate(new CreateStringTableMessage(
            Name: "userinfo",
            MaxEntries: 32,
            Entries: [new StringTableEntry(0, "0", [])],
            IsCompressed: false,
            UndecodedReason: null,
            Wire: null)).ShouldBeNull();
    }

    [Test]
    public void WriteUpdate_AnUpdateWithNoWireForm_DeclinesTheSameWay()
    {
        // The update has its own writer, so the same rule needs stating twice or one of them
        // drifts. Both are reached only by a message a test built.
        StringTableAssembly.WriteUpdate(new UpdateStringTableMessage(
            TableId: 0,
            Entries: [new StringTableEntry(0, "0", [])],
            UndecodedReason: null,
            Wire: null)).ShouldBeNull();
    }

    [Test]
    public void WriteCreate_ATableThatCameFromADemo_DoesRender()
    {
        // **The control.** A WriteCreate that returned null unconditionally would satisfy both
        // assertions above while silently turning every string table in every assembly into raw
        // bits — which still round-trips, and would go unnoticed.
        StringTableAssembly.WriteCreate(
            SyntheticDemo.StringTable("modelprecache", ["", "a.mdl"], maxEntries: 32))
            .ShouldNotBeNull()
            .ShouldNotBeEmpty();
    }

    [Test]
    public void Parse_AssemblyCutInsideATableBlock_SaysTheTableWasNotClosed()
    {
        // The truncation case for the third kind of block. Cut from text this project produced,
        // so every line before the cut is genuine.
        string assembly = Assemble(Demo());

        string[] lines = assembly.Split('\n');
        int index = Array.FindIndex(
            lines, line => line.Contains("svc_createstringtable", StringComparison.Ordinal));

        index.ShouldBeGreaterThanOrEqualTo(0, "the assembly has no create-table line");

        using StringReader reader = new(
            string.Join('\n', lines[..(index + 1)]) + "\n");

        Should.Throw<InvalidDataException>(() => DemoAssembly.Parse(reader))
            .Message.ShouldContain("not closed");
    }

    [Test]
    public void RoundTrip_TheUncutAssembly_ReproducesTheDemo()
    {
        // The sensitivity control for the cut above: the same text, one line longer, must compile
        // back to the original bytes.
        byte[] demo = Demo();

        using StringReader reader = new(Assemble(demo));
        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = DemoAssembly.Parse(reader);

        DemoWriter.Write(header, commands).ShouldBe(demo);
    }

    private static byte[] Demo() =>
        SyntheticDemo.Containing(SyntheticDemo.StringTable(
            "userinfo",
            [("0", new byte[] { 1, 2, 3 }), ("1", new byte[] { 4 })],
            maxEntries: 32));

    private static string Assemble(byte[] demo)
    {
        DemoHeader header = DemoHeader.Parse(demo.AsSpan(0, DemoHeader.SizeBytes));
        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(demo.AsMemory(DemoHeader.SizeBytes))];

        StringWriter text = new() { NewLine = "\n" };
        DemoAssembly.Write(text, header, commands);
        return text.ToString();
    }
}
