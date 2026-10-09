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
