using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>hltv_chase</c>'s fields read whatever CLR type their definition gives them (B335).
/// </summary>
/// <remarks>
/// **19 of `DirectorShot`'s 33 branches were unreached**, and every one of them is an arm of the
/// type switch — which is the single defect class this project has shipped most often. The type's
/// own remarks name two: *"a dump annotation that matched nothing because `customkill` arrives as a
/// byte, and a kill feed whose numeric lookup was handed strings"*.
///
/// **A game event's fields are typed by their DEFINITION**, documented in the comment block at the
/// top of `game/mod_hl2mp/resource/modevents.res` — `short` is 16-bit signed, `byte` unsigned, and
/// so on. `hltv_chase` declares its targets as `short` and its parameters as `float`. So the same
/// field name is not always the same CLR type, and a reader testing one type silently returns the
/// FALLBACK for the rest — which here means the camera keeps the previous shot's angle and looks
/// like an interpolation choice rather than a dropped field.
///
/// **The existing suite reached only the `float` arm**, because it builds its events out of floats.
/// That is not a criticism of it — it was written to test the carry-forward rule and it does — but
/// it is why a whole switch sat uncovered inside a file that looked tested.
/// </remarks>
public sealed class DirectorShotFieldTypeTests
{
    /// <remarks>
    /// **Every arm, with a value only that arm can produce.** Each case uses a distinct number, so
    /// an arm falling through to another cannot be hidden by them agreeing.
    /// </remarks>
    [Test]
    public void From_AFieldOfEveryNumericType_ReadsItRatherThanFallingBack()
    {
        Distance(12f).ShouldBe(12f, "float, the type hltv_chase declares");
        Distance(13d).ShouldBe(13f, "double, narrowed");
        Distance(14).ShouldBe(14f, "int");
        Distance((short)15).ShouldBe(15f, "short, which is what the TARGETS arrive as");
        Distance((byte)16).ShouldBe(16f, "byte, the type that produced the customkill no-op");
        Distance("17.5").ShouldBe(17.5f, "a string, parsed invariantly");
    }

    /// <remarks>
    /// **A bool is 1 or 0, not true or false**, and `ineye` is the field that depends on it: the
    /// shot mode is read as a number and compared against zero.
    /// </remarks>
    [Test]
    public void From_ABooleanField_IsOneOrZero()
    {
        Shot(new Dictionary<string, object?> { ["ineye"] = true }).InEye.ShouldBeTrue();
        Shot(new Dictionary<string, object?> { ["ineye"] = false }).InEye.ShouldBeFalse();

        // And through the numeric arms, since the wire may deliver it as either.
        Shot(new Dictionary<string, object?> { ["ineye"] = (byte)1 }).InEye.ShouldBeTrue();
        Shot(new Dictionary<string, object?> { ["ineye"] = 0 }).InEye.ShouldBeFalse();
    }

    /// <remarks>
    /// **The three ways a field can be absent**, which must all reach the fallback and not throw:
    /// the key missing, the key present with a null value, and the key present with something that
    /// is not a number at all.
    ///
    /// The fallback is the PREVIOUS shot's value for a float, which is what makes an unread field
    /// invisible — the camera simply keeps its last angle.
    /// </remarks>
    [Test]
    public void From_AFieldThatIsAbsentNullOrUnreadable_KeepsThePreviousValue()
    {
        DirectorShot previous = DirectorShot.Default with { Distance = 200f };

        Shot(Nothing, previous).Distance.ShouldBe(200f, "the key is missing");

        Shot(new Dictionary<string, object?> { ["distance"] = null }, previous)
            .Distance.ShouldBe(200f, "the key is present and null");

        Shot(new Dictionary<string, object?> { ["distance"] = "not a number" }, previous)
            .Distance.ShouldBe(200f, "the string does not parse");

        Shot(new Dictionary<string, object?> { ["distance"] = new object() }, previous)
            .Distance.ShouldBe(200f, "and a type no arm matches");
    }

    /// <remarks>
    /// **A string is parsed with the INVARIANT culture**, which matters because a decimal point is
    /// a comma in much of the world. Parsing under the machine's culture would read `"17.5"` as
    /// 175 on a French install — a demo that plays differently depending on who opens it.
    /// </remarks>
    [Test]
    public void From_ADecimalStringUnderAnyCulture_ParsesOnTheDot()
    {
        Distance("0.5").ShouldBe(0.5f);

        // The control: the comma form is NOT a valid invariant decimal, so it must not silently
        // become 5 — it must fall back, which is the observable difference from culture parsing.
        Shot(
            new Dictionary<string, object?> { ["distance"] = "0,5" },
            DirectorShot.Default with { Distance = 77f })
            .Distance.ShouldBe(77f, "a comma is not an invariant decimal point");
    }

    /// <remarks>
    /// **The targets do NOT carry forward and the floats do**, which is the engine's own asymmetry:
    /// it reads the targets with a plain `GetInt` that answers zero for an absent field, while every
    /// float is read with an explicit default of the current value. An implementation that carried
    /// everything forward would keep pointing the camera at a player the director had released.
    /// </remarks>
    [Test]
    public void From_AnEventNamingNoTargets_ZeroesThemWhileKeepingTheAngles()
    {
        DirectorShot previous = DirectorShot.Default with
        {
            Target = 5,
            SecondTarget = 6,
            Theta = 1.25f,
        };

        DirectorShot next = Shot(Nothing, previous);

        next.Target.ShouldBe(0, "GetInt answers zero for a field the event omits");
        next.SecondTarget.ShouldBe(0);
        next.Theta.ShouldBe(1.25f, "while the floats keep the previous shot's value");
    }

    /// <remarks>
    /// A short is SIGNED, so a negative target must survive rather than wrapping to 65,000-odd.
    /// </remarks>
    [Test]
    public void From_ANegativeShortTarget_StaysNegative()
    {
        Shot(new Dictionary<string, object?> { ["target1"] = (short)-3 })
            .Target.ShouldBe(-3);
    }

    /// <summary>An event declaring nothing at all, which is a real case on the wire.</summary>
    private static readonly Dictionary<string, object?> Nothing = [];

    /// <summary>The distance a one-field event resolves to, from the default shot.</summary>
    private static float Distance(object value) =>
        Shot(new Dictionary<string, object?> { ["distance"] = value }).Distance;

    /// <summary>One shot built from the given fields.</summary>
    private static DirectorShot Shot(
        IReadOnlyDictionary<string, object?> values, DirectorShot? previous = null) =>
        DirectorShot.From(values, previous);
}
