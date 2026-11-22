# B779 review 20: the HUD panels' preload, its controls, and the carried-item walk's cost (2026-11-22)

## Observation

Review 19 (B812) put every model a HUD panel can ask for on the level's preload. Review 20 asked whether the probe
that measured it could fail, whether the list of layouts is complete, and what the carried-item walk costs at load.

## Question

1. Does every model the production HUD asks for appear on `DemoModels.Needed`, and on `ToPack`?
2. Does any `.res` file the HUD reads name a model outside `HudPanelModels.Layouts`?
3. How long do `Needed`, `ToPack` and the carried-item walk take on a long demo?

## Method

Probe `hud-model-loads` (`tools/Tf2DemoSalvage.Probe/Probes/HudModelLoadsProbe.cs`), Release, under
`run-exclusive.ps1`, on the base of `dccd734ca` plus the review-20 changes, against the install's stock files
(`GameContent.HudArchives` with no HUD chosen):

```
dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- hud-model-loads "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf/<demo>"
```

The production `VguiHud` runs every 33 ticks with a loader that answers only the `Needed` paths. Each `.res` read is
recorded and walked with `HudPanelModels.Named(read, [file])`. The timings come from one `Stopwatch` per call, run in
order: `Needed`, then `ToPack`, then `AddCarriedItemModels` alone. By then the first two have run the walk, so the last
figure is warm.

## Control

- The class panel's model is asked and held when a frame's local player has a class. The probe reports NOT APPLICABLE
  when no frame's local player has one.
- A path that no list names is answered missing.
- `ToPack` holds every path that `Needed` holds and the HUD asked for.
- Sensitivity: when `HudMatchStatus.res` was removed from `Layouts`, the pub-demo run failed with "layouts with model
  blocks outside HudPanelModels.Layouts: resource/UI/HudMatchStatus.res". It also reported 1 path not listed.

## Result

| demo | frames | `Needed` | `ToPack` | carried walk | preload | asked | not listed | `.res` read | outside `Layouts` | class control |
|---|---|---|---|---|---|---|---|---|---|---|
| `tf2-2026-pub-pov-clean.dem` | 21,220 | 437.4 ms | 310.1 ms | 121.1 ms (342 paths) | 476 | 13 | 0 | 35 | 0 | held |
| `20150119_2240_cp_process_final_(ovo)_blu.dem` | 111,632 | 524.7 ms | 292.4 ms | 179.4 ms (243 paths) | 389 | 21 | 0 | 36 | 0 | held |
| `f12.dem` | 106,219 | 662.2 ms | 389.8 ms | 262.4 ms (303 paths) | 450 | 9 | 0 | 34 | 0 | not applicable |
| `auto-20260817-1720-cp_process_f12.dem` | 2,400 | 71.8 ms | 19.9 ms | 3.4 ms (18 paths) | 136 | 0 | 0 | 34 | 0 | not applicable |

On every demo, the only `.res` file with a model block is `resource/UI/HudMatchStatus.res`.

## Verdict

No prediction was written before the run.

1. Yes, on all four demos. The HUD asked for 0 paths that `Needed` does not list, and `ToPack` holds every listed path
   the HUD asked for. The class-model control applies only to the two POV demos, because no local player on the two
   f12 demos has a class.
2. No. Of the three `Layouts` files, only `HudMatchStatus.res` names a model path on the stock install. The census
   was not run against a custom HUD.
3. On `f12.dem`, `Needed` takes 662 ms and `ToPack` takes 390 ms. The carried-item walk takes 262 ms each time, and
   each list runs it once. Sharing one walk between the two lists could save that time, but this was not measured.

## Identity

- A "held" path is listed in the preload. The probe does not load it: the loader reads no file.
- The timings come from one run each, not a mean. Use them to judge size, not to compare builds.
- The walk runs once in `Needed` and once in `ToPack`, so a long demo's level load spends about twice the
  last column on it.

# Review 21: one walk per load, and a cheaper walk (2026-11-22)

## Question

Review 20 added a carried-item walk to both lists. On f12 that is two walks of 262 ms each, about 0.5 s of level load.
After two changes, what does the load spend on it?

- **Change 1:** `DemoModels.CarriedPanelModels` walks once per timeline and schema, under a lock, and both lists take
  that result.
- **Change 2:** the walk skips an item list it has already seen, compared by reference. B433 hands most frames the
  previous frame's list.

## Prediction

The second list no longer pays for a walk. The walk alone drops by the share it spent checking each item of a repeated
list. That share was not estimated in advance.

## Method

The same probe and demos as above, on the review-21 build. The probe runs the lists in production order:

1. The first `Needed` walks and holds the result.
2. `ToPack` reuses the held result.

Then each of `Needed` (walk held), `ToPack` (walk held) and the walk alone is run once as a discarded warm-up, then 3
more times. The median of those 3 is reported.

## Control

The walk's path counts must equal review 20's: 303, 342, 243 and 18. A cheaper walk that lists fewer models would be a
defect, not a saving. The probe's four controls still pass on every demo.

## Result

Each row is one probe run. The first two time columns are single calls in production order; the last three are warm
medians of 3.

| demo | first `Needed` (walks) | then `ToPack` (reuses) | `Needed` | `ToPack` | walk alone | walk paths |
|---|---|---|---|---|---|---|
| `f12.dem` | 705.6 ms | 148.7 ms | 310.4 ms | 139.2 ms | 156.8 ms | 303 |
| `tf2-2026-pub-pov-clean.dem` | 410.7 ms | 223.1 ms | 221.6 ms | 121.8 ms | 73.4 ms | 342 |
| `20150119_2240_cp_process_final_(ovo)_blu.dem` | 566.3 ms | 141.9 ms | 243.0 ms | 70.3 ms | 130.6 ms | 243 |
| `auto-20260817-1720-cp_process_f12.dem` | 62.6 ms | 14.5 ms | 14.4 ms | 12.8 ms | 2.6 ms | 18 |

The path counts match review 20 on all four demos. On all four, 0 paths asked were not listed, 0 layouts were outside
`Layouts`, and every control passed.

## Verdict

The prediction is confirmed.

- **On f12:** `ToPack` at load fell from 389.8 ms to 148.7 ms. The walk alone fell from 262.4 ms (a single warm run)
  to 156.8 ms (a median of 3). The load now walks once, so its carried-item cost is about 157 ms, down from about
  525 ms.
- **Not cut further:** what remains is reading every frame's players. A frame shares each player's detail with the
  frame before (B433), but `ScenePlayer.Detail` is internal to Core and the timeline exposes nothing equivalent to
  Scene. Skipping by detail needs a Core accessor, which is a separate change.
- **The first `Needed` is cold and not comparable:** it includes JIT, which is why it is 705.6 ms against the warm
  310.4 ms. The review-20 figures were also first calls, so they compare with the first two columns, not with the
  medians.
