using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What one walk of an item's attributes costs at a given list size (B612).</summary>
/// <remarks>
/// The walk and its citation are <c>EconAttributes.Iterate</c>'s; this times it. Each fallback entry is checked against the
/// whole local list, as the engine's wrapper does with <c>GetAttributeByID</c> (<c>econ_item_view.cpp:560-562</c>), so the
/// cost grows with local × fallback. Half the fallback definitions are in the local list. **The control** is the count of
/// attributes walked, which must equal local + fallback / 2.
///
/// <code>
///   attribute-walk
/// </code>
/// </remarks>
public sealed class AttributeWalkProbe : IProbe
{
    private const int Walks = 200_000;

    /// <inheritdoc/>
    public string Name => "attribute-walk";

    /// <inheritdoc/>
    public string Summary => "time one walk of an item's attributes at 5 to 80 per list (B612): attribute-walk";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);

        foreach (int size in new[] { 5, 10, 20, 40, 80 })
        {
            EconAttributeValue[] local = [.. Enumerable.Range(0, size).Select(index => new EconAttributeValue(index * 2, 0))];
            EconAttributeValue[] fallback = [.. Enumerable.Range(0, size).Select(index => new EconAttributeValue(index, 0))];
            ResolvedAttributes walk = EconAttributes.Iterate(local, [], hasValidItemId: false, fallback);

            int walked = Walk(walk);
            Stopwatch clock = Stopwatch.StartNew();
            long sum = 0;

            for (int pass = 0; pass < Walks; pass++)
            {
                sum += Walk(walk);
            }

            clock.Stop();

            double nanoseconds = clock.Elapsed.TotalMilliseconds * 1e6 / Walks;

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{size} local x {size} fallback: {nanoseconds:0.0} ns a walk, {walked} walked (control: {size + (size / 2)}), sum {sum}"));
        }
    }

    private static int Walk(ResolvedAttributes walk)
    {
        int count = 0;

        foreach (EconAttributeValue _ in walk)
        {
            count++;
        }

        return count;
    }
}
