# 2026-11-04 — a skip across frames against a skip in one call (B708)

**Written after the runs:** no pre-run record was committed and no prediction was registered, so read this as a report. It supersedes the "after" column of `2026-11-04-window-thread-through-an-opening.md`, whose fix was removed on review.

## Observation

The opening seek on z1800 ran 201 skip frames and a decal replay in one frame of about 6.5 s, with the window answering nothing. Serving sent messages inside it was rejected on review: a sent `WM_SIZE` or `WM_CLOSE` re-enters a walk, and paint and input still wait.

## Question

With the walk split as the engine splits it, what does a person see, how long does the window go unanswered, and does it land on the same picture?

## Method

`z1800.dem --tick 20000`, game installed, Release, under the desktop lock. "Old" is `ac838c42` plus `PinnedComCalls`, one call. "New" is this branch.

- Walk counts: the viewer's own `skip landed` line.
- Longest answer: `WM_NULL` round trips from another process, every 20 ms, one run.
- Picture on landing: `--shot` from each binary, sampled every second pixel on each axis.
- What is seen: the window copied from the screen every 500 ms from `memory after load`, 24 copies each, not committed because the desktop around it is in them.

## Control

- Picture: old against old again, since a capture is not perfectly repeatable.
- The watch with no sender: the run copying the screen sends nothing, so its gap can be read against the frame ledger's own clock. A `WM_NULL` sender's messages reach the same `WndProc`, so agreement with the sampler shows only that the arithmetic is right.
- Counting distinct screen copies was tried and **tells nothing**: old also gave 24 of 24, because other windows on the desktop changed.

## Environment

Windows 11 Pro 10.0.26300, 6 physical cores, GPU, another agent sharing the machine, 2026-11-04.

## Result

| | old | new |
|---|---|---|
| batches and frames drawn | 201 skip frames in 1 drawn frame | 202 batches over 38 drawn frames |
| `SLOW FRAME` of the walk | 6547 ms (camera 1882, capture 4563) | largest 1155 ms |
| `skip:` line's time | 1860 to 2322 ms | 10,682 ms |
| decal replay | 2465 to 3687 ms | 1897 ms |
| longest `WM_NULL` round trip | 6491 ms | 1258 ms |
| `SLOW FRAME` of the landing | inside the 6547 | 952 ms (camera 907) |

**The walk takes about twice as long in all**: roughly 6.5 s became roughly 12.7 s, because 38 frames are drawn and posed in full.

With no sender, the watch said `dispatched nothing for 1323 ms` where the ledger said `SLOW FRAME 1272 ms`, by two clocks on one stall.

The rest of the longest answers: 1292 ms ending at `memory after load`, which holds the second precache (227 ms) and the post-load collection (763 ms), and the landing's particle rebuild.

Landing picture, 135,792 samples:

| pair | differing | over 30 |
|---|---|---|
| old, old again (control) | 5617 | 2253 |
| old, new | 6827 | 2797 |

Screen copies, read by eye: in old the viewport holds one picture with an empty HUD strip until the walk ends. In new, the kill feed, chat and player strip change through the walk. The free camera's view of the map does not change, since nothing moved it.

Engine, read: the main loop at `0x18020d460` drains with `PeekMessageA`, `TranslateMessage`, `DispatchMessageA` and then calls vtable +0x28, which is `CEngine::Frame` by its own profiler strings (`0x18020e510`). `CEngine::Frame` calls the host state's frame (`0x1801b5640`, not followed further in this build). In the older import, `_Host_RunFrame` runs `_Host_RunFrame_Client` once per accumulated tick and once with none when skipping, then `_Host_RunFrame_Render`, then `_Host_RunFrame_Sound` with no skip test; the mixer asks whether a demo is playing and its timescale, not whether it skips.

## Verdict

**The walk now spreads over 38 frames with the longest answer down from 6491 ms to 1258 ms, at the price of a walk about twice as long.** The landing picture comparison and the "read by eye" line above are withdrawn: see the corrected run below, whose control differs by nothing.

## Corrected run: what the doubling was, and the back buffer in place of the screen

**The screen copies above cannot support a claim about the picture**: the owner looked, and the viewer can barely be seen in them. These frames are the viewer's own, by `TF2VIEW_PLAY_SHOTS` (extended to reach a skip's frames) and `--shot`; every PNG was opened.

