using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <see cref="ScenePropTrack.At(double)"/> is bit-identical to the pose-copying version it replaced (B593).
/// </summary>
/// <remarks>
/// **A fingerprint, recorded from main's `At` at 077d0175 before the copy-free version existed** — the
/// instrument B592 used. Every output bit of every sample of thousands of seeded tracks is folded into one
/// FNV-1a hash, so a single ulp anywhere reddens it.
///
/// **The trim is what reaches the no-animation-bracket fallback**, and nothing else does: the first `Add`
/// always appends to both histories at one arrival tick, so the animation history can be empty-by-now only
/// after <c>TrimBefore</c> kept fewer early animation entries than simulation ones and a moment before the
/// cut is asked. Control: dropping that fallback's third sample — fd33bc3f's cut — reddens this test
/// (0xABA11379C55716C3). Hermite through three equal cycles differs from the lerp of two by an ulp in about
/// 19% of random (cycle, fraction) pairs, measured over ten million, so that cut was a real drift.
/// </remarks>
public sealed class ScenePropTrackAtBitIdentityTests
{
    /// <summary>Main's fingerprint at 077d0175.</summary>
    private const ulong Expected = 0x17B4E77030C3A479UL;

    [Test]
    public void At_SeededHistories_MatchMainsFingerprintBitForBit()
    {
        ulong actual = Fingerprint();

        actual.ShouldBe(Expected, $"0x{actual:X16}");
    }

    private static ulong Fingerprint()
    {
        Random random = new(593);
        ulong hash = 14695981039346656037UL;

        for (int track = 0; track < 8000; track++)
        {
            ScenePropTrack subject = Build(random);

            for (double target = -5; target < 100; target += 0.13)
            {
                if (subject.At(target) is not { } pose)
                {
                    hash = Fold(hash, -1);
                    continue;
                }

                hash = Fold(hash, BitConverter.SingleToInt32Bits(pose.X));
                hash = Fold(hash, BitConverter.SingleToInt32Bits(pose.Y));
                hash = Fold(hash, BitConverter.SingleToInt32Bits(pose.Z));
                hash = Fold(hash, BitConverter.SingleToInt32Bits(pose.Pitch));
                hash = Fold(hash, BitConverter.SingleToInt32Bits(pose.Yaw));
                hash = Fold(hash, BitConverter.SingleToInt32Bits(pose.Roll));
                hash = Fold(hash, BitConverter.SingleToInt32Bits(pose.Cycle));
                hash = Fold(hash, pose.Sequence);
            }
        }

        return hash;
    }

    /// <summary>A track whose two clocks, stillness, sequences, wrapping cycles and spacing are all seeded.</summary>
    private static ScenePropTrack Build(Random random)
    {
        ScenePropTrack track = new(1, "models/props/barrel.mdl");
        int count = random.Next(1, 14);
        int tick = random.Next(0, 10);
        int applied = tick - random.Next(0, 3);
        int animated = tick + random.Next(-20, 20);
        float x = 0f;
        float pitch = 0f;
        float cycle = (float)random.NextDouble();
        int sequence = 3;

        for (int index = 0; index < count; index++)
        {
            tick += random.Next(1, 8);

            // Still at an unchanged simulation time: no simulation entry, while the animation clock still
            // appends. That is the sparse-simulation, dense-animation shape the trim below needs.
            if (random.Next(3) != 0)
            {
                // Zero steps on either clock repeat a changetime: the zero-interval and TimeFixup-refusal cases.
                applied += random.Next(0, 6);
                x += (float)((random.NextDouble() * 50) - 25);
                pitch = (float)(random.NextDouble() * 90);
            }

            animated += random.Next(0, 6);
            cycle = (cycle + (float)(random.NextDouble() * 0.6)) % 1f;

            if (random.Next(6) == 0)
            {
                sequence = 3 + random.Next(2);
            }

            track.Add(
                tick,
                new ScenePose
                {
                    X = x, Y = x * 0.5f, Z = 1f, Pitch = pitch, Yaw = -pitch, Roll = 0f,
                    Cycle = cycle, Sequence = sequence,
                },
                applied,
                animated);
        }

        if (random.Next(2) == 0)
        {
            track.TrimBefore(random.Next(0, tick + 1));
        }

        return track;
    }

    private static ulong Fold(ulong hash, int value)
    {
        for (int shift = 0; shift < 32; shift += 8)
        {
            hash = (hash ^ (uint)((value >> shift) & 0xFF)) * 1099511628211UL;
        }

        return hash;
    }
}
