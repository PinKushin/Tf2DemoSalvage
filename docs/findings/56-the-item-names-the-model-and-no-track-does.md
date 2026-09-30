# 56 — the item names the model, and no track does

**Subject:** why every hat in the game went undrawn, why a clean `MISSING 0` load report was telling the
truth, and how a census spent a day reporting its own omission as a defect in the demo.

**Evidence class:** measured on `20130518_0313_cp_granary_blu_blu` and in the viewer's own log;
read-from-source for `UpdateModelToClass` and `GetPlayerDisplayModel`.

---

## 1. The count that started it

`B379` had a number and no name: **12,069 props offered with no model at all**, about twelve per frame,
across 1,019 sampled frames. Its own entry said exactly what was missing —

> *"What is NOT established: which entities they are. The census counts the rejection and names the
> model (there is none), which is not enough — it needs the entity index and class carried to the
> rejection."*

A model path cannot name a prop that has no model path. The class can, and it is the one field that
survives the thing being missing. Carrying it into `DrawTally.NotDrawable`, with one example entity
index per bucket, split the population in a single run:

| count | class |
|---|---|
| 2,636 | `CTFWearable` |
| 145 | `CTFScatterGun` |
| 115 | `CTFPipebombLauncher` |
| 103 | `CTFRocketLauncher` |
| 65 | `CTFShotgun_Soldier` |
| 8 | `CTFSniperRifle` |

## 2. Most of the defect was the instrument

`ViewerCensusProbe` did this per frame:

```csharp
timeline.PropsAt(tick, drawn);
PlayerProps.Add(timeline.PlayersAt(tick), drawn, appearance, models);
models.Add(drawn, assets.Geometry);
models.UpdateClientSideAnimations(drawn);
models.Instances(drawn, instances, seconds: tick * timeline.IntervalPerTick);
```

which is a plausible-looking subset of `MomentScene.Build` with three things left out:
`WeaponModels.Resolve`, the attached-model supplier, and the paint supplier. A weapon whose model comes
from its **item** rather than from `m_nModelIndex` therefore arrived with no path and was counted as
undrawable. The census was reporting this project's own omission as a defect in the recording.

This is the fifth entry in `docs/memory/instrument-bugs-outnumber-decoder-bugs.md` — *"a probe that
skipped the resolution step the viewer runs"* — committed again, in the same file the memory is about.
The repository's own rule says it in one line: **probes run the production path or they are worthless.**

Driven through `Build` then `Pose`, the no-model population falls from **3,072 to 356**, all
`CTFWearable`, and every weapon bucket disappears. **About 86% of B379 was the probe.**

## 3. What the fixed instrument then exposed

With weapons and worn items resolving, a different bucket grew: `NO GEOMETRY` went from 36 distinct
models to 66, and the new ones are cosmetics — `xms_beard_soldier.mdl` 234 times,
`fwk_medic_stahlhelm.mdl` 173, `medic_mask.mdl` 173, `bdayhat_soldier.mdl` 170,
`soldier_grenade_skulls.mdl` 170, `c_rocketboots_soldier.mdl` 166.

**Confirmed in the viewer rather than only in the probe**, from its log during playback of the same
demo:

```
world pass: asked for 94, produced 14; skipped 12 not-studio,
  13 no-batches [mantreads, xms_allclass_giftbadge, soldier_viking, dex_glasses_soldier,
                 bdayhat_soldier, xms_beard_soldier, soldier_grenade_skulls,
                 witchhat_scout, ugc6participant, *72, *73, *77, *79]
packed models/weapons/c_models/c_rocketboots_soldier.mdl:
```

Thirteen a frame, packed with nothing behind them — and the `[props]` channel says **nothing at all**
about any of them, where a model that really loads emits five lines (`sequences`, `skins`, `pairing`,
`seam`, `baked`). They never reached the loader.

## 4. A clean bill of health that was true

```
ASKED FOR 166 entity models (1 of them the map's own detail models); HAVE 166; MISSING 0
```

Every one of the 166 resolved. The cosmetics are not among the 166.

`MapAssets.Geometry` is a **dictionary lookup, not a loader**:

```csharp
public PropModels.ModelFrames? Geometry(string path) =>
    EntityModels.TryGetValue(path, out PropModels.ModelFrames? frames) ? frames : null;
```

and its own remark says a miss is safe because *"the miss was already reported once, at load, where a
missing asset is worth reading."* That promise holds only for paths the load knew about. For a path
never in the list there is no miss to report: no key, null, remembered as empty, silent.

## 5. The diagnosis is one extra clause

Comparing the two sets directly:

