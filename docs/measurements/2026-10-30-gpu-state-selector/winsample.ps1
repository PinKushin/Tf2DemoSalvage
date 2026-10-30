# Sidecar for one run: waits for the viewer's top-level window, then every 500 ms writes one CSV row until the viewer
# exits: time, foreground is the viewer (1/0), foreground window's process, viewer rect, iconic, how many visible,
# uncloaked top-level windows ABOVE the viewer in z-order intersect its rect, and the first such window's process.
# -Foreground: once the window appears (and again every sample if lost), make it the foreground window
# (AttachThreadInput to the current foreground thread, then SetForegroundWindow + BringWindowToTop).
# -Cover: show a TopMost, never-activated form over the left half of the viewer's rect (the background-plus-overlap
# condition). Reads nothing from the viewer but its window; changes nothing in it.
# -Uncover (added after the base rate): make the viewer TOPMOST without activating it (SetWindowPos HWND_TOPMOST,
# SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE), so it is uncovered but stays in the background: separates focus from occlusion.
# Column `viewers` (added after 08-P): how many tf2demoview processes exist at the sample, to show overlap with
# another agent's run.
# Added after 13-F (state 1 with the viewer minimized, 11-P state 1 behind floorp): column `covered`, the share of a
# 32x18 grid of points over the viewer's rect that lies under a window above it (`above` only says one intersects);
# -CoverFull, the cover form over the WHOLE rect; -Minimize, ShowWindow(SW_SHOWMINNOACTIVE) once the main window exists.
param([string]$Exe, [datetime]$After, [string]$Csv, [switch]$Foreground, [switch]$Cover, [switch]$Uncover,
  [switch]$CoverFull, [switch]$Minimize, [int]$WaitSeconds = 7200)
