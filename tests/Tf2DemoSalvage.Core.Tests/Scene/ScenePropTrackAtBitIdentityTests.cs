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
    /// <summary>The fingerprint on Windows x64, where the viewer ships.</summary>
    /// <remarks>
    /// Main's was 0x17B4E77030C3A479 at 077d0175. **Re-recorded for B629** (2026-11-15): <c>SlerpAngles</c> became Valve's
    /// <c>QuaternionSlerp</c> rather than <c>System.Numerics</c>'s, a deliberate change of bits. Control: with that one call
    /// put back the hash returned to 0x17B4E77030C3A479, so the move is the slerp and nothing else. **Re-recorded for B870
    /// review 2** (2026-11-26): a linear blend and the spline's time fixup became Valve's `Lerp`, `A + (B - A) * flPercent`
    /// (`mathlib.h:629-635`), where `float.Lerp` rounds as `A * (1 - t) + B * t`. Control: with those two lines put back
    /// the hash returned to 0xB2B825C3641C72CA.
    /// </remarks>
    private const ulong ExpectedWindowsX64 = 0x4748661BCF2AE5AAUL;


    [Test]
    public void At_SeededHistories_MatchMainsFingerprintBitForBit()
    {
        ulong? expected = ExpectedForThisPlatform();

        ulong actual = Fingerprint();

        if (expected is null)
        {
            Assert.Ignore(
                $"No fingerprint is recorded on this platform; the math library differs per platform. This run's: 0x{actual:X16}.");
        }

        actual.ShouldBe(expected.Value, $"0x{actual:X16}");
    }

    private static ulong? ExpectedForThisPlatform()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            return ExpectedWindowsX64;
        }

        // Linux x64 (CI) was 0x373E7F84708C0B4C before B629, computed by BOTH the pre-B593 code (run 40287828599 on branch
        // ci-probe/b593-linux-old) and the copy-free code (run 40285862681); its math library rounds some sin/cos/atan2
        // results differently from Windows, so one hash cannot hold on both. **Not yet re-recorded after B629**: no value
        // until a CI run reports one in its ignore message.
        return null;
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

        // **Every sequence loops**, which is how every cycle was blended when the fingerprint was recorded; B726 made a
        // track's flag its model's, and a track never told one blends as a one-shot. Told here, the instrument keeps
        // measuring what it was built for — the copy-free `At` against the copying one — with the cycle on the same path.
        track.OnNewModel([], at: 0, staticPropModel: false, sequenceLoops: _ => true);

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
