using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Each archive read on this thread while armed, with its first callers outside the archive (B595 part 6).</summary>
/// <remarks>
/// `--measure` only. The observer is <see cref="GameArchives.ThreadReadObserver"/>, which is per thread, so arm and
/// report on the thread whose reads are wanted — the main thread. **Reporting disarms**: left armed, every read for the
/// rest of the run kept a stack trace nobody would print.
/// </remarks>
internal sealed class ReadsInPlay
{
    private readonly List<string> _reads = [];

    /// <summary>Starts recording this thread's reads, dropping any recorded before.</summary>
    public void Arm()
    {
        _reads.Clear();
        GameArchives.ThreadReadObserver = Record;
    }

    /// <summary>Writes each read recorded, then stops recording and forgets them.</summary>
    /// <param name="write">Where each line goes.</param>
    public void Report(Action<string> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        foreach (string read in _reads)
        {
            write("    read in play: " + read);
        }

        GameArchives.ThreadReadObserver = null;
        _reads.Clear();
    }

    private void Record(string path, long ticks)
    {
        List<string> callers = [];

        foreach (StackFrame frame in new StackTrace(1, false).GetFrames())
        {
            if (frame.GetMethod() is { DeclaringType: { } type } method
                && type != typeof(GameArchives) && callers.Count < 4)
            {
                callers.Add(type.Name + "." + method.Name);
            }
        }

        _reads.Add(string.Create(CultureInfo.InvariantCulture, $"{path} {ticks * 1000d / Stopwatch.Frequency:0.00} ms <- {string.Join(" <- ", callers)}"));
    }
}
