---
name: time-the-window-thread-with-a-sent-message
description: Time the window thread with sent WM_NULL round trips, never by reading log gaps. A log gap named the wrong 6.5 s (B708). A blocking wait on an STA thread still answers sent messages.
metadata:
  type: feedback
---

**Log gaps do not show what the window thread is doing.** B708 blamed the 6.5 s between `building the world` and `memory after load`; that stretch is the load worker, and the window answered through it. `WM_NULL` round trips found the real hold: a different 6.5 s after it, in the opening seek.

**How to apply:** `WindowThreadSampler` in the UI tests, or the viewer's own `WindowThreadWatch` lines (`window thread dispatched nothing`, `window thread to the first frame on screen`). The two agreed within 29 ms.

**To hold a thread on purpose, spin.** `ManualResetEventSlim.Wait` on an STA thread pumps sent messages: a 1500 ms hold read 0.5 ms.

**A sampler's `WM_NULL` reaches the watch's `WndProc`**: agreement with one shows arithmetic only. Control with no sender: the frame ledger.

**Never serve messages inside a walk** (rejected on review, B708): a sent `WM_SIZE` or `WM_CLOSE` re-enters it. A skip spreads over frames (`SkipWalk`), the engine's shape.
