using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's per-step speed and spin damping — <c>FUN_180077a20</c> (B58, D146, B369).
/// </summary>
/// <remarks>
/// **Pinned to Valve's binary, not to a reading of it.** The `vphysics-math` probe's `damping` mode loaded the game's
/// `vphysics.dll` on 2026-09-13 and called `FUN_180077a20` itself on a zeroed stand-in core — the routine reads its three
/// rotation factors through a pointer and writes only `+0x130..0x148` — with angular velocity `(10, 20, 30)` and velocity
/// `(100, 200, 300)`, and printed the bits it left. Each case below is one of those calls.
///
/// **The cases straddle both thresholds**: `27.2` and `16.6` at `0.015` sit just under the rotational sum's `0.5` and the
/// speed product's `0.25`, `27.3` and `16.7` just over, so each branch of each half is exercised against its neighbour.
/// The port these replace used `MathF.Exp` for all three lanes and a float speed factor, and passed tests written with a
/// tolerance of `1e-4`.
/// </remarks>
public sealed class IvpDampingConformanceTests
{
    /// <remarks>
    /// Rotation factor, speed damping and step as float bits, then the angular velocity and velocity the binary left.
    ///
    /// **The last two factors were found by search, because the first six cannot see the lanes.** For every one of them
    /// `expf(−a)·10` and `(float)exp(−a)·10` round to the same bits, so a port that damped the `x` lane through `exp` passed —
    /// the sabotage that tried it reddened nothing. `27.6396427` and `27.7169571` at `0.015` are the first factors over the
    /// rotational threshold where the two lanes part, and the binary's `x` lane answers `…502` and `…648` where its `y` lane,
    /// through `exp`, answers the next float up.
    /// </remarks>
    [TestCase(0x40800000, 0x00000000, 0x3c783e10, 0x41164d93, 0x41964d93, 0x41e1745d, 0x42c80000, 0x43480000, 0x43960000)]
    [TestCase(0x41f00000, 0x41f00000, 0x3c783e10, 0x40cb1d9c, 0x414b1d9c, 0x41985635, 0x427de502, 0x42fde502, 0x433e6bc2)]
    [TestCase(0x41800000, 0x3dcccccd, 0x3c75c28f, 0x40f33333, 0x41733333, 0x41b66666, 0x42c7b333, 0x4347b333, 0x4395c666)]
    [TestCase(0x41d9999a, 0x4184cccd, 0x3c75c28f, 0x40bd70a4, 0x413d70a4, 0x418e147b, 0x42963333, 0x43163333, 0x43614ccd)]
    [TestCase(0x41da6666, 0x4185999a, 0x3c75c28f, 0x40d4796b, 0x4154796b, 0x419f5b10, 0x429baeab, 0x431baeab, 0x43698600)]
    [TestCase(0x40800000, 0x40800000, 0x3c75c28f, 0x41166666, 0x41966666, 0x41e1999a, 0x42bc0000, 0x433c0000, 0x438d0000)]
    [TestCase(0x41dd1dfd, 0x00000000, 0x3c75c28f, 0x40d36502, 0x41536503, 0x419e8bc2, 0x42c80000, 0x43480000, 0x43960000)]
    [TestCase(0x41ddbc54, 0x00000000, 0x3c75c28f, 0x40d32648, 0x4153264a, 0x419e5cb7, 0x42c80000, 0x43480000, 0x43960000)]
    public void Damp_AFactorSpeedAndStepTheBinaryWasGiven_LeavesItsBits(
        int rotation, int speed, int step, int spinX, int spinY, int spinZ, int velocityX, int velocityY, int velocityZ)
    {
        IvpRigidBody body = Moving();
        float factor = Single(rotation);

        IvpDamping.Damp(body, Single(step), (factor, factor, factor), Single(speed));

        Bits(body.AngularVelocity.X).ShouldBe(spinX);
        Bits(body.AngularVelocity.Y).ShouldBe(spinY);
        Bits(body.AngularVelocity.Z).ShouldBe(spinZ);
        Bits(body.Velocity.X).ShouldBe(velocityX);
        Bits(body.Velocity.Y).ShouldBe(velocityY);
        Bits(body.Velocity.Z).ShouldBe(velocityZ);
    }

    /// <remarks>
    /// **The controller hands each body's own numbers to the applier**, the step widened from float: a corpse's `16` and
    /// `0.1` at `0.015` leave the binary's bits for that call.
    /// </remarks>
    [Test]
    public void Apply_ABodysOwnDamping_LeavesTheBinarysBitsForThoseNumbers()
    {
        IvpRigidBody body = Moving();

        body.RotationDamping = 16f;
        body.Damping = 0.1f;

        IvpDamping.Apply([body], 0.015f);

        Bits(body.AngularVelocity.X).ShouldBe(0x40f33333);
        Bits(body.AngularVelocity.Z).ShouldBe(0x41b66666);
        Bits(body.Velocity.Y).ShouldBe(0x4347b333);
    }

    /// <remarks>
    /// **The control.** A body declaring no damping comes out untouched — the binary's own answer for `0` and `0` — so "damps
    /// correctly" and "scales everything by something" are different observations.
    /// </remarks>
    [Test]
    public void Apply_WithNoDamping_ChangesNothing()
    {
        IvpRigidBody body = Moving();

        body.Damping = 0f;
        body.RotationDamping = 0f;

        IvpDamping.Apply([body], 0.015f);

        Bits(body.Velocity.X).ShouldBe(0x42c80000);
        Bits(body.AngularVelocity.Z).ShouldBe(0x41f00000);
    }

    /// <remarks>
    /// **Bit `0x10` skips this as well as gravity**, because the engine calls it from inside that gate — `FUN_180074c80`
    /// tests the bit and then makes both helper calls and the add. A body that skipped gravity but was still damped would slow
    /// to a halt in mid-air.
    /// </remarks>
    [Test]
    public void Apply_ForABodyThatSkipsGravity_SkipsDampingToo()
    {
        IvpRigidBody body = Moving();

        body.SkipsGravity = true;
        body.RotationDamping = 4f;
        body.Damping = 4f;

        IvpDamping.Apply([body], 0.015f);

        body.Velocity.X.ShouldBe(100f);
        body.AngularVelocity.X.ShouldBe(10f);
    }

    private static IvpRigidBody Moving() =>
        new() { AngularVelocity = (10f, 20f, 30f), Velocity = (100f, 200f, 300f) };

    private static float Single(int bits) => System.BitConverter.Int32BitsToSingle(bits);

    private static int Bits(float value) => System.BitConverter.SingleToInt32Bits(value);
}
