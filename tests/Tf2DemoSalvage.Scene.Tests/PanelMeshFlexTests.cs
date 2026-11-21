using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>One panel model's flexed mesh (B779), on <see cref="PanelMeshFlexFixture"/>'s one-vertex flex.</summary>
public sealed class PanelMeshFlexTests
{
    private const string Path = "models/vgui/flexed.mdl";

    /// <summary>The fixture's rule-less model: descriptor 0 is the weight handed in.</summary>
    private static PropModels.ModelFrames Model(byte speed = 255) => PanelMeshFlexFixture.Flexed(
        new PropModels.ModelFrames([], new Dictionary<int, (int, int, float)>(), [], []) { Flex = new StudioFlexData(["jaw"], [], [], []) },
        speed: speed);

    /// <remarks>
    /// **A rebuild allocates nothing once the model is known** (round 1 review item 1): TfPlayerModelPanel's weights change
    /// every paint through the 0.95 decay, and a class model's stream is large-object sized. The world's face allocates
    /// nothing per rebuild either (findings 80, 92). Each call here changes the weight, so each rebuilds; least over eight,
    /// after a first that sizes the buffers. The control: the weight did change the face.
    /// </remarks>
    [Test]
    public void For_AWeightChangeOnAKnownModel_AllocatesNothing()
    {
        PanelMeshFlex flex = new();
        OrderCache cache = new(PanelMeshFlexFixture.Order);
        PropModels.ModelFrames model = Model();
        float[] weights = [0.1f];
        long least = long.MaxValue;

        flex.For(cache, Path, model, weights);

        for (int run = 0; run < 8; run++)
        {
            weights[0] = 0.2f + (run * 0.05f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            flex.For(cache, Path, model, weights);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        flex.For(cache, Path, model, weights).Face.ShouldNotBeNull()[0].ShouldBe(2f * weights[0], 1e-5f, "the control: it rebuilt");
        least.ShouldBe(0L);
    }

    /// <remarks>
    /// **A new packing rebuilds the face with the same weights** (round 1 review item 5): the set repacks a model as a new
    /// order array, and the face must follow it. [1, 0] puts the moved vertex 1 in slot 0; [0, 1] in slot 1.
    /// </remarks>
    [Test]
    public void For_TheOrderChangesWithUnchangedWeights_SpreadsOverTheNewOrder()
    {
        PanelMeshFlex flex = new();
        PropModels.ModelFrames model = Model();
        float[] weights = [0.25f];

        float[] first = flex.For(new OrderCache(PanelMeshFlexFixture.Order), Path, model, weights).Face.ShouldNotBeNull();
        first[0].ShouldBe(0.5f, 1e-5f, "the control: the first order puts it in slot 0");

        (float[]? face, FlexSlots? range) = flex.For(new OrderCache([0, 1]), Path, model, weights);

        face.ShouldNotBeNull().ShouldBe([0f, 0f, 0f, 0f, 0f, 0f, 0.5f, 0f, 0f, 0f, 0f, 0.25f], 1e-5f);
        range.ShouldBe(new FlexSlots(1, 1));
    }

    /// <remarks>**A model not yet packed draws unflexed, and does not throw** (round 1 review item 5): the cache answers no
    /// order until the HUD's precache packs it.</remarks>
    [Test]
    public void For_AModelNotYetPacked_DrawsUnflexed()
    {
        (float[]? face, FlexSlots? range) = new PanelMeshFlex().For(new OrderCache(null), Path, Model(), [0.25f]);

        face.ShouldBeNull();
        range.ShouldBeNull();
    }

    /// <remarks>
    /// **The delayed weights reach the mesh** (round 1 review item 7): `R_StudioFlexVerts` blends
    /// `(1 − speed)·delayed + speed·current` per vertex (studiorender `0x18001eb90`, finding 75). A speed byte of 0 reads
    /// the delayed weight alone: current 0.25 and delayed 0.1 move the vertex 0.2, where delayed taken as current gives 0.5.
    /// </remarks>
    [Test]
    public void For_DelayedWeightsAndASpeedOfZero_MovesByTheDelayedWeight()
    {
        float[] face = new PanelMeshFlex().For(new OrderCache(PanelMeshFlexFixture.Order), Path, Model(speed: 0), [0.25f], [0.1f])
            .Face.ShouldNotBeNull();

        face[0].ShouldBe(0.2f, 1e-5f);
    }

    /// <summary>A cache answering one packed order, or none, for every path.</summary>
    private sealed class OrderCache(IReadOnlyList<int>? order) : IMdlCache
    {
        public PropModels.ModelFrames? FindMdl(string path) => null;

        public IReadOnlyList<int>? PackedOrder(string path) => order;

        public int FindBodygroup(string modelPath, string group) => -1;

        public int SetBodygroup(string modelPath, int group, int value, int body) => body;
    }
}
