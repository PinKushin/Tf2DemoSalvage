using System;
using System.Diagnostics;
using System.Drawing;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// <see cref="ViewerApplication.Activate(AutomationElement, string)"/> drives a control with no real input (D215).
/// </summary>
/// <remarks>
/// **The claim is about the desktop, so the desktop is what is measured**: another application holds
/// the foreground, the cursor's position is read, a control is driven, and both must be exactly as
/// they were while the control's effect has happened. A helper that clicked would move the cursor to
/// the control and pull the viewer forward, so either half reddens on it; the foreground half still
/// does when the cursor happens to be resting on the control already.
/// </remarks>
public sealed class ViewerApplicationActivateUiTests
{
    private const string PositionItemName = "Position";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static ViewerApplication Viewer => ViewerSession.App;

    [Test]
    public void Activate_AButtonWithAnotherAppInFront_PressesItWithoutTheCursorOrTheForeground()
    {
        using Process other = ViewerApplication.StartStandInWindow(Timeout);

        try
        {
            // The load's last line, read from the log: the transport is disabled until then, a disabled
            // button hears no press, and a tree walk while the viewer is still loading can outlast UIA.
            Retry.WhileFalse(() => Viewer.Count("opening state applied") > 0, TimeSpan.FromSeconds(120), throwOnTimeout: true);

            AutomationElement speed = Viewer.Find(TransportBar.SpeedLabelId);
            string was = speed.Name;

            // Faster does nothing on the top rung, so from there the press is Slower; the other one puts it back.
            bool atTop = was == TransportBar.SpeedDescription(8);
            string press = atTop ? TransportBar.SlowerButtonId : TransportBar.FasterButtonId;
            string undo = atTop ? TransportBar.FasterButtonId : TransportBar.SlowerButtonId;

            ViewerApplication.TakeForeground(other.MainWindowHandle);
            Retry.WhileFalse(() => ViewerApplication.ForegroundProcessId() == (uint)other.Id, Timeout)
                .Success.ShouldBeTrue("the stand-in app did not take the foreground, so there is nothing to keep");

            AutomationElement button = Viewer.Find(press);
            button.IsEnabled.ShouldBeTrue("the transport is disabled after the load, so there is no button to press");
            Point cursor = Mouse.Position;

            Viewer.Activate(button, press);

            try
            {
                Retry.WhileFalse(() => speed.Name != was, Timeout)
                    .Success.ShouldBeTrue($"the speed still reads '{was}', so the button was not pressed");

                // Read after the press has been HANDLED: it is posted, so reading sooner would pass on a
                // click that had not happened yet.
                Mouse.Position.ShouldBe(cursor, "pressing a button moved the real cursor");
                ViewerApplication.ForegroundProcessId().ShouldBe(
                    (uint)other.Id, "pressing a button took the foreground from the app in front");
            }
            finally
            {
                Viewer.Activate(undo);
                Retry.WhileFalse(() => speed.Name == was, Timeout);
            }
        }
        finally
        {
            other.Kill();
        }
    }

    /// <remarks>
    /// **The cursor only, and the foreground is deliberately not asserted here** (measured
    /// 2026-10-13): a WinForms menu that opens brings its form forward, whoever asks, so the viewer
    /// held the foreground after the expand with no click and no key sent. A menu therefore needs
    /// nothing from the desktop and still takes the foreground from it; the button test above is the
    /// one that holds the helper to both.
    /// </remarks>
    [Test]
    public void Activate_AMenuItemWithAnotherAppInFront_TogglesItWithoutTheCursor()
    {
        using Process other = ViewerApplication.StartStandInWindow(Timeout);

        try
        {
            bool was = Viewer.IsMenuItemChecked(MainForm.ViewMenuName, PositionItemName);

            ViewerApplication.TakeForeground(other.MainWindowHandle);
            Retry.WhileFalse(() => ViewerApplication.ForegroundProcessId() == (uint)other.Id, Timeout)
                .Success.ShouldBeTrue("the stand-in app did not take the foreground, so there is nothing to keep");

            AutomationElement menu = Viewer.Window.FindFirstDescendant(search => search.ByName(MainForm.ViewMenuName))
                ?? throw new InvalidOperationException("no View menu");

            Point cursor = Mouse.Position;

            Viewer.Activate(menu, MainForm.ViewMenuName);
            AutomationElement item = Retry.WhileNull(
                    () => menu.FindFirstDescendant(search => search.ByName(PositionItemName)), Timeout).Result
                ?? throw new InvalidOperationException("the View menu did not open, so Activate expanded nothing");
            Viewer.Activate(item, PositionItemName);

            // Read straight after the two calls: this is the only stretch a hand on the mouse can spoil.
            Point cursorAfter = Mouse.Position;

            try
            {
                Retry.WhileFalse(
                        () => Viewer.IsMenuItemChecked(MainForm.ViewMenuName, PositionItemName) != was, Timeout)
                    .Success.ShouldBeTrue("the item's state did not change, so nothing was activated");
                cursorAfter.ShouldBe(cursor, "activating a menu item moved the real cursor");
            }
            finally
            {
                // The viewer is shared, and a readout left on puts white text in every later capture.
                if (Viewer.IsMenuItemChecked(MainForm.ViewMenuName, PositionItemName) != was)
                {
                    Viewer.InvokeMenuItem(MainForm.ViewMenuName, PositionItemName);
                }
            }
        }
        finally
        {
            other.Kill();
        }
    }
}
