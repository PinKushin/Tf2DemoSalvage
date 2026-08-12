using System.Linq;
using System.Windows.Forms;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Tests the transition into and out of full screen.
/// </summary>
/// <remarks>
/// The form is never shown, so these run with no display: the transition is state on controls,
/// and asserting that state is both cheaper and more precise than looking at a screen.
///
/// The property under test throughout is that **the transport bar is MOVED rather than
/// duplicated**. A second copy in the overlay would be a second piece of state to keep in step
/// with playback, and the two would drift apart the moment anything updated only one of them.
/// </remarks>
public sealed class FullScreenTests
{
    [Test]
    public void EnteringFullScreen_HidesTheChromeAndTakesTheTransportBar()
    {
        using MainForm form = new();

        form.SetFullScreen(true);

        form.IsFullScreen.ShouldBeTrue();
        form.FormBorderStyle.ShouldBe(FormBorderStyle.None);

        // Off the form entirely - it now lives on the overlay window.
        form.Controls.OfType<TransportBar>().ShouldBeEmpty();

        // NOT asserted on Control.Visible, and that is the point of this comment. Visible reports
        // EFFECTIVE visibility, so on a form that was never shown every child reads false whatever
        // the code did - the assertion passes identically against a version that hides nothing.
        // The observable difference between the two modes is where the transport bar lives and
        // what the border is, so those are what is measured.
    }

    [Test]
    public void LeavingFullScreen_PutsEverythingBack()
    {
        using MainForm form = new();
        FormBorderStyle border = form.FormBorderStyle;

        form.SetFullScreen(true);
        form.SetFullScreen(false);

        form.IsFullScreen.ShouldBeFalse();
        form.FormBorderStyle.ShouldBe(border);
        form.Controls.OfType<TransportBar>().ShouldHaveSingleItem();
    }

    [Test]
    public void TheTransportBarSurvivesTheRoundTrip()
    {
        // The bar is removed from a form and added to a window and back. If the overlay disposed
        // it on close, this is where that shows up - as a disposed control that throws on the
        // next property read rather than at the point the mistake was made.
        using MainForm form = new();
        form.Transport.SetDemoLength(500);
        form.Transport.ShowTick(123);

        form.SetFullScreen(true);
        form.SetFullScreen(false);

        form.Transport.IsDisposed.ShouldBeFalse();
        form.Transport.CurrentTick.ShouldBe(123);
        form.Transport.LastTick.ShouldBe(500);
    }

    [Test]
    public void EscapeLeavesFullScreenAndIsIgnoredWhenWindowed()
    {
        // Escape must not be swallowed while windowed, or it stops cancelling dialogs and
        // closing menus - so the windowed case is asserted as well as the full-screen one.
        using EscapeProbe form = new();

        form.PressEscape().ShouldBeFalse("Escape was handled while windowed");

        form.SetFullScreen(true);
        form.PressEscape().ShouldBeTrue("Escape did not leave full screen");
        form.IsFullScreen.ShouldBeFalse();
    }

    /// <summary>Exposes the protected key handler, which is otherwise unreachable from a test.</summary>
    private sealed class EscapeProbe : MainForm
    {
        public bool PressEscape()
        {
            Message message = default;
            return ProcessKey(ref message, Keys.Escape);
        }
    }

    [Test]
    public void SettingTheSameModeTwiceDoesNothing()
    {
        // Idempotent because the menu item is a checkbox and its CheckedChanged also calls this:
        // without the guard, entering full screen from the menu would re-enter it from the
        // property assignment and build a second overlay.
        using MainForm form = new();

        form.SetFullScreen(true);
        form.SetFullScreen(true);
        form.SetFullScreen(false);

        form.Controls.OfType<TransportBar>().ShouldHaveSingleItem();
    }
}
