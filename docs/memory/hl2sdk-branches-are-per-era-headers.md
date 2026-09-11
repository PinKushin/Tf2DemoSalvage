---
name: hl2sdk-branches-are-per-era-headers
description: "AlliedModders hl2sdk keeps a branch per Source engine generation, so era-specific SDK headers read on GitHub without decompiling."
metadata: 
  node_type: memory
  type: reference
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-10T22:50:58.098Z
---

**Cloned locally as of 2026-08-28: `F:\src\hl2sdk`, all 27 branches, ~718 MB.** Switch eras with
`git -C F:/src/hl2sdk checkout <branch>`; it currently sits on `orangebox`. Alongside
`F:\src\source-sdk-2013`, which is the 2013 snapshot and the only tree with **shader source**
(`materialsystem/stdshaders`) — hl2sdk is headers and game code, no shaders in any branch.

`github.com/alliedmodders/hl2sdk` keeps a **branch per engine generation**. The full list:
`episode1`, `orangebox` (2007–2011), `css`, `dods`, `hl2dm`, `l4d`, `l4d2`, `portal2`, `swarm`,
`sdk2013`, `csgo`, `cs2`, `dota`, `deadlock`, `tf2` (current), plus mod SDKs (`bms`, `gmod`,
`insurgency`, `doi`, `contagion`, `nucleardawn`, `pvkii`, `bgt`, `blade`, `darkm`, `eye`, `mcv`).

Reading the same header across branches answers "did this change across eras?" without a decompiler.

This settled the B112 slice-3b era question: `PlayerAnimEvent_t` in
`game/shared/Multiplayer/multiplayer_animstate.h` is byte-identical for ordinals 0–29 across
`orangebox`, `source-sdk-2013` and `tf2`, proving the enum is append-only, so one event mapping
decodes every protocol. See `docs/findings/25-gesture-layer.md`.

Use it whenever a question is "is this SDK construct stable across the era span". `source-sdk-2013`
is one 2013 snapshot; hl2sdk gives the older and newer ends. It is published source, not a
decompile — cite it freely.

**A worked negative result, 2026-08-28.** `utils/common/bsplib.cpp`'s `CRC_MapFile` is byte-identical
between `orangebox` and `source-sdk-2013` — same lump walk, same comment. So the map checksum
algorithm never changed across eras, and a day spent suspecting era drift was spent on a hypothesis
this one command would have killed. **Check era stability BEFORE assuming an era difference**, not
after.

**What no SDK has, in any branch or year: engine source.** `checksum_engine.cpp`, the world renderer,
the map CRC's actual caller — none of it ships. Pulling more SDKs cannot answer an engine-behaviour
question; that is a decompiler question. See [[nothing-is-closed]]. Complements [[era-axis-is-measured]] (dating a build) and
[[conformance-test-before-implementation]] (read the source before measuring our data).

---

## `proto-version-h-enumerates-the-boundaries` — the protocol list lives in this same repo

`common/proto_version.h` in `alliedmodders/hl2sdk` (branch `tf2`) is the authoritative list of
network protocol boundaries the TF2 engine still honours. It ships in the *current* SDK because
the live engine still plays old demos, and every constant carries a comment naming what changed:
`PROTOCOL_VERSION_23` "NET_MAX_PAYLOAD_BITS went away", `PROTOCOL_VERSION_14` "create string
tables compression flag", and so on down to an unlabelled `PROTOCOL_VERSION_12`.

**Read the convention first: each constant names the last build _without_ the change.**
`PROTOCOL_VERSION_17` is "MD5 in map version" and the MD5 appears at 18. Getting this backwards
inverts every rule derived from the file.

**Why this matters more than an ordinary reference:** Tf2DemoSalvage had four protocol rules
inferred from reading `demostf/parser`, and treated them as complete. They map exactly onto 16,
17, 22 and 23 — which both validates them and shows what inference misses, because the file lists
five more. One of those, the string table compression flag, was a live bug: string tables are not
skippable, so reading a flag that was never sent shifts everything behind them.

Enumerating beats inferring here for the same reason [[fallbacks-do-not-make-guesses-safe]]:
reading another implementation tells you which rules that implementation needed for the demos it
was tested on, not which rules exist. `demostf/parser` runs on demos.tf's archive, which is
modern, so its coverage of old boundaries is exactly as thin as this project's corpus.

**How to apply:** before implementing or debugging any protocol-conditional behaviour, open
`proto_version.h`. The local clone above already has it; otherwise
`git clone --depth 1 --branch tf2 https://github.com/alliedmodders/hl2sdk`. `orangebox` carries that
era's game-side headers, but not the engine's own netmessage table.

Related: [[arithmetic-settles-disputes]], [[research-before-code]], [[layer2-is-a-dependency-chain]].
