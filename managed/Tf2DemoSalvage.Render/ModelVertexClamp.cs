using System;
using System.Globalization;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// A development control (<c>TF2VIEW_MODEL_VERTS</c>) that caps how many vertices each model draw submits while keeping
/// every draw, bind and state change (B597). The GPU time a pass loses under a cap of 3 is vertex work; the time it
/// keeps is per-draw cost. It changes the picture by design, so it is never a setting and is read once at start-up.
/// </summary>
public static class ModelVertexClamp
{
    /// <summary>The environment variable that holds the cap.</summary>
    public const string Variable = "TF2VIEW_MODEL_VERTS";

    /// <summary>Reads the cap from <paramref name="text"/>: a whole number of at least zero, otherwise off.</summary>
    /// <param name="text">The variable's value, or null when it is unset.</param>
    /// <returns>The cap, or null for off.</returns>
    public static int? Parse(string? text) =>
        int.TryParse(
            text,
            NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
            CultureInfo.InvariantCulture,
            out int cap)
            ? cap
            : null;

    /// <summary>The vertex count to submit: <paramref name="count"/> when off, else no more than the cap.</summary>
    /// <param name="count">The batch's own vertex count.</param>
    /// <param name="cap">The cap, or null for off.</param>
    /// <returns>The count to draw.</returns>
    public static int Apply(int count, int? cap) => cap is { } limit ? Math.Min(count, limit) : count;

    /// <summary>
    /// Whether a draw is skipped outright: only under a cap of zero. Every other cap, and none, issues every draw, an empty
    /// batch included as the default does, so a capped run counts the same draws as an uncapped one.
    /// </summary>
    /// <param name="cap">The cap, or null for off.</param>
    /// <returns>True for a cap of zero.</returns>
    public static bool Suppresses(int? cap) => cap == 0;
}
