using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// What <see cref="EventAssembly"/> refuses in a hand-edited event line (B345).
/// </summary>
/// <remarks>
/// **Routed through <see cref="MessageAssembly"/> rather than calling `EventAssembly.Build*`
/// directly**, for the reason `EventAssemblyTests` records: the real tokenizer understands quoting,
/// so splitting a line on spaces in the test measures a different parser from the one production
/// runs.
///
/// **The field types are checked against their own RANGE, not merely parsed, and that is new.**
/// `modevents.res` documents the widths in a comment block at the top of the file — `short` is
/// 16-bit signed, `byte` 8-bit unsigned — so a value outside that does not fit the bits the writer
/// will give it. `short.Parse` refused those already, with a `FormatException`/`OverflowException`
/// that walked past the handler attaching the line; `int.Parse` plus an explicit range says which
/// bound was passed and by what.
/// </remarks>
public sealed class EventAssemblyRefusalTests
{
    private const ushort Protocol = 24;

    /// <summary>One event with a narrow field of each signedness, and one float.</summary>
    private static GameEventDefinition Definition => new(
        Id: 42,
        Name: "player_hurt",
        Fields:
        [
            new GameEventField("attacker", GameEventValueType.Short),
            new GameEventField("health", GameEventValueType.Byte),
            new GameEventField("damage", GameEventValueType.Float),
        ]);

    /// <remarks>
    /// **A short above 32767 does not fit the 16 bits the writer gives it.** Accepting it would
    /// produce a demo saying something the text did not — the value would come back truncated,
    /// which is a wrong answer rather than a refusal.
    /// </remarks>
    [Test]
    public void Assemble_AShortFieldAboveItsRange_IsRefusedWithBothBounds()
    {
        string failure = Refuse("attacker", "40000");

        failure.ShouldContain("40000", Case.Sensitive);
        failure.ShouldContain("32767", Case.Sensitive);
    }

    /// <remarks>
    /// The other end, and the other signedness: `byte` is UNSIGNED per `modevents.res`, so -1 is
    /// outside it even though it is a perfectly ordinary number.
    /// </remarks>
    [Test]
    public void Assemble_AByteFieldBelowItsRange_IsRefused()
    {
        Refuse("health", "-1").ShouldContain("-1", Case.Sensitive);
    }

    /// <remarks>A float, which takes a different parse from the two narrow types above.</remarks>
    [Test]
    public void Assemble_AFloatFieldThatIsNotANumber_IsRefused()
    {
        Refuse("damage", "lots").ShouldContain("lots", Case.Sensitive);
    }

    /// <remarks>
    /// **The control, and it carries the boundary.** 32767 and 0 are the largest short and the
    /// smallest byte, so a range written with `&lt;` instead of `&lt;=` would refuse this and
    /// nothing else in the file would notice. It also proves the refusals above are about the
    /// VALUES rather than about the line being rejected wholesale.
    /// </remarks>
    [Test]
    public void Assemble_FieldsExactlyOnTheirBounds_StillAssemble()
    {
        Should.NotThrow(() => Assemble(Line("attacker", "32767")));
        Should.NotThrow(() => Assemble(Line("health", "0")));
        Should.NotThrow(() => Assemble(Line("health", "255")));
        Should.NotThrow(() => Assemble(Line("attacker", "-32768")));
    }

    /// <summary>The refusal a bad value for one field produces.</summary>
    private static string Refuse(string field, string value) =>
        Should.Throw<InvalidDataException>(() => Assemble(Line(field, value))).Message;

    /// <summary>An event line whose named field carries the given text.</summary>
    /// <remarks>
    /// Built by RENDERING a valid event and substituting one value, so every other token is exactly
    /// what this project emits — the alternative is hand-writing a line and testing the parser
    /// against the same belief that wrote it.
    /// </remarks>
    private static string Line(string field, string value)
    {
        GameEventMessage valid = new(
            42,
            "player_hurt",
            new Dictionary<string, object?>
            {
                ["attacker"] = (short)1,
                ["health"] = (byte)2,
                ["damage"] = 3f,
            },
            0);

        string line = EventAssembly.WriteEvent(valid)[0];
        List<string> tokens = [.. line.Split(' ')];

        int at = tokens.FindIndex(
            token => token.StartsWith($"{field}=", StringComparison.Ordinal));

        // **The substitution has to have happened.** A field name that matches nothing leaves the
        // line valid, and the test then measures a well-formed line while claiming to measure a
        // malformed one.
        at.ShouldBeGreaterThanOrEqualTo(0, $"the rendered line has no '{field}=' token: {line}");
        tokens[at] = $"{field}={value}";

        return string.Join(' ', tokens);
    }

    /// <summary>Assembles one event line against a state that knows the definition.</summary>
    private static void Assemble(string line)
    {
        NetDecodeState state = new() { NetworkProtocol = Protocol };
        state.AddEventDefinitions([Definition]);

        MessageAssembly.Assemble(line, static () => null, new BitWriter(), state);
    }
}
