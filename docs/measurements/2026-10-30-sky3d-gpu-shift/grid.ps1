# Runs a list of name=exe-folder pairs in order through run.ps1. A finished run on disk (it holds "frames, mean") is kept,
# so a stopped sequence resumes; a gate miss or non-zero exit stops the sequence so a gap is visible.
# Usage: grid.ps1 01-C=gs-4a2878b0 02-B=b597-split ...
param([Parameter(ValueFromRemainingArguments)][string[]]$Plan)
foreach ($p in $Plan) {
  $name, $folder = $p -split '=', 2
  $done = "$PSScriptRoot/$name.txt"
  if ((Test-Path $done) -and (Select-String -LiteralPath $done -SimpleMatch 'frames, mean' -Quiet)) { "kept $name"; continue }
  & "$PSScriptRoot/run.ps1" -Name $name -Exe "D:/measure-f12/$folder/tf2demoview.exe"
  if ($LASTEXITCODE -ne 0) { "stopped at $name, exit $LASTEXITCODE"; exit 1 }
}
'grid done'
