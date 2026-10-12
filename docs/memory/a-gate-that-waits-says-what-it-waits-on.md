---
name: a-gate-that-waits-says-what-it-waits-on
description: "A --shot held by a settle gate logs the gate and its values once a second; an async ring never re-queues a slot its reader still waits on (B520, CI hang 2026-10-11)."
metadata:
  type: project
---

**CI's capture test hung 180 s with an empty stderr and a log of nothing but frames.** Suspect was the newest merge
(occlusion queries). It was the exposure gate from two days earlier.

- Cause: `ExposureHistogram` overwrote the oldest of three staging copies every frame, read or not; `Map( DO_NOT_WAIT )`
  on it answered "still drawing" almost always. Ten readings: 16 s on a GPU, 77 s on WARP here, over 180 s on the runner.
- Engine rule (`viewpostprocess.cpp:551-613`): an entry in flight issues no new query; unfinished = ask again next
  frame; read at `cur_query_frame > m_frame_queued + 2`. Code: `HistogramRing`.
- The UI step had not RUN for a day: Rendering failed first and the later steps were skipped. A red step hides every
  step after it — the newest merge is not the suspect, the whole range since the step last ran is.

**How to apply:**
- A wait with no log line is undebuggable from CI. `capture waiting on <gate>: <values>` is in the log now; a new
  gate in `TakeAutomaticShot` calls `ReportCaptureWaiting`.
- Reproduce a runner locally: `TF2_FOLDER=<empty dir>`, `+cl_game_folder_ask 0`, and `TF2VIEW_WARP=1` for ONE
  diagnostic run (the owner: no test runs on WARP locally).
- `gh run view <id> --json jobs` and read which steps were `skipped` before blaming a commit.
