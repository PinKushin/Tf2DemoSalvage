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

    /// <remarks>A whole gib by a decapitating weapon is a gib: <c>CreateTFHeadGib</c> runs only for a body (<c>:1240</c>).</remarks>
    [Test]
    public void Of_AGibbedDecapitation_HasNoHeadView()
    {
        SceneRagdoll gibbed = Corpse(10) with { Gib = true, DamageCustom = Decapitation };
        int first = RagdollProps.GibEntityIndex(gibbed, 0);
        Dictionary<int, Vector3> centres = new() { [first] = FirstRoot };
        Dictionary<int, (Vector3, (float, float, float))> placements = new() { [first] = (FirstRoot, (0f, 0f, 0f)) };

        ObserverRagdoll ragdoll = ObserverRagdolls.Of([gibbed], Roots, Player, 300, centres, placements).ShouldNotBeNull();

        ragdoll.FirstGib.ShouldBe(FirstRoot);
        ragdoll.HeadGib.ShouldBeNull();
    }

    /// <remarks>The control on the head view's placement: with none read, the first gib is still found and no head made.</remarks>
    [Test]
    public void Of_ADecapitationWithNoPlacements_HasAFirstGibButNoHeadView()
    {
        SceneRagdoll decapitated = Corpse(10) with { DamageCustom = Decapitation };
        Dictionary<int, Vector3> centres = new() { [RagdollProps.GibEntityIndex(decapitated, 6)] = SecondRoot };

        ObserverRagdoll ragdoll = ObserverRagdolls.Of([decapitated], Roots, Player, 300, centres).ShouldNotBeNull();

        ragdoll.FirstGib.ShouldBe(SecondRoot);
        ragdoll.HeadGib.ShouldBeNull();
    }

    /// <remarks>
    /// **Past the ragdoll entity's end its head is still the death cam's** (B746 review): the pieces are client entities of
    /// their own, so the first gib stays while the ragdoll, its root and the body to face are gone.
    /// </remarks>
    [Test]
    public void Of_ADecapitationPastItsRagdoll_KeepsTheHeadWithNoBody()
    {
        SceneRagdoll decapitated = Corpse(10) with { DamageCustom = Decapitation };
        int head = RagdollProps.GibEntityIndex(decapitated, 6);
        Dictionary<int, Vector3> centres = new() { [head] = SecondRoot };
        Dictionary<int, (Vector3, (float, float, float))> placements = new() { [head] = (SecondRoot, (0f, 0f, 0f)) };

        ObserverRagdoll ragdoll = ObserverRagdolls.Of([decapitated], Roots, Player, 600, centres, placements).ShouldNotBeNull();

        (ragdoll.FirstGib, ragdoll.HeadGib is null, ragdoll.Root, ragdoll.Body).ShouldBe((SecondRoot, false, null, null));
    }

    /// <remarks>
    /// **The body to face is the ragdoll's origin: its root once simulated, else where it was made** (<c>RagdollMoved</c>,
    /// <c>c_baseanimating.cpp:4608</c>).
    /// </remarks>
    [Test]
    public void Of_AnUnsimulatedCorpse_FacesWhereItWasMade()
    {
        SceneRagdoll unsimulated = Corpse(10) with { X = 7f, Y = 8f, Z = 9f };

        ObserverRagdolls.Of([unsimulated], new Dictionary<int, Vector3>(), Player, 300).ShouldNotBeNull().Body
            .ShouldBe(new Vector3(7f, 8f, 9f));
        ObserverRagdolls.Of([unsimulated], Roots, Player, 300).ShouldNotBeNull().Body.ShouldBe(FirstRoot, "the control: simulated");
    }

    /// <remarks>
    /// **A death that threw nothing leaves the handle on a feign death's pieces** (B746 review): the spy's feign death
    /// gibbed, no respawn cleared <c>m_hFirstGib</c>, so his real death made none and the camera stays on the feign
    /// death's first piece.
    /// </remarks>
    [Test]
    public void Of_ADeathAfterAFeignDeathsGibs_KeepsTheFeignDeathsFirstPiece()
    {
        SceneRagdoll feigned = Corpse(10) with { Gib = true, FeignDeath = true };
        SceneRagdoll real = Corpse(200) with { EntityIndex = 101, Gib = true };
        Dictionary<int, Vector3> centres = new() { [RagdollProps.GibEntityIndex(feigned, 0)] = SecondRoot };

        ObserverRagdolls.Of([feigned, real], Roots, Player, 300, centres).ShouldNotBeNull().FirstGib.ShouldBe(SecondRoot);
        ObserverRagdolls.Of([feigned with { FeignDeath = false }, real], Roots, Player, 300, centres).ShouldNotBeNull()
            .FirstGib.ShouldBeNull("the control: a real death before it was cleared by a respawn");
    }

    /// <remarks>
    /// **The walk goes back through every feign death that threw nothing** (B746 re-review): a feign gibbing, a plain
    /// feign death, then the real death. The handle stays on the first one's piece. The control: a second feign death
    /// that gibbed holds the handle itself, and with its piece faded the camera has none.
    /// </remarks>
    [Test]
    public void Of_ADeathAfterAFeignGibbingAndAPlainFeign_KeepsTheFirstsPiece()
    {
        SceneRagdoll gibbed = Corpse(10) with { Gib = true, FeignDeath = true };
        SceneRagdoll plain = Corpse(100) with { EntityIndex = 101, FeignDeath = true };
        SceneRagdoll real = Corpse(200) with { EntityIndex = 102, Gib = true };
        Dictionary<int, Vector3> centres = new() { [RagdollProps.GibEntityIndex(gibbed, 0)] = SecondRoot };

        ObserverRagdolls.Of([gibbed, plain, real], Roots, Player, 300, centres).ShouldNotBeNull().FirstGib.ShouldBe(SecondRoot);
        ObserverRagdolls.Of([gibbed, plain with { Gib = true }, real], Roots, Player, 300, centres).ShouldNotBeNull()
            .FirstGib.ShouldBeNull("the control: the second feign death's faded piece holds it");
    }

    /// <remarks>
    /// **`m_hSpawnedGibs` is the last throw's pieces in the order made** (B752): `CreateGibsFromList` appends only what it
    /// makes (`c_tf_player.cpp:7506`), so a piece never made takes no place and the next moves up. Pieces 0 and 2 are
    /// places 0 and 1.
    /// </remarks>
    [Test]
    public void SpawnedGibs_AGibbedCorpse_AreItsPiecesInOrderAtTheirPlaces()
    {
        SceneRagdoll gibbed = Corpse(10) with { Gib = true };
        Dictionary<int, Vector3> centres = new()
        {
            [RagdollProps.GibEntityIndex(gibbed, 0)] = FirstRoot,
            [RagdollProps.GibEntityIndex(gibbed, 2)] = SecondRoot,
        };

        ObserverRagdolls.SpawnedGibs([gibbed], Player, 300, centres).ShouldBe([(0, FirstRoot), (1, SecondRoot)]);
    }

    /// <remarks>
    /// **The list does not need the first piece** (B752 review): `UpdateCallout` walks every handle it holds
    /// (`tf_hud_freezepanel.cpp:792-815`), so pieces left when the first is not are still the list.
    /// </remarks>
    [Test]
    public void SpawnedGibs_AGibbedCorpseWithoutItsFirstPiece_AreThePiecesLeft()
    {
        SceneRagdoll gibbed = Corpse(10) with { Gib = true };
        Dictionary<int, Vector3> centres = new() { [RagdollProps.GibEntityIndex(gibbed, 3)] = SecondRoot };

        ObserverRagdolls.SpawnedGibs([gibbed], Player, 300, centres).ShouldBe([(0, SecondRoot)]);
    }

    /// <remarks>
    /// **A decapitation's list is its head alone, at place 0** (`headGib.AddToHead`, then `CreateGibsFromList`, :7465-7488),
    /// whatever the head's place in the break list.
    /// </remarks>
    [Test]
    public void SpawnedGibs_ADecapitation_IsTheHeadAtPlaceZero()
    {
        SceneRagdoll decapitated = Corpse(10) with { DamageCustom = Decapitation };
        Dictionary<int, Vector3> centres = new() { [RagdollProps.GibEntityIndex(decapitated, 6)] = SecondRoot };

        ObserverRagdolls.SpawnedGibs([decapitated], Player, 300, centres).ShouldBe([(0, SecondRoot)]);
    }

    /// <remarks>The control: an ordinary death threw nothing, so the list is empty.</remarks>
    [Test]
    public void SpawnedGibs_AnOrdinaryCorpse_IsEmpty()
    {
        ObserverRagdolls.SpawnedGibs([Corpse(10)], Player, 300, new Dictionary<int, Vector3>()).ShouldBeEmpty();
    }

    private const int Decapitation = DamageCustoms.Decapitation;

    private const int HeadshotDecapitation = DamageCustoms.HeadshotDecapitation;

    private static SceneRagdoll Corpse(int firstTick) =>
        new(100, 0, 3, SceneTeams.Red, 0f, 0f, 0f, false, false, false, false, firstTick, 500, PlayerIndex: Player);
}
