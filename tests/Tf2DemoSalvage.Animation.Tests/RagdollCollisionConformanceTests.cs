using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// Which of a ragdoll's own bodies may collide with each other (B58).
/// </summary>
/// <remarks>
/// **`RagdollSetupCollisions`, `ragdoll_shared.cpp:315`**, and the whole finding is that there are
/// TWO rules rather than one:
///
/// <code>
/// if ( !bFoundRules )
/// {
///     // these are the default rules - each piece collides with everything
///     // except immediate parent/constrained object.
///     for ( i = 0; i &lt; ragdoll.listCount; i++ )
///         for ( int j = i+1; j &lt; ragdoll.listCount; j++ )
///             pSet-&gt;EnableCollisions( i, j );
///     for ( i = 0; i &lt; ragdoll.listCount; i++ )
///     {
///         int parent = ragdoll.list[i].parentIndex;
///         if ( parent &gt;= 0 )
///             pSet-&gt;DisableCollisions( i, parent );
///     }
/// }
/// </code>
///
/// **That fallback is also the proof that an untouched set collides with NOTHING.** It enables every
/// pair explicitly before disabling the joint ones — work that would be pointless if the default
/// state were "everything collides". So a model that DOES declare rules gets exactly the pairs it
/// names and nothing else, and the two paths are not variations of each other.
///
/// **Measured: 36 of the 37 shipped models with joints declare a block**, so the fallback is the
/// rare path and the declared path is the one every corpse takes.
/// </remarks>
public sealed class RagdollCollisionConformanceTests
{
    /// <remarks>
    /// **The fallback's first loop: every distinct pair.** Two bodies that are not joined to each
    /// other collide, which is what stops a corpse's limbs passing through its own torso.
    /// </remarks>
    [Test]
    public void ShouldCollide_WithNoDeclaredRules_CollidesTwoBodiesThatAreNotJoined()
    {
        RagdollBody body = Chain(rules: null);

        body.ShouldCollide(0, 2).ShouldBeTrue();
        body.ShouldCollide(2, 0).ShouldBeTrue("order cannot matter");
    }

    /// <remarks>
    /// **The fallback's second loop: a joint pair is DISABLED.** Valve's own comment — *"each piece
    /// collides with everything except immediate parent/constrained object"*. Without it every
    /// constrained pair would be pushing itself apart while the joint pulls it together, which is
    /// the classic jittering ragdoll.
    ///
    /// The chain below is 0 → 1 → 2, so (0,1) and (1,2) are joints and (0,2) is not. That is the
    /// input where "disable the joints" and "disable nothing" differ, and the assertion above is its
    /// control.
    /// </remarks>
    [Test]
    public void ShouldCollide_WithNoDeclaredRules_DoesNotCollideABodyWithItsOwnParent()
    {
        RagdollBody body = Chain(rules: null);

        body.ShouldCollide(0, 1).ShouldBeFalse();
        body.ShouldCollide(1, 0).ShouldBeFalse();
        body.ShouldCollide(1, 2).ShouldBeFalse();
    }

    /// <remarks>
    /// **A declared block replaces the fallback entirely rather than adding to it.** The set starts
    /// empty and `CRagdollCollisionRules` only ever calls `EnableCollisions`, so a pair the file does
    /// not name does not collide — even a pair the fallback would have enabled.
    ///
    /// **Picking the distinguishing pair took two attempts and the first one could not fail.** The
    /// block below names (0,2). Asserting that (0,3) does not collide proves nothing, because (0,3)
    /// is a JOINT here and the fallback disables joints too — correct and merged-with-the-fallback
    /// predict the same observation, which is the "wrong condition" trap this project keeps meeting.
    ///
    /// The pair that separates them is one with **no joint between it and not in the list**: (1,3).
    /// The fallback would enable it and a declared block does not, so a reader that merged the two
    /// rules returns true here and the engine returns false.
    /// </remarks>
    [Test]
    public void ShouldCollide_WithADeclaredBlock_CollidesOnlyThePairsItNames()
    {
        RagdollBody body = Chain(new PhysicsCollisionRules(true, [new PhysicsCollisionPair(0, 2)]));

        body.ShouldCollide(0, 2).ShouldBeTrue("the file names this pair");
        body.ShouldCollide(2, 0).ShouldBeTrue();

        body.ShouldCollide(1, 3).ShouldBeFalse(
            "no joint joins them, so the fallback WOULD enable it; a declared block does not");

        body.ShouldCollide(2, 3).ShouldBeFalse("nor this one, for the same reason");
    }

    /// <remarks>
    /// **A block that turns self-collisions off before naming anything collides nothing** — the
    /// engine reaches `EnableCollisions` only while the flag is true, so the set it was handed stays
    /// empty and every query is false.
    /// </remarks>
    [Test]
    public void ShouldCollide_WhenTheBlockTurnsSelfCollisionsOffFirst_CollidesNothing()
    {
        RagdollBody body = Chain(new PhysicsCollisionRules(false, []));

        body.ShouldCollide(0, 2).ShouldBeFalse();
        body.ShouldCollide(0, 3).ShouldBeFalse();
    }

