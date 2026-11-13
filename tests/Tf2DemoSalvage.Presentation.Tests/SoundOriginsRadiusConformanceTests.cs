using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>A model's radius as <c>GetModelRadius</c> answers it (<c>model_t +0x40</c>, engine.dll 0x1801cc9a9), B724.</summary>
public sealed class SoundOriginsRadiusConformanceTests
{
    [Test]
    public void StudioRadius_AHull_IsItsLargestComponentMagnitude()
    {
        // FUN_1801060b0: radius 0, then each of hull_min and hull_max's six components, taken when its magnitude is larger.
        SoundOrigins.StudioRadius(new StudioBox(-24f, -24f, 0f, 24f, 24f, 82f)).ShouldBe(82f);
        SoundOrigins.StudioRadius(new StudioBox(-30f, -5f, -2f, 4f, 5f, 2f)).ShouldBe(30f);
        SoundOrigins.StudioRadius(StudioBox.Empty).ShouldBe(0f);
    }

    private static readonly BspModel[] Brushes =
    [
        default,
        new((-2f, -3f, 0f), (1f, 2f, 11f), default, 0, 0, 0),
    ];

    /// <summary>`studiohdr_t`'s `hull_min`, then `hull_max` at +12 (engine.dll FUN_1801060b0 reads them at 0x68..0x7c).</summary>
    private const int HullMin = 0x68;

    /// <summary>A studio header whose hull is (−24, −24, 0)..(24, 24, 82): radius 82.</summary>
    private static byte[] Header()
    {
        byte[] bytes = new byte[HullMin + 24];
        float[] hull = [-24f, -24f, 0f, 24f, 24f, 82f];

        for (int at = 0; at < 6; at++)
        {
            BitConverter.GetBytes(hull[at]).CopyTo(bytes, HullMin + (at * 4));
        }

        return bytes;
    }

    [Test]
    public void For_AStudioModel_IsZeroUntilReadOffTheFrameThenItsRadiusAndReadOnce()
    {
        int reads = 0;
        List<Action> queued = [];
        Func<int, double, (float X, float Y, float Z, float Radius)?> lookup = SoundOrigins.For(
            static (_, _) => (new ScenePose { X = 1f }, "models/player/scout.mdl"),
            Brushes,
            _ => { reads++; return Header(); },
            queued.Add);

        lookup(5, 0)!.Value.Radius.ShouldBe(0f, "not read on the frame");
        lookup(5, 0)!.Value.Radius.ShouldBe(0f, "and asked once");
        queued.Count.ShouldBe(1);

        queued[0]();

        lookup(5, 0)!.Value.Radius.ShouldBe(82f);
        lookup(6, 0)!.Value.Radius.ShouldBe(82f, "another entity of the model");
        reads.ShouldBe(1);
    }

    [Test]
    public void For_ABrushModel_IsItsRadiusAtOnceWithItsCentreAdded()
    {
        Func<int, double, (float X, float Y, float Z, float Radius)?> lookup = SoundOrigins.For(
            static (_, _) => (new ScenePose { X = 10f }, "*1"), Brushes, static _ => null, static _ => { });

        (float x, _, _, float radius) = lookup(5, 0)!.Value;

        x.ShouldBe(9.5f);
        radius.ShouldBe(13f);
    }

    [Test]
    public void For_AModelThatCannotBeRead_IsNoRadius()
    {
        Func<int, double, (float X, float Y, float Z, float Radius)?> lookup = SoundOrigins.For(
            static (_, _) => (default(ScenePose), "models/missing.mdl"),
            Brushes,
            static _ => throw new IOException("locked"),
            static work => work());

        lookup(5, 0)!.Value.Radius.ShouldBe(0f);
        lookup(5, 0)!.Value.Radius.ShouldBe(0f, "kept, not retried into a throw");
    }

    [Test]
    public void For_NoPose_IsNull()
    {
        SoundOrigins.For(static (_, _) => null, Brushes, static _ => null, static _ => { })(5, 0).ShouldBeNull();
    }

    [Test]
    public void BrushRadius_ABrush_IsTheCornerOfItsWidenedBounds()
    {
        // Mod_LoadSubmodels (FUN_180104c80): mins − 1, maxs + 1, then per axis the larger magnitude: 3, 4 and 12, a length of 13.
        SoundOrigins.BrushRadius(new BspModel((-2f, -3f, 0f), (1f, 2f, 11f), default, 0, 0, 0)).ShouldBe(13f);
    }
}
