<#
.SYNOPSIS
Publishes the viewer (tf2demoview) and the CLI (tf2demosalvage) for win-x64 and zips them as
artifacts/Tf2DemoSalvage-<version>-win-x64.zip. Releases nothing.

.DESCRIPTION
**Self-contained win-x64 (D202)**, so the user installs nothing: the owner's reason is that most
bugs in programs that need a .NET runtime are the user not installing the right one. Each app is a
folder, not a single file, so the native codecs stay beside the exe. The zip is larger than a
framework-dependent one; that is the accepted cost. Non-Windows libopus copies under runtimes/ are
deleted from the folders; runtimes/win-x64/native/opus.dll stays, because NativeLibraryResolver
reads AppContext.BaseDirectory/runtimes/<rid>/native/.

**The version has one home: <Version> in Directory.Build.props.** It is read back here, never
restated.

**Fails loudly** if celt.dll, speex.dll or silk.dll is missing (run tools/native-audio/build.ps1),
and checks the finished zip against the list of files it must contain, so a publish that silently
dropped a library fails here rather than on a user's machine.

**Never bundles game content or demos** - the user supplies their own TF2 install
(docs/memory/the-game-folder-is-the-users-to-provide.md). The zip is checked for .dem/.vpk/.bsp.

.EXAMPLE
pwsh build/package.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$rid = 'win-x64'

function Assert-Success($what) {
    if ($LASTEXITCODE -ne 0) { throw "$what failed with exit code $LASTEXITCODE." }
}

$version = (dotnet msbuild "$repo/managed/Tf2DemoSalvage.Cli/Tf2DemoSalvage.Cli.csproj" -getProperty:Version).Trim()
Assert-Success 'reading Version'
if (-not $version) { throw 'Version is empty - it belongs in Directory.Build.props.' }

$native = 'celt.dll', 'speex.dll', 'silk.dll'
foreach ($lib in $native) {
    if (-not (Test-Path "$repo/tools/native-audio/$lib")) {
        throw "tools/native-audio/$lib is missing. Run: pwsh tools/native-audio/build.ps1"
    }
}

$out = Join-Path $repo 'artifacts'
$stage = Join-Path $out "Tf2DemoSalvage-$version-$rid"
$zip = "$stage.zip"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }

$apps = @{
    viewer = 'managed/Tf2DemoSalvage.Viewer3D/Tf2DemoSalvage.Viewer3D.csproj'
    cli    = 'managed/Tf2DemoSalvage.Cli/Tf2DemoSalvage.Cli.csproj'
}
foreach ($name in $apps.Keys) {
    dotnet publish "$repo/$($apps[$name])" -c Release -r $rid --self-contained true -o "$stage/$name"
    Assert-Success "dotnet publish $name"
    Get-ChildItem "$stage/$name/runtimes" -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne $rid } | Remove-Item -Recurse -Force
}

Copy-Item "$repo/LICENSE" "$stage/LICENSE.txt"
Copy-Item "$repo/RELEASE-NOTES.md" "$stage/RELEASE-NOTES.md"
Copy-Item "$repo/tools/native-audio/licenses" "$stage/licenses" -Recurse

Compress-Archive -Path "$stage/*" -DestinationPath $zip
Remove-Item $stage -Recurse -Force

# --- Check the zip, not the staging folder: the zip is what ships. ---
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try { $entries = $archive.Entries | ForEach-Object { $_.FullName -replace '\\', '/' } }
finally { $archive.Dispose() }

$required = @(
    'LICENSE.txt', 'RELEASE-NOTES.md',
    'licenses/CELT-COPYING.txt', 'licenses/SPEEX-COPYING.txt', 'licenses/SILK-LICENSE.txt',
    'licenses/OPUS-COPYING.txt', 'licenses/OPENAL-SOFT-COPYING.txt',
    'viewer/tf2demoview.exe', 'viewer/tf2demoview.dll', 'viewer/tf2demoview.runtimeconfig.json',
    'viewer/coreclr.dll', 'viewer/hostfxr.dll', 'cli/coreclr.dll', 'cli/hostfxr.dll',
    # libopus keeps its runtimes/ fan-out even under a pinned RID; NativeLibraryResolver reads it
    # there. OpenAL Soft ships for Windows as soft_oal.dll, which Silk.NET's loader looks for.
    'viewer/runtimes/win-x64/native/opus.dll', 'viewer/soft_oal.dll',
    'cli/tf2demosalvage.exe', 'cli/tf2demosalvage.dll', 'cli/tf2demosalvage.runtimeconfig.json'
) + ($native | ForEach-Object { "viewer/$_" })
$missing = $required | Where-Object { $_ -notin $entries }
if ($missing) { throw "The zip is missing: $($missing -join ', ')" }

$forbidden = $entries | Where-Object { $_ -match '\.(dem|vpk|bsp|mdl|vtf|vmt)$' }
if ($forbidden) { throw "The zip contains game content or demos: $($forbidden -join ', ')" }

Write-Host "$zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB, $($entries.Count) files) - contents checked."
