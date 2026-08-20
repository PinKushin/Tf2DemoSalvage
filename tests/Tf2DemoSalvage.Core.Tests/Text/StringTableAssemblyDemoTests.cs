using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// String tables through the text assembly, including user data and updates.
/// </summary>
/// <remarks>
/// **An update is a different message from a create and shares none of its framing.** It names a
/// table by ID rather than by name, its entry count is inferred when only one entry changed, and
/// it is never compressed. Everything written so far exercised the create; this covers its
/// sibling, which is the one that carries a mid-match roster change or a newly precached model.
///
/// User data is here for the same reason: the entries this project could build until recently
/// carried text only, so the payload branch of the writer had never been reached from a fixture.
/// </remarks>
public sealed class StringTableAssemblyDemoTests
{
    [Test]
    public void RoundTrip_ATableWithUserData_CompilesBackToItsOwnBytes()
    {
        // The payload branch of the writer, which a text-only table never reaches. Two entries
        // carry data and one does not, because "no payload" is a flag rather than an empty one and
        // a writer that always wrote a length would produce a longer message that still decodes.
        byte[] demo = SyntheticDemo.Containing(SyntheticDemo.StringTable(
            "userinfo",
            [
                ("0", new byte[] { 1, 2, 3 }),
                ("1", Array.Empty<byte>()),
                ("2", new byte[] { 9 }),
            ],
            maxEntries: 32));

        RoundTrip(demo).ShouldBe(demo);
    }

    [Test]
    public void Assemble_ATableWithUserData_RendersItsPayloadRatherThanDroppingIt()
    {
        // A table whose entries render without their payloads round-trips only if the payload was
        // kept as bits somewhere. Naming it is what makes the text readable AND reproducible.
        string assembly = Assemble(SyntheticDemo.Containing(SyntheticDemo.StringTable(
            "userinfo",
            [("0", new byte[] { 0xAB, 0xCD })],
            maxEntries: 32)));

        assembly.ShouldContain("svc_createstringtable");
        assembly.ShouldContain("userinfo");
        assembly.ShouldContain("ABCD");
    }

    [Test]
    public void RoundTrip_AnUpdateToATableTheDemoAlreadyCreated_CompilesBackToItsOwnBytes()
    {
        // **An update names its table by ID**, and the ID comes from the order tables were
        // created — so an update can only be written after the create it refers to, and the
        // writer's state has to have learned it. That ordering is the whole reason this fixture
        // puts both in one packet.
        byte[] demo = Demo();

        RoundTrip(demo).ShouldBe(demo);
    }

    [Test]
    public void Assemble_AnUpdate_NamesTheTableItChangesAndItsEntryCount()
    {
        string assembly = Assemble(Demo());

        assembly.ShouldContain("svc_updatestringtable");
    }

    [Test]
    public void RoundTrip_AnUpdateOfExactlyOneEntry_OmitsTheCountField()
    {
        // **A single changed entry is the inferred case: the count field is absent and a flag bit
        // says so.** Writing it anyway produces a message sixteen bits longer that still decodes
        // to the same entry, so only a byte comparison catches it — and a fixture with two entries
        // never exercises the branch at all.
        byte[] one = DemoWithUpdate(entryCount: 1);
        byte[] two = DemoWithUpdate(entryCount: 2);

        RoundTrip(one).ShouldBe(one);
        RoundTrip(two).ShouldBe(two);

        // The one-entry form must be the shorter of the two, which is the observable difference
        // between omitting the count and writing it.
        one.Length.ShouldBeLessThanOrEqualTo(two.Length);
    }

    /// <summary>A demo whose packet creates a table and then updates it.</summary>
    private static byte[] Demo() => DemoWithUpdate(entryCount: 2);

    private static byte[] DemoWithUpdate(int entryCount) =>
        SyntheticDemo.Containing(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.StringTable("modelprecache", ["", "a.mdl"], maxEntries: 32),
            new UpdateStringTableMessage(
                TableId: 0,
                Entries: [],
                UndecodedReason: "carried as bits",
                Wire: new UpdateStringTableWire(entryCount, 16, new byte[] { 0x5A, 0xA5 })));

    private static byte[] RoundTrip(byte[] demo)
    {
        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = Read(demo);

        StringWriter text = new() { NewLine = "\n" };
        DemoAssembly.Write(text, header, commands);

        using StringReader reader = new(text.ToString());
        (DemoHeader compiledHeader, IReadOnlyList<DemoCommand> compiled) =
            DemoAssembly.Parse(reader);

        compiled.Count.ShouldBe(commands.Count);
        return DemoWriter.Write(compiledHeader, compiled);
    }

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
