using System;
using System.Threading;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// That closing the viewer runs its shutdown once.
/// </summary>
/// <remarks>
/// **Found by reading the log rather than by a failure.** Every viewer log ends with the shutdown
/// line twice — once reporting `device released after 4 ms` and again reporting `0 ms` — because
/// `Dispose(bool)` carried no re-entry guard and WinForms calls it more than once: `Form.Close`
/// disposes a top-level form, and `Application.Run` disposes it again on the way out.
///
/// **Why it matters beyond a duplicated line.** The second pass reports 0 ms for both stages
/// because the first pass already unhooked the idle handler and released the device, so the
/// timing the block exists to capture is overwritten by a measurement of nothing. A slow exit
/// would be recorded and then immediately papered over — a log naming a quantity it did not
/// measure.
///
/// **This measured the log first, and that was the wrong instrument.** ViewerLog is process-wide
/// and this assembly's fixtures run in parallel, several of them constructing and disposing a
/// form of their own, so a before-and-after count of shutdown lines attributed other fixtures'
/// disposals to this one. It failed one run in four with the code under test innocent — the
/// symptom of a shared instrument, not of a race in the viewer. The count moved onto the instance
/// and the ambiguity went with it.
/// </remarks>
[TestFixture]

// STA and serial, because this constructs a Windows Form — see B178.
[NonParallelizable]
[Apartment(ApartmentState.STA)]
public sealed class MainFormDisposeTests
{
    [Test]
    public void DisposingTwice_ShutsDownOnce()
    {
        MainForm form = new();

        // Twice, deliberately, because that is what WinForms does to a form that has been shown:
        // Close disposes it and the message loop disposes it again. Disposing once here would be
        // an input for which the guarded and unguarded versions predict the same observation.
        form.Dispose();
        form.Dispose();

        // Exactly one. Not "at least one", which the defect also satisfies, and not "fewer than
        // two", which says nothing about whether shutdown ran at all.
        form.ShutdownRuns.ShouldBe(1);
    }

    [Test]
    public void ANewForm_HasNotShutDown()
    {
        // The control for the assertion above: it fixes the starting value, so "one run" cannot
        // be satisfied by a counter that was already sitting at one before Dispose was called.
        using MainForm form = new();

        form.ShutdownRuns.ShouldBe(0);
    }
}
