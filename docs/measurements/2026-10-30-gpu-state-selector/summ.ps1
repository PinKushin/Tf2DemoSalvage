# Per run, one line: state (1 if Clear GPU above 1.0, 2 if under 0.3, ? between), frame mean, render thread draw, the
# main-thread phases camera/advance/hud/between, frames drawn/read on GPU/skipped/disjoint, GPU ms of Clear, Sky3D,
# World and OpaqueModels (regexes as 2026-10-30-sky3d-gpu-shift/summ.ps1), then from the sidecars over the run's last
# 20 s of window samples: the share of samples with the viewer in the foreground, the mean number of windows above it
# that overlap it, its rect, and the GPU's pstate(s) and median graphics/memory clock and utilisation in that interval.
# -Dir reads another folder's NN-*.txt (archive mode: sidecar columns are blank where no sidecar exists).
param([string[]]$Runs, [string]$Dir = $PSScriptRoot)
foreach ($r in $Runs) {
  $t = Get-Content "$Dir/$r.txt"
  $mm = $t | Select-String 'frames, mean ([\d.]+) ms' | Select-Object -First 1
  if ($null -eq $mm) { "{0,-12} no result" -f $r; continue }
  $mean = $mm.Matches[0].Groups[1].Value
  $ph = ($t | Select-String 'phases, mean ms: (.*?);').Matches[0].Groups[1].Value
  $get = { param($k) if ($ph -match "$k ([\d.]+)") { $Matches[1] } else { '-' } }
  $rt = $t | Select-String 'render thread draw ([\d.]+)'
  $rtd = if ($rt) { $rt.Matches[0].Groups[1].Value } else { '-' }
  $h = ($t | Select-String 'mean per frame at (\S+): (\d+) frames drawn, (\d+) read on the GPU, (\d+) skipped, (\d+) disjoint').Matches[0].Groups
  $g = @{}
  foreach ($w in 'Clear', 'Sky3D', 'World', 'OpaqueModels') {
    $m = ($t | Select-String "^\s{4}${w}: cpu ([\d.]+) ms, gpu ([\d.]+) ms").Matches
    $g[$w] = if ($m.Count) { [double]$m[0].Groups[2].Value } else { [double]::NaN }
  }
  $tick = ($t | Select-String 'ending at tick (\d+)').Matches[0].Groups[1].Value
  $state = if ($g.Clear -gt 1.0) { '1' } elseif ($g.Clear -lt 0.3) { '2' } else { '?' }
  $line = "{0,-12} t{16} S{1} frame {2,5} rtd {3,4} cam {4,4} adv {5,4} hud {6,4} btw {7,4} | {8}/{9}/{10}/{11} | Clr {12:0.000} Sky3D {13:0.000} World {14:0.000} Opq {15:0.000}" -f `
    $r, $state, $mean, $rtd, (& $get 'camera'), (& $get 'advance'), (& $get 'hud'), (& $get 'between'),
    $h[2].Value, $h[3].Value, $h[4].Value, $h[5].Value, $g.Clear, $g.Sky3D, $g.World, $g.OpaqueModels, $tick
  $win = "$Dir/$r.win.csv"; $gpu = "$Dir/$r.gpu.csv"
  if (Test-Path $win) {
    # Rows of the main window only (01-P and 02-P sampled the splash, whose rect reads 0 once it closes).
    # The interval ends at the last sample of any window (the viewer exits right after the measured 20 s).
    $all = @(Import-Csv $win)
    $rows = @($all | Where-Object { ([int]$_.right - [int]$_.left) -ge 1000 })
    if ($all.Count) {
      $end = [datetime]::ParseExact($all[-1].time, 'HH:mm:ss.fff', $null)
      $last = @($rows | Where-Object { ($end - [datetime]::ParseExact($_.time, 'HH:mm:ss.fff', $null)).TotalSeconds -le 20 })
      if ($last.Count) {
        $fg = ($last | Measure-Object -Property fg -Average).Average
        $ab = ($last | Measure-Object -Property above -Average).Average
        $procs = ($last | Group-Object fgproc | Sort-Object Count -Descending | ForEach-Object { "$($_.Name)x$($_.Count)" }) -join '+'
        $cv = if ($last[0].PSObject.Properties['covered']) { '{0:0.00}' -f ($last | Measure-Object -Property covered -Average).Average } else { '-' }
        $line += " cov $cv"
        $vw = if ($last[0].PSObject.Properties['viewers']) { ($last | Measure-Object -Property viewers -Maximum).Maximum } else { '-' }
        $line += " | fg {0:0.00} above {1:0.0} viewers {7} fgproc {2} rect {3},{4},{5},{6}" -f $fg, $ab, $procs, $last[-1].left, $last[-1].top, $last[-1].right, $last[-1].bottom, $vw
      } else { $line += ' | main window not sampled' }
      if (Test-Path $gpu) {
        $gr = @(Import-Csv $gpu | ForEach-Object {
            $ts = [datetime]::ParseExact($_.timestamp.Trim(), 'yyyy/MM/dd HH:mm:ss.fff', $null)
            if (($end.TimeOfDay - $ts.TimeOfDay).TotalSeconds -le 20 -and ($end.TimeOfDay - $ts.TimeOfDay).TotalSeconds -ge 0) { $_ }
          })
        $med = { param($col) $v = @($gr | ForEach-Object { [double](($_.$col -replace '[^\d.]', '')) } | Sort-Object); if ($v.Count) { $v[[int]($v.Count / 2)] } else { '-' } }
        $ps = ($gr | Group-Object { $_.pstate.Trim() } | ForEach-Object { "$($_.Name)x$($_.Count)" }) -join '+'
        $line += " | gpu {0} gr {1} mem {2} W {3} util {4}" -f $ps, (& $med 'clocks.current.graphics [MHz]'), (& $med 'clocks.current.memory [MHz]'), (& $med 'power.draw [W]'), (& $med 'utilization.gpu [%]')
      }
    }
  }
  $line
}
