---
name: a-level-reads-its-pakfile-first
description: "Anything the engine reads through the GAME path at or after level load sees the map's pakfile first; a reader handed the install alone misses a third of the maps' own content (B465, B485)."
metadata:
  node_type: memory
  type: project
  modified: 2026-10-04T00:00:00.000Z
---

**Before wiring a file read to `game.Archives.Read`, ask whether the engine reads it per LEVEL.** The engine mounts
the loaded map's pakfile at the head of the `"GAME"` search path, so every read through it — scripts, sounds,
materials, models — tries the map's own zip first. A reader built once per install, from the VPKs, cannot see it.

**Why:** the soundscape catalog was built once in `OpenGame` on the reasoning that it "comes from the install, not the
level". `C_SoundscapeSystem::LevelInitPreEntity` is `Shutdown(); Init();` and `Init` appends the map's own
`scripts/soundscapes_<map>.txt` — 71 of 239 installed maps ship one in the pakfile, and 3,700 placements on 66 maps
resolved to nothing (B465, measured with the `soundscape-map-scripts` probe). The sound cache still has the same
blind spot for pakfile waves — pl_venice carries 35 (B485, open). Map assets already did it right
(`pak.ReadFile(path) ?? archives.Read(path)` in `MapAssets`, `IvpMapWorld`, `PropModels`).

**How to apply:** a per-level reader is `pak.ReadFile(path) ?? install(path)` — `SoundscapeCatalog.ForLevel` is the
soundscape one. Check the system's `LevelInitPreEntity` for a rebuild before deciding a table belongs to the install.
And a census is cheap: the `pak` probe lists a map's pakfile, so count how many installed maps ship the file before
calling a gap rare.
