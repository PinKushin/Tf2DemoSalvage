using System;

using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// How engine.dll splits a channel across a two-speaker device opened as headphones, the default (B724).
/// </summary>
/// <remarks>
/// Read in disassembly, x64 <c>engine.dll</c> build 11191711.
/// <code>
/// FUN_18001e0b0   the panner SND_Spatialize calls (0x18002a282). Angles of the direction to the sound (FUN_18027add0,
///                 VectorAngles) and of the listener's RIGHT vector (0x18048de10, written by AngleVectors at 0x180030be7)
///                 flattened to the ground; bearing = sound yaw − right yaw, + 360 when negative. A two-channel device
///                 (+0x10 &lt; 3) with +0x1d set calls FUN_18001efb0 once per ear: angle 0 into speaker 1 (right), 180
///                 into speaker 0 (left).
/// FUN_18001efb0   d = |bearing − angle|, 360 − d past 180; share = 1 − FastPow(d × 0.0055555557, 1.5); the elevation
///                 (pitch folded to 0..90) past 45 raises the mono weight by (e − 45) × 0.022222223, clamped 0..1.
/// FUN_18001f980   for two channels: share + (0.9 − share) × mono.
/// FUN_18027b2a0   FastPow: log2 from the float's bits (× 2^-23 at 0x180480040, − 127 at 0x18038e04c, corrected by
///                 (f − f²) × 0.346607 at 0x1803d61f4), × power, back by (y + 127 − (t − t²) × 0.33971 at 0x1803d61f0)
///                 × 2^23 (0x18048003c) as the bits. 0 for x ≤ 2^-23.
/// </code>
/// **Headphones are the default**: <c>snd_surround_speakers</c> is "-1" (registered by <c>FUN_180001f20</c>), the sound
/// init (<c>FUN_18002c300</c>) asks for max(it, 0) = 0 speakers, and the XAudio2 device (<c>FUN_1802807a0</c>) sets
/// +0x1d when 0 was asked, with two channels (<c>FUN_18027fe80</c>). The <c>snd_headphone_pan_*</c> cvars are vestigial:
/// their callback (<c>FUN_18001fc40</c>) writes <c>0x1804620b8</c>, which nothing reads; the panner's 1.5 is a constant.
/// </remarks>
public sealed class SpeakerPanConformanceTests
{
    /// <summary>A listener looking down +X, whose right is −Y.</summary>
    private static readonly (float X, float Y, float Z) Right = (0f, -1f, 0f);

    /// <summary>1 − FastPow(0.5, 1.5): an ear 90° from the sound.</summary>
    private static readonly float Square = 1f - BitConverter.Int32BitsToSingle(0x3eb52100);

    [TestCase(1f, 1.5f, 0x3f800000)]
    [TestCase(0.25f, 1.5f, 0x3e000000)]
    [TestCase(0.5f, 1.5f, 0x3eb52100)]
    [TestCase(0.75f, 1.5f, 0x3f266480)]
    [TestCase(0.3f, 1.5f, 0x3e26c500)]
    [TestCase(0f, 1.5f, 0x00000000)]
    [TestCase(1.1920929e-7f, 1.5f, 0x00000000)]
    public void FastPow_ByTheEnginesBitArithmetic_IsTheseBits(float x, float power, int bits)
    {
        Assert.That(BitConverter.SingleToInt32Bits(SpeakerPan.FastPow(x, power)), Is.EqualTo(bits));
    }

    [TestCase(1f, 0f, 0f, nameof(Square), nameof(Square))]
    [TestCase(-1f, 0f, 0f, nameof(Square), nameof(Square))]
    [TestCase(0f, -1f, 0f, "0", "1")]
    [TestCase(0f, 1f, 0f, "1", "0")]
    public void Headphones_AtABearing_IsTheEarsShare(float x, float y, float z, string left, string right)
    {
        (float l, float r) = SpeakerPan.Headphones((x, y, z), Right, mono: 0f);

        Assert.Multiple(() =>
        {
            Assert.That(l, Is.EqualTo(Value(left)));
            Assert.That(r, Is.EqualTo(Value(right)));
        });
    }

    [Test]
    public void Headphones_FortyFiveDegreesRight_IsOneMinusEachEarsFastPow()
    {
        // Bearing 45: the right ear is 45° away (0.25 → FastPow 0.125), the left 135° (0.75).
        (float l, float r) = SpeakerPan.Headphones((1f, -1f, 0f), Right, mono: 0f);

        Assert.Multiple(() =>
        {
            Assert.That(r, Is.EqualTo(0.875f));
            Assert.That(l, Is.EqualTo(1f - BitConverter.Int32BitsToSingle(0x3f266480)));
        });
    }

