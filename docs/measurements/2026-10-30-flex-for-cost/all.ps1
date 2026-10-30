# A sequence of runs, "<name>=<exe>;<name>=<exe>;...", in the order given (one string: `pwsh -File` passes an array as
# one argument). A run that misses the quiet gate or exits non-zero stops the sequence, so a gap is visible rather than
# silently skipped; a finished run already on disk (it holds the measurement's own line) is kept, so a stopped sequence
# resumes.
param([string]$Plan)
foreach ($entry in ($Plan -split ';')) {
  ($name, $exe) = $entry -split '=', 2
  $done = "$PSScriptRoot/$name.txt"
  if ((Test-Path $done) -and (Select-String -LiteralPath $done -SimpleMatch 'frames, mean' -Quiet)) { "kept $name"; continue }
  &"$PSScriptRoot/run.ps1" -Name $name -Exe $exe
  if ($LASTEXITCODE -ne 0) { "stopped at $name, exit $LASTEXITCODE"; exit 1 }
}
'all done'
