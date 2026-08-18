---
name: hl2sdk-branches-are-per-era-headers
description: AlliedModders hl2sdk keeps a branch per Source engine generation, so era-specific SDK headers read on GitHub without decompiling.
metadata:
  type: reference
---

`github.com/alliedmodders/hl2sdk` keeps a **branch per engine generation** — `orangebox`
(2007–2011), `sdk2013`, `tf2` (current), plus `css`, `dods`, `l4d2`, `csgo`, `cs2` and more.
Reading the same header across branches answers "did this change across eras?" without a decompiler
and without downloading anything — raw GitHub fetch of one file.

This settled the B112 slice-3b era question: `PlayerAnimEvent_t` in
`game/shared/Multiplayer/multiplayer_animstate.h` is byte-identical for ordinals 0–29 across
`orangebox`, `source-sdk-2013` and `tf2`, proving the enum is append-only, so one event mapping
decodes every protocol. See `docs/findings/25-gesture-layer.md`.

Use it whenever a question is "is this SDK construct stable across the era span". `source-sdk-2013`
is one 2013 snapshot; hl2sdk gives the older and newer ends. List branches with
`gh api repos/alliedmodders/hl2sdk/branches --jq '.[].name'`. It is published source, not a
decompile — cite it freely. Complements [[era-axis-is-measured]] (dating a build) and
[[conformance-test-before-implementation]] (read the source before measuring our data).
