<#
.SYNOPSIS
    Makes celt.dll, speex.dll and silk.dll exist in this directory, with no manual step.
.DESCRIPTION
    Called by Tf2DemoSalvage.Audio.csproj's EnsureNativeAudio target only when one is missing
    (a fresh git worktree: the DLLs are git-ignored, see README.md). Cheapest source first:
    1. the main checkout's copy (seconds) - found through git's common dir, so it works from any worktree;
    2. build.ps1 (minutes: MSVC plus a network fetch).
    Fails with a message saying what to do, rather than leaving a later DllNotFoundException.
#>
$ErrorActionPreference = 'Stop'
$libs = 'celt.dll', 'speex.dll', 'silk.dll'
function Get-Missing { @($libs | Where-Object { -not (Test-Path (Join-Path $PSScriptRoot $_)) }) }

$missing = Get-Missing
if ($missing.Count -eq 0) { return }

Write-Host "native-audio: missing $($missing -join ', ') in $PSScriptRoot"
$common = git -C $PSScriptRoot rev-parse --path-format=absolute --git-common-dir 2>$null
if ($LASTEXITCODE -eq 0 -and $common) {
    $main = Join-Path (Split-Path $common -Parent) 'tools/native-audio'
    if ((Test-Path $main) -and ((Resolve-Path $main).Path -ne (Resolve-Path $PSScriptRoot).Path)) {
        foreach ($l in $missing) {
            if (Test-Path (Join-Path $main $l)) {
                Copy-Item (Join-Path $main $l) $PSScriptRoot
                Write-Host "native-audio: copied $l from the main checkout ($main)"
            }
        }
    }
}

if ((Get-Missing).Count -gt 0) {
    Write-Host "native-audio: still missing $((Get-Missing) -join ', '); running build.ps1 (needs MSVC and network)"
    & (Join-Path $PSScriptRoot 'build.ps1')
}
$missing = Get-Missing
if ($missing.Count -gt 0) {
    throw "native-audio: $($missing -join ', ') could not be produced. Run: pwsh tools/native-audio/build.ps1"
}
