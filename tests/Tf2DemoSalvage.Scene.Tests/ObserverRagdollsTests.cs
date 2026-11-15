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

    private static SceneRagdoll Corpse(int firstTick) =>
        new(100, 0, 3, SceneTeams.Red, 0f, 0f, 0f, false, false, false, false, firstTick, 500, PlayerIndex: Player);
}
