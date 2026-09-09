using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <see cref="PlayerGibs"/> against <c>CreatePlayerGibs</c> and <c>CreateGibsFromList</c> (B371).
/// </summary>
/// <remarks>
/// **Every expected value here is arithmetic from the engine's own lines, not from this code.** The
/// throw is `normalize(force + velocity)`, `z += 1`, normalise, `× 500`, capped at 400; the
/// per-piece scatter preserves that speed exactly and only turns it.
/// </remarks>
[TestFixture]
public class PlayerGibsConformanceTests
{
    private const float Tolerance = 1e-3f;

    /// <remarks>
    /// **The cap is reached by every ordinary kill, which is why it matters.** 500 is above the 400
    /// ceiling, so the throw is always scaled down and the FORCE constant never reaches a piece
    /// intact — a reader expecting 500 units a second would be wrong about every gib in the game.
    /// </remarks>
    [Test]
    public void Throw_ForAnyKill_IsCappedAtMaximumSpeed()
    {
        (float x, float y, float z) = PlayerGibs.Throw(Corpse(force: (1000f, 0f, 0f)));

        MathF.Sqrt((x * x) + (y * y) + (z * z))
            .ShouldBe(PlayerGibs.MaximumSpeed, Tolerance, "500 is over the 400 cap");
    }

    /// <remarks>
    /// **A corpse with no force and no velocity goes straight up.** `VectorNormalize` of a zero
    /// vector leaves zero, so the only thing left is the `z += tf_playersgib_forceup` — which is
    /// the engine's answer for a body that simply came apart.
    /// </remarks>
    [Test]
    public void Throw_WithNeitherForceNorVelocity_IsStraightUp()
    {
        (float x, float y, float z) = PlayerGibs.Throw(Corpse());

        x.ShouldBe(0f, Tolerance);
        y.ShouldBe(0f, Tolerance);
        z.ShouldBe(PlayerGibs.MaximumSpeed, Tolerance, "capped, and all of it upward");
    }

    /// <remarks>
    /// **The kill's magnitude is thrown away and only its direction survives**, because
    /// `CreateTFGibs` normalises before `CreatePlayerGibs` ever sees it. A rocket that hit ten times
    /// harder scatters the pieces the same distance — this is the assertion that says so, and it is
    /// the one a reader is most likely to expect to fail.
    /// </remarks>
    [Test]
    public void Throw_WithTenTimesTheForce_IsUnchanged()
    {
        (float X, float Y, float Z) gentle = PlayerGibs.Throw(Corpse(force: (100f, 0f, 0f)));
        (float X, float Y, float Z) savage = PlayerGibs.Throw(Corpse(force: (1000f, 0f, 0f)));

        savage.X.ShouldBe(gentle.X, Tolerance);
        savage.Y.ShouldBe(gentle.Y, Tolerance);
        savage.Z.ShouldBe(gentle.Z, Tolerance);
    }

    /// <remarks>
    /// **The scatter turns a piece without speeding it up or slowing it down**, which is what
    /// normalise-scatter-normalise-rescale means. Asserted on several pieces because one could pass
    /// by accident.
    /// </remarks>
    [Test]
    public void Velocity_ForEveryPiece_KeepsTheThrowsSpeed()
    {
        SceneRagdoll corpse = Corpse(force: (300f, -200f, 50f));

        (float tx, float ty, float tz) = PlayerGibs.Throw(corpse);
        float expected = MathF.Sqrt((tx * tx) + (ty * ty) + (tz * tz));

        for (int piece = 0; piece < 9; piece++)
        {
            (float x, float y, float z) = PlayerGibs.Velocity(corpse, piece);

            MathF.Sqrt((x * x) + (y * y) + (z * z)).ShouldBe(expected, Tolerance);
        }
    }

    /// <remarks>
    /// **Nine pieces must not leave along one line**, which is the failure a scatter exists to
    /// prevent and the one a broken hash would produce silently — every piece would still have the
    /// right speed and the body would come apart as a single dart.
    /// </remarks>
    [Test]
    public void Velocity_AcrossThePieces_PointsInDifferentDirections()
    {
        SceneRagdoll corpse = Corpse(force: (300f, -200f, 50f));

        (float X, float Y, float Z) first = PlayerGibs.Velocity(corpse, 0);
        int different = 0;

        for (int piece = 1; piece < 9; piece++)
        {
            (float x, float y, float z) = PlayerGibs.Velocity(corpse, piece);

            if (MathF.Abs(x - first.X) > 1f ||
                MathF.Abs(y - first.Y) > 1f ||
                MathF.Abs(z - first.Z) > 1f)
            {
                different++;
            }
        }

        different.ShouldBe(8, "every other piece leaves on its own heading");
    }

