using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// <c>EventAssembly</c> both ways — game event definitions and fired events, as text.
/// </summary>
/// <remarks>
/// **Written for the same reason as <c>MessageAssemblyTests</c>: 103 mutants that no test in
/// `Core.Tests` reached** (measured 2026-08-18, `docs/MEASUREMENT-PLAN.md`). The corpus exercises
/// this code constantly, but only over the events ten demos happen to fire, with the field types
/// those events happen to use.
///
/// **The value types are the point.** A definition can carry seven, and a real demo's common events
/// are mostly `Short` and `Byte` — so `Float`, `UInt64`-shaped values and especially `Local` (a
/// field the server declares and never broadcasts, occupying no bits at all) are thinly covered by
/// any corpus. `Local` is the one worth naming: it is the type that must NOT consume bits, and the
/// only way a test notices a mistake there is by placing a readable field after it.
///
/// The property is definition/event → text → definition/event, with nothing hand-written on either
/// side.
/// </remarks>
public sealed class EventAssemblyTests
{
    private const ushort Protocol = 24;

    private static NetDecodeState NewState() => new() { NetworkProtocol = Protocol };

    /// <summary>Every value type a definition can declare, in one event.</summary>
    private static GameEventDefinition EveryType => new(
        Id: 42,
        Name: "player_hurt",
        Fields:
        [
            new GameEventField("attacker", GameEventValueType.Short),
            new GameEventField("weapon", GameEventValueType.String),

            // **Declared but never broadcast, so it must occupy no bits — and it is placed in the
            // MIDDLE deliberately.** With this field last, the test could not fail: writing a
            // spurious bit for it would land after every value that gets asserted, and the whole
            // suite stayed green under exactly that sabotage. Everything below it is what makes
            // the claim measurable, because a stray bit here shifts all of them.
            new GameEventField("secret", GameEventValueType.Local),

            new GameEventField("damage", GameEventValueType.Float),
            new GameEventField("userid", GameEventValueType.Long),
            new GameEventField("health", GameEventValueType.Byte),
            new GameEventField("crit", GameEventValueType.Bool),
        ]);

    [Test]
    public void AnEventListRoundTripsEveryFieldTypeInOrder()
    {
        GameEventListMessage list = new([EveryType]);

        IReadOnlyList<string> lines = MessageAssembly.Write(list, Protocol, null)!;

        GameEventListMessage read = Rebuild<GameEventListMessage>(lines, NewState());

        read.Definitions.Count.ShouldBe(1);

        GameEventDefinition definition = read.Definitions[0];

        definition.Id.ShouldBe(42);
        definition.Name.ShouldBe("player_hurt");

        // Field order is the whole contract: an event body carries values positionally, so a
        // reordered definition decodes every later field at the wrong width.
        definition.Fields.ShouldBe(EveryType.Fields);
    }

    [Test]
    public void SeveralDefinitionsKeepTheirOwnIdsAndFields()
    {
        // One definition round-trips even if the id is ignored and the position used instead, so
        // there are three with ids that are deliberately not their positions.
        GameEventListMessage list = new(
        [
            new GameEventDefinition(7, "player_death", [new GameEventField("victim", GameEventValueType.Short)]),
            new GameEventDefinition(3, "round_start", [new GameEventField("full_reset", GameEventValueType.Bool)]),
            new GameEventDefinition(91, "player_say", [new GameEventField("text", GameEventValueType.String)]),
        ]);

        GameEventListMessage read = Rebuild<GameEventListMessage>(MessageAssembly.Write(list, Protocol, null)!, NewState());

        read.Definitions.Count.ShouldBe(3);
        read.Definitions[0].Id.ShouldBe(7);
        read.Definitions[0].Name.ShouldBe("player_death");
        read.Definitions[1].Id.ShouldBe(3);
        read.Definitions[2].Id.ShouldBe(91);
        read.Definitions[2].Fields[0].Name.ShouldBe("text");
    }

    [Test]
    public void ADefinitionWithNoFieldsSurvives()
    {
        // An event with no payload is real - several round-state events carry nothing - and an
        // empty block is exactly what a block parser gets wrong.
        GameEventListMessage list = new([new GameEventDefinition(5, "round_end", [])]);

        GameEventListMessage read = Rebuild<GameEventListMessage>(MessageAssembly.Write(list, Protocol, null)!, NewState());

        read.Definitions.Count.ShouldBe(1);
        read.Definitions[0].Name.ShouldBe("round_end");
        read.Definitions[0].Fields.ShouldBeEmpty();
    }