**The doubling was corpse physics, per step.** `z1800.dem --tick 20000 --autoplay --measure 3`, Release, no captures, viewport 1280x720, builds alternated under the lock. "Before" is the walk across frames as first committed; "after" holds corpses for the whole walk and folds the decal replay into its batches. Sums over `SLOW FRAME` and `SLOW CAMERA`, two runs each. They include the landing frame.

| ms | before | after |
|---|---|---|
| `skip landed at 6` to `skip landed at 20000` | 11,755; 10,754 | 6,926; 7,117 |
| that, plus the landing frame | 13,389; 11,761 | 8,717; 8,972 |
| camera | 8,991; 7,766 | 2,593; 2,677 |
| of which device, mostly the pose | 7,373; 6,702 | 475; 467 |
| of which particles | 1,552; 1,006 | 1,679; 1,759 |
| advance, the batches and the drawn moment | 5,200; 4,699 | 5,562; 5,716 |
| capture, where the decal replay begins | 3; 3 | 1,139; 1,104 |
| hud | 980; 476 | 521; 536 |
| sound | 134; 88 | 84; 86 |
| drawn frames of the walk | 39; 40 | 57; 54 |

The log's own part names it: a moment before reads `advance 378.1 (corpses 373…)`, and after reads `advance 0 (corpses 0)`. The walk's line says `corpses stepped 0 ms on the way`. One call was about 6.5 s, so about 2 s remains: mostly 38 to 57 drawn frames, which the engine also draws. The first `capture` row moved rather than appeared: `BeginReplay`'s run of the props' decals up to its window used to fall at the end of the frames.

**The longest gap did not improve:** the landing frame was 1,085 to 1,754 ms and is now 2,367 to 2,401 ms: particles about 1.7 s (the jump's rebuild), and the corpses, which now catch up there and only there, 360 ms. The last batches of the walk share that frame.

**What it deals is unchanged.** `--shot` on z1800 at 20000, one-call build against this one: `wave deck` 347 script sounds, sequence `4d744172eacbd6fc`, both; 332 animation script sounds both; 75 model decals held both. Footsteps, which only read, are 1252 and 1246; the six are not explained.

**The picture, sampled every second pixel of 1280x720 (230,400):**

| pair | differing | over 30 |
|---|---|---|
| one-call, one-call again (control) | 0 | 0 |
| one-call, this, z1800 at 20000 | 11,905 | 5,048 |
| one-call, this, f12 at 90000 | 12,188 | 7,596 |

Opened, the world is the same. The difference is the HUD: the chat is on screen after a walk and not after one call, as TF2's is, and in the f12 pair the spectator top bar (below).

**Is the view drawn during a walk, and do the kill feed, chat and strip advance?** Yes, from the frames at 22500, 45000, 67500 and the landing at 90000 of `f12.dem` (clock 3:40, 3:09, 2:03, 2:49; chat, kill feed, health and death timers each different; players in the world move). The free camera's view does not change. Against TF2 itself on the same demo, `demo_gototick 90000` at clock 3:42 (its own screenshot): chat, the kill feed `Zolak + koel` and the strip agree with ours at 3:40, line for line but for one older chat line. TF2 draws the same. The one-call build has no frames between: its PNGs are landings at those ticks, shown only as what each tick should look like. There, scorch marks appear that the walk's frames lack, since the replay reaches only the decals still alive at the target.

**`--then-seek` on z1800, `--measure 3`:** the paused half printed `0 samples` over 835 frames (before) and 701 frames (after), so the sample count says nothing about a walk either way.

**The spectator top bar is missing from every `--autoplay --measure` frame, in the one-call build too, so it is not this walk's.** TF2 draws it, and so do `--shot` runs. Pixel (100, 20) of f12, where the bar is 49,44,41: one-call `--shot` at each of four ticks, the bar; one-call `--autoplay --measure` at 90100, 125,122,51, no bar; this build's `--autoplay --measure`, two runs of four frames, no bar; this build's `--shot`, five runs of six, the bar. A trace of `--shot` runs read the panel visible, 1280x49, opaque; none of a missing run was taken, so where it goes is unknown. **The sixth `--shot`, at 16:39, had no bar and is not explained.** Filed as open in B708.
