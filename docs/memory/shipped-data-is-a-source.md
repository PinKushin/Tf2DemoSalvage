---
name: shipped-data-is-a-source
description: VMTs, .res files and VPK contents answered two questions filed as needing a decompiler; the game's data ships with prose explaining itself.
metadata:
  type: project
---

**The source menu lists the SDK, the Rust parser, the wiki and a decompiler. It is missing the
game's own shipped data**, and that fifth source settled two questions this project had filed as
closed. Both on 2026-08-16, within an hour of each other.

- **`$modblend`** was the standing worked example for "the SDK cannot answer this, decompile it". No
  decompiler needed. It is declared in three shipped VMTs and read by **nothing in TF2** — the only
  consumer is an `Equals` proxy **commented out four lines below it** in the same file. No published
  shader declares it, and it is absent from 21 TF2 binaries across six eras including all five
  `stdshader_*.dll` (re-verified 2026-08-19 with a positive control, because the claim rests on an
  absence). Correct implementation here is nothing.

  **It was never a shader parameter.** A proxy resolves `srcVar1` by NAME on the material —
  `pMaterial->FindVar( pSrcVar1, &foundVar, true )` (`functionproxy.cpp:210`,
  `imaterial.h:484`) — and any key written into a VMT becomes a material var. So `$modblend` is an
  **artist-authored variable** holding a constant for the `Equals` proxy that is commented out
  beside it. No shader declares it because none ever could; no binary names it because none would.
  All three declaring materials are TF2 content and two are MvM (2012), so it is not inherited
  boilerplate either.

  **Do not call it dead, and do not generalise from it.** "Dead" was the old wording and the owner
  corrected it: a parameter this game ignores may be live elsewhere in the Source family. That is
  true for `$vertexcolor` — a `MATERIAL_VAR_*` flag, engine-level, consumed by `cable_dx6.cpp` and
  the fixed-function `decal.cpp`, merely unreachable from a DX9 `LightmappedGeneric` world face. It
  is NOT true for `$modblend`, which is a name someone typed. The distinction is flag versus
  authored key, and it decides whether "unimplemented" means anything.
- **Game event field widths and signedness** were "outside the SDK, `GameEventManager` is closed".
  They are in the comment block atop `game/mod_hl2mp/resource/modevents.res`: `short` is 16-bit
  **signed**, `long` 32-bit signed, `bool` 1 bit unsigned. Signedness had been assumed, and getting
  it backwards yields a plausible number rather than an error.

**Why it gets skipped: data does not feel like a source.** The habit is to look for code, and a
`.res` or `.vmt` reads as content rather than as documentation — but Valve's data files carry prose
explaining their own format, written for the people who author them.

**The rule: when the question is about a format the GAME reads, read what the game ships.** When it
is about engine behaviour, the code sources still apply.

**A third instance, 2026-08-26: `tf/cvarlist.log`.** The game ships a plain-text dump of **3,668
convars and concommands with their defaults, flags and help strings**, in fixed columns:

```
fps_max                                  : 400      :                  : Frame rate limiter, cannot be set while connected to a server.
engine_no_focus_sleep                    : 50       : , "a"            :
```

Look one up with `grep -E "^<name> +:"` — anchored and with the colon, or `fps_max` matches inside
other convars' help text and `volume` matches eleven others. It covers everything registered in
`engine.dll`, `materialsystem.dll` and `vguimatsurface.dll`, none of which are in `source-sdk-2013`.

**It is a dump, not a declaration**, so an `FCVAR_ARCHIVE` convar the user has changed could be
captured as if it were the default. Cross-check against a registration where one exists; where none
does, this beats scanning PE strings by a wide margin. Detail in
`docs/findings/40-the-game-ships-its-own-cvar-list.md`.

Practical notes for doing it:

- VMTs live inside VPKs. `grep -a` works directly on the `.vpk`, and `dd` around the byte offset from
  `grep -abo` gives readable context without unpacking anything.
- Extracting `$`-prefixed strings from `bin/stdshader_dx9.dll` yields **515** parameter names — a
  usable denominator for what the shipped shaders actually accept, no decompiler involved.
- An absence measured this way needs a control in the same sweep. A grep returning zero has been a
  fact about the grep three times in this project; see
  [[an-uncoverable-gap-is-usually-your-reader]].

Related: [[tf2-game-code-is-in-the-sdk]], [[binaries-answer-what-the-sdk-cannot]],
[[read-the-spec-before-measuring-our-data]].
