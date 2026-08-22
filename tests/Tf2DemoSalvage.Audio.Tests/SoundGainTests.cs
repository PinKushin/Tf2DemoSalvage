using System;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// Distance gain and stereo placement, separating what Valve publishes from what this project chose.
/// </summary>
/// <remarks>
/// **The tests are split on purpose, by evidence class.** The cutoff and the attenuation mapping are
/// Valve's and are compared against the SDK; the falloff shape and the pan law are ours, so their
/// tests assert *properties* that any acceptable curve must hold — monotonic, bounded, continuous —
/// rather than pinning numbers that would have to be rewritten when the real curve is recovered
/// (B142).
///
/// That distinction matters more than it looks: a test asserting `AtDistance(75, 500) == 0.31f`
/// would be a change-detector for an interpolation this project has already flagged as provisional,
/// and would make replacing it look like a regression.
/// </remarks>
public sealed class SoundGainTests
{
    [Test]
    public void AtDistance_BeyondTheAudibleRadius_IsSilentAsTheEngineDefinesIt()
    {
        // **Valve's, and the strongest claim here.** recipientfilter.cpp computes
        // (2 * SOUND_NORMAL_CLIP_DIST) / attenuation as the distance past which the server does not
        // send the event at all — so it is a fact about audibility, not a mixing preference.
        float attenuation = SoundAttenuation.FromSoundLevel(SoundAttenuation.Normal);
        float radius = SoundAttenuation.AudibleRadius(attenuation);

        radius.ShouldBe(2500f, 0.01, "2 * 1000 / 0.8");

        SoundGain.AtDistance(SoundAttenuation.Normal, radius).ShouldBe(0f);
        SoundGain.AtDistance(SoundAttenuation.Normal, radius + 1f).ShouldBe(0f);
        SoundGain.AtDistance(SoundAttenuation.Normal, radius - 1f).ShouldBeGreaterThan(0f);
    }

    [Test]
    public void AtDistance_SoundLevelNone_IsFullVolumeEverywhereRatherThanSilent()
    {
        // **Valve's two macros disagree at zero, and this pins which reading we took.**
        // `ATTN_TO_SNDLVL(0)` is 0 and `recipientfilter.cpp` leaves every recipient in when
        // attenuation is zero — but `SNDLVL_TO_ATTN(0)` returns 4.0, near MAX_ATTENUATION, which
        // would make the sound intensely local instead. 676 shipped soundscript entries declare
        // SNDLVL_NONE, so the choice is not academic.
        //
        // Taken as unattenuated. B143 tracks confirming it against the binary; if it is wrong, this
        // test is what will have to change, which is the point of pinning it.
        SoundGain.AtDistance(0, 0f).ShouldBe(1f);
        SoundGain.AtDistance(0, 10_000f).ShouldBe(1f);
        SoundGain.AtDistance(0, 1_000_000f).ShouldBe(1f);
    }

    [Test]
    public void AtDistance_WithinTheReferenceDistance_IsFullVolume()
    {
        // snd_refdist = 36, recovered from engine.dll. Inside it there is nothing to attenuate.
        SoundGain.ReferenceDistance.ShouldBe(36f);

        SoundGain.AtDistance(SoundAttenuation.Normal, 0f).ShouldBe(1f);
        SoundGain.AtDistance(SoundAttenuation.Normal, 36f).ShouldBe(1f);
    }

    [Test]
    public void AtDistance_AcrossItsRange_FallsMonotonicallyAndStaysBounded()
    {
        // **A property, not a curve.** The shape is this project's interpolation (B142), so pinning
        // values would make replacing it look like a regression. What any acceptable curve must do
        // is never rise with distance and never leave 0..1 — and a wrong implementation breaks one
        // of those long before it gets the shape wrong.
        float previous = float.MaxValue;

        for (float distance = 0f; distance <= 3000f; distance += 25f)
        {
            float gain = SoundGain.AtDistance(SoundAttenuation.Normal, distance);

            gain.ShouldBeInRange(0f, 1f, $"at {distance}");
            gain.ShouldBeLessThanOrEqualTo(previous, $"gain rose between {distance - 25f} and {distance}");

            previous = gain;
        }

        previous.ShouldBe(0f, "and it has reached silence by the cutoff");
    }

