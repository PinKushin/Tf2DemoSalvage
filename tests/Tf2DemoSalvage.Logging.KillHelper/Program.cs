using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

using Tf2DemoSalvage.Logging;

// Usage: <async|sync|freeze> <folder>. Line 1 is "start <UTC ticks>"; line n+1 is "n", logged n ms after start.
// `freeze` logs for 500 ms and then hangs the logging thread, as a frozen viewer would, until killed.
if (args.Length != 2 || args[0] is not ("async" or "sync" or "freeze"))
{
    await Console.Error.WriteLineAsync("usage: <async|sync|freeze> <folder>").ConfigureAwait(false);
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

    write(n.ToString(CultureInfo.InvariantCulture));
}

return 0;
