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
///
/// **The mouse half counts input, not the cursor** (2026-11-12): a hand on the mouse failed the cursor comparison
/// whenever it moved inside the window. <see cref="MouseInputWatch"/> splits what arrives into injected and human, so a
/// synthesized click fails the test whatever the hand does, and the cursor is still compared when no hand moved.
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

            using MouseInputWatch watch = new();

            Viewer.Activate(button, press);

            try
            {
                Retry.WhileFalse(() => speed.Name != was, Timeout)
                    .Success.ShouldBeTrue($"the speed still reads '{was}', so the button was not pressed");

                // Read after the press has been HANDLED: it is posted, so reading sooner would pass on a
                // click that had not happened yet.
                AssertNoMouseDriven(watch, cursor, "pressing a button");
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
    ///
    /// **What it guards, in the owner's words** (2026-11-07, after a hand on the mouse failed it twice): *"that test doesnt
    /// stop new tests from taking over the mouse, its only testing the test framework really and making sure you are using
    /// it correctly, keep it because its the right example, but its not a human needed test"*. It holds the helper, not
    /// the tests that call it; a real mouse moving during its one second fails it.
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

            using MouseInputWatch watch = new();

            Viewer.Activate(menu, MainForm.ViewMenuName);
            AutomationElement item = Retry.WhileNull(
                    () => menu.FindFirstDescendant(search => search.ByName(PositionItemName)), Timeout).Result
                ?? throw new InvalidOperationException("the View menu did not open, so Activate expanded nothing");
            Viewer.Activate(item, PositionItemName);

            try
            {
                Retry.WhileFalse(
                        () => Viewer.IsMenuItemChecked(MainForm.ViewMenuName, PositionItemName) != was, Timeout)
                    .Success.ShouldBeTrue("the item's state did not change, so nothing was activated");
                AssertNoMouseDriven(watch, cursor, "activating a menu item");
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

    /// <remarks>
    /// **The control on <see cref="MouseInputWatch"/>**: an instrument whose answer is "nothing injected" has to be seen
    /// counting something that is. A one-pixel <c>SendInput</c> move and back must be counted injected; a hand on the
    /// mouse at the same moment adds to <see cref="MouseInputWatch.Human"/> and changes nothing here. FlaUI's
    /// <c>Mouse.MoveBy</c> is not that control: it uses <c>SetCursorPos</c>, which sends no input (it counted nothing).
    /// </remarks>
    [Test]
    public void MouseInputWatch_ASyntheticMove_IsCountedInjected()
    {
        using MouseInputWatch watch = new();

        MouseInputWatch.SendMove(1, 0);
        MouseInputWatch.SendMove(-1, 0);

        Retry.WhileFalse(() => watch.Injected >= 2, Timeout)
            .Success.ShouldBeTrue($"the hook counted {watch.Injected} injected events for two synthesized moves");
    }

    /// <summary>
    /// Nothing drove the mouse while <paramref name="watch"/> ran: no injected event at all, and — when no hand was on the
    /// mouse either — the cursor where it was, which catches a <c>SetCursorPos</c> the hook cannot see.
    /// </summary>
    private static void AssertNoMouseDriven(MouseInputWatch watch, Point cursor, string action)
    {
        watch.Injected.ShouldBe(0, $"{action} synthesized mouse input");

        if (watch.Human == 0)
        {
            Mouse.Position.ShouldBe(cursor, $"{action} moved the real cursor");
        }
    }
}