    /// <remarks>
    /// **A pair enabled BEFORE the flag went off stays enabled, and this is where the obvious
    /// implementation is wrong.** Reading `selfcollisions` as "nothing collides" is the natural
    /// summary of the handler and it does not survive the one input that separates it: there is no
    /// call that disables anything, so a `collisionpair` that already reached `EnableCollisions`
    /// remains in the set for the rest of the ragdoll's life.
    ///
    /// **Found by sabotage, not by reading.** A first version tested the flag inside `ShouldCollide`
    /// as well as in the parser; removing that test reddened nothing, because the only case exercised
    /// had an EMPTY pair list — where both readings agree. This is that case with a pair in it, and
    /// the state below is exactly what the parser produces from
    /// `collisionpair "0,2"` followed by `selfcollisions "0"`.
    /// </remarks>
    [Test]
    public void ShouldCollide_ForAPairEnabledBeforeSelfCollisionsWentOff_StillCollides()
    {
        RagdollBody body = Chain(
            new PhysicsCollisionRules(false, [new PhysicsCollisionPair(0, 2)]));

        body.ShouldCollide(0, 2).ShouldBeTrue("nothing ever disables what was already enabled");
        body.ShouldCollide(1, 3).ShouldBeFalse("the control: an unnamed pair is still off");
    }

    /// <remarks>
    /// **A body never collides with itself**, in either path: the fallback's inner loop starts at
    /// `i+1`, and no shipped file names a pair twice over the same index.
    /// </remarks>
    [Test]
    public void ShouldCollide_ForABodyAgainstItself_IsFalse()
    {
        Chain(rules: null).ShouldCollide(2, 2).ShouldBeFalse();
    }

    /// <remarks>
    /// **An index outside the ragdoll is not a collision and not an exception.** The engine's set is
    /// created with `maxElementCount = ragdoll.listCount` and asked only about indices it owns; a
    /// caller here can hold a stale index while a corpse is rebuilt.
    /// </remarks>
    [Test]
    public void ShouldCollide_ForAnIndexOutsideTheRagdoll_IsFalse()
    {
        RagdollBody body = Chain(rules: null);

        body.ShouldCollide(0, 99).ShouldBeFalse();
        body.ShouldCollide(-1, 0).ShouldBeFalse();
    }

    /// <summary>A four-body chain, 0 to 1 to 2, with 3 hanging off 0.</summary>
    /// <param name="rules">The declared rules, or null for Valve's fallback.</param>
    /// <returns>The ragdoll.</returns>
    /// <remarks>
    /// **Branching rather than a straight chain on purpose.** With 3 attached to 0 rather than to 2,
    /// the pair (0,3) is a joint and (2,3) is not — so the fallback and a declared block disagree
    /// about more than one pair, and a test cannot pass by accident of a two-body shape.
    /// </remarks>
    private static RagdollBody Chain(PhysicsCollisionRules? rules)
    {
        ConstraintAxis axis = new(0f, 0f, 0f);

        PhysicsSolid[] solids =
        [
            new PhysicsSolid(0, "bone_a", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f),
            new PhysicsSolid(1, "bone_b", "bone_a", "flesh", 5f, 1f, 0f, 0f, 50f, 0f),
            new PhysicsSolid(2, "bone_c", "bone_b", "flesh", 5f, 1f, 0f, 0f, 50f, 0f),
            new PhysicsSolid(3, "bone_d", "bone_a", "flesh", 5f, 1f, 0f, 0f, 50f, 0f),
        ];

        RagdollConstraint[] constraints =
        [
            new RagdollConstraint(0, 1, axis, axis, axis),
            new RagdollConstraint(1, 2, axis, axis, axis),
            new RagdollConstraint(0, 3, axis, axis, axis),
        ];

        PhysicsModel physics = PhysicsModel.From(solids, constraints, solids.Length, 0, rules);

        RagdollBody? body = RagdollBody.Build(physics, Skeleton());

        body.ShouldNotBeNull("the control: the ragdoll did build");

        return body;
    }

    /// <summary>Four bones matching the solids above.</summary>
    /// <returns>The skeleton.</returns>
    private static IReadOnlyList<StudioBone> Skeleton() =>
        [
            Bone("bone_a", -1, 1f, 2f, 3f),
            Bone("bone_b", 0, 4f, 6f, 3f),
            Bone("bone_c", 1, 7f, 9f, 3f),
            Bone("bone_d", 0, 1f, 5f, 8f),
        ];

    /// <summary>One bone at a bind position, with no rotation.</summary>
    /// <param name="name">Its name, which a solid matches on.</param>
    /// <param name="parent">Its parent bone, or −1.</param>
    /// <param name="x">Bind X.</param>
    /// <param name="y">Bind Y.</param>
    /// <param name="z">Bind Z.</param>
    /// <returns>The bone.</returns>
    private static StudioBone Bone(string name, int parent, float x, float y, float z) =>
        new(
            name,
            parent,
            (x, y, z),
            (0f, 0f, 0f, 1f),
            new float[]
            {
                1f, 0f, 0f, -x,
                0f, 1f, 0f, -y,
                0f, 0f, 1f, -z,
            });
}
