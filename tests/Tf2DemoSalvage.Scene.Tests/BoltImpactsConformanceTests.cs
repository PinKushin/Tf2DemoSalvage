using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`StickRagdollNowTF`'s trace and `UTIL_ImpactTrace` for a `"TFBoltImpact"` dispatch (`c_tf_stickybolt.cpp:106`) (B415).</summary>
public sealed class BoltImpactsConformanceTests
{
    /// <summary>A wall at x = 100 facing −X, texinfo 7.</summary>
    private static readonly Func<(float X, float Y, float Z), (float X, float Y, float Z), BspTrace> Wall =
        static (from, to) => from.X < 100f && to.X >= 100f
            ? new BspTrace((100f - from.X) / (to.X - from.X), 7, (-1f, 0f, 0f), false, -100f)
            : new BspTrace(1f, -1, default, false);

    [Test]
    public void From_ABoltAtAWall_IsAClientImpactFromSixteenBehindIt()
    {
        // `UTIL_TraceLine( origin − dir · 16, origin + dir · 64, MASK_SOLID_BRUSHONLY )`, then `UTIL_ImpactTrace`.
        List<ShotImpact> impacts = [];

        BoltImpacts.From([Bolt((98f, 0f, 0f))], Names, Wall, impacts);

        ShotImpact impact = impacts.ShouldHaveSingleItem();

        impact.Start.ShouldBe((82f, 0f, 0f));
        impact.End.X.ShouldBe(100f, 1e-4f);
        impact.Texinfo.ShouldBe(7);
        impact.BrushOnly.ShouldBeTrue();
        impact.FromServer.ShouldBeFalse("its surface is the trace's, not a dispatched surfaceprop");
    }

    [Test]
    public void From_ABoltWithNothingAhead_IsNoImpact()
    {
        // `UTIL_ImpactTrace` returns on a fraction of 1.
        List<ShotImpact> impacts = [];

        BoltImpacts.From([Bolt((0f, 0f, 0f))], Names, Wall, impacts);

        impacts.ShouldBeEmpty();
    }

    [Test]
    public void From_AnotherEffect_IsNotABolt()
    {
        List<ShotImpact> impacts = [];

        BoltImpacts.From([Bolt((98f, 0f, 0f)) with { Name = 1 }], Names, Wall, impacts);

        impacts.ShouldBeEmpty();
    }

    [Test]
    public void Stuck_AnArrow_StandsFiveBackAlongItsFlightWithTheTeamSkin()
    {
        // `SpawnTempModel( w_arrow, origin − dir · 5, VectorAngles( dir ), … , 30, FTENT_NONE )`, `m_nSkin = m_nColor`.
        StuckArrow arrow = BoltImpacts.Stuck(
            [Bolt((98f, 0f, 0f)) with { Flags = 8, Colour = 1, Normal = (0f, 0.6f, -0.8f) }], Names, static (_, _) => false)
            .ShouldHaveSingleItem();

        arrow.Model.ShouldBe("models/weapons/w_models/w_arrow.mdl");
        arrow.At.X.ShouldBe(98f);
        arrow.At.Y.ShouldBe(-3f, 1e-4f);
        arrow.At.Z.ShouldBe(4f, 1e-4f);
        arrow.Yaw.ShouldBe(90f, 1e-3f);
        arrow.Pitch.ShouldBe(53.1301f, 1e-3f, "pointing down is a positive pitch in Source");
        arrow.Skin.ShouldBe(1);
        arrow.Life.ShouldBe(30f);
    }

    [Test]
    public void Stuck_ABoltIntoSky_LeavesNoArrow()
    {
        // `if ( tr.surface.flags & SURF_SKY ) return;` before anything is made.
        BoltImpacts.Stuck([Bolt((98f, 0f, 0f)) with { Flags = 8 }], Names, static (_, _) => true).ShouldBeEmpty();
    }

    [Test]
    public void Fill_AnArrowPastItsLife_IsGone()
    {
        // `FTENT_NONE`: no fade — the model stands until `die` and is removed there.
        StuckArrow arrow = new(0, 10, "a.mdl", (0f, 0f, 0f), 0f, 0f, 0, 1f, 30f);
        List<SceneProp> standing = [];
        List<SceneProp> gone = [];

        BoltImpacts.Fill([arrow], 10 + 1999, 0.015f, standing);
        // 2,000 ticks of the float 0.015 is 29.9999993 s, still standing; the next tick is past thirty.
        BoltImpacts.Fill([arrow], 10 + 2001, 0.015f, gone);

        standing.ShouldHaveSingleItem().ModelPath.ShouldBe("a.mdl");
        gone.ShouldBeEmpty();
    }

    private static string? Names(int index) => index switch
    {
        0 => "TFBoltImpact",
        1 => "Impact",
        _ => null,
    };

    /// <summary>A bolt at <paramref name="at"/> flying +X — `m_vNormal` is its direction.</summary>
    private static SceneEffectDispatch Bolt((float X, float Y, float Z) at) =>
        new(10, 0, at, default, (1f, 0f, 0f), default, 0, 0f, 0, -1, 0, 0, 0, 0, 0);
}
