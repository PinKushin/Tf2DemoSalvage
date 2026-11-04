# 2026-11-04 — what holds the window thread through a load and its opening seek (B707, B708)

**Written after the runs:** no pre-run record was committed and no prediction was registered, so read this as a report.

**Its "after" columns measure a fix since removed on review (`ServeWindow`).** The "before" column and the slow-machine run stand; the current figures are in `2026-11-04-skip-across-frames.md`.

## Observation

`LoadingOverlay_OnceTheWorldIsBuilt_IsGone` failed with a UI Automation timeout when run first on the shared viewer. RISKS first read the time between `building the world` and `memory after load`, 6.5 s, as the hold. On run 41669140049 a client also arrived late (B707).

## Question

Which steps stop the window thread answering a sent message, for how long, and does the same hold on a slow two-core machine?

## Method

`WM_NULL` by `SendMessageTimeout` from another process, every 20 ms, on `z1800.dem --tick 20000 +developer 1` with the game installed, under the desktop lock, one run per binary, Release. Each round trip over 100 ms is printed against the viewer log's clock. Stacks: one `dotnet-trace` sampled run, exploratory, on the Debug build. Second instrument, new on this branch: `WindowThreadWatch` in the viewer, on the window thread.

Slow machine: `dotnet test` of the session's tests and everything under it on two cores, four spinning processes on those two cores, `TF2VIEW_WARP=1`, `TF2_FOLDER` at an empty folder. The test host is a real UI Automation client. **Cold archives could not be done:** this account is not an administrator, so the file cache cannot be flushed.

## Control

`Sampler_OverAWindowItsThreadHolds_ReportsTheHold` holds a window's thread for 1500 ms with a spin. Its first version held with a blocking wait and read 0.5 ms, because a wait on an STA thread pumps sent messages. The two instruments agree on the same run: the watch said 3122 ms where `WM_NULL` said 3093 ms.

## Environment

Windows 11 Pro 10.0.26300, 6 physical cores, GPU, another agent sharing the machine (decode 33.09 s before, 34.17 s after, 52.10 s in the middle run), 2026-11-04. "Before" is the binary from the first B706 fix, `ac838c42` plus `PinnedComCalls`, with no pump. "Middle" serves between skip frames and decal ticks. "After" also serves inside the corpse catch-up.

## Result

Round trips over 200 ms, Release, game installed, one run each. Placed against the log's lines, so a row's name is a reading of what lies between two lines unless it says otherwise:

| step | before | middle | after |
|---|---|---|---|
| map read, models, sounds, on the worker, no round trip over 200 ms | none | none | none |
| `Apply`, start: the demo's systems and the timeline's sounds | 753 | 361 | |
| `Apply`, to `memory after load`: second precache, then the post-load collection (723, 812 and 691 ms by its own line) | 1206 | 1253 | 1251 |
| first frame's start: light styles, soundscape, scenes and sentences first read, model set upload (`STALL` 333, 336 ms) | 228 | 243, 1036 | 1084, 698 |
| the walk, `ReplaySkip` then `ReplayModelDecals` (1860 and 2465 ms before; each over 200 skip frames and 800 decal ticks, by their own lines) | **all in the last row's 6491** | 2375 | 1058 |
| after the walk: `SLOW CAMERA` particles 1508 ms, device 249 ms, and the HUD 439 ms, in the after run's `SLOW FRAME` line | **6491** | 3093 | 2021 |

**The total does not shrink**, and the after run's `SLOW FRAME` is still 6308 ms: the thread answers sent messages sooner, and the walk costs what it cost.

Stacks through a sampled run before the corpse step was served: the walk's samples are mostly `CorpsePhysics.Advance` under `EntityModelSet.AdvanceCorpses`, under `MomentPresenter.PoseNow` (834 of 975 in the window read). The middle column's 2375 and 3093 fell after serving inside that loop, which supports it.

**The correction:** the 6.5 s between `building the world` and `memory after load` is mostly the worker and the window thread answered through it. A different 6.5 s came after it. RISKS named the wrong stretch.

UI suite, sampled from launch to the first frame, Debug: pump removed **10,060 ms**, and the middle version under the 5000 ms line.

Engine, read from `bin/x64/engine.dll` with `llvm-objdump` on the installed build: `PeekMessageA` (import slot `0x18035b560`) is called at four addresses, `0x18020d4e1`, `0x18020d515`, `0x18020d602` and `0x18020d673`, all in the function at `0x18020d460`. The first two are a drain, `PeekMessageA`, `TranslateMessage`, `DispatchMessageA`, repeated until empty. `inputsystem.dll` imports them too, and was not read.

Slow machine, the watch's lines, one run:

```
11:11:38.323 [viewer] startup: window shown 2262 ms after the process started
11:11:38.331 [viewer] first WM_GETOBJECT served, 535 ms after the session opened
11:11:38.353 [demo] opening z1800.dem
11:11:38.662 [viewer] UI Automation client attached: the traffic wait is on
11:14:23.315 [demo] building the position timeline took 164.96s
11:14:44.102 [viewer] window thread to the first frame on screen: largest gap between messages 3708 ms, between idle passes 7319 ms; first WM_GETOBJECT at 535 ms
```

The decode took about three times CI's and no gap of a second was said during it.

CI, read from the job logs of the two runs: `Tf2DemoSalvage.Viewer3D.UiTests` was built in 47 s on the slow run (16:08:24 to 16:09:11) and 8 s on the fast one (09:21:12 to 09:21:20). The viewer's log opened 14 s after the test run began on the slow run and 2 s on the fast one.

## Verdict

**The hold was the opening seek's walk in one call, and serving sent messages through it takes the longest answer from 6491 ms to 2021 ms, while the frame itself still takes about 6.3 s with no paint and no input.** CPU starvation did not reproduce CI's late client. The slow run's own build took six times as long as the fast run's, so that machine was slow, but about 12 s and then 29 s of its gap are inside the viewer and were never placed, so nothing here shows the viewer blameless; the new `startup:` and watch lines place them on the next CI run that hits it.
