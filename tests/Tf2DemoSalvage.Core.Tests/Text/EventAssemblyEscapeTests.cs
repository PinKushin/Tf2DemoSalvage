using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// A game event whose string field carries characters the text form has to escape.
/// </summary>
/// <remarks>
/// **A game event string is player-controlled**, so it can hold a quote, a backslash, a newline or
/// a carriage return — and the assembly form is line-based, so every one of those would end the
/// line, the field or the block if it were written literally. The escape rule exists for that, and
/// it is written twice: once to quote and once to read back.
///
/// **`\r` is distinct from `\n` deliberately**, and that is the part worth a test. Mapping both
/// onto a newline reads back cleanly, produces a value that looks right, and turns a carriage
/// return into a line feed — so the demo compiled from the text is not the demo that was read. A
/// round trip through the BYTES is what catches it; a round trip through the string does not,
/// because the corrupted value is still a valid string.
///
/// Only a written demo can carry this. No corpus recording contains a chat line with a carriage
/// return in it, and waiting for one to turn up is not a plan.
/// </remarks>
public sealed class EventAssemblyEscapeTests
{
    /// <summary>Every character the escape rule has to survive, in one value.</summary>
    /// <remarks>
    /// One string rather than four tests: they share a single code path and separating them would
    /// only multiply the fixture. The interesting distinction is not between the characters, it is
    /// between the two that look alike.
    /// </remarks>
    private const string Awkward = "say \"gg\" \\ then\nnewline\rreturn";

    [Test]
    public void RoundTrip_AnEventStringWithQuotesAndNewlines_CompilesBackToItsOwnBytes()
    {
        // Byte-exact, which is the only assertion that separates a correct escape from one that
        // reads back a plausible different string.
        byte[] demo = Demo();

        RoundTrip(demo).ShouldBe(demo);
    }

    [Test]
    public void Assemble_AnEventStringWithANewline_DoesNotBreakTheLine()
    {
        // The mechanism, stated where it is visible: the value occupies one line whatever it
        // contains. A literal newline here would make the next line parse as a field name.
        string assembly = Assemble(Demo());

        assembly.ShouldContain("\\n");
        assembly.ShouldContain("\\r");
        assembly.ShouldContain("\\\"");
    }

    [Test]
    public void RoundTrip_AnEventOfEveryFieldType_KeepsEveryValuesType()
    {
        // **A field's type lives in the definition, so the assembler has to reconstruct it from a
        // string.** Every type is one arm of that conversion, and a type read one place along the
        // enum produces a value of the wrong width that still parses — which is RISKS B14, where
        // the wire numbering was assumed to match CS:GO's and `local` was read as a 64-bit int.
        //
        // `local` is here because it is the one that occupies NO bits: a converter that gave it a
        // value would write bits the reader does not expect and desynchronise the event after it.
        byte[] demo = EveryTypeDemo();

        RoundTrip(demo).ShouldBe(demo);

        // And the values themselves, because a byte-exact round trip of a body this project wrote
        // proves the two halves agree rather than that either is right.
        GameEventMessage fired = SyntheticDemo.MessagesIn(demo)
            .OfType<GameEventMessage>().ShouldHaveSingleItem();

        fired.Values["health"].ShouldBe((byte)125);
        fired.Values["crit"].ShouldBe(true);
        fired.Values["userid"].ShouldBe((short)12);
        fired.Values["damagebits"].ShouldBe(1048576);
        fired.Values["distance"].ShouldBe(23.5f);
    }

    [Test]
    public void Parse_AnEventWithNoDefinition_SaysItsFieldsCannotBeTyped()
    {
        // **A game event's field types live in svc_GameEventList, not in the event.** Without the
        // definition there is no way to know whether four bytes are a long or a float, so an event
        // that names an unknown id is refused rather than guessed at.
        string assembly = Assemble(Demo());

        // Renaming the id in the fired event, leaving the definition list declaring only the real
        // one. That is exactly the state a hand-edited assembly reaches by changing one number.
        string broken = assembly.Replace(
            "svc_gameevent 3 ", "svc_gameevent 77 ", StringComparison.Ordinal);

        broken.ShouldNotBe(assembly, "the fired event's id was never rewritten");

        using StringReader reader = new(broken);

        Should.Throw<InvalidDataException>(() => DemoAssembly.Parse(reader))
            .Message.ShouldContain("no definition");
    }

    /// <summary>A demo declaring one event and firing it with an awkward string.</summary>
    private static byte[] Demo() =>
        SyntheticDemo.Containing(
            new GameEventListMessage(
            [
                new GameEventDefinition(
                    Id: 3,
                    Name: "player_say",
                    Fields: [new GameEventField("text", GameEventValueType.String)]),
            ]),
            new GameEventMessage(
                EventId: 3,
                Name: "player_say",
                Values: new Dictionary<string, object?> { ["text"] = Awkward },
                BodyBits: 0));

    /// <summary>A demo firing one event carrying a field of every broadcast type, plus a local.</summary>
    private static byte[] EveryTypeDemo() =>
        SyntheticDemo.Containing(
            new GameEventListMessage(
            [
                new GameEventDefinition(
                    Id: 3,
                    Name: "player_hurt",
                    Fields:
                    [
                        new GameEventField("weapon", GameEventValueType.String),
                        new GameEventField("distance", GameEventValueType.Float),
                        new GameEventField("damagebits", GameEventValueType.Long),
                        new GameEventField("userid", GameEventValueType.Short),
                        new GameEventField("health", GameEventValueType.Byte),
                        new GameEventField("crit", GameEventValueType.Bool),
                        new GameEventField("secret", GameEventValueType.Local),
                    ]),
            ]),
            new GameEventMessage(
                EventId: 3,
                Name: "player_hurt",
                Values: new Dictionary<string, object?>
                {
                    ["weapon"] = "scattergun",
                    ["distance"] = 23.5f,
                    ["damagebits"] = 1048576,
                    ["userid"] = (short)12,
                    ["health"] = (byte)125,
                    ["crit"] = true,
                    ["secret"] = null,
                },
                BodyBits: 0));

    private static byte[] RoundTrip(byte[] demo)
    {
        using StringReader reader = new(Assemble(demo));
        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = DemoAssembly.Parse(reader);

        return DemoWriter.Write(header, commands);
    }

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
