using System;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A <c>.phy</c>'s <c>collisionrules</c> block: which of a ragdoll's own bodies may touch (B58).
/// </summary>
/// <remarks>
/// **The third block Valve's ragdoll code reads and this project skipped**, and a census over every
/// `.phy` in `tf2_misc_dir.vpk` says it is not optional: 36 of the 37 models that carry ragdoll
/// joints declare it, the one that does not being a hinged door.
///
/// <code>
/// virtual void ParseKeyValue( void *pData, const char *pKey, const char *pValue )
/// {
///     if ( !strcmpi( pKey, "selfcollisions" ) )
///     {
///         // keys disabled by default
///         Assert( atoi(pValue) == 0 );
///         m_bSelfCollisions = false;
///     }
///     else if ( !strcmpi( pKey, "collisionpair" ) )
///     {
///         if ( m_bSelfCollisions )
///         {
///             char szToken[256];
///             const char *pStr = nexttoken(szToken, pValue, ',');
///             int index0 = atoi(szToken);
///             nexttoken( szToken, pStr, ',' );
///             int index1 = atoi(szToken);
///             m_pSet-&gt;EnableCollisions( index0, index1 );
///         }
///     }
/// }
/// </code>
///
/// `ragdoll_shared.cpp:72-109`, with `m_bSelfCollisions` starting **true** in the constructor.
///
/// **Three things in that handler are traps, and every one of them needs an input no shipped file
/// supplies** — so those cases are authored here, per
/// `docs/memory/author-the-specimen-the-corpus-lacks.md`.
/// </remarks>
public sealed class PhysicsCollisionRulesConformanceTests
{
    /// <remarks>
    /// **Against the game's own file first, because the question is whether this reads what Valve
    /// ships.** The predictions are structural rather than a memorised count: every index has to
    /// name a real solid, no pair may name the same body twice, and the list has to be SHORTER than
    /// every possible pair — a block that enabled all of them would be identical to having no block,
    /// which is the one thing it cannot be.
    /// </remarks>
    [Test]
    public void Read_TheHeavysPhysicsModel_DeclaresCollisionPairsWithinItsOwnSolids()
    {
        PhysicsModel physics = Physics("models/player/heavy.phy");

        physics.CollisionRules.ShouldNotBeNull();

        PhysicsCollisionRules rules = physics.CollisionRules;

        rules.SelfCollisions.ShouldBeTrue("no shipped ragdoll turns its own collisions off");
        rules.Pairs.Count.ShouldBeGreaterThan(0);

        int solids = physics.Solids.Count;

        rules.Pairs.ShouldAllBe(pair =>
            pair.First >= 0 && pair.First < solids &&
            pair.Second >= 0 && pair.Second < solids &&
            pair.First != pair.Second);

        rules.Pairs.Count.ShouldBeLessThan(
            solids * (solids - 1) / 2, "enabling every pair would make the block a no-op");
    }

    /// <remarks>
    /// **`selfcollisions` turns them off WHATEVER its value**, because the value is never read. The
    /// only thing Valve does with it is `Assert( atoi(pValue) == 0 )`, which compiles out of a
    /// release build; the assignment underneath is unconditional.
    ///
    /// So the specimen below says `"1"` — the value that reads like "yes, self-collisions" — and the
    /// engine turns them off. A reader that parsed the value would get this backwards on any file
    /// that ever wrote a non-zero, and would look more careful while doing it.
    /// </remarks>
    [Test]
    public void Read_ASelfCollisionsKeySayingOne_StillTurnsSelfCollisionsOff()
    {
        PhysicsModel physics = PhysicsModel.Read(Authored("""
            collisionrules {
              "selfcollisions" "1"
            }
            """));

        physics.CollisionRules.ShouldNotBeNull();
        physics.CollisionRules.SelfCollisions.ShouldBeFalse("the value is asserted, never read");
    }

