using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The smallest model that draws, for tests about everything except geometry.
/// </summary>
/// <remarks>
/// **Three files had a byte-identical copy of this** — `EntityModelsTests`, `MomentSceneTests` and
/// the fade wiring — which is the second occurrence the DRY rule names. It is one triangle because
/// the cases that use it are about placement, alpha and upload counts: with no geometry at all the
/// set never grows, so "did not upload" and "had nothing to upload" become the same observation.
/// </remarks>
public static class ModelFramesFixture
{
    /// <summary>
    /// One triangle with a real render box, so a cull or a sub-view's collation has something to test: a model with no
    /// bounds is unplaced and only the main view collates it (`docs/memory/an-empty-box-must-never-cull.md`).
    /// </summary>
    /// <param name="path">Ignored; the parameter exists so this matches the loader delegate.</param>
    /// <returns>Frames with a 32 by 32 by 64 box.</returns>
    public static PropModels.ModelFrames BoxedTriangle(string path) =>
        OneTriangle(path) with { HeaderBounds = new StudioBox(-16f, -16f, 0f, 16f, 16f, 64f) };

    /// <summary>One triangle, one frame, one material.</summary>
    /// <param name="path">Ignored; the parameter exists so this matches the loader delegate.</param>
    /// <returns>Frames a model set will accept and draw.</returns>
    public static PropModels.ModelFrames OneTriangle(string path) =>
        new(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
            [0],
            [true]);

    /// <summary>One triangle skinned to one bone, its one sequence looping once a second over 31 frames.</summary>
    /// <param name="path">Ignored; the parameter exists so this matches the loader delegate.</param>
    /// <returns>Frames whose client-side cycle advances with demo time.</returns>
    public static PropModels.ModelFrames OneSecondLoop(string path) => OneSecond(path, SequenceLooping);

    /// <summary><see cref="OneSecondLoop"/> with no sequence flag: a one-shot of 31 frames, held at its end.</summary>
    /// <param name="path">Ignored; the parameter exists so this matches the loader delegate.</param>
    /// <returns>Frames whose sequences do not carry <c>STUDIO_LOOPING</c>.</returns>
    public static PropModels.ModelFrames OneSecondOneShot(string path) => OneSecond(path, 0);

    private static PropModels.ModelFrames OneSecond(string path, int flags)
    {
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithBones("root");

        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> flagged = [];

        foreach ((int group, IReadOnlyList<StudioSequence> sequences) in model.Groups)
        {
            List<StudioSequence> marked = [];

            foreach (StudioSequence sequence in sequences)
            {
                marked.Add(sequence with { Flags = flags });
            }

            flagged.Add((group, marked));
        }

        return OneTriangle(path) with
        {
            Skinned = model with { Models = [AnimatedStudioBytes.OneSecondLoop(animations: 3)], Groups = flagged },
        };
    }

    /// <summary><see cref="OneSecondLoop"/> with every sequence also <c>STUDIO_REALTIME</c>: posed off the clock (B309).</summary>
    /// <param name="path">Ignored; the parameter exists so this matches the loader delegate.</param>
    /// <returns>Frames whose drawn cycle is <c>seconds × 1</c>, fractional, whatever the entity's cycle.</returns>
    public static PropModels.ModelFrames RealtimeLoop(string path)
    {
        PropModels.ModelFrames frames = OneSecondLoop(path);
        PropModels.SkinnedModel skinned = frames.Skinned ?? throw new System.InvalidOperationException("the loop is skinned");
        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> realtime = [];

        foreach ((int group, IReadOnlyList<StudioSequence> sequences) in skinned.Groups)
        {
            List<StudioSequence> marked = [];

            foreach (StudioSequence sequence in sequences)
            {
                marked.Add(sequence with { Flags = sequence.Flags | SequenceRealtime });
            }

            realtime.Add((group, marked));
        }

        return frames with { Skinned = skinned with { Groups = realtime } };
    }

    /// <summary><c>STUDIO_LOOPING</c>, <c>studio.h</c>.</summary>
    private const int SequenceLooping = 0x0001;

    /// <summary><c>STUDIO_REALTIME</c>, <c>studio.h:3086</c>.</summary>
    private const int SequenceRealtime = 0x0100;
}
