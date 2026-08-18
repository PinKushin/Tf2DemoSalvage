#!/usr/bin/env bash
#
# Builds libcelt.so (CELT 0.11.3) and libspeex.so (Speex 1.2.1) from upstream Xiph source.
#
#   bash tools/native-audio/build.sh [output-directory]
#
# The Linux counterpart of build.ps1, and a transcription of the same recipe rather than a new
# one — every compile-time decision below is explained in build.ps1 or in README.md, and the
# reasons are identical because they are facts about the SOURCE, not about the toolchain.
#
# WHY IT EXISTS: mutation testing runs on a Linux ARM64 box, and two of the three voice decoders
# could not load there at all. libopus comes from NuGet with a linux-arm64 asset, but celt and
# speex are built from source and only build.ps1 existed, which needs MSVC. See
# docs/MEASUREMENT-PLAN.md.
#
# Idempotent: existing output is overwritten, not appended to.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="${1:-$ROOT}"
WORK="$(mktemp -d -t tf2demosalvage-native-audio-XXXXXX)"
trap 'rm -rf "$WORK"' EXIT

# **Whichever C compiler is installed, rather than a hardcoded gcc.** The measurement box has
# clang and `cc` but no gcc at all — it was provisioned for the libFuzzer bridge, which needs
# clang — so hardcoding gcc failed there with "gcc is required" on a machine that can compile C
# perfectly well. CC overrides; otherwise cc, then clang, then gcc.
CC="${CC:-}"

if [ -z "$CC" ]; then
  for candidate in cc clang gcc; do
    if command -v "$candidate" >/dev/null; then
      CC="$candidate"
      break
    fi
  done
fi

[ -n "$CC" ] || { echo "no C compiler found (tried cc, clang, gcc; set CC)" >&2; exit 1; }
command -v git >/dev/null || { echo "git is required" >&2; exit 1; }

echo "==> Compiler: $CC ($("$CC" --version 2>/dev/null | head -1))"

echo "==> Building into $OUT (work: $WORK)"

# =================================================================================================
# CELT 0.11.3
# =================================================================================================
echo "==> Cloning CELT 0.11.3"
git clone --quiet --branch v0.11.3 --depth 1 https://github.com/Distrotech/celt.git "$WORK/celt-src"

CELT_LIB="$WORK/celt-src/libcelt"
CELT_BUILD="$WORK/celt-build"
mkdir -p "$CELT_BUILD"

# Neither CUSTOM_MODES nor ENABLE_POSTFILTER is configure.ac's default and BOTH are required —
# RISKS B33 established each the hard way. CUSTOM_MODES because TF2 asks for 22050 Hz / 512
# samples, which is not one of the static modes a default build compiles in; ENABLE_POSTFILTER
# because without it celt.c returns CELT_CORRUPTED_DATA the moment a frame's postfilter bit is
# set, which was 56 % of real corpus frames.
#
# No CELT_BUILD/WIN32 here: those exist on Windows only to make celt.h's EXPORT macro emit
# __declspec(dllexport). ELF exports every non-static symbol by default, so the macro is a no-op
# and defining WIN32 on Linux would be a lie the headers act on.
cat > "$CELT_BUILD/config.h" <<'EOF'
#define FLOATING_POINT
#define USE_ALLOCA
#define CUSTOM_MODES
#define ENABLE_POSTFILTER
EOF

# The upstream gap — see README.md "The CELT 0.11.3 upstream gap". 0.11.3's checked-in
# static_modes_float.c references two tables it never defines. Values transcribed verbatim from
# libcelt/modes.c's own `static const` definitions of the same arrays.
cat > "$CELT_BUILD/missing_tables.c" <<'EOF'
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
EOF

# extern decls forced ONLY into static_modes_float.c's compile — modes.c defines its own `static`
# copies of the same names, and an extern declaration in that translation unit would conflict.
cat > "$CELT_BUILD/extern_decls.h" <<'EOF'
#include "celt_types.h"
extern const celt_int16 eband5ms[];
extern const unsigned char band_allocation[];
EOF

echo "==> Compiling CELT"

# From libcelt/Makefile.am's source list, minus c64_fft.c (TI C6x DSP target — kiss_fft.c is the
# FFT actually used) and minus static_modes_fixed.c (FIXED_POINT is not defined).
CELT_SOURCES=(
  bands.c celt.c cwrs.c entcode.c entdec.c entenc.c header.c
  kiss_fft.c laplace.c mathops.c mdct.c modes.c pitch.c plc.c
  quant_bands.c rate.c vq.c
)

