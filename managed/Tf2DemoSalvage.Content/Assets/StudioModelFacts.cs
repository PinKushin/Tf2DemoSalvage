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
    public ModelFacts? FactsOf(string modelPath)
    {
        if (_facts.TryGetValue(modelPath, out ModelFacts? known))
        {
            return known;
        }

        try
        {
            return _facts.GetOrAdd(modelPath, readFile(modelPath) is { } root ? Read(root) : null);
        }
        catch (Exception failure) when (failure is InvalidDataException or IOException)
        {
            // Not a studio model this reader can load, or a file the archives could not read (`PropModels.Find` catches the
            // same pair): no facts, which the build treats as unknown for a `.mdl` (B873). One bad file must not stop the
            // demo opening; the viewer reports it when it loads it to draw. **Cached either way** (B870 review 4): an
            // uncached read failure let the whole timeline and a window walk of the same bytes disagree, and re-read a bad
            // archive entry on every update that set the model.
            return _facts.GetOrAdd(modelPath, value: null);
        }
    }

    private ModelFacts Read(byte[] root)
    {
        StudioModelInfo model = StudioModel.Read(root);

        List<byte[]> files = [root, .. StudioModelGroups.Read(root).Select(Include).OfType<byte[]>()];
        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups =
            [.. files.Select((file, group) => (group, StudioSequences.Read(file)))];

        StudioSequenceTable table = StudioSequenceTable.Merge(groups);

        (IReadOnlyList<StudioPoseParameter> pose, _) =
            StudioPoseParameterMerge.Merge([.. files.Select(file => StudioSequences.PoseParameters(file))]);

        return new ModelFacts(
            [.. pose.Select(parameter => parameter.Loop != 0f)], model.IsStaticProp, AsPlayed(LoopingFlags(table, groups)));
    }

    /// <summary>An `$includemodel`'s bytes, or null when it cannot be read — skipped as `PropModels.Find` skips it (B870 review 5).</summary>
    /// <remarks>
    /// One unreadable include drops that file and keeps the root and the others, as the draw path does; a failure here
    /// must not make the whole model "no facts" while the viewer draws it from the same files.
    /// </remarks>
    private byte[]? Include(string path)
    {
        try
        {
            return readFile(path);
        }
        catch (IOException)
        {
            // The draw path logs the same file when it loads the model (`PropModels.Find`); here it is one include fewer.
            return null;
        }
    }

    /// <summary>One flag per merged sequence, so the cache holds a bool each rather than every parsed sequence.</summary>
    /// <remarks>
    /// **Its own method so the returned delegate closes over the array alone** (B870 review 3, Sonnet): two lambdas in one
    /// scope share one closure object, which kept the table and every include's sequences alive per cached model.
    /// </remarks>
    internal static bool[] LoopingFlags(
        StudioSequenceTable table, List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups)
    {
        bool[] looping = new bool[table.Count];

        for (int sequence = 0; sequence < looping.Length; sequence++)
        {
            looping[sequence] = table.At(sequence) is { } where &&
                where.Group < groups.Count &&
                where.Local < groups[where.Group].Sequences.Count &&
                groups[where.Group].Sequences[where.Local].Loops;
        }

        return looping;
    }

    /// <summary>
    /// `IsSequenceLooping( GetSequence() )` of the sequence the model PLAYS — 0 for one out of range
    /// (`c_baseanimating.cpp:1967-1970`), as `PropModels.SkinnedModel.Played` answers it.
    /// </summary>
    internal static Func<int, bool> AsPlayed(bool[] looping) =>
        sequence => looping.Length > 0 && looping[sequence >= 0 && sequence < looping.Length ? sequence : 0];
}