```
the load list holds 165 paths; 34 of the no-geometry models were never in it
  NEVER ASKED FOR models/player/items/all_class/bdayhat_soldier.mdl   (no track names it)
  NEVER ASKED FOR models/player/items/all_class/all_penguin.mdl       (no track names it)
  …
```

**"No track names it" is the whole finding.** Absent-from-the-list plus named-by-a-track would mean the
walk that builds the list is wrong. Absent plus named-by-nothing means the path is **derived after the
load**, and the fix belongs where it is derived.

It is derived by `WeaponPropModels.Resolve`, which replaces a prop's model for every prop carrying an
item index, because the engine lets the item win — `CEconEntity::UpdateModelToClass`,
`econ_entity.cpp:411`:

```cpp
pszModel = pItem->GetPlayerDisplayModel( m_iOldOwnerClass, nTeam );
if ( pszModel && pszModel[0] )
    if ( V_stricmp( STRING( GetModelName() ), pszModel ) != 0 )
        SetModel( pszModel );
```

So the path that reaches the renderer is named by the item and appears on no track, while
`DemoModels.Needed` builds the load list by walking tracks. **The two can never meet.**

## 6. The walk that was missing

Three walks resolve item models for the load list and only two existed:

| walk | resolves |
|---|---|
| `AllIn` | what a player **holds**, from each frame's roster |
| `AllAttachmentsIn` | an item's `attached_models` |
| **`AllWornIn`** (new) | **an item's own `model_player`** |

The new one takes each distinct `ItemDefinitionIndex` on the timeline and asks `For` across every player
class, because `model_player_per_class` differs per class and the wearer's class is a per-tick fact —
the same superset argument `AllAttachmentsIn` already makes for teams, one axis wider. It goes into
**both** `Needed` and `ToPack`, since B195 is precisely that those two disagreeing is itself a defect: a
path packed and not loaded draws nothing, one loaded and not packed hitches on first sight, and the worn
models were in neither.

## 7. Measured at the output

255 frames of `20130518_0313_cp_granary_blu_blu`:

| | before | after |
|---|---|---|
| props drawn | 7,419 | **9,700** |
| models packing no geometry | 66 distinct | **32** |
| no-geometry models never in the load list | 34 | **0** |
| load list | 165 paths | 320 |

**+2,281 draws, about nine more props on every frame.** The 32 that remain are all `*NN` brush
submodels — `func_door` blockers and respawn-room visualizers whose faces are tool textures, which
`BrushModels` already documents as a real answer rather than a failure.

## 8. The first two tests could not fail

Written against the walk, they passed — and then three sabotages of `ResolveWorn` reddened **nothing**:
pinning the class loop to a single class, dropping `track.ClassName`, and removing the item guard
entirely. All twelve assertions stayed green through every one.

The cause was the fixture, not the assertions, which is the usual answer
(`docs/memory/most-of-a-decoder-is-untested.md`: *a sabotage that reddens nothing names the missing
input*). Both tests used the rocket launcher, an item the schema knows by index with a single
`model_player` — so `For` answered from the item route for every class, and a walk asking once passed
exactly as well as one asking nine times. Two inputs were missing:

- **an item whose model differs per class** (`model_player_per_class`), without which the class axis is
  not observable at all;
- **an item index the schema has never heard of**, without which the class-name fallback is never
  reached — the ordinary case for a demo recorded on a later build than the installed game, and 22 of
  56 held weapons on z1800.

Both are now in the schema fixture, with a test each.

**The third sabotage stays uncovered, deliberately.** Removing the `ItemDefinitionIndex` guard reddens
nothing because `For(null, …)` returns null and the walk yields nothing either way: the guard saves a
call and decides no behaviour. Writing a test for it would produce one that cannot fail, which is worse
than not having one, so the remark on the neighbouring test says this instead.

## 9. Still open

The load list doubled and nothing has measured what that costs at map load; D86 says up-front is the
right place, which is an argument about correctness rather than about the number. The 356 `CTFWearable`
props that still name no model are a different population and untouched — an item that names nothing
leaves the networked model in place, which is the other half of `UpdateModelToClass`'s condition, so
some of them may be correct. The 2,805 `light_glow03.vmt#Sprite` rejections are B378, unchanged.

## 10. An item that names "" has answered — and a weapon draws its item, not the wire (B105, 2026-09-30)

**What was believed:** B105 filed eighteen `"model_player" ""` declarations "over a prefab" — the engine's merge
lets an item's own "" win (`econ_item_schema.cpp:2909`, `:2962`, `:2967`), the port's search skipped empties, so the
port would draw a prefab's model where the engine draws none.

