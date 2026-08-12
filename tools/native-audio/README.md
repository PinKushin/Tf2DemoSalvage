# Native voice codec binaries

`celt.dll` and `speex.dll` are not committed. They are built from upstream Xiph source by
`build.ps1` in this directory, run once per machine (or in CI), and placed where
`Tf2DemoSalvage.Audio`'s native resolver looks for them.

## Why these versions specifically

TF2's voice system used three codecs across its history (`docs/findings/02-net-messages.md`):

| era | codec | source pinned here |
|---|---|---|
| 2007–2016 | `vaudio_speex` | Speex 1.2.1 — the bitstream has been stable across the whole 1.2.x line, so the latest release decodes older frames correctly. |
| 2016–~2018 | `vaudio_celt` | **CELT 0.11.3 exactly** — CELT's bitstream was never guaranteed stable across versions, which is *why* it was folded into Opus rather than kept standalone. A newer CELT does not exist as a separate thing to fetch; only 0.11.3 decodes what `vaudio_celt.dll` produced. |
| 2018–present | `steam` (Opus) | Not here — ships as the `libopus` NuGet package, prebuilt per-RID. See `managed/Tf2DemoSalvage.Audio`. |

## Building

```powershell
pwsh tools/native-audio/build.ps1
```

Requires the MSVC C++ toolset (same one the rest of this repo builds with) and network access to
clone two small upstream repositories into a temp directory. Produces `celt.dll` and `speex.dll`
in this directory; both are `.gitignore`d.

## The CELT 0.11.3 upstream gap

CELT 0.11.3's checked-in `libcelt/static_modes_float.c` references two tables —
`eband5ms` and `band_allocation` — that it never defines. They exist, `static` and private, in
`modes.c` instead. This is a real gap in the upstream release, not a mistake in this build: both
`static_modes_fixed.c` and `static_modes_float.c` have it, and it is why a plain `cl` invocation
over the official source tree fails with `C2065: 'eband5ms': undeclared identifier`.

`build.ps1` supplies both tables verbatim, byte-for-byte from `modes.c`'s own definitions, as a
small separate translation unit (`missing_tables.c`, generated at build time) — not by editing the
vendored source. See the script for the exact values and where they come from.

## Provenance

- CELT: `https://github.com/Distrotech/celt`, tag `v0.11.3` — a mirror of `git://git.xiph.org/celt.git`
  (the original host is gone), confirmed via the GitHub API (`"description": "Mirror of
  git://git.xiph.org/celt.git"`, not a fork).
- Speex: `https://github.com/xiph/speex`, tag `Speex-1.2.1` — the canonical upstream repository.

Both BSD-style licensed (see each project's `COPYING`); the compiled binaries are redistributable.
