---
name: logging-goes-on-a-background-thread
description: The file log is written by a background thread and the caller only enqueues (D230, 2026-10-24); never put a disk write or flush back on the caller to make a log "safer".
type: feedback
---

**Logging is done on a background thread; the thread that logs only enqueues** (D230). The owner, 2026-10-24: *"async
the f\*\*\*ing out of it, background thread should be where basically all logging is done nowadays"*, and it runs on
when the main thread or the program locks up.

**Why:** a per-line flush on the main thread was 6 of 7 frames over 100 ms on f12 (B595 part 3). The writer thread also
keeps the log current through a freeze, which the synchronous log could not.

**How to apply:**
- Durability comes from the writer flushing every batch and from `Flush(timeout)` in the crash/exit handlers, never from
  a flush on the caller. Do not "fix" a lost tail by flushing in `Write`.
- A test or tool that reads the log of a writer in the same process must `Flush` first; a live tail sees lines one batch
  late (the UI suite polls, so it is unaffected).
- The trade: a hard kill loses only the lines queued at that instant (measured in
  `docs/measurements/2026-10-24-d230-async-file-log.md`). The owner judged it an edge case.
- This supersedes B191's "keep `AutoFlush`, demote the line instead". Demoting per-frame lines to Debug is still right
  for volume, but no longer needed to protect frame time.
