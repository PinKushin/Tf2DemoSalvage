<#
.SYNOPSIS
    Builds celt.dll (CELT 0.11.3) and speex.dll (Speex 1.2.1) from upstream Xiph source, and
    silk.dll from Skype's SILK SDK 1.0.9.

.DESCRIPTION
    See README.md in this directory for why these exact versions are pinned. This script
    reproduces the build worked out by hand on 2026-08-11: clone the tagged source, apply the
    one known upstream gap (CELT 0.11.3's missing eband5ms/band_allocation table definitions),
    compile with the MSVC toolset already required to build the rest of this repo, and place the
    two DLLs where Tf2DemoSalvage.Audio's NativeLibraryResolver looks for them.

    Idempotent: safe to re-run. Existing output DLLs are overwritten, not appended to.
#>

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$work = Join-Path $env:TEMP 'tf2demosalvage-native-audio-build'
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $work | Out-Null

# --- Locate the MSVC toolset the rest of this repo already requires --------------------------
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    throw "vswhere.exe not found. Install the 'Desktop development with C++' workload."
}

$vsPath = & $vswhere -latest -products '*' `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsPath) {
    throw "No Visual Studio installation with the C++ (VC.Tools.x86.x64) component was found."
}

Import-Module "$vsPath\Common7\Tools\Microsoft.VisualStudio.DevShell.dll"
Enter-VsDevShell -VsInstallPath $vsPath -SkipAutomaticLocation -DevCmdArguments "-arch=x64 -host_arch=x64" | Out-Null

function Assert-Success($what) {
    if ($LASTEXITCODE -ne 0) { throw "$what failed with exit code $LASTEXITCODE." }
}

# =================================================================================================
# CELT 0.11.3
# =================================================================================================
Write-Host "==> Cloning CELT 0.11.3"
$celtSrc = Join-Path $work 'celt-src'
git clone --quiet --branch v0.11.3 --depth 1 https://github.com/Distrotech/celt.git $celtSrc
Assert-Success "git clone celt"

$celtLib = Join-Path $celtSrc 'libcelt'
$celtBuild = Join-Path $work 'celt-build'
New-Item -ItemType Directory -Path $celtBuild | Out-Null

# Floating-point build with CUSTOM_MODES, for a Win32 target. Autotools does not run under
# MSVC, so this is hand-written per README.Win32's own instructions rather than generated.
#
# Neither CUSTOM_MODES nor ENABLE_POSTFILTER is configure.ac's default, and BOTH are required.
# B33 established each the hard way.
#
# CUSTOM_MODES: TF2's vaudio_celt.dll picks parameters from a { Fs, frame_size, len } table and
# uses entry 3 - 22050 Hz, 512 samples, 64-byte frames. That is not one of the static modes a
# default build compiles in (those are 48000 Hz only), so celt_mode_create refuses it outright.
#
# ENABLE_POSTFILTER: without it, celt.c does not merely skip the postfilter fields - it returns
# CELT_CORRUPTED_DATA the moment a frame's postfilter bit is set. Leaving it off (to "match
# upstream's default") made 56% of real corpus frames fail as corrupt when they were perfectly
# valid. With it, the corpus goes from 43.7% to 100%.
@'
#define CELT_BUILD
#define FLOATING_POINT
#define USE_ALLOCA
#define CUSTOM_MODES
#define ENABLE_POSTFILTER
'@ | Set-Content (Join-Path $celtBuild 'config.h')

# The upstream gap - see README.md "The CELT 0.11.3 upstream gap". Values transcribed verbatim
# from libcelt/modes.c's own `static const` definitions of the same two arrays.
@'
/* Copyright 2007-2009 Xiph.Org Foundation. Same license as the rest of CELT (see COPYING). */
/* Supplies two tables CELT 0.11.3's checked-in static_modes_float.c references but never
   defines - see README.md "The CELT 0.11.3 upstream gap" in this repo. Values transcribed
   verbatim from libcelt/modes.c. */
#include "celt_types.h"

const celt_int16 eband5ms[] = {
  0,  1,  2,  3,  4,  5,  6,  7,  8, 10, 12, 14, 16, 20, 24, 28, 34, 40, 48, 60, 78, 100
};

const unsigned char band_allocation[] = {
  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,
 90, 80, 75, 69, 63, 56, 49, 40, 34, 29, 20, 18, 10,  0,  0,  0,  0,  0,  0,  0,  0,
110,100, 90, 84, 78, 71, 65, 58, 51, 45, 39, 32, 26, 20, 12,  0,  0,  0,  0,  0,  0,
118,110,103, 93, 86, 80, 75, 70, 65, 59, 53, 47, 40, 31, 23, 15,  4,  0,  0,  0,  0,
126,119,112,104, 95, 89, 83, 78, 72, 66, 60, 54, 47, 39, 32, 25, 17, 12,  1,  0,  0,
134,127,120,114,103, 97, 91, 85, 78, 72, 66, 60, 54, 47, 41, 35, 29, 23, 16, 10,  1,
144,137,130,124,113,107,101, 95, 88, 82, 76, 70, 64, 57, 51, 45, 39, 33, 26, 15,  1,
152,145,138,132,123,117,111,105, 98, 92, 86, 80, 74, 67, 61, 55, 49, 43, 36, 20,  1,
162,155,148,142,133,127,121,115,108,102, 96, 90, 84, 77, 71, 65, 59, 53, 46, 30,  1,
172,165,158,152,143,137,131,125,118,112,106,100, 94, 87, 81, 75, 69, 63, 56, 45, 20,
200,200,200,200,200,200,200,200,198,193,188,183,178,173,168,163,158,153,148,129,104,
};
'@ | Set-Content (Join-Path $celtBuild 'missing_tables.c')

# extern decls forced ONLY into static_modes_float.c's compile - modes.c defines its own
# `static` copies of the same names, and an extern declaration in that same translation unit
# would conflict with them.
@'
#include "celt_types.h"
extern const celt_int16 eband5ms[];
extern const unsigned char band_allocation[];
'@ | Set-Content (Join-Path $celtBuild 'extern_decls.h')

Push-Location $celtBuild
try {
    Write-Host "==> Compiling CELT"
    # From libcelt/Makefile.am's source list, minus c64_fft.c (TI C6x DSP target, never built
    # on this platform - kiss_fft.c is the FFT actually used here) and minus static_modes_fixed.c
    # (FIXED_POINT is not defined, so it would never be linked and its own EXPORT-macro-adjacent
    # symbols would just be dead weight).
    $celtNormalSources = @(
        'bands.c', 'celt.c', 'cwrs.c', 'entcode.c', 'entdec.c', 'entenc.c', 'header.c',
        'kiss_fft.c', 'laplace.c', 'mathops.c', 'mdct.c', 'modes.c', 'pitch.c', 'plc.c',
        'quant_bands.c', 'rate.c', 'vq.c'
    ) | ForEach-Object { Join-Path $celtLib $_ }

    # WIN32 (not the MSVC-default _WIN32) is what celt.h's EXPORT macro actually checks -
    # without it, __declspec(dllexport) never applies and the DLL exports nothing.
    cl /nologo /c /O2 /DHAVE_CONFIG_H /DWIN32 /I $celtBuild /I $celtLib $celtNormalSources
    Assert-Success "CELT normal sources"

    cl /nologo /c /O2 /DHAVE_CONFIG_H /DWIN32 /FI (Join-Path $celtBuild 'extern_decls.h') `
        /I $celtBuild /I $celtLib (Join-Path $celtLib 'static_modes_float.c')
    Assert-Success "CELT static_modes_float.c"

    cl /nologo /c /O2 /I $celtLib (Join-Path $celtBuild 'missing_tables.c')
    Assert-Success "CELT missing_tables.c"

    Write-Host "==> Linking celt.dll"
    link /nologo /DLL /OUT:celt.dll *.obj
    Assert-Success "CELT link"

    Copy-Item celt.dll $root -Force
}
finally {
    Pop-Location
}

