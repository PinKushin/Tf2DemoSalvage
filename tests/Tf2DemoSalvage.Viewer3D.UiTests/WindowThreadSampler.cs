using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>Times <c>WM_NULL</c> round trips to a window from another thread, as a UI Automation request would wait for it (B708).</summary>
/// <remarks>Sent, not posted, so a round trip lasts as long as the window's thread is not answering. Not a UI Automation client.</remarks>
internal sealed partial class WindowThreadSampler : IDisposable
{
    private const uint WmNull = 0;

    /// <summary>Long past any stall worth reading: a window that never answers still ends the sample.</summary>
    private const uint SendLimitMilliseconds = 120_000;

    private readonly nint _window;
    private readonly ManualResetEventSlim _stop = new();
    private readonly Thread _sender;
    private long _longestTicks;
    private int _samples;
    private int _failures;

    public WindowThreadSampler(nint window)
    {
        _window = window;
        _sender = new Thread(Sample) { IsBackground = true, Name = "WindowThreadSampler" };
        _sender.Start();
    }

    /// <summary>The longest answered round trip so far.</summary>
    public double LongestMilliseconds =>
        Interlocked.Read(ref _longestTicks) * 1000d / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>How many round trips were answered, the instrument's control: none means nothing was measured.</summary>
    public int Samples => Volatile.Read(ref _samples);

    /// <summary>How many sends were not answered: a closed window or the limit, neither of them a measurement.</summary>
    public int Failures => Volatile.Read(ref _failures);

    public void Dispose()
    {
        _stop.Set();
        _sender.Join();
        _stop.Dispose();
    }

    private void Sample()
    {
        // Paced on the stop signal, so ending the sample does not wait for a pause to run out.
        while (!_stop.Wait(TimeSpan.FromMilliseconds(20)))
        {
            long at = System.Diagnostics.Stopwatch.GetTimestamp();

            if (SendMessageTimeout(_window, WmNull, 0, 0, 0, SendLimitMilliseconds, out _) == 0)
            {
                Interlocked.Increment(ref _failures);
                continue;
            }

            long taken = System.Diagnostics.Stopwatch.GetTimestamp() - at;

            if (taken > Interlocked.Read(ref _longestTicks))
            {
                Interlocked.Exchange(ref _longestTicks, taken);
            }

            Interlocked.Increment(ref _samples);
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static partial nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
}
