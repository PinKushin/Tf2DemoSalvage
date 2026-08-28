using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// That a model loaded the way the viewer loads one carries the box the renderer sorts by.
/// </summary>
/// <remarks>
/// **The component tests cannot catch what this catches.** `StudioBounds` is asserted against a real
/// `.mdl` and passes whether or not the loader ever calls it; `OpaqueBuckets` is asserted against
/// hand-built boxes and passes whether or not a real model ever reaches it. Between them sits the
/// join that has failed three times in this project — decoded, retained, and read by nothing.
///
/// The failure would be silent and specific: an unset <see cref="ModelInstance.Bounds"/> is a
/// zero-sized box, every model buckets as the smallest, and the sort returns the input order. The
/// picture is unchanged, the suite is green, and the draw order is exactly what it was before the
/// work was done.
///
/// So this loads the scout through <see cref="MapCache"/> — the real `PropModels` path, real game
/// files — and predicts its bucket by name.
/// </remarks>
public sealed class ModelBoundsWiringTests
{
    private const string Scout = "models/player/scout.mdl";

    private static float[] Identity() =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];

    /// <summary>That the loader filled in the hull the file holds, to the float.</summary>
    /// <remarks>
    /// **The same six numbers `StudioBoundsTests` pins, arrived at by a different route.** That test
    /// reads `scout.mdl` off disk and calls `StudioBounds` directly; this one asks the viewer's own
    /// asset loader for the model it would draw. Agreement between two independent routes to one
    /// value is evidence rather than a restatement — see
    /// `docs/memory/two-recordings-of-one-value.md`.
    ///
    /// **No tolerance, for the reason recorded there.** A four-byte misread lands on a neighbouring
    /// float of similar magnitude, which is precisely what slack forgives.
    /// </remarks>
    [Test]
    public void RenderBoundsFor_TheScoutAsTheViewerLoadsIt_IsTheHullTheFileHolds()
    {
        MapAssets assets = MapCache.Load(entityModels: [Scout]);

        PropModels.ModelFrames scout =
            assets.Geometry(Scout).ShouldNotBeNull("the scout should have loaded");

        scout.RenderBoundsFor(0).ShouldBe(
            new StudioBox(
                MinX: -19.270922f,
                MinY: -16.550379f,
                MinZ: -3.506928f,
                MaxX: 6.600247f,
                MaxY: 16.550385f,
                MaxZ: 83.02696f));
    }

    /// <summary>That a real player model lands in the bucket Valve named for players.</summary>
    /// <remarks>
    /// **The prediction is arithmetic on the hull above, and it is checkable by hand.** The spans
    /// are 25.87 in X, 33.10 in Y and 86.53 in Z, so the longest axis is 86.53 — over Valve's
    /// `80.f // player size` threshold and under its `200.f // tree size`. Bucket 1.
    ///
    /// **It is not a tautology that it lands there.** 86.53 clears 80 by six units, so a hull read
    /// one component early, a box left at its default, or a vertex extent substituted for the
    /// authored hull all give a different bucket. The margin is small enough to be a real
    /// prediction and large enough not to be a boundary case.
    /// </remarks>
    [Test]
    public void BucketFor_TheScoutAsTheViewerLoadsIt_IsValvesPlayerSizeBucket()
    {
        MapAssets assets = MapCache.Load(entityModels: [Scout]);

        PropModels.ModelFrames scout =
            assets.Geometry(Scout).ShouldNotBeNull("the scout should have loaded");

        float longest = WorldSpaceBounds.LongestAxis(scout.RenderBoundsFor(0), Identity());

        longest.ShouldBe(86.534f, 0.001);
        OpaqueBuckets.BucketFor(longest).ShouldBe(1);
    }

    /// <summary>That the box survives the trip from the loaded model onto the drawn instance.</summary>
    /// <remarks>
    /// **The last hop, and the one with nothing else watching it.** `EntityModelSet.Instances` is
    /// what the renderer consumes, and a `Bounds` it never assigns is the zero box — which is not an
    /// error anywhere, just a sort that stops sorting.
    ///
    /// Two sequences with deliberately different boxes, so the assertion also pins that the PLAYING
    /// sequence is the one asked for. A single box would pass against an implementation that always
    /// answered sequence zero.
    /// </remarks>
    [Test]
    public void Bounds_ForAnInstancePlayingASequence_AreThatSequencesBox()
    {
        StudioBox standing = new(-10f, -10f, 0f, 10f, 10f, 80f);
        StudioBox crouching = new(-10f, -10f, 0f, 10f, 10f, 40f);

        PropModels.ModelFrames Frames(string path) =>
            new(
                [[
                    new PropVertex(0f, 0f, 0f, 0f, 0f, 0),
                    new PropVertex(1f, 0f, 0f, 1f, 0f, 0),
                    new PropVertex(1f, 1f, 0f, 1f, 1f, 0),
                ]],
                new Dictionary<int, (int, int, float)>(),
                [0, 1],
                [false, false],
                BoundsBySequence: [standing, crouching]);

        SceneProp[] props =
        [
            new(
                1,
                "models/player/scout.mdl",
                SceneModelKind.Studio,
                new ScenePose { Scale = 1f, Sequence = 1 },
                null),
        ];

        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        models.Add(props, Frames);
        models.Instances(props, instances);

        instances.Count.ShouldBe(1);
        instances[0].Bounds.ShouldBe(crouching);
    }
}