# =================================================================================================
# Speex 1.2.1
# =================================================================================================
Write-Host "==> Cloning Speex 1.2.1"
$speexSrc = Join-Path $work 'speex-src'
git clone --quiet --branch Speex-1.2.1 --depth 1 https://github.com/xiph/speex.git $speexSrc
Assert-Success "git clone speex"

$speexLib = Join-Path $speexSrc 'libspeex'
$speexInc = Join-Path $speexSrc 'include'
$speexBuild = Join-Path $work 'speex-build'
New-Item -ItemType Directory -Path $speexBuild | Out-Null

# Official upstream Windows build config and export list - win32/config.h and
# win32/libspeex.def, unmodified. Unlike CELT, Speex ships an actual Windows build path, so
# nothing here is hand-derived.
Copy-Item (Join-Path $speexSrc 'win32\config.h') $speexBuild
Copy-Item (Join-Path $speexSrc 'win32\libspeex.def') $speexBuild

Push-Location $speexBuild
try {
    Write-Host "==> Compiling Speex"
    # Source list from win32/VS2008/libspeex/libspeex.vcproj - the most complete of the
    # checked-in Visual Studio project generations (VS2003/VS2005 both omit fftwrap.c,
    # kiss_fft.c, kiss_fftr.c and smallft.c, which win32/config.h's USE_SMALLFT path needs).
    $speexSources = @(
        'bits.c', 'cb_search.c', 'exc_10_16_table.c', 'exc_10_32_table.c', 'exc_20_32_table.c',
        'exc_5_256_table.c', 'exc_5_64_table.c', 'exc_8_128_table.c', 'fftwrap.c', 'filters.c',
        'gain_table.c', 'gain_table_lbr.c', 'hexc_10_32_table.c', 'hexc_table.c',
        'high_lsp_tables.c', 'kiss_fft.c', 'kiss_fftr.c', 'lpc.c', 'lsp.c', 'lsp_tables_nb.c',
        'ltp.c', 'modes.c', 'modes_wb.c', 'nb_celp.c', 'quant_lsp.c', 'sb_celp.c', 'smallft.c',
        'speex.c', 'speex_callbacks.c', 'speex_header.c', 'stereo.c', 'vbr.c', 'vq.c', 'window.c'
    ) | ForEach-Object { Join-Path $speexLib $_ }

    # WIN32;HAVE_CONFIG_H match win32/VS2008/libspeex/libspeex.vcproj's own
    # PreprocessorDefinitions exactly.
    cl /nologo /c /O2 /DWIN32 /DHAVE_CONFIG_H /I $speexBuild /I $speexLib /I $speexInc $speexSources
    Assert-Success "Speex sources"

    Write-Host "==> Linking speex.dll"
    link /nologo /DLL /DEF:libspeex.def /OUT:speex.dll *.obj
    Assert-Success "Speex link"

    Copy-Item speex.dll $root -Force
}
finally {
    Pop-Location
}

