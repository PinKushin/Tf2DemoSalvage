using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Which shipped blend grids have a corner whose frame count differs from the sequence's own animation (B742).</summary>
/// <remarks>
/// The question and its engine citations are B742 in <c>docs/RISKS.md</c>; this is only the instrument.
///
/// <code>
///   grid-frames [dir.vpk]
/// </code>
///
/// Walks every <c>.mdl</c> in the archive and every sequence that blends, reading each grid corner's frame count with
/// <c>StudioAnimation.Frames</c> in the model's own file, the count production computes its one frame from.
/// **The control** is the corner count: zero means the walk read no grid.
/// </remarks>
public sealed class GridFrameProbe : IProbe
{
    private const int Listed = 25;

    /// <inheritdoc/>
    public string Name => "grid-frames";

    /// <inheritdoc/>
    public string Summary =>
        "every shipped blend grid with a corner whose frame count differs from its sequence's animation (B742): grid-frames [dir.vpk]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (ProbeArguments.Archive(arguments, output) is not (VpkArchive archive, string path))
        {
            return;
        }

        int grids = 0;
        int corners = 0;
        int differing = 0;
        List<string> listed = [];

        foreach (string model in archive.Paths.Where(entry => entry.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            if (archive.ReadFile(model) is not { } bytes)
            {
                continue;
            }

            IReadOnlyList<StudioSequence> declared = StudioSequences.Read(bytes);

            for (int sequence = 0; sequence < declared.Count; sequence++)
            {
                if (declared[sequence].Blend is not { Blends: true } grid)
                {
                    continue;
                }

                grids++;

                int main = StudioAnimation.Frames(bytes, declared[sequence].Animation);
                List<int> counts = [];

                for (int y = 0; y < grid.GroupY; y++)
                {
                    for (int x = 0; x < grid.GroupX; x++)
                    {
                        counts.Add(StudioAnimation.Frames(bytes, grid.Animation(x, y)));
                    }
                }

                corners += counts.Count;

                if (counts.All(count => count == main))
                {
                    continue;
                }

                differing++;

                if (listed.Count < Listed)
                {
                    listed.Add($"{model} #{sequence} {declared[sequence].Label}: main {main}, corners {string.Join(' ', counts)}");
                }
            }
        }

        output.WriteLine($"archive {path}");
        output.WriteLine($"blend grids {grids}, corners read {corners} (the control: nonzero)");
        output.WriteLine($"grids with a corner whose frame count differs from the sequence's animation: {differing}");

        foreach (string line in listed)
        {
            output.WriteLine($"  {line}");
        }
    }
}
