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

    /// <remarks>
    /// B797: a static prop is the world with a hitbox, the prop's index plus one (`physics.cpp:1314-1316`), and `Impact`
    /// sends it to `AddDecalToStaticProp( …, iHitbox − 1, … )` (`fx_impact.cpp:149-152`).
    /// </remarks>
    [Test]
    public void From_AnImpactOnAStaticProp_IsAServerImpactOnThatProp()
    {
        List<ShotImpact> impacts = [];

        ServerImpacts.From([Impact(entity: 0) with { HitBox = 5 }], Names, Wall, impacts);

        ShotImpact impact = impacts.ShouldHaveSingleItem();

        impact.FromServer.ShouldBeTrue();
        impact.StaticProp.ShouldBe(4, "the hitbox less one");
        impact.End.ShouldBe((100f, 0f, 0f));
        impact.Normal.ShouldBe((-1f, 0f, 0f));
        impact.SurfaceProp.ShouldBe(12, "the dispatch's own surfaceprop");
    }

    /// <remarks>
    /// B797 review 2: the trace's own surface rides with the impact — `PlayImpactSound` takes it when the server names
    /// surfaceprop 0 (`fx_impact.cpp:378-381`). The trace here strikes a prop whose `$surfaceprop` is 9; the wall's
    /// trace, which carries none, is the control.
    /// </remarks>
    [Test]
    public void From_AnImpactWhoseTraceStrikesAProp_CarriesThePropsSurface()
    {
        List<ShotImpact> onProp = [];
        List<ShotImpact> onWall = [];

        ServerImpacts.From(
            [Impact(entity: 0) with { HitBox = 5 }],
            Names,
            (from, to) => Wall(from, to) with { StaticProp = 4, StudioSurfaceProp = 9 },
            onProp);
        ServerImpacts.From([Impact(entity: 0) with { HitBox = 5 }], Names, Wall, onWall);

        (onProp.ShouldHaveSingleItem().StudioSurfaceProp, onWall.ShouldHaveSingleItem().StudioSurfaceProp).ShouldBe((9, -1));
    }

    [Test]
    public void From_AnImpactOnTheWorldWithNoHitbox_IsOnNoStaticProp()
    {
        List<ShotImpact> impacts = [];

        ServerImpacts.From([Impact(entity: 0)], Names, Wall, impacts);

        impacts.ShouldHaveSingleItem().StaticProp.ShouldBe(-1);
    }

    // `ImpactCallback`'s gate (`tf_fx_impacts.cpp:35-49`, B796): `m_nDamageType` is the damage BITS, compared with the
    // struck entity's team number, and with a disguised player's disguise team.
    [TestCase(2, 2, null, false, TestName = "Impacts_DamageBitsEqualToTheStruckTeam_IsRefused")]
    [TestCase(2, 3, null, true, TestName = "Impacts_DamageBitsUnequalToTheStruckTeam_Impacts")]
    [TestCase(1, 1, null, true, TestName = "Impacts_DamageBitsEqualToATeamBelowTheFirstGameTeam_Impacts")]
    [TestCase(0, null, null, true, TestName = "Impacts_NoDamageBitsOnAnEntityOfNoTeam_Impacts")]
    [TestCase(2, 3, 2, false, TestName = "Impacts_ADisguiseOnTheDamageBitsTeam_IsRefused")]
    [TestCase(2, 3, 3, true, TestName = "Impacts_ADisguiseOnAnotherTeam_Impacts")]
    [TestCase(1, 1, 1, true, TestName = "Impacts_BitsEqualToATeamBelowTheFirstGameTeam_IsNotAskedAboutTheDisguise")]
    public void Impacts_TheTeamGate_IsImpactCallbacks(int damageType, int? team, int? disguiseTeam, bool expected) =>
        ServerImpacts.Impacts(damageType, team, disguiseTeam).ShouldBe(expected);

    [Test]
    public void Impacts_ADisguiseTeamWhileNotDisguised_IsNotAsked()
    {
        ScenePlayer spy = new(2, 0f, 0f, 0f, 3, 100, 8) { DisguiseTeam = 2 };

        ServerImpacts.Impacts(2, spy).ShouldBeTrue("only InCond( TF_COND_DISGUISED ) reads GetDisguiseTeam");
        ServerImpacts.Impacts(2, spy with { Conditions = new PlayerConditions(1 << PlayerConditions.Disguised, 0, 0, 0, 0) })
            .ShouldBeFalse("disguised as the team the bits name");
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
