using System;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The cross-fade times a sequence carries, read off real models.
/// </summary>
/// <remarks>
/// **An offset in a struct is a claim that has to be checked against bytes**, and this file's
/// neighbours record what happens when it is not: a field read four bytes early is a plausible
/// number in the wrong units. `fadeintime` and `fadeouttime` sit at 104 and 108 by counting fields
/// from `baseptr`, and the same count puts `weightlistindex` at 156 — which was measured
/// independently against a shipped model. That agreement is the argument for 104.
///
/// **Valve's authored default is 0.2 seconds** (`studio.h:854`), so a correct read of a stock TF2
/// model lands there for most sequences. A wrong offset would land on a frame count, an index or a
/// bounding-box coordinate, none of which look like a fifth of a second.
/// </remarks>
public sealed class StudioSequenceFadeTests
{
    [Test]
    public void FadeTimes_OnAShippedPlayerModel_AreTenthsOfASecond()
    {
        if (Read("models/player/scout.mdl") is not { } file)
        {
            Assert.Ignore("scout.mdl is not available");
            return;
        }

        int sequences = StudioSequences.Read(file).Count;

        sequences.ShouldBeGreaterThan(50, "the control: the sequences must have been read at all");

        float[] fadeIn = [.. Enumerable.Range(0, sequences).Select(one => StudioSequenceFade.In(file, one))];
        float[] fadeOut = [.. Enumerable.Range(0, sequences).Select(one => StudioSequenceFade.Out(file, one))];

        TestContext.Out.WriteLine(
            $"FADE in {fadeIn.Min():0.###}..{fadeIn.Max():0.###}, " +
            $"out {fadeOut.Min():0.###}..{fadeOut.Max():0.###}");

        // **A range, not a value.** Sequences are authored, so some legitimately differ from the
        // 0.2 default — but a wrong offset lands on a frame count or an index, which are integers
        // in the hundreds or thousands, or on a bounding-box coordinate, which is tens of units.
        fadeIn.ShouldAllBe(one => one >= 0f && one <= 2f, "a fade time is a fraction of a second");
        fadeOut.ShouldAllBe(one => one >= 0f && one <= 2f, "a fade time is a fraction of a second");

        fadeIn.ShouldContain(
            one => Math.Abs(one - 0.2f) < 0.001f,
            "0.2 is studiomdl's default and most sequences never override it");
    }

    /// <remarks>
    /// **The curve, checked at the three points that matter.** `GetFadeout` is
    /// `3s² − 2s³` over `s = 1 − elapsed / fade` (`animationlayer.h:84`), which is a smoothstep: it
    /// starts at full weight, ends at zero, and passes through exactly one half at the midpoint
    /// where a LINEAR fade would also be — so the midpoint alone cannot tell the two apart, and the
    /// quarter point can.
    /// </remarks>
    [Test]
    public void Fadeout_AcrossItsWindow_FollowsValvesSpline()
    {
        StudioSequenceFade.Fadeout(0d, 0.2f).ShouldBe(1f, 0.001d, "no time has passed");
        StudioSequenceFade.Fadeout(0.1d, 0.2f).ShouldBe(0.5f, 0.001d, "the midpoint");
        StudioSequenceFade.Fadeout(0.2d, 0.2f).ShouldBe(0f, 0.001d, "the window has closed");

        // s = 0.75 there; the spline gives 0.844 where a linear fade would give 0.75.
        StudioSequenceFade.Fadeout(0.05d, 0.2f).ShouldBe(
            0.84375f, 0.001d, "the spline, not a straight line");
    }

    /// <remarks>
    /// **Both guards, and each is Valve's own.** A sequence authored with no fade-out disappears at
    /// once rather than lingering at full weight, and a clock that has run backwards — which a demo
    /// viewer scrubbing does routinely — is clamped to one rather than allowed past it.
    /// </remarks>
    [Test]
    public void Fadeout_WithNoWindowOrABackwardClock_IsZeroAndOne()
    {
        StudioSequenceFade.Fadeout(0.1d, 0f).ShouldBe(0f, 0.001d, "no fade window is no weight");

        StudioSequenceFade.Fadeout(-1d, 0.2f).ShouldBe(
            1f, 0.001d, "Valve's own 'maybe curtime is behind animtime' clamp");
    }

    /// <summary>Reads a model out of the installed game.</summary>
    private static ReadOnlyMemory<byte>? Read(string path)
    {
        string game = GameInstall.Require();

        string[] archives =
        [
            .. new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" }
                .Select(name => Path.Combine(game, name))
                .Where(File.Exists),
        ];

        foreach (string archive in archives)
        {
            if (VpkArchive.Open(archive).ReadFile(path) is { } bytes)
            {
                return bytes;
            }
        }

        return null;
    }
}
