using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A pickup's spin, read through the production sampler at each of its authored frames (B60).</summary>
/// <remarks>
/// **The small ammo pack's once-a-turn pause is in the model, and the engine draws it.** Its `idle` has 31 frames at
/// 10 fps where every other pickup has 30, and the extra one is a copy of the FIRST: the spin bone does not move from
/// frame 0 to frame 1, then turns the same 360 degrees the medkit does over the remaining 29 intervals. `CalcPoseSingle`
/// samples <c>fFrame = cycle * (numframes - 1)</c> and blends the two frames either side (<c>bone_setup.cpp:915</c>),
/// and <c>Studio_CPS</c> runs a cycle at <c>fps / (numframes - 1)</c> (<c>:5545</c>), so the game holds the pack still
/// for one interval, a tenth of a second in each three-second turn, as this does. There is no frame-dropping left
/// for B60 to have blamed: the sampler is the engine's arithmetic.
/// </remarks>
public sealed class PickupSpinTests
{
    /// <summary>The bone the pickups' `idle` turns; the other two hold still.</summary>
    private const int SpinBone = 2;

    /// <summary>Below this a step is a held frame: the medkit's smallest step is 12 degrees.</summary>
    private const double Held = 0.01;

    [Test]
    [Category(TestCategories.MutationHeavy)]
    public void Idle_TheSmallAmmoPack_HoldsItsFirstFrameForOneIntervalThenTurnsAFullCircle()
    {
        PropModels.SkinnedModel pack = Load("models/items/ammopack_small.mdl");

        pack.Frames(0).ShouldBe(31);
        pack.Loops(0).ShouldBeTrue();
        pack.CyclesPerSecond(0).ShouldBe(10f / 30f, 1e-6f, "Studio_CPS: 10 fps over 30 intervals");

        double[] steps = Steps(pack);

        steps[0].ShouldBeLessThan(Held, "frame 1 is a copy of frame 0: the authored pause");
        steps.Skip(1).ShouldAllBe(step => step > 11.9, "every later interval turns");
        steps.Sum().ShouldBe(360.0, 0.05, "one full turn, over the 29 intervals that move");
    }

    [Test]
    [Category(TestCategories.MutationHeavy)]
    public void Idle_TheSmallMedkit_TurnsAFullCircleWithNoHeldFrame()
    {
        // The control: the same decoder and sampler on the pickup the owner called perfect.
        PropModels.SkinnedModel medkit = Load("models/items/medkit_small.mdl");

        medkit.Frames(0).ShouldBe(30);

        double[] steps = Steps(medkit);

        steps.ShouldAllBe(step => step > 11.9, "no interval holds");
        steps.Sum().ShouldBe(360.0, 0.05);
    }

    /// <summary>The spin bone's turn across each interval, sampled at the cycle that lands on each authored frame.</summary>
    private static double[] Steps(PropModels.SkinnedModel model)
    {
        int frames = model.Frames(0);
        float[] none = new float[model.PoseParameters.Count];

        // Each frame's cycle is `frame / (numframes - 1)`, and the last is a cycle of one, which wraps a loop to frame 0:
        // so the step into it is the turn from the last distinct frame back to where the turn began.
        List<(float X, float Y, float Z, float W)> rotations =
        [
            .. Enumerable.Range(0, frames).Select(frame =>
                model.Locals(0, frame / (float)(frames - 1), none).Single(bone => bone.Bone == SpinBone).Rotation),
        ];

        return [.. Enumerable.Range(1, frames - 1).Select(frame => Angle(rotations[frame - 1], rotations[frame]))];
    }

    private static PropModels.SkinnedModel Load(string model)
    {
        string root = GameInstall.Require();
        string map = Path.Combine(root, "maps", "cp_process_f12.bsp");

        if (!File.Exists(map))
        {
            Assert.Ignore("Team Fortress 2 with cp_process_f12 is not installed.");
        }

        MapAssets assets = MapAssets.Load(
            File.ReadAllBytes(map), SharedGame.Archives, maximumTextureSize: 16, entityModels: [model]);

        return assets.Geometry(model).ShouldNotBeNull(model).Skinned.ShouldNotBeNull(model);
    }

    /// <summary>The angle between two rotations, in degrees.</summary>
    private static double Angle((float X, float Y, float Z, float W) a, (float X, float Y, float Z, float W) b)
    {
        double dot = Math.Abs((a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z) + (a.W * b.W));

        return 2.0 * Math.Acos(Math.Min(1.0, dot)) * 180.0 / Math.PI;
    }
}