$ErrorActionPreference = 'Stop'
Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
[DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
[StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
[DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int s);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
public delegate bool EnumProc(IntPtr h, IntPtr l);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
// The process's largest visible top-level window: the viewer shows a small splash first, and
// Process.MainWindowHandle returned that splash in the first version of this script (run 01-P).
public static IntPtr Largest(uint pid) {
  IntPtr best = IntPtr.Zero; long area = 0;
  EnumWindows((h, l) => {
    uint p; GetWindowThreadProcessId(h, out p);
    R r;
    if (p == pid && IsWindowVisible(h) && GetWindowRect(h, out r)) {
      long a = (long)(r.Rt - r.L) * (r.B - r.T);
      if (a > area) { area = a; best = h; }
    }
    return true;
  }, IntPtr.Zero);
  return best;
}
'@
function ProcName([IntPtr]$h) {
  $p = 0; $null = [W.U]::GetWindowThreadProcessId($h, [ref]$p)
  try { (Get-Process -Id $p -ErrorAction Stop).ProcessName } catch { "pid$p" }
}
function Focus([IntPtr]$h) {
  $fg = [W.U]::GetForegroundWindow(); $x = 0
  $ft = [W.U]::GetWindowThreadProcessId($fg, [ref]$x); $me = [W.U]::GetCurrentThreadId()
  $null = [W.U]::AttachThreadInput($me, $ft, $true)
  $null = [W.U]::SetForegroundWindow($h); $null = [W.U]::BringWindowToTop($h)
  $null = [W.U]::AttachThreadInput($me, $ft, $false)
}
# The viewer is a grandchild (pwsh, run-exclusive, viewer), so it is found by its path and a start after the run began.
$full = [IO.Path]::GetFullPath($Exe)
$deadline = (Get-Date).AddSeconds($WaitSeconds)
$h = [IntPtr]::Zero; $p = $null
while ($h -eq [IntPtr]::Zero) {
  if ((Get-Date) -gt $deadline) { exit 0 }
  $p = Get-Process tf2demoview -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $full -and $_.StartTime -gt $After } | Select-Object -First 1
  if ($null -ne $p) {
    # Wait for the MAIN window (at least 1000 px wide), not the splash shown while archives load.
    $c = [W.U]::Largest([uint32]$p.Id); $q = New-Object W.U+R
    if ($c -ne [IntPtr]::Zero -and [W.U]::GetWindowRect($c, [ref]$q) -and ($q.Rt - $q.L) -ge 1000) { $h = $c }
  }
  if ($h -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 200 }
}
$form = $null
if ($Cover) {
  Add-Type -AssemblyName System.Windows.Forms
  $r = New-Object W.U+R; $null = [W.U]::GetWindowRect($h, [ref]$r)
  $form = New-Object System.Windows.Forms.Form
  $form.FormBorderStyle = 'None'; $form.TopMost = $true; $form.ShowInTaskbar = $false
  $form.StartPosition = 'Manual'; $form.BackColor = 'DimGray'
  $form.Location = New-Object System.Drawing.Point($r.L, $r.T)
  $wide = if ($CoverFull) { $r.Rt - $r.L } else { [int](($r.Rt - $r.L) / 2) }
  $form.Size = New-Object System.Drawing.Size($wide, ($r.B - $r.T))
  $form.Show()   # a form shown by a background process does not take the foreground
}
if ($Minimize) { $null = [W.U]::ShowWindow($h, 7) }   # SW_SHOWMINNOACTIVE
'time,fg,fgproc,left,top,right,bottom,iconic,above,aboveproc,viewers,covered' | Set-Content -LiteralPath $Csv
while (-not $p.HasExited) {
  $c = [W.U]::Largest([uint32]$p.Id); if ($c -ne [IntPtr]::Zero) { $h = $c }
  if ($Foreground -and [W.U]::GetForegroundWindow() -ne $h) { Focus $h }
  if ($Uncover) { $null = [W.U]::SetWindowPos($h, [IntPtr]::new(-1), 0, 0, 0, 0, 0x13) }   # TOPMOST; NOSIZE|NOMOVE|NOACTIVATE
  if ($form) { [System.Windows.Forms.Application]::DoEvents() }
  $fg = [W.U]::GetForegroundWindow()
  $r = New-Object W.U+R; $null = [W.U]::GetWindowRect($h, [ref]$r)
  $n = 0; $first = ''; $w = [W.U]::GetWindow($h, 3)   # GW_HWNDPREV: the window above
  $over = @()
  while ($w -ne [IntPtr]::Zero) {
    $c = 0; $null = [W.U]::DwmGetWindowAttribute($w, 14, [ref]$c, 4)   # DWMWA_CLOAKED
    if ([W.U]::IsWindowVisible($w) -and $c -eq 0 -and -not [W.U]::IsIconic($w)) {
      $o = New-Object W.U+R; $null = [W.U]::GetWindowRect($w, [ref]$o)
      if ($o.L -lt $r.Rt -and $o.Rt -gt $r.L -and $o.T -lt $r.B -and $o.B -gt $r.T -and ($o.Rt - $o.L) -gt 1 -and ($o.B - $o.T) -gt 1) {
        $n++; if ($first -eq '') { $first = ProcName $w }; $over += $o
      }
    }
    $w = [W.U]::GetWindow($w, 3)
  }
  # Share of a 32x18 point grid over the viewer's rect lying under any window above it, or off the 1920x1080 screen.
  $hit = 0; $all = 0
  if (-not [W.U]::IsIconic($h)) {
    for ($i = 0; $i -lt 32; $i++) { for ($j = 0; $j -lt 18; $j++) {
        $x = $r.L + ($r.Rt - $r.L) * ($i + 0.5) / 32; $y = $r.T + ($r.B - $r.T) * ($j + 0.5) / 18; $all++
        if ($x -lt 0 -or $y -lt 0 -or $x -ge 1920 -or $y -ge 1080) { $hit++; continue }
        foreach ($o in $over) { if ($x -ge $o.L -and $x -lt $o.Rt -and $y -ge $o.T -and $y -lt $o.B) { $hit++; break } }
    } }
  }
  $cov = if ($all) { '{0:0.00}' -f ($hit / $all) } else { '1.00' }
  $row = '{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}' -f (Get-Date -Format 'HH:mm:ss.fff'), [int]($fg -eq $h), (ProcName $fg),
    $r.L, $r.T, $r.Rt, $r.B, [int][W.U]::IsIconic($h), $n, $first, @(Get-Process tf2demoview -ErrorAction SilentlyContinue).Count, $cov
  Add-Content -LiteralPath $Csv -Value $row
  Start-Sleep -Milliseconds 500
}
if ($form) { $form.Close() }
