using System;
using System.Runtime.InteropServices;

using NUnit.Framework.Interfaces;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// Puts the running test's name in the viewer's title bar, so a person watching can tell which one
/// is driving.
/// </summary>
/// <remarks>
/// **Asked for by the owner while watching the suite**: *"actually is there a way to display the
/// test name or number live?"* — after seeing a defect on screen and not being able to say which
/// test had produced it. Thirty-one tests share ONE viewer, so the window is the only thing in
/// front of you and it says nothing about which test is running.
///
/// **An `ITestAction` applied at assembly level runs before every test in the assembly**, which is
/// the hook a `[SetUpFixture]` cannot give: its `[OneTimeSetUp]` runs once for the whole namespace.
/// <see cref="Targets"/> must be <see cref="ActionTargets.Test"/> or NUnit runs it per FIXTURE
/// instead, which would name the class and skip every test after the first.
///
/// **`WM_SETTEXT` rather than `SetWindowText`.** The window belongs to the viewer's process, and
/// `SetWindowText` is documented as not changing text across processes; the message it would have
/// sent does work, so it is sent directly.
///
/// **Best-effort by design.** A test that runs before the window exists, or after it has gone, must
/// not fail for want of a caption — the title is an aid to a person watching, not an assertion, and
/// a failure here would be a UI test failing for a reason that has nothing to do with the viewer.
/// The handle is checked instead of catching, so nothing is swallowed silently.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
internal sealed class TestNameInTitleAttribute : Attribute, ITestAction
{
    /// <inheritdoc/>
    public ActionTargets Targets => ActionTargets.Test;

    /// <inheritdoc/>
    public void BeforeTest(ITest test)
    {
        ArgumentNullException.ThrowIfNull(test);

        Retitle($"{test.Name} — {ViewerSession.DemoName}");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// **Left naming the test that just finished, deliberately.** Between tests the window would
    /// otherwise flick back to a generic caption, and the last name shown is the useful one when
    /// something is frozen on screen or when the suite has stopped.
    /// </remarks>
    public void AfterTest(ITest test)
    {
    }

    private static void Retitle(string title)
    {
        if (ViewerSession.Launched is not { } viewer)
        {
            return;
        }

        nint handle = viewer.Window.Properties.NativeWindowHandle.ValueOrDefault;

        if (handle == 0)
        {
            return;
        }

        _ = SendMessage(handle, SetTextMessage, 0, title);
    }

    /// <summary><c>WM_SETTEXT</c>.</summary>
    private const uint SetTextMessage = 0x000C;

    // **`System32` only, which is CA5392.** An unqualified `user32.dll` is resolved by the normal
    // search order, and that order includes the application's own directory — so a file dropped
    // beside the test binaries would be loaded in preference to Windows' own. Pinning the search
    // path removes the question rather than trusting the working directory.
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint SendMessage(nint window, uint message, nint parameter, string text);
}