    [Test]
    public void AtDistance_ALouderSound_CarriesFurtherThanAQuieterOne()
    {
        // The control on the whole model: soundlevel has to *do* something. A gunshot at SNDLVL 140
        // must beat an idle at SNDLVL 60 at the same distance, or the attenuation mapping is not
        // being consulted at all.
        //
        // **The cutoffs differ by 9x, which is what makes this decisive.** SNDLVL 60 gives
        // attenuation 2.0 and a 1,000-unit radius; SNDLVL 140 gives 0.22 and a 9,000-unit one. So a
        // distance beyond the idle's radius but inside the gunfire's separates them completely,
        // where a distance inside both would only compare two curve values.
        SoundAttenuation.AudibleRadius(SoundAttenuation.FromSoundLevel(60))
            .ShouldBe(1000f, 0.01);

        SoundGain.AtDistance(140, 900f).ShouldBeGreaterThan(SoundGain.AtDistance(60, 900f));

        SoundGain.AtDistance(60, 1200f).ShouldBe(0f, "past the idle sound's own cutoff");
        SoundGain.AtDistance(140, 1200f).ShouldBeGreaterThan(0f, "but well inside gunfire's");
    }

    [Test]
    public void AtDistance_NaNOrInfiniteDistance_IsNotNaN()
    {
        // A NaN gain silences a sound and reports nothing, which is indistinguishable from the
        // sound never having played. Origins come off the wire, so this is reachable.
        SoundGain.AtDistance(SoundAttenuation.Normal, float.NaN).ShouldBe(1f);
        SoundGain.AtDistance(SoundAttenuation.Normal, float.PositiveInfinity).ShouldBe(1f);
        SoundGain.AtDistance(SoundAttenuation.Normal, -5f).ShouldBe(1f);
    }

    [Test]
    public void Pan_AcrossTheSweep_HoldsConstantPower()
    {
        // left^2 + right^2 == 1 throughout, which is what stops a sound dipping in loudness as it
        // crosses the centre — the audible defect of naive linear panning.
        for (float rightward = -1f; rightward <= 1f; rightward += 0.05f)
        {
            (float left, float right) = SoundGain.Pan(rightward);

            ((left * left) + (right * right)).ShouldBe(1f, 0.0001, $"at {rightward}");
        }
    }

    [Test]
    public void Pan_TheThreeLandmarks_AreHardLeftCentreAndHardRight()
    {
        (float left, float right) = SoundGain.Pan(-1f);
        left.ShouldBe(1f, 0.0001);
        right.ShouldBe(0f, 0.0001);

        (left, right) = SoundGain.Pan(0f);
        left.ShouldBe(0.70710678f, 0.0001, "centre is 1/sqrt(2) on both, not 1.0 on both");
        right.ShouldBe(0.70710678f, 0.0001);

        (left, right) = SoundGain.Pan(1f);
        left.ShouldBe(0f, 0.0001);
        right.ShouldBe(1f, 0.0001);
    }

    [Test]
    public void Rightward_ASourceToTheRight_IsPositiveAndToTheLeftNegative()
    {
        // Valve's world is X forward, Y left, Z up, so a listener facing +X has a right vector of
        // (0,-1,0). A source at +Y is therefore to their LEFT.
        (float, float, float) listener = (0f, 0f, 0f);
        (float, float, float) right = (0f, -1f, 0f);

        SoundGain.Rightward(listener, right, (0f, -100f, 0f)).ShouldBe(1f, 0.0001);
        SoundGain.Rightward(listener, right, (0f, 100f, 0f)).ShouldBe(-1f, 0.0001);
    }

    [Test]
    public void Rightward_ASourceDirectlyAheadOrBehind_IsCentred()
    {
        // **The control that catches using the forward vector by mistake.** With the right vector,
        // a source ahead and a source behind both give 0. With the forward vector they would give
        // +1 and -1 — a mix that swings as the listener walks toward a sound and stays centred as
        // they turn past it, which sounds like a bug in the demo rather than in the mixer.
        (float, float, float) listener = (0f, 0f, 0f);
        (float, float, float) right = (0f, -1f, 0f);

        SoundGain.Rightward(listener, right, (500f, 0f, 0f)).ShouldBe(0f, 0.0001);
        SoundGain.Rightward(listener, right, (-500f, 0f, 0f)).ShouldBe(0f, 0.0001);
    }

    [Test]
    public void Rightward_ASourceOnTheListener_IsCentredRatherThanNaN()
    {
        // Dividing by a zero length would pan the sound to NaN, silencing it with no report.
        SoundGain.Rightward((10f, 20f, 30f), (0f, -1f, 0f), (10f, 20f, 30f)).ShouldBe(0f);
    }

    [Test]
    public void Attenuation_TheMapping_MatchesTheMacroTheSdkPublishes()
    {
        // The one piece of this file that can be compared against Valve directly rather than
        // asserted as a property.
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string header = SourceSdk.Text("src/public/soundflags.h")
            ?? throw new InvalidOperationException("soundflags.h is missing");

        header.ShouldContain("20.0f / (float)(a - 50)", Case.Sensitive);

        SoundAttenuation.FromSoundLevel(75).ShouldBe(0.8f, 0.0001, "SNDLVL_NORM is ATTN_NORM");
        SoundAttenuation.FromSoundLevel(50).ShouldBe(4f, "at or below 50 the macro yields 4.0");
    }
}
