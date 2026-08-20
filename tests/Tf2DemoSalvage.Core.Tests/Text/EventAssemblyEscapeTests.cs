using System;
using System.Collections.Generic;
using System.IO;

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