    [Test]
    public void Headphones_FullyMono_IsPointNineInBothEars()
    {
        (float l, float r) = SpeakerPan.Headphones((0f, -1f, 0f), Right, mono: 1f);

        Assert.Multiple(() =>
        {
            Assert.That(l, Is.EqualTo(0.9f));
            Assert.That(r, Is.EqualTo(0.9f));
        });
    }

    [TestCase(0f, 0f, 1f)]
    [TestCase(0f, 0f, -1f)]
    [TestCase(0f, 0f, 0f)]
    public void Headphones_StraightUpDownOrAtTheListener_IsFullyMono(float x, float y, float z)
    {
        // Elevation 90 adds (90 − 45) × 0.022222223 to the mono weight, clamped to 1; a zero direction has pitch 90.
        (float l, float r) = SpeakerPan.Headphones((x, y, z), Right, mono: 0f);

        Assert.Multiple(() =>
        {
            Assert.That(l, Is.EqualTo(0.9f).Within(1e-7f));
            Assert.That(r, Is.EqualTo(0.9f).Within(1e-7f));
        });
    }

    [Test]
    public void Headphones_SixtyDegreesUpAhead_BlendsAThirdTowardMono()
    {
        // Pitch 60: mono 15 × 0.022222223; each ear 90° off, so share + (0.9 − share) × 0.33333334.
        (float l, float r) = SpeakerPan.Headphones((0.5f, 0f, 0.8660254f), Right, mono: 0f);
        float expected = ((0.9f - Square) * (15f * 0.022222223f)) + Square;

        Assert.Multiple(() =>
        {
            Assert.That(l, Is.EqualTo(expected).Within(1e-6f));
            Assert.That(r, Is.EqualTo(expected).Within(1e-6f));
            Assert.That(l, Is.EqualTo(0.7308217f).Within(1e-6f));
        });
    }

    [TestCase(1f, 0f, -1f)]
    [TestCase(0.8660254f, 0f, -0.5f)]
    [TestCase(1f, 0f, 1f)]
    public void Headphones_AtOrBelowFortyFiveDegreesUpOrDown_KeepsTheMonoItWasGiven(float x, float y, float z)
    {
        // Pitch 45 (down 45 is 45; up 45 is 315, folded to 45) is not past 45, and 30 down is 30: no mono added, so each
        // ear is 90° off and keeps its share.
        (float l, float r) = SpeakerPan.Headphones((x, y, z), Right, mono: 0f);

        Assert.Multiple(() =>
        {
            Assert.That(l, Is.EqualTo(Square).Within(1e-6f));
            Assert.That(r, Is.EqualTo(Square).Within(1e-6f));
        });
    }

    [Test]
    public void Headphones_AnUnnormalisedRightVector_IsReadForItsYawOnly()
    {
        (float l, float r) = SpeakerPan.Headphones((0f, -5f, 0f), (0f, -40f, 3f), mono: 0f);

        Assert.Multiple(() =>
        {
            Assert.That(l, Is.EqualTo(0f));
            Assert.That(r, Is.EqualTo(1f));
        });
    }

    [TestCase(0, "", false, 1f)]
    [TestCase(0, ")weapons/x.wav", true, 1f)]
    [TestCase(75, "weapons/x.wav", false, 0f)]
    [TestCase(75, "@weapons/x.wav", false, 1f)]
    [TestCase(75, "^@<weapons/x.wav", false, 1f)]
    [TestCase(75, ")@weapons/x.wav", false, 0f)]
    [TestCase(75, "@)weapons/x.wav", false, 0f)]
    [TestCase(75, "w@eapons/x.wav", false, 0f)]
    [TestCase(75, "weapons/x.wav", true, 1f)]
    [TestCase(75, ")weapons/x.wav", true, 0f)]
    [TestCase(75, "^weapons/x.wav", true, 0f)]
    [TestCase(75, "#music/x.wav", true, 0f)]
    public void Mono_ForASoundLevelNameAndWave_IsTheEnginesStartingWeight(
        int soundLevel, string name, bool stereo, float expected)
    {
        // SND_Spatialize: 1 when the distance multiplier (+0x100) is 0, as soundlevel 0's is, or the channel's spatial
        // character (+0x12a) is '@'. FUN_18002dc10 sets it from the leading sound characters, ')' over '@' over '^' over
        // '<' over '>' wherever each stands; FUN_18002db70 makes a stereo wave '@' when it has none, is not '#' music, has
        // a distance multiplier, and is not the local player's (that last not yet carried here).
        Assert.That(SpeakerPan.Mono(soundLevel, name, stereo), Is.EqualTo(expected));
    }

    private static float Value(string name) => name switch
    {
        nameof(Square) => Square,
        "0" => 0f,
        _ => 1f,
    };
}
