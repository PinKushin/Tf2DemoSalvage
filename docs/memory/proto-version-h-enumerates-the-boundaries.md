---
name: proto-version-h-enumerates-the-boundaries
description: Valve's proto_version.h lists every demo-compatibility protocol boundary with what changed; check it before inferring protocol rules from another parser.
metadata:
  type: reference
---

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
`proto_version.h`. Clone with `git clone --depth 1 --branch tf2 https://github.com/alliedmodders/hl2sdk`.
The `orangebox` branch is the 2009-era SDK if game-side headers are needed, though it does not
carry the engine's own netmessage table.

Related: [[arithmetic-settles-disputes]], [[research-before-code]], [[layer2-is-a-dependency-chain]].
