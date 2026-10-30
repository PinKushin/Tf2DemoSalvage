# Runs a list of run names in order through run.ps1; the letter after the dash picks the condition:
#   P plain, F foreground, C cover (background, overlapped), O window at 40,40 (pos), K cold (300 s with no viewer),
#   Z +mat_queue_mode 0.  e.g. grid.ps1 01-P 02-P 09-F 10-P
# A finished run on disk (it holds "frames, mean") is kept, so a stopped sequence resumes; a gate miss or non-zero exit
# stops the sequence so a gap is visible.
param([Parameter(ValueFromRemainingArguments)][string[]]$Plan)
$exe = 'D:/measure-f12/gss-19f59cc3/tf2demoview.exe'
foreach ($name in $Plan) {
  $done = "$PSScriptRoot/$name.txt"
  if ((Test-Path $done) -and (Select-String -LiteralPath $done -SimpleMatch 'frames, mean' -Quiet)) { "kept $name"; continue }
  $c = @{}
  switch -Regex ($name) {
    '-F' { $c.Foreground = $true }
    '-C' { $c.Cover = $true }
    '-U' { $c.Uncover = $true }   # added after the base rate: background, made TOPMOST without activation
    '-W' { $c.CoverFull = $true }   # added after 13-F: background, the whole rect under a TopMost form
    '-M' { $c.Minimize = $true }    # added after 13-F: minimized without activation
    '-S' { $c.Pos = '1820,100'; $c.Uncover = $true }   # added after 37-X: TOPMOST, no activation, only a 100 px strip on screen
    '-X' { $c.Pos = '-3000,100' }   # added after 18-U: the whole window off the screen (invisible, not minimized, not covered)
    '-O' { $c.Pos = '40,40' }
    '-K' { $c.ColdSeconds = 300 }
    '-Z' { $c.Mode = 0 }
  }
  & "$PSScriptRoot/run.ps1" -Name $name -Exe $exe @c
  if ($LASTEXITCODE -ne 0) { "stopped at $name, exit $LASTEXITCODE"; exit 1 }
}
'grid done'
