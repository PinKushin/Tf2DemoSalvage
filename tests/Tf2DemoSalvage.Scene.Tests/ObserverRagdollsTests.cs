using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Which corpse the death view backs off from (B745).</summary>
public sealed class ObserverRagdollsTests
{
    private const int Player = 7;

    private static readonly Vector3 FirstRoot = new(1f, 2f, 3f);

    private static readonly Vector3 SecondRoot = new(4f, 5f, 6f);

    private static readonly Dictionary<int, Vector3> Roots = new()
    {
        [RagdollProps.FirstCorpseEntityIndex] = FirstRoot,
        [RagdollProps.FirstCorpseEntityIndex + 1] = SecondRoot,
    };

    [Test]
    public void Of_TwoCorpsesOfOnePlayerAlive_TakesTheLaterOnesSimulatedRoot()
    {
        ObserverRagdoll ragdoll = ObserverRagdolls.Of([Corpse(10), Corpse(200)], Roots, Player, tick: 300).ShouldNotBeNull();

        ragdoll.Root.ShouldBe(SecondRoot);
        ragdoll.DeathAnimation.ShouldBeFalse("an ordinary death plays no death animation");
    }

    [Test]
    public void Of_AGibbedCorpse_HasNoRoot()
    {
        ObserverRagdolls.Of([Corpse(10) with { Gib = true }], Roots, Player, tick: 300).ShouldNotBeNull().Root.ShouldBeNull();
    }

    [Test]
    public void Of_AnotherPlayersCorpse_IsNone()
    {
        ObserverRagdolls.Of([Corpse(10) with { PlayerIndex = 8 }], Roots, Player, tick: 300).ShouldBeNull();
    }

    [Test]
    public void Of_ACorpseGoneByTheTick_IsNone()
    {
        ObserverRagdolls.Of([Corpse(10)], Roots, Player, tick: 600).ShouldBeNull();
    }

    /// <remarks>
    /// **A gibbed player's observer origin is his first piece's mass centre** (B746): <c>m_hFirstGib</c>, the first piece
    /// <c>CreatePlayerGibs</c> made (<c>c_tf_player.cpp:7506</c>), read by <c>GetObserverCamOrigin</c> (<c>:6817-6838</c>).
    /// </remarks>
    [Test]
    public void Of_AGibbedCorpse_TakesItsFirstPiecesMassCentre()
    {
        SceneRagdoll gibbed = Corpse(10) with { Gib = true };
        Dictionary<int, Vector3> centres = new()
        {
            [RagdollProps.GibEntityIndex(gibbed, 0)] = FirstRoot,
            [RagdollProps.GibEntityIndex(gibbed, 1)] = SecondRoot,
        };

        ObserverRagdoll ragdoll = ObserverRagdolls.Of([gibbed], Roots, Player, 300, centres).ShouldNotBeNull();

        ragdoll.FirstGib.ShouldBe(FirstRoot);
        ragdoll.HeadGib.ShouldBeNull("a whole gib has no head view");
    }

    /// <remarks>
    /// **A handle to a faded piece is null**: once the first piece is gone the engine falls back to the death view, it does
    /// not move on to the next piece.
    /// </remarks>
    [Test]
    public void Of_AGibbedCorpseWhoseFirstPieceHasFaded_HasNoFirstGib()
    {
        SceneRagdoll gibbed = Corpse(10) with { Gib = true };
        Dictionary<int, Vector3> centres = new() { [RagdollProps.GibEntityIndex(gibbed, 1)] = SecondRoot };

        ObserverRagdolls.Of([gibbed], Roots, Player, 300, centres).ShouldNotBeNull().FirstGib.ShouldBeNull();
    }

    /// <remarks>
    /// **A decapitated player's first gib is his head, and the death cam views from it** (B746): <c>CreatePlayerGibs</c>
    /// with <c>bOnlyHead</c> sets <c>m_hFirstGib = m_hHeadGib</c> (<c>c_tf_player.cpp:7486-7490</c>). Its piece is wherever
    /// the head sits in the break list, the one piece of the corpse's block.
    /// </remarks>
    [Test]
    public void Of_ADecapitatedCorpse_ViewsFromItsHead()
    {
        SceneRagdoll decapitated = Corpse(10) with { DamageCustom = Decapitation };
        int head = RagdollProps.GibEntityIndex(decapitated, 6);
        Vector3 centre = new(7f, 8f, 9f);
        Vector3 origin = new(6f, 8f, 9f);
        Dictionary<int, Vector3> centres = new() { [head] = centre };
        Dictionary<int, (Vector3, (float, float, float))> placements = new() { [head] = (origin, (10f, 20f, 30f)) };

        ObserverRagdoll ragdoll = ObserverRagdolls.Of([decapitated], Roots, Player, 300, centres, placements).ShouldNotBeNull();

        ragdoll.FirstGib.ShouldBe(centre);
        ragdoll.HeadGib.ShouldBe(new ObserverHeadGib(centre, origin, (10f, 20f, 30f)));
        ragdoll.Root.ShouldBe(FirstRoot, "the headless body is still a ragdoll");
    }

    /// <remarks>
    /// **A headshot decapitation keeps the first gib and loses the head view**: <c>CreateTFHeadGib</c> clears
    /// <c>m_hHeadGib</c> for it (<c>c_tf_player.cpp:1077-1081</c>) after <c>m_hFirstGib</c> was set.
    /// </remarks>
    [Test]
    public void Of_AHeadshotDecapitation_HasAFirstGibButNoHeadView()
    {
        SceneRagdoll decapitated = Corpse(10) with { DamageCustom = HeadshotDecapitation };
        int head = RagdollProps.GibEntityIndex(decapitated, 6);
        Dictionary<int, Vector3> centres = new() { [head] = SecondRoot };
        Dictionary<int, (Vector3, (float, float, float))> placements = new() { [head] = (SecondRoot, (0f, 0f, 0f)) };

        ObserverRagdoll ragdoll = ObserverRagdolls.Of([decapitated], Roots, Player, 300, centres, placements).ShouldNotBeNull();

        ragdoll.FirstGib.ShouldBe(SecondRoot);
        ragdoll.HeadGib.ShouldBeNull();
    }

    /// <summary><c>TF_DMG_CUSTOM_DECAPITATION</c>.</summary>
    private const int Decapitation = 20;

    /// <summary><c>TF_DMG_CUSTOM_HEADSHOT_DECAPITATION</c>.</summary>
    private const int HeadshotDecapitation = 51;

    private static SceneRagdoll Corpse(int firstTick) =>
        new(100, 0, 3, SceneTeams.Red, 0f, 0f, 0f, false, false, false, false, firstTick, 500, PlayerIndex: Player);
}
