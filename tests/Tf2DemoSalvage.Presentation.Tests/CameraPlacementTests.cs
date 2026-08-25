using System;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// Reading a camera placement out of TF2's own <c>cl_showpos</c> readout.
/// </summary>
/// <remarks>
/// Parity work keeps needing the same frame twice — once from the engine and once from here — and
/// reproducing a viewpoint by hand with mouse and keys is neither quick nor repeatable. TF2 prints
/// <c>pos: x y z</c> and <c>ang: pitch yaw roll</c>, so this takes those numbers in that order.
/// </remarks>
public sealed class CameraPlacementTests
{
    [Test]
    public void AShowposReadout_IsReadAsWritten()
    {
        // Copied from a real capture of cp_process, negatives and decimals included.
        ((float X, float Y, float Z) Origin, float Pitch, float Yaw)? placed =
            FreeCameraController.Parse("-625.75 -1702.36 689.03 -0.62 -37.58");

        placed.ShouldNotBeNull();
        placed.Value.Origin.X.ShouldBe(-625.75f, 0.001f);
        placed.Value.Origin.Y.ShouldBe(-1702.36f, 0.001f);
        placed.Value.Origin.Z.ShouldBe(689.03f, 0.001f);
        placed.Value.Pitch.ShouldBe(-0.62f, 0.001f);
        placed.Value.Yaw.ShouldBe(-37.58f, 0.001f);
    }

    [Test]
    public void CommasAndExtraSpacing_AreAccepted()
    {
        // Pasted coordinates arrive in whatever shape the source used.
        FreeCameraController.Parse("1,2,3, 4, 5").ShouldNotBeNull();
        FreeCameraController.Parse("  1   2  3   4   5  ").ShouldNotBeNull();
    }

    [Test]
    public void ARollValue_IsIgnoredRatherThanRejected()
    {
        // TF2's ang readout carries three numbers. Requiring exactly five would reject a
        // straight copy of it, which is the one input this exists to accept.
        FreeCameraController.Parse("1 2 3 4 5 0").ShouldNotBeNull();
    }

    [Test]
    public void SomethingThatIsNotFiveNumbers_IsRefused()
    {
        // **Null rather than a default, deliberately.** A mistyped variable that quietly placed the
        // camera at the origin would look like the viewer ignoring the request, and the request is
        // specifically to be somewhere exact.
        FreeCameraController.Parse("1 2 3 4").ShouldBeNull();
        FreeCameraController.Parse("pos: 1 2 3 4 5").ShouldBeNull();
        FreeCameraController.Parse("").ShouldBeNull();
        FreeCameraController.Parse("   ").ShouldBeNull();
    }
}
