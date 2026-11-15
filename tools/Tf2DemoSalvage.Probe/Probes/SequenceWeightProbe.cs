using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Which shipped sequences weigh a bone anything but one, split by the delta flag (B740).</summary>
/// <remarks>
/// The question and its engine citations are B740 in <c>docs/RISKS.md</c>; this is only the instrument.
///
/// <code>
///   sequence-weights [dir.vpk]
/// </code>
///
/// Walks every <c>.mdl</c> in the archive and every sequence it declares, reading its weight list with
/// <c>StudioGestureWeights.ForSequence</c> at the model's own bone count, as <c>SkinnedModel.BoneWeights</c> does.
/// **The control** is the total weight count: zero means the reader read nothing.
/// </remarks>
public sealed class SequenceWeightProbe : IProbe
{
    private const int Listed = 25;

    /// <inheritdoc/>
    public string Name => "sequence-weights";

    /// <inheritdoc/>
    public string Summary =>
        "every shipped sequence whose per-bone weights are not all one, delta or not (B740): sequence-weights [dir.vpk]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        string? folder = new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder();
        string? path = folder is null ? null : Path.Combine(folder, "tf2_misc_dir.vpk");

        if (arguments.Count > 0)
        {
            path = arguments[0];
        }

        if (path is null || !File.Exists(path))
        {
            output.WriteLine($"No archive: {path ?? "the game folder could not be found"}");
            return;
        }

        VpkArchive archive = VpkArchive.Open(path);
        long weights = 0;
        int sequences = 0;
        int partialPose = 0;
        int partialDelta = 0;
        List<string> poses = [];

        foreach (string model in archive.Paths.Where(entry => entry.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            if (archive.ReadFile(model) is not { } bytes)
            {
                continue;
            }

            int bones = StudioBones.Read(bytes).Count;
            IReadOnlyList<StudioSequence> declared = StudioSequences.Read(bytes);

            for (int sequence = 0; sequence < declared.Count; sequence++)
            {
                float[] own = StudioGestureWeights.ForSequence(bytes, sequence, bones);

                sequences++;
                weights += own.Length;

                // Exactly one: `SlerpBones` at s2 = 1 is a copy, anything else is not.
                if (own.All(weight => weight is >= 1f and <= 1f))
                {
                    continue;
                }

                bool delta = (declared[sequence].Flags & StudioFlags.SequenceDelta) != 0;

                if (delta)
                {
                    partialDelta++;
                    continue;
                }

                partialPose++;

                if (poses.Count < Listed)
                {
                    poses.Add($"{model} #{sequence} {declared[sequence].Label}: {own.Count(weight => weight <= 0f)} at or below zero," +
                        $"{own.Count(weight => weight is > 0f and < 1f)} partial of {own.Length}");
                }
            }
        }

        output.WriteLine($"archive {path}");
        output.WriteLine($"sequences {sequences}, weights read {weights} (the control: nonzero)");
        output.WriteLine($"not all one: {partialPose} non-delta, {partialDelta} delta");

        foreach (string line in poses)
        {
            output.WriteLine($"  {line}");
        }
    }
}