# =================================================================================================
# SILK SDK 1.0.9 (Skype) - the codec inside Steam Voice, 2011-2016 (B441, D201)
# =================================================================================================
# Skype's own release zip, as archived from developer.skype.com (the host is gone). The archive's
# SHA-256 is checked before a byte of it is compiled - see README.md "Provenance". Only the
# fixed-point package is built: it is the reference implementation, and SILK's decoder is the
# same fixed-point code in every package of the zip.
Write-Host "==> Fetching SILK SDK 1.0.9"
$silkZip = Join-Path $work 'SILK_SDK_SRC_v1.0.9.zip'
Invoke-WebRequest -UseBasicParsing -OutFile $silkZip `
    -Uri 'https://web.archive.org/web/20130205194757id_/https://developer.skype.com/silk/SILK_SDK_SRC_v1.0.9.zip'
$silkHash = (Get-FileHash $silkZip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($silkHash -ne 'a060e71470680ff44a53b33d62c15787419c57fa0b4ccee68da76df2b4718582') {
    throw "SILK_SDK_SRC_v1.0.9.zip has SHA-256 $silkHash, not the recorded one - refusing to build it."
}

$silkSrc = Join-Path $work 'silk-src'
Expand-Archive $silkZip -DestinationPath $silkSrc
$silkFix = Join-Path $silkSrc 'SILK_SDK_SRC_FIX_v1.0.9'
$silkBuild = Join-Path $work 'silk-build'
New-Item -ItemType Directory -Path $silkBuild | Out-Null

# The SDK ships no DLL build and no export list (its Visual Studio project is a static library),
# so the exports are named here: the decoder, plus the encoder that lets the tests author a real
# SILK packet instead of trusting hand-built bytes.
@'
EXPORTS
SKP_Silk_SDK_Get_Decoder_Size
SKP_Silk_SDK_InitDecoder
SKP_Silk_SDK_Decode
SKP_Silk_SDK_Get_Encoder_Size
SKP_Silk_SDK_InitEncoder
SKP_Silk_SDK_Encode
'@ | Set-Content (Join-Path $silkBuild 'silk.def')

Push-Location $silkBuild
try {
    Write-Host "==> Compiling SILK"
    # Every file in src/, which is what the SDK's own Makefile builds (SRCS_C = src/*.c), with
    # Silk_FIX.vcxproj's Release definitions.
    $silkSources = Get-ChildItem (Join-Path $silkFix 'src') -Filter '*.c' | ForEach-Object FullName
    cl /nologo /c /O2 /DWIN32 /DNDEBUG /I (Join-Path $silkFix 'interface') /I (Join-Path $silkFix 'src') $silkSources
    Assert-Success "SILK sources"

    Write-Host "==> Linking silk.dll"
    link /nologo /DLL /DEF:silk.def /OUT:silk.dll *.obj
    Assert-Success "SILK link"

    Copy-Item silk.dll $root -Force
}
finally {
    Pop-Location
}

Write-Host "==> Done. celt.dll, speex.dll and silk.dll are in $root"
