using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What one moving model's <see cref="ModelLightAverage"/> draw costs, as the viewer's `lighting` column prints to 0.1 ms only (B676).</summary>
/// <remarks>
/// The worst case a draw can be: four lamps and the sun, sixteen comparisons, every one blended. **The control is the same loop with the
/// average off**, which hands the state back through <see cref="LightingState.Lighting"/>, so the difference is not the loop's.
/// </remarks>
public sealed class LightAverageCostProbe : IProbe
{
    private const int Draws = 2_000_000;

    /// <inheritdoc/>
    public string Name => "light-average-cost";

    /// <inheritdoc/>
    public string Summary => "ns and bytes per moving model's light average, four lamps and the sun: light-average-cost";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);

        LightingState state = new(new AmbientCube((1f, 1f, 1f), (1f, 1f, 1f), (1f, 1f, 1f), (1f, 1f, 1f), (1f, 1f, 1f), (1f, 1f, 1f)));

        for (int lamp = 0; lamp < LocalLights.MaximumLocalLights; lamp++)
        {
            state.Put(new BspWorldLight((lamp, 0f, 0f), (40f, 20f, 10f), default, WorldLightKind.Point, QuadraticAttenuation: 1f));
        }

        SunLight sun = new(2f, 2f, 2f, 0f, 0f, -1f);

        foreach (bool enabled in new[] { true, false, true, false })
        {
            ModelLightAverage average = new() { Enabled = enabled };
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();

            for (int draw = 0; draw < Draws; draw++)
            {
                _ = average.Draw(1, "model", state, state.Cube, sun, 0f, 0f, 0f, draw * 0.003d, draw & 1, null);
            }

            double seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;

            output.WriteLine($"average {(enabled ? "on " : "off")}: {seconds / Draws * 1e9:0.0} ns a draw, {(GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)Draws:0.0} bytes a draw");
        }
    }
}
