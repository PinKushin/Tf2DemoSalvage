using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// What `C_BaseAnimating::OnNewModel` reads of a model, from the installed game's files, for the build (B870).
/// </summary>
/// <param name="readFile">Opens a game file by path; null when absent.</param>
/// <remarks>
/// **The studio header alone, with what it includes**: the pose parameters merged across the base model and its
/// `$includemodel`s in group order (`StudioPoseParameterMerge`), `STUDIOHDR_FLAGS_STATIC_PROP`, and each merged sequence's
/// `STUDIO_LOOPING`. The same reads `PropModels` makes when it loads a model to draw, without the geometry. Cached per path:
/// a match sets a few hundred models tens of thousands of times.
/// </remarks>
public sealed class StudioModelFacts(Func<string, byte[]?> readFile) : IModelFacts
{
    private readonly ConcurrentDictionary<string, ModelFacts?> _facts = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public ModelFacts? FactsOf(string modelPath) => _facts.GetOrAdd(modelPath, Read);

    private ModelFacts? Read(string path)
    {
        try
        {
            return readFile(path) is { } root ? Read(root) : null;
        }
        catch (Exception failure) when (failure is InvalidDataException or IOException)
        {
            // Not a studio model the engine could load, or a file the archives could not read (`PropModels.Find` catches
            // the same pair), which is the no-header answer: `OnNewModel` returns before `SetMaxCount`
            // (`c_baseanimating.cpp:1042-1072`). One bad file must not stop the demo opening; the viewer reports the same
            // file when it loads it to draw.
            return null;
        }
    }

    private ModelFacts Read(byte[] root)
    {
        StudioModelInfo model = StudioModel.Read(root);

        List<byte[]> files = [root, .. StudioModelGroups.Read(root).Select(readFile).OfType<byte[]>()];
        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups =
            [.. files.Select((file, group) => (group, StudioSequences.Read(file)))];

        StudioSequenceTable table = StudioSequenceTable.Merge(groups);

        (IReadOnlyList<StudioPoseParameter> pose, _) =
            StudioPoseParameterMerge.Merge([.. files.Select(file => StudioSequences.PoseParameters(file))]);

        // One flag per merged sequence, so the cache holds a bool each rather than every parsed sequence of every include.
        bool[] looping = [.. Enumerable.Range(0, table.Count).Select(sequence => Looping(table, groups, sequence))];

        // `IsSequenceLooping( GetSequence() )` of the sequence the model PLAYS — 0 for one out of range
        // (`c_baseanimating.cpp:1967-1970`), as `PropModels.SkinnedModel.Played` answers it.
        return new ModelFacts(
            [.. pose.Select(parameter => parameter.Loop != 0f)],
            model.IsStaticProp,
            sequence => looping.Length > 0 && looping[sequence >= 0 && sequence < looping.Length ? sequence : 0]);
    }

    private static bool Looping(
        StudioSequenceTable table, List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups, int sequence) =>
        table.At(sequence) is { } where &&
        where.Group < groups.Count &&
        where.Local < groups[where.Group].Sequences.Count &&
        groups[where.Group].Sequences[where.Local].Loops;
}
