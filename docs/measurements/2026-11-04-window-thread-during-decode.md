# 2026-11-04 — does the window thread stop answering during a decode? (B707)

**Written after the run:** no pre-run record was committed and no prediction was registered, so read this as a report.

## Observation

CI, run 41669140049 (`f7ec0bb6`), viewer log for `z1800.dem`:

| time | line |
|---|---|
| 16:09:41.273 | `startup: window shown 15843 ms after the process started` |
| 16:09:41.277 | the demo's header line |
| 16:10:10.302 | `no UI Automation client: the traffic wait is off` |
| 16:10:34.158 | `building the position timeline took 52.88s` |
| 16:10:35.474 | `UI Automation client attached: the traffic wait is on` |

That line is written from the idle path, so for 29 s after the window was shown the window thread either was not pumping or never went idle. The same demo on run 41805476486 attempt 1: window shown after 609 ms, header at 09:21:23.248, `UI Automation client attached` at 09:21:23.381, during a 28.40 s decode, with the render lines at about 18 frames a second from there on.

## Question

Does the window thread stop answering messages during a decode, as the 29 s gap could mean?

## Method

Launch the viewer on `z1800.dem` and, from another process that is not a UI Automation client, send `WM_NULL` to its main window with `SendMessageTimeout` (60 s limit) every 20 ms for 100 s, timing each round trip. A stall of the window thread shows as a round trip as long as the stall. The script is not committed; this paragraph is all of what it did.

Settings as near CI as this machine gets: `TF2VIEW_WARP=1`, affinity two cores, `TF2_FOLDER` at an empty folder, `TF2VIEW_MAP_CACHE` at an empty folder, `TF2VIEW_MAP_MIRROR` at a closed port, `+developer 1`, the Release binary, under the desktop lock. One run.

## Control

The same series covers the map load after the decode, which does run on or stop the window thread (`ClearWorld`, the upload), so the instrument should and did see long round trips there. It was not given a stall of known length.

## Environment

Windows 11 Pro 10.0.26300, 6 physical cores restricted to 2, a warm disk, 2026-11-04, `fix/ci-render-crash`. CI's runner has a cold disk and no GPU, and this machine has neither condition exactly: `TF2VIEW_WARP` forces the rasteriser but not the rest.

## Result

```
label=run1 samples=1912 first_at_ms=1097 last_at_ms=99994
latency_ms median=0.2 p99=105.6 max=1269.3 timeouts=0
worst five (at ms, latency ms): 40344:1269 39705:390 38537:371 38940:279 57003:152
09:03:01.843      [viewer] startup: window shown 1955 ms after the process started
09:03:01.882      [demo] opening z1800.dem
09:03:01.905      [viewer] no UI Automation client: the traffic wait is off
09:03:35.456      [demo] building the position timeline took 33.57s
```

The decode ended about 35.5 s into the series, by the log's times against the window at 1.955 s. All of the worst five came after that, so no round trip during this decode exceeded 152 ms. The first idle came 23 ms after the demo was opened.

## Verdict

**On this machine, in one run, the window thread answered throughout a 33.57 s decode, so nothing here shows the decode blocking it.** That does not explain CI's 29 s, which was not reproduced: whether the window thread stopped pumping on that run, or pumped without going idle, cannot be determined from its log, and a stall on a cold runner stays possible.
