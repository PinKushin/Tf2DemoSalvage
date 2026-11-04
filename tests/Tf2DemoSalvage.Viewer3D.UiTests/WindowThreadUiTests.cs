using System;
using System.Threading;
using System.Windows.Forms;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>The instrument B708 was found with: a thread held for a known time reads as that time.</summary>
public sealed class WindowThreadUiTests
{
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

        double free;
        double stalled;
        int answered;

        using (WindowThreadSampler sampler = new(handle))
        {
            // Ended on the first answered round trips, not on a time.
            SpinWait.SpinUntil(() => sampler.Samples >= 10, TimeSpan.FromSeconds(30)).ShouldBeTrue();
            free = sampler.LongestMilliseconds;

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

            int before = sampler.Samples;

            SpinWait.SpinUntil(() => sampler.Samples > before + 5, TimeSpan.FromSeconds(30)).ShouldBeTrue();
            stalled = sampler.LongestMilliseconds;
            answered = sampler.Samples;
            sampler.Failures.ShouldBe(0);
        }

        form.BeginInvoke(form.Close);
        owner.Join(TimeSpan.FromSeconds(30)).ShouldBeTrue();

        answered.ShouldBeGreaterThan(10);

        // Half the hold, because the sampler's next send lands somewhere inside it, later on a busy machine.
        stalled.ShouldBeGreaterThan(HoldMilliseconds / 2d);
        free.ShouldBeLessThan(stalled);
    }
}
