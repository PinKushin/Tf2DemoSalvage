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

    /// <summary>`STUDIO_WORLD`, `studio.h:3092`.</summary>
    private const int StudioWorld = 0x4000;

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

        if (ProbeArguments.Archive(arguments, output) is not (VpkArchive archive, string path))
        {
            return;
        }

        long weights = 0;
        int sequences = 0;
        int partialPose = 0;
        int partialDelta = 0;
        int aboveOne = 0;
        int worlds = 0;
        List<string> poses = [];
        List<string> worldSpace = [];

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
                aboveOne += own.Any(weight => weight > 1f) ? 1 : 0;

                // `STUDIO_WORLD` (`studio.h:3092`), which `SlerpBones` hands to `WorldSpaceSlerp` (`bone_setup.cpp:1391`).
                if ((declared[sequence].Flags & StudioWorld) != 0 && worldSpace.Count < Listed)
                {
                    worldSpace.Add($"{model} #{sequence} {declared[sequence].Label}");
                }

                worlds += (declared[sequence].Flags & StudioWorld) != 0 ? 1 : 0;

                bool delta = (declared[sequence].Flags & StudioFlags.SequenceDelta) != 0;

                // Production's own rule, with no bone map: read in its own file, every bone is the model's own, so this
                // is an upper bound on what an includer's map would leave composed.
                if (PropModels.SkinnedModel.IsCopy(own, null, delta: false))
                {
                    continue;
                }

                if (delta)
                {
                    partialDelta++;
                    continue;
                }

                partialPose++;

                if (poses.Count < Listed)
                {
                    poses.Add($"{model} #{sequence} {declared[sequence].Label}: {own.Count(weight => weight <= 0f)} at or below zero, " +
                        $"{own.Count(weight => weight is > 0f and < 1f)} partial, " +
                        $"{own.Count(weight => weight > 1f)} above one, of {own.Length}");
                }
            }
        }

        output.WriteLine($"archive {path}");
        output.WriteLine($"sequences {sequences}, weights read {weights} (the control: nonzero)");
        output.WriteLine($"not a copy by production's rule (no bone map, an upper bound): {partialPose} non-delta, {partialDelta} delta");
        output.WriteLine($"sequences weighing some bone above one: {aboveOne}");
        output.WriteLine($"STUDIO_WORLD sequences: {worlds}");

        foreach (string line in worldSpace)
        {
            output.WriteLine($"  world: {line}");
        }

        foreach (string line in poses)
        {
            output.WriteLine($"  {line}");
        }
    }
}