cd "$CELT_BUILD"

for source in "${CELT_SOURCES[@]}"; do
  "$CC" -c -O2 -fPIC -DHAVE_CONFIG_H -I "$CELT_BUILD" -I "$CELT_LIB" "$CELT_LIB/$source"
done

"$CC" -c -O2 -fPIC -DHAVE_CONFIG_H -include "$CELT_BUILD/extern_decls.h" \
  -I "$CELT_BUILD" -I "$CELT_LIB" "$CELT_LIB/static_modes_float.c"

"$CC" -c -O2 -fPIC -I "$CELT_LIB" -I "$CELT_BUILD" "$CELT_BUILD/missing_tables.c"

echo "==> Linking libcelt.so"
"$CC" -shared -o "$OUT/libcelt.so" ./*.o -lm

# =================================================================================================
# Speex 1.2.1
# =================================================================================================
echo "==> Cloning Speex 1.2.1"
git clone --quiet --branch Speex-1.2.1 --depth 1 https://github.com/xiph/speex.git "$WORK/speex-src"

SPEEX_SRC="$WORK/speex-src"
SPEEX_LIB="$SPEEX_SRC/libspeex"
SPEEX_INC="$SPEEX_SRC/include"
SPEEX_BUILD="$WORK/speex-build"
mkdir -p "$SPEEX_BUILD"

# **win32/config.h is used here too, and that is deliberate rather than lazy.** It is a plain
# feature-macro header — USE_SMALLFT, FLOATING_POINT, EXPORT — with nothing Windows-specific in
# the parts that matter, and using it keeps this build byte-comparable with build.ps1's. Running
# autotools instead would produce a DIFFERENT configuration (it probes for FFT libraries and CPU
# features), so the two platforms would then be testing different code.
sed 's/#define EXPORT __declspec(dllexport)/#define EXPORT/' \
  "$SPEEX_SRC/win32/config.h" > "$SPEEX_BUILD/config.h"

# **speex_config_types.h is GENERATED, and nothing in the tree ships one for this path.**
# speex_types.h includes it unconditionally; autotools writes it from
# include/speex/speex_config_types.h.in by substituting the four integer type names, and the
# Windows build gets its copy from win32/. Building without either is the error this hit:
#
#     fatal error: 'speex_config_types.h' file not found
#
# The substitutions are not a guess — they are the sizes the .in file's own @SIZE16@/@SIZE32@
# placeholders are filled with on every platform where short is 16 bits and int is 32, which is
# every platform this runs on. Written into the include directory so `#include "..."` finds it
# beside speex_types.h exactly as a configured tree would.
cat > "$SPEEX_INC/speex/speex_config_types.h" <<'EOF'
#ifndef __SPEEX_CONFIG_TYPES_H__
#define __SPEEX_CONFIG_TYPES_H__

typedef short spx_int16_t;
typedef unsigned short spx_uint16_t;
typedef int spx_int32_t;
typedef unsigned int spx_uint32_t;

#endif
EOF

echo "==> Compiling Speex"

# Source list from win32/VS2008/libspeex/libspeex.vcproj — the most complete of the checked-in
# project generations (VS2003/VS2005 omit the FFT sources win32/config.h's USE_SMALLFT needs).
SPEEX_SOURCES=(
  bits.c cb_search.c exc_10_16_table.c exc_10_32_table.c exc_20_32_table.c
  exc_5_256_table.c exc_5_64_table.c exc_8_128_table.c fftwrap.c filters.c
  gain_table.c gain_table_lbr.c hexc_10_32_table.c hexc_table.c
  high_lsp_tables.c kiss_fft.c kiss_fftr.c lpc.c lsp.c lsp_tables_nb.c
  ltp.c modes.c modes_wb.c nb_celp.c quant_lsp.c sb_celp.c smallft.c
  speex.c speex_callbacks.c speex_header.c stereo.c vbr.c vq.c window.c
)

cd "$SPEEX_BUILD"

for source in "${SPEEX_SOURCES[@]}"; do
  "$CC" -c -O2 -fPIC -DHAVE_CONFIG_H \
    -I "$SPEEX_BUILD" -I "$SPEEX_LIB" -I "$SPEEX_INC" "$SPEEX_LIB/$source"
done

# No .def file: that is the Windows way of choosing exports. On ELF every non-static symbol is
# exported already, and the four this project imports are among them.
echo "==> Linking libspeex.so"
"$CC" -shared -o "$OUT/libspeex.so" ./*.o -lm

echo "==> Done:"
ls -l "$OUT/libcelt.so" "$OUT/libspeex.so"