**What the file said:** none of the eighteen sits over a model. Fifteen are items' own — medals, the Upgradeable
Fists, gifts, the Duel MiniGame, the Pallet of Crates — over prefabs naming nothing; three are parentless prefabs,
`weapon_fists`, `halloween2013_spellbook` and `randomgift`, reached by nineteen more. Counting every scalar key an
item's "" hides from a prefab found no model key at all: `craft_class` 1,412, `craft_material_type` 279,
`armory_remap` 15, `xifier_class_remap` 1, `anim_slot` 1 (the Half-Zatoichi), `item_slot` 1 (a tool). The leak the
question predicted cannot happen on shipped data.

**What was wrong was one layer down, in what "no model" meant.** The engine answers "" (`BInitFromKV`, `:3158`;
`GetPlayerDisplayModel`, `econ_item_view.cpp:969`); the port answered null, and the two only look alike. For a TF
weapon the client rebuilds the world model from a valid item's answer and writes it over the networked index
(`tf_weaponbase.cpp:681-701`, `:3597-3607`), so "" draws nothing (`c_basecombatweapon.cpp:401`). The port read null as
"unknown", asked the class's stock item — itself "" — and kept the wire, which for an attach-to-hands weapon sending no
world index is `m_nModelIndex`: the carrier's first-person hands.

**And then the census said nothing on screen changes.** Over 59 demos the items sit on 248 prop tracks — the Duel
MiniGame on 80 worn props, the fists on 52, the spellbooks on 114 — and every one names no model on the wire or is never
a prop at all, but one: an engineer's Basic Spellbook in `20150119_2240_cp_process_final_(ovo)_blu`, whose wire names
`c_engineer_arms.mdl` for its 54 ticks. The census's first report called it drawn. It is holstered at every one of those
ticks, so `WeaponVisibility` had hidden it all along: **resolved is not shown**, and the probe asks that rule now. The
fix is parity for the model a held spellbook or fists would draw; the corpus holds no such moment.

**The instrument needed its controls, besides the visibility rule above.** The `item-props` census first sampled each track at its first tick and
found the stock sticky launcher absent — a holstered weapon is not a prop then — so it samples the whole life; and it
prints the tracks carrying ANY item index beside the named ones, which is zero before 2009, because an empty census
beside an empty control measures nothing.

*Evidence class: counted in shipped data (the declarations, the key census); read from published source (the merge,
the weapon's and the wearable's world model); measured on the corpus through the production path (the census).*

## 11. The wire was right, and the item overwrote it — `model_world` (B105, 2026-09-30)

**What was filed:** `GetWorldModel` asks a valid item's `model_world` before its `model_player` (`tf_weaponbase.cpp:686-687`),
four shipped weapons declare one, and the port read `model_player` for the world weapon. The expectation, going in, was
the same as §10's: parity for a case the corpus does not hold, since three of the four — the Ap-Sap, the Festive Sapper,
the Snack Attack — name their `model_player` again, and only the Hot Hand's `w_slapping_glove.mdl` differs from its
first-person `c_slapping_glove.mdl`.

**What the census said:** a pyro holds a Hot Hand in `tf2-2026-pub-pov-clean` for 148 ticks, seen by a soldier recording
in the first person, **and the wire already named `w_slapping_glove.mdl`**. The server precaches `GetWorldModel()` into
`m_iWorldModelIndex` when the weapon spawns with its item (`basecombatweapon_shared.cpp:291-300`), so the networked index
carried the right answer and `WeaponPropModels.Resolve`'s item-wins rule replaced it with the first-person glove. §5's rule
— the item wins over the wire — is the engine's for an econ entity's `SetModel`; a TF weapon's world model has its own
answer, and it was already on the wire. The sappers' draws were right throughout: their `model_world` repeats their
`model_player`, and every stock sapper (a `CTFWeaponBuilder`, whose owner-only object type would pick `objects.txt`'s
`Playermodel` — `c_sapper.mdl` again) draws its item.

**The instrument lied once, in the test harness rather than the probe.** The first output test ran its four z1800 moments
as parallel `[TestCase]`s on `TimelineCache`'s shared timeline, and three found no sapper at ticks where the census had
found one; each case alone passed. `PropsAt` keeps a sample between calls (B259's incremental rebuild), so the "finished,
only queried" timeline the cache hands every test is not read-only, and two tests sampling it at once read each other's
state. The corpus tests for this build their own timeline and walk it in tick order.

*Evidence class: read from published source (the rule, the builder classes, the server's precache); counted in shipped
data (four `model_world` keys); measured on the corpus through the production path (`item-props`, and `MomentScene.Build`
in `CorpusHeldWeaponWorldModelTests`).*
