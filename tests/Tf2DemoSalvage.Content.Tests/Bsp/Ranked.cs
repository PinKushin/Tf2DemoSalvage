using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>The slotted lights of the one ranking, <see cref="LocalLights.Build"/>, as the shader takes them, for a test that asks only which lights.</summary>
/// <remarks>
/// The production `Strongest` was a second ranking (rank first, trace strongest-down) that nothing in production called; it was deleted
/// (B170) and its tests ask the one ranking through here. The lights come in slot order, the engine's: insertion, an evicted slot reused.
/// </remarks>
internal static class Ranked
{
    /// <summary>The local lights <see cref="LocalLights.Build"/> slots at a point.</summary>
    /// <param name="lights">The lights.</param>
    /// <param name="x">The point.</param>
    /// <param name="y">The point.</param>
    /// <param name="z">The point.</param>
    /// <param name="into">Room for <see cref="LocalLights.MaximumLocalLights"/>.</param>
    /// <param name="styleScale">Each style's value; null holds every style at one.</param>
    /// <param name="traceKeeps">The visibility trace by index; null traces none.</param>
    /// <returns>How many were written.</returns>
    internal static int Strongest(
        IReadOnlyList<BspWorldLight> lights, float x, float y, float z, Span<LocalLight> into, Func<int, float>? styleScale = null,
        Func<int, bool>? traceKeeps = null)
    {
        IReadOnlyList<LocalLight> locals = LocalLights.Build(lights, x, y, z, default, styleScale, traceKeeps).Lighting(styleScale).Locals;

        for (int at = 0; at < locals.Count; at++)
        {
            into[at] = locals[at];
        }

        return locals.Count;
    }
}