    /// <remarks>
    /// **The handler is a STREAM, so a pair is kept or dropped by where it sits relative to the
    /// `selfcollisions` key.** `EnableCollisions` is called only `if ( m_bSelfCollisions )`, tested
    /// at the moment that pair arrives — so the same two keys in the other order mean different
    /// things.
    ///
    /// This is the input where a reader that gathered the block into a dictionary and decided
    /// afterwards differs from the engine: it would drop both pairs, or keep both, depending on
    /// which way it read the flag. The engine keeps exactly the first.
    /// </remarks>
    [Test]
    public void Read_CollisionPairsAroundASelfCollisionsKey_KeepsOnlyThoseBeforeIt()
    {
        PhysicsModel physics = PhysicsModel.Read(Authored("""
            collisionrules {
              "collisionpair" "0,2"
              "selfcollisions" "0"
              "collisionpair" "1,3"
            }
            """));

        physics.CollisionRules.ShouldNotBeNull();

        physics.CollisionRules.SelfCollisions.ShouldBeFalse();

        physics.CollisionRules.Pairs.Count.ShouldBe(
            1, "the pair after the key never reaches EnableCollisions");

        physics.CollisionRules.Pairs[0].First.ShouldBe(0);
        physics.CollisionRules.Pairs[0].Second.ShouldBe(2);
    }

    /// <remarks>
    /// **A pair is one string split on a comma**, read with `nexttoken` and `atoi` — so whitespace
    /// around the indices is ordinary and the values are plain integers.
    /// </remarks>
    [Test]
    public void Read_ACollisionPairWithSpacesAroundItsComma_ReadsBothIndices()
    {
        PhysicsModel physics = PhysicsModel.Read(Authored("""
            collisionrules {
              "collisionpair" "4, 11"
            }
            """));

        physics.CollisionRules.ShouldNotBeNull();
        physics.CollisionRules.Pairs.Count.ShouldBe(1);
        physics.CollisionRules.Pairs[0].First.ShouldBe(4);
        physics.CollisionRules.Pairs[0].Second.ShouldBe(11);
    }

    /// <remarks>
    /// **Absent is not empty, and the difference decides the whole behaviour.** When no
    /// `collisionrules` block exists, `RagdollSetupCollisions` builds Valve's own fallback instead —
    /// *"these are the default rules - each piece collides with everything except immediate
    /// parent/constrained object"* (`ragdoll_shared.cpp:348-369`). A reader that reported an EMPTY
    /// rules object for a missing block would select "nothing collides", which is the opposite.
    ///
    /// So a missing block reads as null, and the caller branches on that — the same
    /// `docs/memory/sentinels-conflate-unknown-with-answer.md` rule as everywhere else here.
    /// </remarks>
    [Test]
    public void Read_APhysicsFileWithNoCollisionRules_ReportsNoRulesRatherThanEmptyOnes()
    {
        PhysicsModel physics = PhysicsModel.Read(Authored("""
            solid {
              "index" "0"
              "name" "bip_pelvis"
              "mass" "7.470685"
            }
            """));

        physics.Solids.Count.ShouldBe(1, "the control: the file did parse");
        physics.CollisionRules.ShouldBeNull();
    }

    /// <summary>Wraps KeyValues text in a minimal <c>phyheader_t</c>.</summary>
    /// <param name="text">The block or blocks to parse.</param>
    /// <returns>A readable <c>.phy</c>.</returns>
    private static byte[] Authored(string text) =>
    [
        .. BitConverter.GetBytes(16),
        .. BitConverter.GetBytes(0x59485056),
        .. BitConverter.GetBytes(1),
        .. BitConverter.GetBytes(0),
        .. Encoding.ASCII.GetBytes(text),
    ];

    /// <summary>Reads one of the game's own physics models, skipping without an install.</summary>
    /// <param name="path">Its path inside the archives.</param>
    /// <returns>The model.</returns>
    private static PhysicsModel Physics(string path) =>
        Skip.Unless(Read(path), GameInstall.Missing);

    /// <summary>The model, or null when the game is not installed.</summary>
    /// <param name="path">Its path inside the archives.</param>
    /// <returns>The model, or null.</returns>
    private static PhysicsModel? Read(string path)
    {
        if (GameInstall.Root is not { } tf)
        {
            return null;
        }

        GameArchives archives = GameArchives.Open(tf);

        return archives.Read(path) is { } bytes ? PhysicsModel.Read(bytes) : null;
    }
}
