using System;
using System.Globalization;

namespace Tf2DemoSalvage.Presentation;

/// <summary>How fast a demo plays, and which way.</summary>
/// <param name="Speed">The multiplier; negative runs backwards.</param>
/// <remarks>
/// **Continuous from 0.01 to 8, mirrored into the negatives** (D97). Two of the three differences
/// from Valve here are deliberate and one was an accident, and only the owner could tell them apart:
///
/// | | Valve | ours | status |
/// |---|---|---|---|
/// | slowest | `TIMESCALE_MIN 0.01f` | 0.01 | matched — ours was 0.25, unintentionally |
/// | fastest | `TIMESCALE_MAX 3.0f` | 8 | deliberate |
/// | shape | slider, 10,001 positions | continuous | matched — ours was 11 fixed steps |
/// | reverse | none | yes | deliberate |
///
/// Constants read from `game/client/replay/vgui/replayperformanceeditor.cpp:78,79,83`;
/// `TimeScaleConformanceTests` carries the citations and was written before this type existed.
///
/// **The floor mattered more than the shape.** Our slowest was 0.25, so the whole band Valve offers
/// between 0.01 and 0.25 — twenty-five times finer than anything we had — could not be reached at
/// all, and that band is exactly what frame-exact review needs
/// (`docs/memory/surf-and-jump-are-an-audience.md`).
///
/// **This lives in Presentation because the owner caught it going into the view.** The ladder, the
/// clamping and the spoken description were all inside `TransportBar`, a WinForms control, so
/// building a slider there would have *added* a range, a clamp and a position mapping to a view —
/// the opposite of D90. His words: *"this fix touched the 3d viewer doesnt it?"* The widget keeps the
/// widget; the quantity lives here.
///
/// **`PlaybackClock.TimeScale` was already a `double`**, so nothing below Presentation changes. The
/// model was continuous all along and the view was quantising it.
/// </remarks>
public readonly record struct TimeScale(double Speed)
{
    /// <summary>The slowest forward speed: Valve's `TIMESCALE_MIN`.</summary>
    public const double Slowest = 0.01d;

    /// <summary>The fastest speed. Valve stops at 3; the extra is a deliberate departure.</summary>
    /// <remarks>
    /// Skimming a forty-minute match wants more than three times speed, and nothing in the engine's
    /// reasoning for its ceiling applies to a viewer that has already decoded the whole demo.
    /// </remarks>
    public const double Fastest = 8d;

    /// <summary>Normal speed, forwards.</summary>
    public static TimeScale Normal => new(1d);

    /// <summary>How many positions a slider offers in one direction.</summary>
    /// <remarks>
    /// **Chosen so the step is no coarser than Valve's over Valve's own span.** The engine's slider
    /// is 10,001 integer positions across `[0.01, 3.0]`, a step of about 0.000299. Ours spans
    /// `[0.01, 8]`, which is 2.67 times wider, so it needs at least 26,722 positions to match that
    /// density; 30,000 is the round number above it.
    ///
    /// **Density rather than count is the thing to match.** A slider with Valve's 10,000 positions
    /// spread over our wider range would be coarser than Valve's while looking equal, which is the
    /// comparison `TimeScaleConformanceTests` refuses by asserting over Valve's span rather than ours.
    /// </remarks>
    public const int Positions = 30_000;

    /// <summary>The finest difference a slider position can express.</summary>
    public static double SmallestStep => (Fastest - Slowest) / Positions;

    /// <summary>Whether the demo runs backwards at this speed.</summary>
    public bool IsReverse => Speed < 0d;

    /// <summary>A speed, clamped into the range and away from the dead band around zero.</summary>
    /// <param name="speed">The wanted multiplier.</param>
    /// <returns>The nearest speed this viewer offers.</returns>
    /// <remarks>
    /// **Zero is not a speed, it is the pause button.** The range excludes it from both sides, so a
    /// value inside the dead band resolves to the slowest motion in the direction it was heading
    /// rather than to a stop nothing asked for. Exactly zero is taken as forwards.
    /// </remarks>
    public static TimeScale From(double speed)
    {
        if (double.IsNaN(speed))
        {
            return Normal;
        }

        double magnitude = Math.Clamp(Math.Abs(speed), Slowest, Fastest);

        return new TimeScale(speed < 0d ? -magnitude : magnitude);
    }

    /// <summary>The speed at a slider position. The sign is the direction.</summary>
    /// <param name="position">Minus <see cref="Positions"/> to plus <see cref="Positions"/>.</param>
    /// <returns>The speed that position selects.</returns>
    /// <remarks>
    /// **One slider through zero rather than a magnitude and a separate direction**, at the owner's
    /// request: drag left to run backwards, right to run forwards, and reverse stops being a mode to
    /// find. It also makes the control honest about the range, which really is
    /// <c>[-8, -0.01] ∪ [0.01, 8]</c>.
    ///
    /// **The CENTRE is the slowest speed, not a stop**, which is the one thing about this that
    /// surprises. Zero is not in the range — stopping is what the play button is for — so the middle
    /// of the travel is 0.01 in whichever direction was last chosen, and either side accelerates away
    /// from it. A centre that meant "stopped" would be a second pause control disagreeing with the
    /// first.
    ///
    /// **Linear over the range, as Valve's is** (`replayperformanceeditor.cpp:567,669,724`). A
    /// logarithmic mapping would give the slow band more of the slider and is not what the engine
    /// does; matching the shape keeps a person's muscle memory from TF2 worth something. It does mean
    /// 1× sits about a tenth of the way along each half, exactly as it sits a third of the way along
    /// Valve's narrower one.
    /// </remarks>
    public static TimeScale At(int position)
    {
        int clamped = Math.Clamp(position, -Positions, Positions);
        double fraction = Math.Abs(clamped) / (double)Positions;
        double speed = Slowest + ((Fastest - Slowest) * fraction);

        return new TimeScale(clamped < 0 ? -speed : speed);
    }

    /// <summary>Where this speed sits on a slider, signed by direction.</summary>
    /// <returns>Minus <see cref="Positions"/> to plus <see cref="Positions"/>.</returns>
    public int Position()
    {
        int magnitude =
            (int)Math.Round((Math.Abs(Speed) - Slowest) / (Fastest - Slowest) * Positions);

        return IsReverse ? -magnitude : magnitude;
    }

    /// <summary>The speeds the shuttle buttons step between.</summary>
    /// <remarks>
    /// **Stops on a continuous range, not the range itself** — which is the distinction D97 turned
    /// on. They were the whole vocabulary before: eleven `double`s in a WinForms control, and any
    /// speed not among them was unreachable. Now they are somewhere convenient to land, and the band
    /// between them exists.
    ///
    /// **Negative speeds are real here and impossible in TF2.** The engine streams a demo forward
    /// and each snapshot is a delta on the last, so it has nothing to step back into. This viewer
    /// decodes the whole demo to absolute positions first, so reverse costs what forward costs.
    ///
    /// The ladder is the one a video editor uses — halves and doubles either side of one — and it
    /// omits zero, because stopping is what the play button is for.
    ///
    /// **They stop at 0.25 rather than reaching down to <see cref="Slowest"/>**, deliberately: a
    /// button that needs seven presses to cross the range is a worse button. Reaching the fine band
    /// is a slider's job, and adding one is a visible change to the transport bar rather than
    /// something to slip in.
    /// </remarks>
    public static readonly double[] ShuttleStops =
        [-4, -2, -1, -0.5, -0.25, 0.25, 0.5, 1, 2, 4, 8];

    /// <summary>The speed as it is written on screen.</summary>
    /// <returns>A short label, such as <c>0.25x</c>.</returns>
    /// <remarks>
    /// **Two decimals, because the floor is 0.01** — one would render every speed in the band this
    /// change exists to reach as `0.0x`, which is the quantising the ladder used to do, moved into
    /// the label.
    /// </remarks>
    public string Label() =>
        string.Create(CultureInfo.InvariantCulture, $"{Speed:0.##}x");

    /// <summary>The speed as a screen reader says it.</summary>
    /// <returns>A spoken phrase, such as <c>speed 2 times, reversed</c>.</returns>
    /// <remarks>
    /// **Was `TransportBar.SpeedDescription`, a static on a WinForms control** (D97, D90). It is a
    /// formatting rule about a quantity, so it belongs with the quantity — and putting it here means
    /// a second frontend gets the same wording for free rather than reinventing it.
    ///
    /// **Exposed rather than written out by hand in a test.** A test that spells the phrase out is
    /// asserting on a string constant it also owns, which passes until someone rewords the label and
    /// then fails without anything being wrong. Worse, once: the wording a test was written against
    /// came from a hand-written literal in a constructor that no update ever reproduced, so the test
    /// failed against a bar that was working.
    /// </remarks>
    public string Description() =>
        Speed < 0d
            ? string.Create(CultureInfo.InvariantCulture, $"speed {-Speed:0.##} times, reversed")
            : string.Create(CultureInfo.InvariantCulture, $"speed {Speed:0.##} times");
}
