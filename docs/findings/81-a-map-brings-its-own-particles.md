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

**Inside one file, "later" means later in the root's array.** *Disassembled*, `ReadParticleDefinitions` (`000e5150`).
It unserializes the root and compares the root's type with `DmeParticleSystemDefinition`. If the root is a definition,
it calls `AddParticleSystem( root )` once and returns. Otherwise it reads the root's `particleSystemDefinitions`
attribute, checks that its type is 0xf (an element array), and adds each entry in array order. It never walks the
element table. The first version here walked the element table, and a review caught it. That root is element 0 is
*measured*: every shipped file's element 0 is `untitled`, and which element the closed unserializer returns as the
root was not read. On stock the difference is invisible. 73 of the 107 files store their definitions in a different
order from their array, but always the same set, and no file repeats a name (`ParticleDuplicateNamesDiagnostic`).

**There are two passes, and they are handed two different names.** *Read*, `CHLClient::LevelInitPreEntity`,
`game/client/cdll_client_int.cpp:1605-1619`:

1. It calls `ReloadParticleEffects()`. That function reads the map's manifest under `Q_FileBase( GetLevelName() )`,
   lowered (`particle_parse.cpp:150-161`), and *then* reloads every stock file (`:164-166`). The stock reload takes
   back every name the install defines.
2. It calls `ParseParticleEffectsMap( pMapName, true )` again (`:1619`). This pass runs after the stock reload, so
   whatever it reads wins over stock.

`pMapName` comes from the engine. *Disassembled*: `CClientState::SetSignonState`, signon state 5 (`engine.dll`
`FUN_1800a3e70`), calls `FUN_18008dd50` on the level name just before logging `LevelInitPreEntity: start`.
`FUN_18008dd50` keeps what follows the last `\` or `/` and cuts at the FIRST `.`; its search, `18031a82c`, is a
forward scan that stops at the first match or the null. So `pMapName` **is a base name**, and the paragraph's
conclusion stands. That the input to this cut is the level's own file name (`maps/<name>.bsp`) is *interpolated*:
the interface call that yields it (`PTR_PTR_180471748`, slot `0x20`) was not named.

**The two names differ when the level's name has a dot in it.** `V_FileBase` stops at the LAST dot
(`tier1/strtools.cpp:1850`), while the engine's cut stops at the FIRST. For an ordinary map (`cp_x`) both passes
read `maps/cp_x_particles.txt`, and the second pass is the whole answer. For a workshop level,
`maps/workshop/cp_x.ugc123.bsp`, the first pass reads `maps/cp_x.ugc123_particles.txt` and the second reads
`maps/cp_x_particles.txt`. A manifest that only the first pass finds keeps the names the install does not define, and
loses any stock name it redefines to the reload. The pakfile-root `particles.txt` comes first in both passes, so a
map that ships one is unaffected. The review that raised this proposed `cp_x.ugc123` for both passes. The
disassembly shows that is right only for the first pass. No demo in lcor is of a workshop map: all 772 were checked,
and the header's map-name field was read correctly as the control. `ParticleManifest.MapNames` and `Level` model
both passes.

## What was built

- `ParticleManifest.MapFiles` covers the three candidates, the folder rule and the cap.
- `ParticleManifest.Layer` adds the map's definitions on top of the install's set, with a later definition of a name
  replacing an earlier one and case folded.
- `ParticleManifest.Level` runs both passes around a stock "reload" that restores every stock name.
- `MapAssets.LoadRocketTrail` calls it with the level's name. The name comes from `LevelSystems.Load`'s `mapName`
  through `LoadedMap.Read`. **The parameter is required there**, so a probe cannot read a map without its manifest
  (D126). Probes pass the `.bsp` path, and `MapNames` cuts a path and a demo's name alike. The name does not come
  from `MapNameIn(pak)`, which is a compile-time path and is absent from a map with no patched materials.
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
