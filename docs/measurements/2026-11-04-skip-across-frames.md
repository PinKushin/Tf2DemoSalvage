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

**The walk now spreads over 38 frames with the HUD moving and the longest answer down from 6491 ms to 1258 ms, at the price of a walk about twice as long**, and its landing picture differs from old by slightly more than old differs from itself, which is not shown to be a real difference and not ruled out as one.
