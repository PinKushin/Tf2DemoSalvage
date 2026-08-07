# tf2-demo-salvage

A standalone parser (and, eventually, viewer) for Team Fortress 2 `.dem` files — built to work on demos from any era of TF2's 18-year history, including ones Valve's own client updates have broken.

Independent, clean-room project. Not affiliated with Valve. Ships no TF2 game assets.

## Status

Planning complete, implementation not started. See `ROADMAP.md` for the phased plan and `docs/DECISIONS.md` for why each architectural choice was made. `docs/FORMAT_NOTES.md` has the concrete format findings gathered so far. `CLAUDE.md` is the handoff brief for picking up implementation.

## Why

TF2 demos are self-contained — the network entity schema (`SendTables`) that describes how to decode a demo's data is embedded in the file itself, so a demo doesn't actually need to match the *current* game client's schema to be readable. The client crashes on old demos because it validates against its own live schema; a standalone parser that only reads what the file provides sidesteps that entirely. See `ROADMAP.md` §1 for the full explanation, and `docs/FORMAT_NOTES.md` for a real example (`z1800.dem`, a ~2015 demo that fails in the live client but is structurally intact).

## Plan at a glance

1. **Parse** — decode any-era `.dem` into structured/readable output (the actual "restore lost demos" deliverable).
2. **2D viewer** — scrub a match on a top-down map.
3. **3D viewer** — starts at primitive geometry (spheres/capsules, same shapes TF2's hitboxes already use), fidelity work comes later and isn't scoped yet.

Pure C# (`managed/`) — decode engine, CLI, and both viewers all live there. `native/libtf2dem` is a placeholder only; native code is a last resort, revisited only if Phase 3 profiling proves C# genuinely isn't enough for something. Full reasoning in `docs/DECISIONS.md`.

## Repo layout

```
native/libtf2dem/     placeholder — not used unless Phase 3 proves it necessary
managed/               decode engine (Tf2Demo.Core), CLI, viewers — all C#
tools/corpus/          reference demos + manifest
docs/                  decisions, format notes
tests/                 golden-output regression tests
```
