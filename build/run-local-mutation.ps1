# Weekly overnight Stryker run on the owner's PC, for the projects a GitHub runner cannot fully test.
#
# Why here and not in Actions (D212): Audio, Content and Rendering have tests that read client-only TF2
# archives (tf2_textures_*.vpk, tf2_sound_misc_*.vpk). Anonymous SteamCMD only fetches the dedicated
# server, which lacks them, and Valve's content must never be uploaded anywhere a public repo reaches.
# The owner's install has them, so these three projects get their full measurement here.
#
# It takes the machine-wide lock (run-exclusive.ps1): Stryker rebuilds mutated copies continuously and
# holds obj/, so it must never overlap a UI suite or a manual check. It waits rather than refusing.
#
#   pwsh build/run-local-mutation.ps1                 # all three
#   pwsh build/run-local-mutation.ps1 -Project Audio  # one
#
# The first scheduled run (2026-10-11) finished Audio in six minutes, then spent seven and a half hours
# inside Content and was stopped. Two causes, both fixed: Stryker reran 25-40 second real-map tests for
# every mutant they cover (now tagged MutationHeavy and filtered out by each stryker-config.json - see
# tests/STRYKER-NOTES.md), and this checkout had no native codec DLLs, so fifteen Audio tests failed
# before a single mutant ran (copied in below).
#
# Runs against a dedicated worktree at -Checkout, reset to origin/main each time, so it never touches a
# worktree an agent is working in. Reports land in -OutRoot\<yyyy-MM-dd>\<project>.
param(
    [string[]]$Project = @('Audio', 'Content', 'Rendering'),
    [string]$Repo = 'C:\Users\pinku\source\repos\PinKushin\Tf2DemoSalvage',
    [string]$Checkout = 'F:\Tf2DemoSalvage-mutation',
    [string]$OutRoot = "$env:LOCALAPPDATA\Tf2DemoSalvage\mutation",
    [string]$Lcor = 'F:\Tf2DemoSalvage-lcor'
)
$ErrorActionPreference = 'Stop'
$lock = 'C:\Users\pinku\source\repos\PinKushin\run-exclusive.ps1'

if (-not (Test-Path $Checkout)) {
    git -C $Repo worktree add --detach $Checkout origin/main
    if ($LASTEXITCODE -ne 0) { throw "worktree add failed ($LASTEXITCODE)" }
}
git -C $Checkout fetch --quiet origin
git -C $Checkout checkout --quiet --detach origin/main
if ($LASTEXITCODE -ne 0) { throw "checkout of origin/main failed ($LASTEXITCODE)" }

# Demo-reading tests need lcor; a worktree reaches it through a junction (docs/memory/lcor-is-not-in-a-worktree.md).
$local = Join-Path $Checkout 'tools\corpus\local'
if (-not (Test-Path $local)) { New-Item -ItemType Junction -Path $local -Target $Lcor | Out-Null }

# celt/speex/silk are built once by tools/native-audio/build.ps1 and are not committed, so a fresh worktree
# has none and the codec tests fail in it. Copy the main checkout's; refuse to measure without them.
foreach ($dll in 'celt.dll', 'speex.dll', 'silk.dll') {
    $from = Join-Path $Repo "tools
ative-audio\$dll"
    if (-not (Test-Path $from)) { throw "$from is missing - run tools/native-audio/build.ps1 in $Repo first" }
    Copy-Item $from (Join-Path $Checkout "tools
ative-audio\$dll") -Force
}

Push-Location $Checkout
try { dotnet tool restore; if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed ($LASTEXITCODE)" } }
finally { Pop-Location }

$day = Get-Date -Format 'yyyy-MM-dd'
$failed = @()
foreach ($p in $Project) {
    $out = Join-Path $OutRoot "$day\$p"
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $dir = Join-Path $Checkout "tests\Tf2DemoSalvage.$p.Tests"
    Write-Host "== $p -> $out"
    # From the test project's folder, so its own stryker-config.json applies - as in mutation.yml.
    Push-Location $dir
    try {
        & pwsh $lock -TimeoutMinutes 600 dotnet stryker --output $out
        if ($LASTEXITCODE -ne 0) { $failed += "$p ($LASTEXITCODE)" }
    }
    finally { Pop-Location }
}
if ($failed) { throw "Stryker failed: $($failed -join ', ')" }
