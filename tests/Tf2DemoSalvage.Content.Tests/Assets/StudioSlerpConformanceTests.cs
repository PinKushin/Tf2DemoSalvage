using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary><see cref="StudioBones.Slerp"/> against Valve's <c>QuaternionSlerp</c> (<c>mathlib_base.cpp:1605-1658</c>), B629.</summary>
/// <remarks>
/// **Valve aligns, then slerps without aligning**: <c>QuaternionAlign</c> negates <c>q</c> when the sum of squared
/// differences beats the sum of squared sums (<c>:1509-1538</c>), and <c>QuaternionSlerpNoAlign</c> weighs the two by
/// <c>sin( (1 - t) ω ) / sin ω</c> and <c>sin( t ω ) / sin ω</c> (<c>:1631-1632</c>). At <c>t = 0</c> the first is a value
/// divided by itself, exactly 1, and the second is exactly 0, so the result is <c>p</c> to the bit; at <c>t = 1</c> it is
/// the aligned <c>q</c> to the bit. <c>System.Numerics.Quaternion.Slerp</c> multiplies by <c>1 / sin ω</c> instead, which is
/// not exact. The subjects are seeded random unit rotations, half of them more than a right angle apart, so both arms of
/// the alignment are reached.
/// </remarks>
public sealed class StudioSlerpConformanceTests
{
    private const int Pairs = 2000;

    [Test]
    public void Slerp_AtZero_ReturnsTheFromRotationBitForBit()
    {
        Random draws = new(629);
        int wrong = 0;
        int flipped = 0;

        for (int pair = 0; pair < Pairs; pair++)
        {
            (float X, float Y, float Z, float W) from = Unit(draws);
            (float X, float Y, float Z, float W) to = Unit(draws);

            flipped += Dot(from, to) < 0f ? 1 : 0;
            wrong += Bits(StudioBones.Slerp(from, to, 0f)) == Bits(from) ? 0 : 1;
        }

        flipped.ShouldBeGreaterThan(Pairs / 4, "the control: the alignment's negating arm was reached");
        wrong.ShouldBe(0);
    }

    [Test]
    public void Slerp_AtOne_ReturnsTheAlignedToRotationBitForBit()
    {
        Random draws = new(1629);
        int wrong = 0;
        int flipped = 0;

        for (int pair = 0; pair < Pairs; pair++)
        {
            (float X, float Y, float Z, float W) from = Unit(draws);
            (float X, float Y, float Z, float W) to = Unit(draws);
            (float X, float Y, float Z, float W) aligned = Aligned(from, to);

            flipped += Bits(aligned) == Bits(to) ? 0 : 1;
            wrong += Bits(StudioBones.Slerp(from, to, 1f)) == Bits(aligned) ? 0 : 1;
        }

        flipped.ShouldBeGreaterThan(Pairs / 4, "the control: the alignment negated the target");
        wrong.ShouldBe(0);
    }

    /// <remarks>
    /// **`QuaternionBlend` is a normalised lerp** (<c>mathlib_base.cpp:1545-1576</c>), which `CalcDecompressedAnimation`
    /// uses for an IK rule's error (<c>bone_setup.cpp:638</c>). A quarter of the way from identity to 90° about Z: the lerp
    /// gives (0, 0, ¼ sin 45°, ¾ + ¼ cos 45°), normalised, a turn of 2·atan( 0.1767767 / 0.9267767 ) = 21.598°, where a
    /// slerp gives 22.5°, the control.
    /// </remarks>
    [Test]
    public void Blend_AQuarterWayToARightAngle_IsTheNormalisedLerpNotTheSlerp()
    {
        (float X, float Y, float Z, float W) identity = (0f, 0f, 0f, 1f);
        (float X, float Y, float Z, float W) right = (0f, 0f, MathF.Sin(MathF.PI / 4f), MathF.Cos(MathF.PI / 4f));

        float Degrees((float X, float Y, float Z, float W) q) => 2f * MathF.Atan2(q.Z, q.W) * 180f / MathF.PI;

        Degrees(Core.Scene.ValveQuaternion.Slerp(identity, right, 0.25f)).ShouldBe(22.5f, 0.001f, "the control: the slerp");
        Degrees(Core.Scene.ValveQuaternion.Blend(identity, right, 0.25f)).ShouldBe(21.598f, 0.001f);
    }

    /// <summary><c>QuaternionAlign</c>, as written at <c>mathlib_base.cpp:1515-1530</c>.</summary>
    private static (float X, float Y, float Z, float W) Aligned((float X, float Y, float Z, float W) p, (float X, float Y, float Z, float W) q)
    {
        float[] pp = [p.X, p.Y, p.Z, p.W];
        float[] qq = [q.X, q.Y, q.Z, q.W];
        float a = 0f;
        float b = 0f;

        for (int i = 0; i < 4; i++)
        {
            a += (pp[i] - qq[i]) * (pp[i] - qq[i]);
            b += (pp[i] + qq[i]) * (pp[i] + qq[i]);
        }

        return a > b ? (-q.X, -q.Y, -q.Z, -q.W) : q;
    }

    private static float Dot((float X, float Y, float Z, float W) p, (float X, float Y, float Z, float W) q) =>
        (p.X * q.X) + (p.Y * q.Y) + (p.Z * q.Z) + (p.W * q.W);

    private static (int, int, int, int) Bits((float X, float Y, float Z, float W) q) =>
        (BitConverter.SingleToInt32Bits(q.X), BitConverter.SingleToInt32Bits(q.Y),
            BitConverter.SingleToInt32Bits(q.Z), BitConverter.SingleToInt32Bits(q.W));

    private static (float X, float Y, float Z, float W) Unit(Random draws)
    {
        float x = (float)((draws.NextDouble() * 2.0) - 1.0);
        float y = (float)((draws.NextDouble() * 2.0) - 1.0);
        float z = (float)((draws.NextDouble() * 2.0) - 1.0);
        float w = (float)((draws.NextDouble() * 2.0) - 1.0);
        float length = MathF.Sqrt((x * x) + (y * y) + (z * z) + (w * w));

        return (x / length, y / length, z / length, w / length);
    }
}
