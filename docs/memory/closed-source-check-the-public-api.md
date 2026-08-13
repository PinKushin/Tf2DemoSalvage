---
name: closed-source-check-the-public-api
description: When SDK code is closed, read the public API it exposes rather than treating it as unknowable
metadata:
  type: feedback
---

**Hitting a closed-source component in `source-sdk-2013` is not the end of the search.** The
engine, `materialsystem` and the client are not published, but every one of them is *used* by code
that is — through headers in `src/public/`, interface declarations, and the call sites in `vbsp`,
`vrad`, `stdshaders` and the game DLLs.

**Go to the public API first.** What a black box exposes, and what its callers do with it, is
usually enough to reverse what is needed — because a demo or a BSP only ever exercises that public
surface anyway. If the private implementation mattered, the file format would not be readable by
anything but the engine.

**Why:** stated 2026-08-13. The alternative failure is treating "closed" as "unknowable" and
falling back to guessing or to copying another implementation's workaround, which imports that
implementation's bugs along with its behaviour.

**How to apply:** when a grep lands in a closed component, immediately search `src/public/` for the
interface, the constants, and the callers. Constants especially: `NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS`,
`m_DepthBias_Decal` and the bump basis were all public even though the code consuming them is not.
Related: [[read-the-encoder-not-the-decoder]], [[valve-publishes-bitbuf]],
[[differential-beats-fixtures]].
