using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

using Tf2DemoSalvage.Logging;

// Usage: <async|sync|freeze> <folder>. Line 1 is "start <UTC ticks>"; line n+1 is "n", logged n ms after start.
// `freeze` logs for 500 ms and then hangs the logging thread, as a frozen viewer would, until killed.
// `crash` logs 500 lines and throws, unhandled, with the handler wired as the viewer's Program.cs wires it.
if (args.Length != 2 || args[0] is not ("async" or "sync" or "freeze" or "crash" or "crash-noflush"))
{
    await Console.Error.WriteLineAsync("usage: <async|sync|freeze|crash|crash-noflush> <folder>").ConfigureAwait(false);
    return 2;
}

string mode = args[0];
Action<string> write;

if (mode == "sync")
{
    // The writer before D230: a held stream, flushed per line on the caller.
    Directory.CreateDirectory(args[1]);
    StreamWriter sync = new(
        new FileStream(
            Path.Combine(args[1], string.Create(CultureInfo.InvariantCulture, $"sync-{Environment.ProcessId}.log")),
            FileMode.Create,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete),
        Encoding.UTF8)
    {
        AutoFlush = true,
    };
    write = sync.WriteLine;
}
else
{
    FileLogWriter writer = new(args[1], mode);
    write = writer.Write;

    AppDomain.CurrentDomain.UnhandledException += (_, _) =>
    {
        writer.Write("unhandled exception, terminating");

        // `crash-noflush` is the control: the same handler without the drain.
        if (mode == "crash")
        {
            writer.Flush(TimeSpan.FromSeconds(2));
        }

        // Ends the process at once, so the writer thread gets no grace the runtime's slower exit would give it:
        // without this the control drains anyway and cannot fail (measured, 10 of 10).
        Environment.FailFast("forced crash, after the handler");
    };
}

write(string.Create(CultureInfo.InvariantCulture, $"start {DateTime.UtcNow.Ticks}"));
Stopwatch clock = Stopwatch.StartNew();

// Runs until killed: the kill IS the measurement.
long n = -1;

while (++n >= 0)
{
    while (clock.ElapsedTicks < n * Stopwatch.Frequency / 1000)
    {
        Thread.SpinWait(20);
    }

    if (mode == "freeze" && n == 500)
    {
        using ManualResetEventSlim never = new();
        never.Wait();
    }

    if (mode.StartsWith("crash", StringComparison.Ordinal) && n == 500)
    {
        // A burst first, so the queue is deep when the exception lands; at one line a millisecond it is
        // nearly always empty and the control cannot fail (measured, 10 of 10 with and without FailFast).
        for (int b = 0; b < 20_000; b++)
        {
            write(string.Create(CultureInfo.InvariantCulture, $"burst {b}"));
        }

        throw new InvalidOperationException("forced, to test that the crash handler drains the log");
    }

    write(n.ToString(CultureInfo.InvariantCulture));
}

return 0;