    /// <remarks>
    /// **The engine's asymmetry, asserted where it is drawn rather than where it shows.** x and y
    /// come from `RandomFloat( -1, 1 )` and z from `RandomFloat( 0, 1 )`, so the scatter can only
    /// tilt a piece upward.
    ///
    /// **This test replaced one that could not fail.** The first version asserted on the finished
    /// velocity — but `Throw` has already added `tf_playersgib_forceup` and renormalised, so every
    /// piece leaves steeply upward regardless; widening z to −1..1 in the source left that
    /// assertion green. Sabotage found it, which is the entire reason the rule exists.
    /// </remarks>
    [Test]
    public void Scatter_AcrossThePieces_DrawsZeroToOneOnZAndSymmetricallyOnXAndY()
    {
        SceneRagdoll corpse = Corpse(force: (300f, -200f, 50f));

        bool negativeX = false;
        bool negativeY = false;

        for (int piece = 0; piece < 64; piece++)
        {
            (float x, float y, float z) = PlayerGibs.Scatter(corpse, piece);

            x.ShouldBeInRange(-1f, 1f);
            y.ShouldBeInRange(-1f, 1f);
            z.ShouldBeInRange(0f, 1f, "RandomFloat( 0.0f, 1.0f ) — never negative");

            negativeX |= x < 0f;
            negativeY |= y < 0f;
        }

        // The control: x and y really do go negative, so the z bound above is a fact about z and
        // not about a scatter that never goes negative at all.
        negativeX.ShouldBeTrue("x draws from -1..1");
        negativeY.ShouldBeTrue("y draws from -1..1");
    }

    /// <remarks>
    /// **One draw per corpse, shared by every piece** — `CreatePlayerGibs` builds the impulse before
    /// walking the list. Zero about z is the engine's own literal.
    /// </remarks>
    [Test]
    public void Spin_ForOneCorpse_IsWithinRangeAndFlatAboutZ()
    {
        (float x, float y, float z) = PlayerGibs.Spin(Corpse(force: (300f, 0f, 0f)));

        x.ShouldBeInRange(0f, PlayerGibs.MaximumSpin);
        y.ShouldBeInRange(0f, PlayerGibs.MaximumSpin);
        z.ShouldBe(0f, "AngularImpulse( RandomFloat, RandomFloat, 0.0 )");
    }

    /// <remarks>
    /// **The whole point of keying the draw to the corpse**: a viewer can seek, so the same corpse
    /// asked twice — in any order, at any tick — must come apart the same way. A stream generator
    /// cannot do this and it is why the engine's own numbers are unreachable.
    /// </remarks>
    [Test]
    public void Velocity_AskedTwiceForOneCorpse_IsTheSame()
    {
        SceneRagdoll corpse = Corpse(force: (300f, -200f, 50f));

        for (int piece = 0; piece < 9; piece++)
        {
            PlayerGibs.Velocity(corpse, piece).ShouldBe(PlayerGibs.Velocity(corpse, piece));
        }
    }

    /// <remarks>
    /// **Two corpses that reused one entity slot must not come apart identically.** The serial is
    /// what separates them, and this is the control that says the hash reads it.
    /// </remarks>
    [Test]
    public void Velocity_ForTwoCorpsesSharingASlot_Differs()
    {
        SceneRagdoll first = Corpse(force: (300f, 0f, 0f)) with { Serial = 1 };
        SceneRagdoll second = Corpse(force: (300f, 0f, 0f)) with { Serial = 2 };

        PlayerGibs.Velocity(first, 0).ShouldNotBe(PlayerGibs.Velocity(second, 0));
    }

    private static SceneRagdoll Corpse(
        (float X, float Y, float Z)? force = null,
        (float X, float Y, float Z)? velocity = null) =>
        new(
            EntityIndex: 42,
            Serial: 7,
            PlayerClass: 5,
            Team: 2,
            X: 0f,
            Y: 0f,
            Z: 0f,
            Gib: true,
            Burning: false,
            FeignDeath: false,
            WasDisguised: false,
            FirstTick: 100,
            LastTick: 200,
            Force: force,
            Velocity: velocity);
}
