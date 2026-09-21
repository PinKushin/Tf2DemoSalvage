using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A server `Impact` dispatch on the world, as `ImpactCallback` → `Impact` → `AddBrushModelDecal` takes it
/// (`tf_fx_impacts.cpp`, `fx_impact.cpp:102`) (B415).
/// </summary>
public sealed class ServerImpactsConformanceTests
{
    /// <summary>A wall at x = 100 facing −X, texinfo 7.</summary>
    private static readonly Func<(float X, float Y, float Z), (float X, float Y, float Z), BspTrace> Wall =
        static (from, to) => from.X < 100f && to.X >= 100f
            ? new BspTrace((100f - from.X) / (to.X - from.X), 7, (-1f, 0f, 0f), false, -100f)
            : new BspTrace(1f, -1, default, false);

    [Test]
    public void From_AnImpactOnTheWorld_IsAServerImpactOnTheTracedSurface()
    {
        List<ShotImpact> impacts = [];

        ServerImpacts.From([Impact(entity: 0)], Names, Wall, impacts);

        ShotImpact impact = impacts[0];

        impact.FromServer.ShouldBeTrue();
        impact.Shot.ShouldBe(-1, "−1 − its index in the feed");
        impact.End.ShouldBe((100f, 0f, 0f), "the decal's centre is m_vOrigin");
        impact.Texinfo.ShouldBe(7);
        impact.Normal.ShouldBe((-1f, 0f, 0f));
        impact.SurfaceProp.ShouldBe(12);
    }

    [Test]
    public void From_AnImpactOnAnEntity_IsNotAWorldImpact()
    {
        List<ShotImpact> impacts = [];

        ServerImpacts.From([Impact(entity: 3)], Names, Wall, impacts);

        impacts.ShouldBeEmpty();
    }

    [Test]
    public void From_AnImpactWhoseDecalTraceMissesTheWorld_IsNothing()
    {
        // `ClipRayToEntity` from m_vStart through 8 units past m_vOrigin, bloated by 1.1: nothing there, no decal, and
        // `Impact` returns false, so no effects either.
        List<ShotImpact> impacts = [];

        ServerImpacts.From([Impact(entity: 0) with { Origin = (50f, 0f, 0f) }], Names, Wall, impacts);

        impacts.ShouldBeEmpty();
    }

    [Test]
    public void From_AnotherEffect_IsNotAnImpact()
    {
        List<ShotImpact> impacts = [];

        ServerImpacts.From([Impact(entity: 0) with { Name = 1 }], Names, Wall, impacts);

        impacts.ShouldBeEmpty();
    }

    private static string? Names(int index) => index switch
    {
        0 => "Impact",
        1 => "Tracer",
        _ => null,
    };

    private static SceneEffectDispatch Impact(int entity) =>
        new(10, 0, (100f, 0f, 0f), (0f, 0f, 0f), default, default, 0, 1f, 0, 12, 0, 2, 0, entity, 0);
}
