using System;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What a loading display says and how full its bar is, for a stage and how far through it a load has got.</summary>
/// <remarks>
/// In the window's overlay until slice 9 of D214: a string and a clamp, written with plain values, which the domain
/// reach could not see. The overlay shows what these return.
/// </remarks>
public static class LoadingProgress
{
    /// <summary>The stage as the display words it: its name, an ellipsis, and a whole percentage when one is known.</summary>
    /// <param name="stage">What the load is doing, in words.</param>
    /// <param name="fraction">How far through it, or null when the stage cannot say.</param>
    /// <returns>The words.</returns>
    public static string Label(string stage, double? fraction) =>
        fraction is { } done
            ? $"{stage}… {done * 100d:0}%"
            : $"{stage}…";

    /// <summary>Where a bar of a given length stands for a fraction, which is held to between nothing and all of it.</summary>
    /// <param name="fraction">How far through, or null for a stage that cannot say, which shows an empty bar.</param>
    /// <param name="maximum">The bar's full length.</param>
    /// <returns>The bar's value.</returns>
    public static int Position(double? fraction, int maximum) =>
        (int)Math.Round(Math.Clamp(fraction ?? 0d, 0d, 1d) * maximum);
}
