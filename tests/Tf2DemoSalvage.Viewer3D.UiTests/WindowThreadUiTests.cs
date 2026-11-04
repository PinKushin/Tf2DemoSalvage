using System;
using System.Threading;
using System.Windows.Forms;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>The window thread answers through a load and the seek that opens it (B708).</summary>
public sealed class WindowThreadUiTests
{
    /// <summary>
    /// Windows' own line for a hung window: `IsHungAppWindow` calls an application not responding when it has not asked for
    /// messages for 5 seconds. Past it the window ghosts, and a UI Automation client times out.
    /// </summary>
    private const double NotRespondingMilliseconds = 5000;

    /// <summary>The session's own viewer on z1800, sampled from the window appearing until the first frame after its seek.</summary>
    [Test]
    public void Opening_ADemoAtATick_AnswersWithinTheNotRespondingLine()
    {
        ViewerSession.WindowThread.Samples.ShouldBeGreaterThan(0, "nothing was measured");
        ViewerSession.WindowThread.LongestMilliseconds.ShouldBeLessThan(NotRespondingMilliseconds);
    }

    /// <summary>The control: a thread held for a known time reads as that time, and a free one does not.</summary>
    [Test]
    public void Sampler_OverAWindowItsThreadHolds_ReportsTheHold()
    {
        const int HoldMilliseconds = 1500;

        using ManualResetEventSlim shown = new();
        using ManualResetEventSlim held = new();
        nint handle = 0;
        Form? form = null;

        Thread owner = new(() =>
        {
            using Form window = new() { ShowInTaskbar = false, WindowState = FormWindowState.Minimized };

            window.Shown += (_, _) =>
            {
                form = window;
                handle = window.Handle;
                shown.Set();
            };

            Application.Run(window);
        })
        { IsBackground = true };

        owner.SetApartmentState(ApartmentState.STA);
        owner.Start();
        shown.Wait(TimeSpan.FromSeconds(30)).ShouldBeTrue("the control's window never appeared");

        WindowThreadSampler free = new(handle);

        // Ended on the first answered round trips, not on a time.
        SpinWait.SpinUntil(() => free.Samples >= 10, TimeSpan.FromSeconds(30)).ShouldBeTrue();
        free.Dispose();

        WindowThreadSampler stalled = new(handle);

        SpinWait.SpinUntil(() => stalled.Samples >= 1, TimeSpan.FromSeconds(30)).ShouldBeTrue();

        // A spin, because a blocking wait on an STA thread pumps sent messages: the first version of this control read 0.5 ms.
        form!.BeginInvoke(() =>
        {
            long began = System.Diagnostics.Stopwatch.GetTimestamp();

            while (System.Diagnostics.Stopwatch.GetElapsedTime(began).TotalMilliseconds < HoldMilliseconds)
            {
                Thread.SpinWait(1000);
            }

            held.Set();
        });
        held.Wait(TimeSpan.FromSeconds(30)).ShouldBeTrue();
        SpinWait.SpinUntil(() => stalled.LongestMilliseconds > 0 && stalled.Samples >= 10, TimeSpan.FromSeconds(30)).ShouldBeTrue();
        stalled.Dispose();

        form.BeginInvoke(form.Close);
        owner.Join(TimeSpan.FromSeconds(30)).ShouldBeTrue();

        // Half the hold, because the sampler's next send lands somewhere inside it, later on a busy machine.
        stalled.LongestMilliseconds.ShouldBeGreaterThan(HoldMilliseconds / 2d);
        free.LongestMilliseconds.ShouldBeLessThan(stalled.LongestMilliseconds);
    }
}
