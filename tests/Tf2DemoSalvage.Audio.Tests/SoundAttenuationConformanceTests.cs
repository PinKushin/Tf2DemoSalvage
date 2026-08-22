using System;
using System.Globalization;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// The soundlevel scale and the audible radius, parsed from Valve's source and compared against ours.
/// </summary>
/// <remarks>
/// **Written before the mixer exists, which is the point.** The gain curve is closed and
/// unrecovered; the CUTOFF is published, and pinning it now means the mixer is built against a
/// measured boundary rather than against whatever it happens to produce.
///
/// Every constant is extracted rather than restated — the macros out of <c>soundflags.h</c>,
/// <c>SOUND_NORMAL_CLIP_DIST</c> out of <c>const.h</c>, and the radius expression out of the
/// server's recipient filter.
/// </remarks>
public sealed class SoundAttenuationConformanceTests
{
    private const string Flags = "src/public/soundflags.h";
    private const string Const = "src/public/const.h";
    private const string Filter = "src/game/server/recipientfilter.cpp";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void SoundLevel_Normal_IsValvesSndlvlNorm()
    {
        Match declared = Regex.Match(
            Sdk(Flags),
            @"SNDLVL_NORM\s*=\s*(?<value>\d+)",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        declared.Success.ShouldBeTrue("SNDLVL_NORM was not found in soundflags.h");

        SoundAttenuation.Normal.ShouldBe(
            int.Parse(declared.Groups["value"].Value, CultureInfo.InvariantCulture));
    }

    [Test]
    public void ClipDistance_OurConstant_IsValvesSoundNormalClipDist()
    {
        Match declared = Regex.Match(
            Sdk(Const),
            @"#define\s+SOUND_NORMAL_CLIP_DIST\s+(?<value>[0-9.]+)f",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        declared.Success.ShouldBeTrue("SOUND_NORMAL_CLIP_DIST was not found in const.h");

        SoundAttenuation.NormalClipDistance.ShouldBe(
            float.Parse(declared.Groups["value"].Value, CultureInfo.InvariantCulture));
    }

    [Test]
    public void FromSoundLevel_AcrossTheScale_MatchesValvesMacro()
    {
        // The macro, transcribed once here so the comparison is against Valve's TEXT rather than
        // against a second copy of my own arithmetic:
        //
        //   #define SNDLVL_TO_ATTN( a ) ((a > 50) ? (20.0f / (float)(a - 50)) : 4.0)
        Sdk(Flags).ShouldContain(
            "#define SNDLVL_TO_ATTN( a ) ((a > 50) ? (20.0f / (float)(a - 50)) : 4.0 )",
            Case.Sensitive,
            "the macro has changed, so the values below are no longer Valve's");

        // **Both sides of the branch, and the boundary itself.** A test that only sampled above 50
        // would pass against an implementation with no clamp at all, and the clamp is the half that
        // prevents a divide by zero.
        SoundAttenuation.FromSoundLevel(75).ShouldBe(0.8f, 1e-5f);
        SoundAttenuation.FromSoundLevel(70).ShouldBe(1f, 1e-5f);
        SoundAttenuation.FromSoundLevel(51).ShouldBe(20f, 1e-5f);

        SoundAttenuation.FromSoundLevel(50).ShouldBe(4f, "at and below 50 Valve clamps");
        SoundAttenuation.FromSoundLevel(0).ShouldBe(4f);
    }

    [Test]
    public void AudibleRadius_IsTwiceTheClipDistanceOverAttenuation()
    {
        // The expression, from the server's own recipient filter — the code that decides who is
        // sent a sound at all:
        //
        //   maxAudible = ( 2 * SOUND_NORMAL_CLIP_DIST ) / attenuation;
        Sdk(Filter).ShouldContain(
            "maxAudible = ( 2 * SOUND_NORMAL_CLIP_DIST ) / attenuation;",
            Case.Sensitive);

        // At SNDLVL_NORM: attenuation 0.8, so 2000 / 0.8 = 2500 units.
        float normal = SoundAttenuation.FromSoundLevel(SoundAttenuation.Normal);

        SoundAttenuation.AudibleRadius(normal).ShouldBe(2500f, 0.01f);

        // The clamped end is the SHORTEST radius, not the longest — worth asserting because the
        // intuition runs the other way: a low soundlevel is a quiet sound, and a quiet sound does
        // not carry.
        SoundAttenuation.AudibleRadius(SoundAttenuation.FromSoundLevel(0)).ShouldBe(500f, 0.01f);
    }

    [Test]
    public void AudibleRadius_AtAttenuationZero_IsUnboundedRatherThanZero()
    {
        // **Valve's `Filter` returns EARLY when attenuation is zero**, leaving every recipient in —
        // so zero means ATTN_NONE, a sound audible anywhere the PVS reaches. Reading it as a radius
        // of zero would silence exactly the sounds meant to carry, and it is the kind of inversion
        // that produces a plausible mix rather than an error.
        Sdk(Filter).ShouldContain("if ( attenuation <= 0 )", Case.Sensitive);

        float.IsPositiveInfinity(SoundAttenuation.AudibleRadius(0f)).ShouldBeTrue();
        float.IsPositiveInfinity(SoundAttenuation.AudibleRadius(-1f)).ShouldBeTrue();

        // The control: an ordinary attenuation must NOT be unbounded, or the assertion above is
        // satisfied by a function that always returns infinity.
        float.IsFinite(SoundAttenuation.AudibleRadius(0.8f)).ShouldBeTrue();
    }

    [Test]
    public void ToSoundLevel_IsValvesInverse_AndIsLossyByConstruction()
    {
        Sdk(Flags).ShouldContain(
            "#define ATTN_TO_SNDLVL( a ) (soundlevel_t)(int)((a) ? (50 + 20 / ((float)a)) : 0 )",
            Case.Sensitive);

        SoundAttenuation.ToSoundLevel(0.8f).ShouldBe(75);
        SoundAttenuation.ToSoundLevel(1f).ShouldBe(70);
        SoundAttenuation.ToSoundLevel(0f).ShouldBe(0);

        // **Not an exact inverse, and that is Valve's design rather than our defect.** The cast
        // truncates, and the clamp below 50 is not reversible at all — every level at or under 50
        // maps to attenuation 4, which maps back to 55. Asserted so nobody later "fixes" the
        // round trip and diverges from the engine.
        SoundAttenuation.ToSoundLevel(SoundAttenuation.FromSoundLevel(30)).ShouldBe(55);
        SoundAttenuation.ToSoundLevel(SoundAttenuation.FromSoundLevel(50)).ShouldBe(55);
    }

    [Test]
    public void MaximumAttenuation_IsWhatEightBitsCanCarry()
    {
        // 255 / 64 = 3.984375, and Valve's comment says exactly that: the wire sends
        // attenuation * 64 in eight bits.
        Sdk(Flags).ShouldContain("#define MAX_ATTENUATION\t\t3.98f", Case.Sensitive);

        SoundAttenuation.Maximum.ShouldBe(3.98f);
        SoundAttenuation.Maximum.ShouldBeLessThan(255f / 64f);
    }

    /// <summary>Reads an SDK file, or fails loudly.</summary>
    private static string Sdk(string path) =>
        SourceSdk.Text(path) ?? throw new InvalidOperationException($"{path} is missing from the SDK");
}
