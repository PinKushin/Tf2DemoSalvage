# Instrument control: a pwsh process with a named managed thread spinning and a thread-pool item spinning, for $Seconds.
# The sampler must read the named thread by name at ~100% of a core and the pool thread by the runtime's name.
param([int]$Seconds = 25)
$code = @'
using System; using System.Threading; using System.Diagnostics;
public static class Spin {
  public static long N;
  static void Loop(int s) { var w = Stopwatch.StartNew(); while (w.Elapsed.TotalSeconds < s) Interlocked.Increment(ref N); }
  public static void Run(int s) {
    var t = new Thread(() => Loop(s)) { Name = "control spinner", IsBackground = true }; t.Start();
    ThreadPool.QueueUserWorkItem(_ => Loop(s));
    t.Join();
  }
}
'@
Add-Type $code
"pid $PID"
[Spin]::Run($Seconds)