    [Test]
    public void AFiredEventRoundTripsItsValues()
    {
        NetDecodeState state = NewState();
        state.AddEventDefinitions([EveryType]);

        Dictionary<string, object?> values = new()
        {
            ["attacker"] = (short)-12,
            ["weapon"] = "tf_weapon_rocketlauncher",
            ["damage"] = 87.5f,
            ["userid"] = 123456,
            ["health"] = (byte)200,
            ["crit"] = true,
        };

        GameEventMessage read = Rebuild<GameEventMessage>(
            MessageAssembly.Write(new GameEventMessage(42, "player_hurt", values), Protocol, null)!, state);

        read.EventId.ShouldBe(42);
        read.Name.ShouldBe("player_hurt");

        // Each value asserted by its own type: a text form that renders everything as a string
        // round-trips through a parser that reads everything as a string, and both are wrong.
        read.Values["attacker"].ShouldBe((short)-12);
        read.Values["weapon"].ShouldBe("tf_weapon_rocketlauncher");
        read.Values["damage"].ShouldBe(87.5f);
        read.Values["userid"].ShouldBe(123456);
        read.Values["health"].ShouldBe((byte)200);
        read.Values["crit"].ShouldBe(true);
    }

    [Test]
    public void ANegativeShortAndAFalseBoolSurvive()
    {
        // The controls for the test above. A bool asserted only as true passes against a writer
        // that hardcodes it, and a short is where a sign is lost silently.
        NetDecodeState state = NewState();
        state.AddEventDefinitions([EveryType]);

        Dictionary<string, object?> values = new()
        {
            ["attacker"] = (short)-32768,
            ["weapon"] = string.Empty,
            ["damage"] = 0f,
            ["userid"] = -1,
            ["health"] = (byte)0,
            ["crit"] = false,
        };

        GameEventMessage read = Rebuild<GameEventMessage>(
            MessageAssembly.Write(new GameEventMessage(42, "player_hurt", values), Protocol, null)!, state);

        read.Values["attacker"].ShouldBe((short)-32768);
        read.Values["crit"].ShouldBe(false);
        read.Values["userid"].ShouldBe(-1);
        read.Values["weapon"].ShouldBe(string.Empty);
    }

    [Test]
    public void AStringValueSurvivesQuotesAndNonAscii()
    {
        NetDecodeState state = NewState();
        state.AddEventDefinitions(
            [new GameEventDefinition(1, "player_say", [new GameEventField("text", GameEventValueType.String)])]);

        const string Awkward = "he said \"gg\" — Ω名前";

        GameEventMessage read = Rebuild<GameEventMessage>(
            MessageAssembly.Write(
                new GameEventMessage(
                    1, "player_say", new Dictionary<string, object?> { ["text"] = Awkward }),
                Protocol,
                null)!,
            state);

        read.Values["text"].ShouldBe(Awkward);
    }

    /// <summary>Assembles rendered lines back to bits and decodes them.</summary>
    /// <remarks>
    /// **Routed through <c>MessageAssembly</c> rather than calling <c>EventAssembly.Build*</c>
    /// directly, and the first version of this file got that wrong.** It split the header line on
    /// spaces, which is not what the assembler does: the real tokenizer understands quoting, so a
    /// string value came back still wearing its quotes and three tests failed against correct code.
    /// Reimplementing a tokenizer in the test is the hand-built-fixture trap in miniature — the
    /// assembler already has one, and going through it exercises the path production uses.
    /// </remarks>
    private static TMessage Rebuild<TMessage>(IReadOnlyList<string> lines, NetDecodeState state)
        where TMessage : class, INetMessage
    {
        BitWriter writer = new();
        int next = 1;

        MessageAssembly.Assemble(
            lines[0], () => next < lines.Count ? lines[next++] : null, writer, state);

        // Read with a state that already knows the definitions, exactly as the reader would at
        // this point in a real stream: an event cannot be decoded without them.
        NetDecodeState reading = new() { NetworkProtocol = Protocol };
        reading.AddEventDefinitions(state.EventDefinitions.Values);

        foreach (INetMessage message in NetMessageReader.Read(writer.Build(), reading).Messages)
        {
            if (message is TMessage wanted)
            {
                return wanted;
            }
        }

        throw new System.InvalidOperationException($"no {typeof(TMessage).Name} came back");
    }
}
