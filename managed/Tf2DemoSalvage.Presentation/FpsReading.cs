using System;
using System.Globalization;

namespace Tf2DemoSalvage.Presentation;

/// <summary>One frame's worth of TF2's frame rate meter.</summary>
/// <param name="Fps">The rate to show: the average when smoothed, this frame otherwise.</param>
/// <param name="Low">The worst single frame since the pair was seeded.</param>
/// <param name="High">The best single frame since the pair was seeded.</param>
/// <param name="FrameMilliseconds">How long this frame took, unsmoothed.</param>
/// <param name="Smoothed">Whether the meter is in mode two.</param>
/// <remarks>
/// Top level rather than nested inside <see cref="FpsMeter"/>, which is CA1034 — a nested public
/// type is harder to name and harder to use from outside.
/// </remarks>
public readonly record struct FpsReading(
    int Fps,
    int Low,
    int High,
    double FrameMilliseconds,
    bool Smoothed)
{
    /// <summary>The colour this reading is drawn in.</summary>
    public (byte Red, byte Green, byte Blue) Colour => FpsMeter.ColourFor(Fps);

    /// <summary>Renders the line TF2 would draw.</summary>
    /// <param name="levelName">The map, named as the engine names it.</param>
    /// <returns>The meter's text.</returns>
    /// <remarks>
    /// The two format strings verbatim — <c>"%3i fps on %s"</c> and
    /// <c>"%3i fps (%3i, %3i) %.1f ms on %s"</c>. The three-column alignment is what stops the line
    /// juddering sideways as the rate crosses 100, which is worth keeping for exactly the reason the
    /// meter exists.
    ///
    /// **The map keeps its extension**, because Valve passes
    /// <c>V_GetFileName( engine->GetLevelName() )</c> and <c>V_GetFileName</c> is
    /// <c>V_UnqualifiedFileName</c>: it strips the directory and keeps the rest.
    /// </remarks>
    public string Text(string levelName) => Smoothed
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"{Fps,3} fps ({Low,3}, {High,3}) {FrameMilliseconds:0.0} ms on {levelName}")
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{Fps,3} fps on {levelName}");
}
