---
name: a-worktree-lcor-is-a-junction
description: "A worktree's tools/corpus/local may be a junction to the main checkout's lcor; deleting \"my copy\" there deletes the real demo."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-01T03:47:25.217Z
---

2026-09-30: `rm -f tools/corpus/local/demostf-cp_process_f12-2026-08-07.dem` in a worktree whose
`tools/corpus/local` was a junction ([[lcor-is-not-in-a-worktree]]) deleted the only copy of the f12 parity
reference. `cp -n` into it had been a no-op, so the "cleanup" removed the original. Unrecoverable (source
never recorded; `rm` bypasses the Recycle Bin). Owner: no recovery hunt — *"just find another fucking demo"*.
Reference is now `demostf-cp_process_f12-2026-08-08-2207.dem` ([[the-f12-demo-is-the-parity-reference]]).

**How to apply:**
- Never `rm` under `tools/corpus/local` (or any path) without `ls -la` on its parent first; a junction makes it the original.
- Clean up only what the copy actually created: check `cp -n` output/exit, delete nothing it skipped.
- Every lcor demo gets its SHA-256 and source in `tools/corpus/manifest.json` when it arrives.
