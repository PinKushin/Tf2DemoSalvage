using System;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Rebuilding a hitscan shot from its seed — <c>FX_FireBullets</c> (B415, `docs/findings/57`).</summary>
/// <remarks>
/// **`DT_TEFireBullets` sends no bullet paths**, so every pellet's direction is reconstructed from <c>m_iSeed</c>. The
/// loop, from `tf_fx_shared.cpp`:
///
/// <code>
/// RandomSeed( iSeed );                                    // reseeded EVERY bullet
/// x = RandomFloat(-v, v) + RandomFloat(-v, v);            // v = 0.5 by default
/// y = RandomFloat(-v, v) + RandomFloat(-v, v);
/// dir = vecShootForward + (x * flSpread * vecShootRight) + (y * flSpread * vecShootUp);
/// dir.NormalizeInPlace();
/// ++iSeed;                                                // the next bullet's seed
/// </code>
///
/// **No oracle exists for this loop and that is recorded rather than glossed.** `FX_FireBullets` lives in `client.dll`
/// and is not exported; calling it would need a live `CTFPlayer`, a weapon-info handle and the whole entity list, which
/// is a different order of work from the `vstdlib-random` probe that pins the RNG underneath it. These tests therefore
/// check the SHAPE of the reconstruction — that zero spread is forward exactly, that the seed advances per bullet, that
/// the draws are taken in the documented order and count — against a stream that IS oracle-verified. What they cannot
/// prove is that Valve composes them in this order; that is read from the source above and nothing here re-checks it.
/// </remarks>
public sealed class FireBulletsSpreadConformanceTests
{
    /// <remarks>
    /// With no spread the random draws are multiplied by zero, so the direction must be the aim vector EXACTLY — the one
    /// case where the answer is known without reference to the RNG at all.
    /// </remarks>
    [Test]
    public void Directions_WithNoSpread_AreTheAimVectorExactly()
    {
        (float X, float Y, float Z)[] shot =
            FireBulletsSpread.Directions(pitch: 0f, yaw: 0f, spread: 0f, seed: 1234, bullets: 1);

        (float X, float Y, float Z) forward = AngleVectors.Forward(0f, 0f);

        shot.Length.ShouldBe(1);
        shot[0].X.ShouldBe(forward.X, 1e-6f);
        shot[0].Y.ShouldBe(forward.Y, 1e-6f);
        shot[0].Z.ShouldBe(forward.Z, 1e-6f);
    }

    /// <remarks>
    /// **A shotgun's pellets are a seed RUN.** `++iSeed` at the bottom of the loop means bullet <c>n</c> is seeded at
    /// <c>seed + n</c> — so a nine-pellet blast is nine different streams, not one stream read nine times. Reading it
    /// the other way gives every pellet the same direction, which is the failure this pins.
    /// </remarks>
    [Test]
    public void Directions_ForAShotgunBlast_UseOneSeedPerPelletAndAllDiffer()
    {
        (float X, float Y, float Z)[] shot =
            FireBulletsSpread.Directions(pitch: 0f, yaw: 0f, spread: 0.08f, seed: 7, bullets: 9);

        shot.Length.ShouldBe(9);

        for (int pellet = 1; pellet < shot.Length; pellet++)
        {
            shot[pellet].ShouldNotBe(shot[0], $"pellet {pellet} is seeded at seed + {pellet}");
        }
    }

    /// <remarks>
    /// The same shot asked for twice is the same shot — the property the whole reconstruction rests on, since the demo
    /// carries only the seed and every viewer must rebuild the identical spread.
    /// </remarks>
    [Test]
    public void Directions_AskedTwice_AreIdentical()
    {
        (float X, float Y, float Z)[] first =
            FireBulletsSpread.Directions(pitch: 10f, yaw: -35f, spread: 0.05f, seed: 99, bullets: 6);
        (float X, float Y, float Z)[] second =
            FireBulletsSpread.Directions(pitch: 10f, yaw: -35f, spread: 0.05f, seed: 99, bullets: 6);

        second.ShouldBe(first);
    }

    /// <remarks>
    /// **Four draws per bullet, in the order x, x, y, y** — two per axis, which makes the spread triangular rather than
    /// uniform. A single draw per axis would put far too many pellets at the edge of the cone, and would leave the
    /// stream two draws further back for the next bullet if the seed were not reset. Checked against a separately
    /// seeded copy of the oracle-verified stream.
    /// </remarks>
    [Test]
    public void Directions_ForOneBullet_TakeTwoDrawsPerAxisFromTheSeedsOwnStream()
    {
        const int Seed = 4242;
        const float Spread = 0.1f;

        UniformRandomStream stream = new();
        stream.SetSeed(Seed);

        float x = stream.RandomFloat(-0.5f, 0.5f) + stream.RandomFloat(-0.5f, 0.5f);
        float y = stream.RandomFloat(-0.5f, 0.5f) + stream.RandomFloat(-0.5f, 0.5f);

        (float X, float Y, float Z) forward = AngleVectors.Forward(0f, 0f);
        (float X, float Y, float Z) right = AngleVectors.Right(0f, 0f, 0f);
        (float X, float Y, float Z) up = AngleVectors.Up(0f, 0f, 0f);

        (float X, float Y, float Z) expected = Normalized((
            forward.X + (x * Spread * right.X) + (y * Spread * up.X),
            forward.Y + (x * Spread * right.Y) + (y * Spread * up.Y),
            forward.Z + (x * Spread * right.Z) + (y * Spread * up.Z)));

        (float X, float Y, float Z) drawn =
            FireBulletsSpread.Directions(pitch: 0f, yaw: 0f, spread: Spread, seed: Seed, bullets: 1)[0];

        drawn.X.ShouldBe(expected.X, 1e-6f);
        drawn.Y.ShouldBe(expected.Y, 1e-6f);
        drawn.Z.ShouldBe(expected.Z, 1e-6f);
    }

    /// <remarks><c>NormalizeInPlace</c> is the loop's last act, so every direction is a unit vector.</remarks>
    [Test]
    public void Directions_WithSpread_AreUnitVectors()
    {
        foreach ((float X, float Y, float Z) direction in
            FireBulletsSpread.Directions(pitch: -12f, yaw: 71f, spread: 0.12f, seed: 31337, bullets: 12))
        {
            float length = MathF.Sqrt(
                (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));

            length.ShouldBe(1f, 1e-5f);
        }
    }

    private static (float X, float Y, float Z) Normalized((float X, float Y, float Z) vector)
    {
        float length = MathF.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y) + (vector.Z * vector.Z));

        return (vector.X / length, vector.Y / length, vector.Z / length);
    }
}
