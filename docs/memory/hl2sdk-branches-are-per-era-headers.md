---
name: hl2sdk-branches-are-per-era-headers
description: "AlliedModders hl2sdk keeps a branch per Source engine generation, so era-specific SDK headers read on GitHub without decompiling."
metadata: 
  node_type: memory
  type: reference
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-10T22:50:58.098Z
---

**Cloned locally: `F:\src\hl2sdk`, all 27 branches, ~718MB.** Switch eras: `git -C F:/src/hl2sdk
checkout <branch>`. `F:\src\source-sdk-2013` is the 2013 snapshot and only tree with shader source
(`materialsystem/stdshaders`) — hl2sdk has headers/game code, no shaders anywhere.

`alliedmodders/hl2sdk` keeps a branch per engine generation: `episode1`, `orangebox` (2007-2011),
`css`, `dods`, `hl2dm`, `l4d`, `l4d2`, `portal2`, `swarm`, `sdk2013`, `csgo`, `cs2`, `dota`,
`deadlock`, `tf2` (current), plus mod SDKs. Reading the same header across branches answers "did this
change across eras?" without a decompiler.

Settled B112: `PlayerAnimEvent_t` is byte-identical for ordinals 0-29 across `orangebox`,
`source-sdk-2013`, `tf2` — proving the enum is append-only, one event mapping decodes every protocol.

**A worked negative result:** `CRC_MapFile` is byte-identical between `orangebox` and
`source-sdk-2013` — the map checksum never changed across eras. Check era stability BEFORE assuming a
difference.

**What no SDK branch has: engine source** — `checksum_engine.cpp`, the world renderer, none of it
ships. That's a decompiler question. See [[nothing-is-closed]]. Complements
[[era-axis-is-measured]], [[conformance-test-before-implementation]].

---

## `proto-version-h-enumerates-the-boundaries` — the protocol list lives in this same repo

`common/proto_version.h` (branch `tf2`) is the authoritative list of protocol boundaries the engine
still honours, since the live engine still plays old demos. Each constant names what changed.

**Read the convention: each constant names the last build WITHOUT the change** —
`PROTOCOL_VERSION_17` is "MD5 in map version", MD5 appears at 18. Getting this backwards inverts every
derived rule.

**Why more than an ordinary reference:** four protocol rules inferred from `demostf/parser` mapped
exactly onto 16, 17, 22, 23 — validating them, but the file lists five more, one of which (string
table compression flag) was a live bug — reading a flag never sent shifts everything behind it.
Inferring from another implementation only tells you what THAT implementation needed for the demos it
was tested on.

**How to apply:** before any protocol-conditional work, open `proto_version.h`
(`git clone --depth 1 --branch tf2 https://github.com/alliedmodders/hl2sdk`). `orangebox` has that
era's game-side headers, not the engine's own netmessage table.

Related: [[arithmetic-settles-disputes]], [[research-before-code]], [[layer2-is-a-dependency-chain]].
