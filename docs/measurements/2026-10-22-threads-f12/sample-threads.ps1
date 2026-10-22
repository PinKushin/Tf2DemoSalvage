# Per-thread CPU sampler (measurement 2026-10-22-threads-f12). No admin needed for own-user processes.
# Every -IntervalMs, writes one CSV row per thread: elapsed seconds, thread id, cumulative
# TotalProcessorTime (ms), Win32 start address (NtQueryInformationThread class 9) and the module that
# contains it. A cpu delta between two rows of one thread is that thread's CPU over the interval,
# spin-waiting included. Usage: pwsh sample-threads.ps1 -ProcName tf_win64 -Seconds 60 -Out file.csv
param([string]$ProcName, [int]$ProcessId = 0, [int]$Seconds = 60, [int]$IntervalMs = 1000, [string]$Out)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class Nt {
  [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenThread(int a, bool i, int id);
  [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
  [DllImport("ntdll.dll")] public static extern int NtQueryInformationThread(IntPtr h, int c, out IntPtr v, int l, IntPtr r);
  public static long Start(int tid) {
    IntPtr h = OpenThread(0x0040, false, tid); // THREAD_QUERY_INFORMATION
    if (h == IntPtr.Zero) return -1;
    try { IntPtr v; return NtQueryInformationThread(h, 9, out v, IntPtr.Size, IntPtr.Zero) == 0 ? v.ToInt64() : -2; }
    finally { CloseHandle(h); }
  }
  [DllImport("kernel32.dll")] static extern int GetThreadDescription(IntPtr h, out IntPtr d);
  [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
  public static string Name(int tid) { // SetThreadDescription name, "" when none
    IntPtr h = OpenThread(0x1000, false, tid); // THREAD_QUERY_LIMITED_INFORMATION
    if (h == IntPtr.Zero) return "?";
    try { IntPtr d; if (GetThreadDescription(h, out d) < 0) return "?"; string s = Marshal.PtrToStringUni(d); LocalFree(d); return s.Replace(",", ";"); }
    finally { CloseHandle(h); }
  }
}
'@
$p = if ($ProcessId) { Get-Process -Id $ProcessId } else { Get-Process $ProcName | Select-Object -First 1 }
function Mods { $p.Refresh(); $p.Modules | ForEach-Object { [pscustomobject]@{ N = $_.ModuleName; B = $_.BaseAddress.ToInt64(); E = $_.BaseAddress.ToInt64() + $_.ModuleMemorySize } } }
$script:mods = Mods
function Find([long]$a) { foreach ($m in $script:mods) { if ($a -ge $m.B -and $a -lt $m.E) { return "$($m.N)+0x{0:x}" -f ($a - $m.B) } }; return $null }
# A module loaded after the last snapshot (a driver DLL, say) is found by taking the snapshot again.
function ModOf([long]$a) { $n = Find $a; if (-not $n) { $script:mods = Mods; $n = Find $a }; if ($n) { $n } else { 'unknown:0x{0:x}' -f $a } }
$starts = @{}
't,tid,cpu_ms,start' | Set-Content $Out
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt $Seconds -and -not $p.HasExited) {
  $p.Refresh(); $t = '{0:F3}' -f $sw.Elapsed.TotalSeconds
  $rows = foreach ($th in $p.Threads) {
    try { $ms = $th.TotalProcessorTime.TotalMilliseconds } catch { Write-Warning "thread $($th.Id) gone: $_"; continue }
    if (-not $starts.ContainsKey($th.Id)) { $starts[$th.Id] = (ModOf ([Nt]::Start($th.Id))) + ' ' + [Nt]::Name($th.Id) }
    "$t,$($th.Id),$ms,$($starts[$th.Id])"
  }
  $rows | Add-Content $Out
  Start-Sleep -Milliseconds $IntervalMs
}
