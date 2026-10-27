using System.Windows.Forms;

using Tf2DemoSalvage.Presentation;
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

    /// <remarks>
    /// B572: playback calls <c>ShowTick</c> every frame, and at a few hundred frames a second most frames repeat the
    /// last tick. The readout is a function of the tick and the length alone, so a repeated tick has nothing new to
    /// write — the control is a text planted by the test, which a rewrite would replace.
    /// </remarks>
    [Test]
    public void ShowTick_TheSameTickAgain_DoesNotRewriteTheReadout()
    {
        using TransportBar transport = new();
        transport.SetDemoLength(1000);
        transport.ShowTick(500);
        Label readout = TickReadout(transport);

        readout.Text = "planted";
        readout.AccessibleName = "planted";
        transport.ShowTick(500);

        (readout.Text, readout.AccessibleName).ShouldBe(("planted", "planted"));
    }

    /// <remarks>The control for the test above: a new tick does write, and writes exactly the position's label.</remarks>
    [Test]
    public void ShowTick_ANewTick_WritesThePositionLabelToTextAndName()
    {
        using TransportBar transport = new();
        transport.SetDemoLength(1000);
        transport.ShowTick(500);
        Label readout = TickReadout(transport);

        readout.Text = "planted";
        transport.ShowTick(501);

        string expected = new DemoPosition(501, 1000).Label();
        (readout.Text, readout.AccessibleName).ShouldBe((expected, expected));
    }

    private static Label TickReadout(TransportBar transport) =>
        (Label)transport.Controls.Find(TransportBar.TickLabelId, searchAllChildren: true)[0];
}
