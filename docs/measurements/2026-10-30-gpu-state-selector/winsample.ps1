# Sidecar for one run: waits for the viewer's top-level window, then every 500 ms writes one CSV row until the viewer
# exits: time, foreground is the viewer (1/0), foreground window's process, viewer rect, iconic, how many visible,
# uncloaked top-level windows ABOVE the viewer in z-order intersect its rect, and the first such window's process.
# -Foreground: once the window appears (and again every sample if lost), make it the foreground window
# (AttachThreadInput to the current foreground thread, then SetForegroundWindow + BringWindowToTop).
# -Cover: show a TopMost, never-activated form over the left half of the viewer's rect (the background-plus-overlap
# condition). Reads nothing from the viewer but its window; changes nothing in it.
param([string]$Exe, [datetime]$After, [string]$Csv, [switch]$Foreground, [switch]$Cover, [int]$WaitSeconds = 7200)
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
  if ($null -ne $p) { $p.Refresh(); $h = $p.MainWindowHandle }
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
  $form.Size = New-Object System.Drawing.Size([int](($r.Rt - $r.L) / 2), ($r.B - $r.T))
  $form.Show()   # a form shown by a background process does not take the foreground
}
'time,fg,fgproc,left,top,right,bottom,iconic,above,aboveproc' | Set-Content -LiteralPath $Csv
while (-not $p.HasExited) {
  if ($Foreground -and [W.U]::GetForegroundWindow() -ne $h) { Focus $h }
  if ($form) { [System.Windows.Forms.Application]::DoEvents() }
  $fg = [W.U]::GetForegroundWindow()
  $r = New-Object W.U+R; $null = [W.U]::GetWindowRect($h, [ref]$r)
  $n = 0; $first = ''; $w = [W.U]::GetWindow($h, 3)   # GW_HWNDPREV: the window above
  while ($w -ne [IntPtr]::Zero) {
    $c = 0; $null = [W.U]::DwmGetWindowAttribute($w, 14, [ref]$c, 4)   # DWMWA_CLOAKED
    if ([W.U]::IsWindowVisible($w) -and $c -eq 0 -and -not [W.U]::IsIconic($w)) {
      $o = New-Object W.U+R; $null = [W.U]::GetWindowRect($w, [ref]$o)
      if ($o.L -lt $r.Rt -and $o.Rt -gt $r.L -and $o.T -lt $r.B -and $o.B -gt $r.T -and ($o.Rt - $o.L) -gt 1 -and ($o.B - $o.T) -gt 1) {
        $n++; if ($first -eq '') { $first = ProcName $w }
      }
    }
    $w = [W.U]::GetWindow($w, 3)
  }
  $row = '{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}' -f (Get-Date -Format 'HH:mm:ss.fff'), [int]($fg -eq $h), (ProcName $fg),
    $r.L, $r.T, $r.Rt, $r.B, [int][W.U]::IsIconic($h), $n, $first
  Add-Content -LiteralPath $Csv -Value $row
  Start-Sleep -Milliseconds 500
}
if ($form) { $form.Close() }
