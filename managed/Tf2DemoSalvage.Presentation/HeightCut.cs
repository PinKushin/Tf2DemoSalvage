using System;
using System.Globalization;

namespace Tf2DemoSalvage.Presentation;

/// <summary>How much of a map's height is hidden, as a fraction from the top.</summary>
/// <param name="Fraction">Zero for the whole map, up to <see cref="Deepest"/>.</param>
/// <remarks>
/// **This was two lines of arithmetic and two status strings inside `MainForm.ProcessCmdKey`**
/// (B188, D90). Which key does it is the window's business; how far a press moves the cut, where it
/// stops, and what to tell the user are not.
///
/// **A value rather than a mutable setting**, so a press produces a new cut instead of editing one
/// in place. The window holds the current value and replaces it — which is what `_heightCut` already
/// was, and makes the clamping impossible to skip at a call site.
/// </remarks>
public readonly record struct HeightCut(float Fraction)
{
    /// <summary>How much one press moves the cut.</summary>
    /// <remarks>
    /// Two percent, so crossing a map's full height takes about fifty presses and a held key sweeps
    /// smoothly rather than jumping floor to roof.
    /// </remarks>
    public const float Step = 0.02f;

    /// <summary>The deepest the cut goes.</summary>
    /// <remarks>
    /// **Never 1.0, and the limit is the point rather than a rounding.** Cutting the whole map
    /// leaves nothing drawn, which on screen is indistinguishable from a map that failed to load —
    /// so the control would appear to break the viewer.
    /// </remarks>
    public const float Deepest = 0.95f;

    /// <summary>Nothing cut: the whole map.</summary>
    public static HeightCut None => new(0f);

    /// <summary>Cut one step deeper.</summary>
    /// <returns>The new cut.</returns>
    public HeightCut Deeper() => new(Math.Clamp(Fraction + Step, 0f, Deepest));

    /// <summary>Restore one step.</summary>
    /// <returns>The new cut.</returns>
    public HeightCut Shallower() => new(Math.Clamp(Fraction - Step, 0f, Deepest));

    /// <summary>What to show in the status line.</summary>
    /// <returns>The line.</returns>
    /// <remarks>
    /// **Names what is LEFT rather than what was cut**, because that is what the user can see.
    /// Saying "5% cut" while 95% of the map is on screen sends them looking for the 5%.
    /// </remarks>
    public string Describe() => Fraction > 0f
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"Showing the lower {1f - Fraction:P0} of the map. Page Down cuts deeper, Page Up or Home restores it.")
        : "Showing the whole map.";
}
