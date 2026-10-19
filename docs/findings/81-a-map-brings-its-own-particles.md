# 81. A map brings its own particles (2026-10-19)

B554: a map's own `maps/<name>_particles.txt` was not read, so a community map's custom effects were lost. The owner
said to fill it: *"Yes it's a gap, we need to fill, even if it doesn't come up much"*. This finding is what the engine
does, read before anything was built, and the two places where it differs from what the B554 entry assumed.

## What we believed

The B554 entry assumed one file, `maps/<name>_particles.txt`, read from the pakfile first. It assumed a 64-entry cap
where "the 65th entry is ignored", and a pak definition that overrides a stock one. It assumed nothing about
repeated names inside the stock set. `MapAssets.ReadDefinitions` had a comment saying *"Earlier files win, which is
the manifest's own order"*, and that comment was an interpolation.

## What the engine does

**Three candidate manifests. The first one that loads is used, and they are never merged.** *Read from published
source*, `ParseParticleEffectsMap`, `game/shared/particle_parse.cpp:191-197`:

```cpp
// In order:
//  - particles.txt within the map BSP
//  - mapname_particles.txt within the map BSP
//  - mapname_particles.txt in the GAME path
bool bLoaded = ( manifest->LoadFromFile( filesystem, "particles.txt", "BSP" ) ||
                 manifest->LoadFromFile( filesystem, szMapManifestFilename, "BSP" ) ||
                 manifest->LoadFromFile( filesystem, szMapManifestFilename, "GAME" ) );
```

The path is `maps/%s_particles.txt` (`:186`). The name is the level's base name, lowered first (`Q_FileBase`,
`Q_strlower`, `:159-160`). The B554 entry did not know about the first candidate, a bare `particles.txt` at the
pakfile's root. When no candidate loads, the function returns without a warning: *"Don't print a warning, and don't
proceed any further if the file doesn't exist!"* (`:237`).

**Only a file directly inside `particles/`.** *Read*, `:214-227`. The engine strips the filename with
`V_StripFilename`, which truncates at the last `/` or `\` (`tier1/strtools.cpp:2078-2093`), skips one leading `!`, and
compares the rest to `particles` with `Q_stricmp`. Anything else gets a warning and is skipped: *"Custom particles
must be placed in the particles directory."* A subfolder (`particles/sub/x.pcf`), another folder, and a bare name all
fail. A bare name fails because, with no separator, the truncation leaves an empty string.

**The cap counts accepted entries and ends the walk.** *Read*, `:205-210`. The check runs before every subkey, and
`files` holds only accepted entries:

```cpp
if ( files.Count() >= PARTICLES_MANIFEST_MAX_MAP_ENTRIES )
{
    Warning( "CParticleMgr::LevelInit:  Map particles manifest '%s' specifies more than %d entries.\n", ... );
    break;
}
```

So the first 64 accepted entries are kept, there is one warning, and nothing after that is read, valid or not. A
rejected entry does not use up a place under the cap. `PARTICLES_MANIFEST_MAX_MAP_ENTRIES` is 64 (`:31`). Valve's
comment says why the cap exists: people would *"drop bogus manifests into download directory to DoS rival community
maps"*.

**Each file is read through `"GAME"`, so the pakfile comes first.** *Disassembled*: `ReadParticleConfigFile` in the
SDK's `particles.lib` (`particles.obj`, the Ghidra project `tf2enginex64`, `000e4f50`) does three things. It strips
the `!`. It tries a `_dx80` or `_dx90_slow` variant only below DX level 90, or at 90 on slow hardware. It opens the
path through `"GAME"`. A missing file prints `Particles: Missing '%s'` and returns false. A file that is not a
particle DMX prints `Unable to read particle definition` (`ReadParticleDefinitions`, `000e5150`). The caller's loop
continues to the next file in both cases. The pakfile's place at the head of `"GAME"` is not re-derived here:
`PakFile.AheadOf` already holds it for soundscapes and sounds (B465, B485).

**A repeated name is replaced, with case folded.** *Disassembled*, `CParticleSystemDictionary::AddParticleSystem`
(`000dcd90`). It looks the name up in its `CUtlStringMap`, and if the name is present it destroys the old
definition and stores the new one under the same name. The map is case-insensitive: the dictionary's constructor
(`000db680`) builds the symbol table with `caseInsensitive = 1`, which matches `CUtlStringMap( bool caseInsensitive
= true )` (`public/tier1/utlstringmap.h:19`, read). **The later definition wins.** That held for the stock set too.
The comment that said otherwise was wrong, and that part is filed separately as B555.

**The map's pass runs last, so the map wins over stock.** *Read*, `CHLClient::LevelInitPreEntity`,
`game/client/cdll_client_int.cpp:1605-1619`. It calls `ReloadParticleEffects()` first. That function parses the map's
manifest and then reloads every stock file (`particle_parse.cpp:149-166`), so on its own the stock reload would
overwrite the map. Then `LevelInitPreEntity` calls `ParseParticleEffectsMap( pMapName, true )` again. Because of that
second call, the map's definitions are the ones left standing. Map-over-stock is a consequence of call order, not a
rule anywhere in the code.

## What was built

- `ParticleManifest.MapFiles` covers the three candidates, the folder rule and the cap.
- `ParticleManifest.Layer` adds the map's definitions on top of the install's set, with a later definition of a name
  replacing an earlier one and case folded.
- `MapAssets.LoadRocketTrail` calls both with the level's name. The name comes from `LevelSystems.Load`'s `mapName`,
  the engine's `MapName()`, through `LoadedMap.Read`. It does not come from `MapNameIn(pak)`, which is a compile-time
  path and is absent from a map with no patched materials.
- **The install's shared set (finding 80) is never written into.** A map whose manifest accepts no file gets that
  instance itself, so it costs no copy. A map that has one gets a copy with its own definitions on top. Tests:
  `MapParticleManifestTests` (synthetic), and `Load_TwoMapsOnOneInstall_ShareOneSetOfParticleDefinitions`, which
  asserts the shared instance on f12 when a map name is passed.

## The reading picked, and what is not reproduced

**Per-map isolation is the fresh-client reading, and it is not the engine's.** *Read, and interpolated from the
absence of any removal*: nothing in `particle_parse.cpp` or `cdll_client_int.cpp` clears the definition dictionary at
level shutdown, and the closed library was not searched for one. A client that loaded map A and then map B would
therefore still hold A's uniquely named definitions while on B. Same names are overwritten by B's stock reload; only
names A alone defined would linger. A demo does not record which map the client had loaded before, so a viewer cannot
reproduce that leak. Each map here sees only its own definitions over the install's. The owner can falsify this
reading.

**Not modelled:**

- Whether a malformed first candidate falls through to the next. `KeyValues::LoadFromFile` decides that, and the
  stream reader here counts any non-empty file as loaded.
- The `_dx80` and `_dx90_slow` variants.
- A pakfile that ships a `.pcf` under a stock path. The engine's stock reload reads through `"GAME"`, so such a file
  probably replaces the stock one even with no manifest. When the pakfile is mounted relative to that reload has not
  been checked, so this is filed in the report rather than built.
