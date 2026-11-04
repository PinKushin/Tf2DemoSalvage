using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What one moving model's <see cref="ModelLightAverage"/> draw costs, as the viewer's `lighting` column prints to 0.1 ms only (B676).</summary>
/// <remarks>
/// Three shapes, each through a real style delegate and run twice so the second pass is past tiered JIT. **All matched**: four lamps and the
/// sun, the same each draw, five first-match compares. **None matched**: four lamps and the sun, the lamps alternating with four others, which is
/// the most comparisons a draw can make (4 x 5 on the lamps and 5 on the sun, up to 25) and five blends. **Left**: one lamp in the state and
/// three alternating lamps stored behind it, so left lights are written each draw. **The control is the average off**, which hands the caller's list back.
/// </remarks>
public sealed class LightAverageCostProbe : IProbe
{
    private const int Draws = 2_000_000;

    /// <inheritdoc/>
    public string Name => "light-average-cost";

    /// <inheritdoc/>
    public string Summary => "ns and bytes per moving model's light average, in its best and worst shapes: light-average-cost";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);

        LightingState[] a = [Lamps(0, 4)];
        LightingState[] alternating = [Lamps(0, 4), Lamps(10, 4)];
        LightingState[] left = [Lamps(0, 3), Lamps(0, 1)];
        SunLight sun = new(2f, 2f, 2f, 0f, 0f, -1f);

        foreach ((string name, LightingState[] states, bool enabled) in new[]
                 {
                     ("all matched", a, true), ("none matched", alternating, true), ("left", left, true), ("off", a, false),
                     ("all matched", a, true), ("none matched", alternating, true), ("left", left, true), ("off", a, false),
                 })
        {
            ModelLightAverage average = new() { Enabled = enabled };
            IReadOnlyList<LocalLight> locals = [];
            Func<int, float> style = static value => value == 0 ? 1f : 0.5f;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();

            for (int draw = 0; draw < Draws; draw++)
            {
                LightingState state = states[draw % states.Length];

                _ = average.Draw(1, "model", state, state.Cube, sun, locals, 0f, 0f, 0f, draw * 0.003d, draw & 1, style);
            }

            double seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;

            output.WriteLine($"{name}: {seconds / Draws * 1e9:0.0} ns a draw, {(GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)Draws:0.0} bytes a draw");
        }
    }

    private static LightingState Lamps(int first, int count)
    {
        LightingState state = new(new AmbientCube((1f, 1f, 1f), (1f, 1f, 1f), (1f, 1f, 1f), (1f, 1f, 1f), (1f, 1f, 1f), (1f, 1f, 1f)));

        for (int lamp = first; lamp < first + count; lamp++)
        {
            state.Put(new BspWorldLight((lamp, 0f, 0f), (40f, 20f, 10f), default, WorldLightKind.Point, QuadraticAttenuation: 1f, LumpIndex: lamp));
        }

        return state;
    }
}
