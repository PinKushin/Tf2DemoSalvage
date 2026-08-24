using System;
using System.Linq;
using System.Threading;
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
/// **That claim quietly stopped being true and had to be restored.** Full screen later grew an
/// overlay window for the transport bar, and <c>Show</c> on it puts a real window on the desktop
/// whether or not its owner is visible — so a suite that opens no display was opening one window
/// per test and stealing focus from whoever was at the machine. The overlay is now shown only when
/// the form itself is visible, which is the runtime-correct rule as well: there is nothing to
/// overlay otherwise. Nothing measured below changed, because the bar is still MOVED either way.
///
/// The property under test throughout is that **the transport bar is MOVED rather than
/// duplicated**. A second copy in the overlay would be a second piece of state to keep in step
/// with playback, and the two would drift apart the moment anything updated only one of them.
/// </remarks>
/// <remarks>
/// **STA and serial, because this constructs a Windows Form (B178).** The assembly opts into
/// `Parallelizable(ParallelScope.All)` on the stated grounds that "nothing left in this project
/// constructs a form" — which stopped being true, in six files, without anything failing. A form
/// built on an MTA worker thread, concurrently with other forms each holding a D3D swap chain and
/// an OpenAL context whose current-context is process-wide, is undefined behaviour.
///
/// The symptom was a test host that crashed in about half of all runs, at three unrelated native
/// sites — D3D device creation, overlay positioning, and audio teardown. Nothing pointed at a
/// culprit because there was not one: it was whichever native call happened to be executing.
/// </remarks>
[NonParallelizable]
[Apartment(ApartmentState.STA)]
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
    public void EverythingHiddenForFullScreen_IsADirectChildOfTheForm()
    {
        // **The invariant a real bug broke.** Hiding a control only gives its space back if the
        // hidden control is the one that is docked. When the playlist gained a search box the two
        // moved inside a panel, and the code went on hiding the playlist and the search box - so
        // full screen left a 280-pixel empty panel docked to the right and the viewport came out
        // the wrong width.
        //
        // Control.Visible cannot catch this: it reports EFFECTIVE visibility, so on a form that
        // was never shown every child reads false whatever the code did. The parent relationship
        // is the property that actually decides the layout, and it is observable without a window.
        using MainForm form = new();

        form.HiddenInFullScreen.ShouldNotBeEmpty();

        foreach (Control hidden in form.HiddenInFullScreen)
        {
            hidden.Parent.ShouldBe(form, $"{hidden.Name} is hidden for full screen but is not docked on the form");
        }
    }

    [Test]
    public void FullScreen_ThePlaylistContainer_IsHidden()
    {
        // The specific case, named. The panel is what occupies the width, so the panel is what has
        // to go - checking only the invariant above would pass on a form that hid nothing at all
        // if the list happened to be empty, which is why the count is asserted too.
        using MainForm form = new();

        Control playlist = form.Controls.Find(MainForm.PlaylistId, searchAllChildren: true).Single();

        form.HiddenInFullScreen.ShouldContain(playlist.Parent!);
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
    public void FullScreen_TheTransportBar_SurvivesTheRoundTrip()
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
    public void FullScreen_Escape_LeavesFullScreenAndIsIgnoredWhenWindowed()
    {
        // Escape must not be swallowed while windowed, or it stops cancelling dialogs and
        // closing menus - so the windowed case is asserted as well as the full-screen one.
        using EscapeProbe form = new();

        form.PressEscape().ShouldBeFalse("Escape was handled while windowed");

        form.SetFullScreen(true);
        form.PressEscape().ShouldBeTrue("Escape did not leave full screen");
        form.IsFullScreen.ShouldBeFalse();
    }

    [Test]
    public void EnteringFullScreen_TheWindowHandle_SurvivesTheTransition()
    {
        // **A hypothesis about the focus loss, tested and ELIMINATED — kept as the guard it turned
        // into.** A window that loses its HWND loses the foreground and cannot simply take it back:
        // `SetForegroundWindow` from a process that is no longer foreground is refused rather than
        // obeyed, so `Activate()` would flash the taskbar button and nothing else. If assigning
        // `FormBorderStyle` recreated the handle, that would explain the owner's report exactly —
        // "the window is losing focus or something on the first full screen test and it stalls from
        // there", cured by alt-tabbing away and back, which supplies the input Windows wants before
        // it will hand the foreground over.
        //
        // It does not. WinForms updates the styles in place here, and this passed the moment it was
        // written. The cause of the focus loss is elsewhere.
        //
        // Left in because the invariant is real even though the bug was not: any future change that
        // reaches for a mechanism which DOES recreate the handle breaks the foreground, and this is
        // the cheapest possible place to notice. Asserted on the handle rather than on focus,
        // because focus needs a shown window and a desktop; the handle is the mechanism underneath
        // and is checkable with neither.
        using MainForm form = new();

        IntPtr before = form.Handle;

        form.SetFullScreen(true);

        form.Handle.ShouldBe(
            before,
            "entering full screen recreated the window handle, which drops the foreground");
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
    public void FullScreen_TheSameModeTwice_DoesNothing()
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
