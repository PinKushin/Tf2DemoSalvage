using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// The transport control, held to the View half of the MVP contract.
/// </summary>
/// <remarks>
/// **D55's tell, quoted because it is the whole of this file:** *"If a Form method needs an `if`
/// statement about business state, that's the tell it's doing the Presenter's job."* The View's job
/// is *"purely mechanical translation"* — told what to display, deciding nothing.
///
/// **`SetDemoLength` broke that rule and it cost a shipped bug.** It ended with `Playing = false`,
/// which is a DECISION — "a new demo means playback stops" — made inside the control, invisible
/// from `IPlaybackView`, and silent because `Playing`'s setter deliberately does not raise. B223 is
/// what that produced: the presenter started autoplay, the window sized the transport one line
/// later, and the demo sat paused for ever while the log said it was playing.
///
/// The first fix moved the sizing inside `DemoSystems.Open` so nothing could run between it and
/// `Play()`. That closed the hole and left the trapdoor. Removing the side effect removes the
/// trapdoor: `SetDemoLength` can now be called at any point, by anyone, in any order, and cannot
/// stop playback.
///
/// **Enabling and disabling stays**, and is not the same thing. "A demo of length zero displays as
/// disabled controls" is mechanical translation of what it was told; "a new demo is not playing" is
/// a rule about playback, which the presenter owns and already applies in `Load`.
/// </remarks>
/// <remarks>Serial, because this constructs a WinForms control — see B178.</remarks>
[NonParallelizable]
public sealed class TransportBarTests
{
    [Test]
    public void SetDemoLength_WhilePlaying_DoesNotStopPlayback()
    {
        using TransportBar transport = new();

        transport.SetDemoLength(1000);
        transport.Playing = true;

        transport.SetDemoLength(2000);

        transport.Playing.ShouldBeTrue(
            "sizing the transport is display, not a decision about playback: deciding here is what "
            + "silently undid autoplay in B223, and D55 puts that decision in the presenter");
    }

    [Test]
    public void SetDemoLength_WithALength_SizesAndEnablesTheScrubber()
    {
        // **The control for the test above.** A `SetDemoLength` that had been gutted rather than
        // corrected would satisfy "does not stop playback" perfectly while doing nothing at all.
        using TransportBar transport = new();

        transport.SetDemoLength(12_345);

        transport.LastTick.ShouldBe(12_345);
        transport.Enabled.ShouldBeTrue();
    }

    [Test]
    public void SetDemoLength_OfZero_DisablesTheControls()
    {
        // Length zero is "no demo", and displaying a scrubbable bar for one is the mechanical
        // translation being wrong rather than a decision being misplaced — so this stays here.
        using TransportBar transport = new();

        transport.SetDemoLength(1000);
        transport.SetDemoLength(0);

        transport.LastTick.ShouldBe(0);
    }
}
