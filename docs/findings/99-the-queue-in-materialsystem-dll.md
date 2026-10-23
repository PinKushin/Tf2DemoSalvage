# 99 — The queue in `materialsystem.dll`: two contexts, one frame in flight, no held frame

**The question.** Finding 98 left three things interpolated because the material system's implementation is not in the
SDK: what `mat_queue_mode -1` resolves to, when the game thread waits on the render thread, and how many frames can be
in flight. This is the Ghidra read of TF2's own x64 `bin/x64/materialsystem.dll` (image base `0x180000000`, analysed
project on `D:`), done 2026-10-22. **Docs only.** Addresses are Ghidra's for that build; the logic below is written by
hand, nothing pasted. Field names in parentheses are this document's labels for unnamed offsets in `CMaterialSystem`.

Evidence classes: **binary** = read from the disassembly/decompilation of the shipped DLL; **SDK** = published source
(`F:/src/source-sdk-2013`); **inferred** = reasoned from the two, not observed.

## (1) What `-1` resolves to

- **The cvar has no change callback.** Its registration (`0x180001c70`) passes name, default string `"-1"`
  (`0x1800b69b4`), flags and help text only (**binary**). The mode is therefore not applied when the cvar changes; it is
  re-read every frame in `EndFrame`.
- **`CMaterialSystem::EndFrame` is `0x180014bc0`** (identified by its own profiler label `"CMaterialSystem::EndFrame"`
  and source-line telemetry pointing at `cmaterialsystem.cpp`). It maps the cvar to a requested mode (**binary**):
  `-2` → 2; `1` → 0 (queued-single is never used on PC); any other negative value, i.e. `-1`, → the stored default
  (*default mode*, offset `0x2f08`); 0 and 2 pass through.
- **The default mode is set by `AllowThreading`** (`0x180010f90`, matching `AllowThreading( bool bAllow, int
  nServiceThread )`, `imaterialsystem.h:1118`) (**binary**, struct layout **SDK**):
  - reads the `-threads` command-line parameter with a default of 2; a value of 1 or less forces `bAllow` false;
  - reads `GetCPUInformation()` byte 6, which is `m_nPhysicalProcessors` (`public/tier0/platform.h:1282-1325`:
    `int m_Size` at 0, the feature bitfield byte at 4, `m_nLogicalProcessors` at 5, `m_nPhysicalProcessors` at 6);
  - **fewer than 2 physical processors: threading off**, no mode change. Otherwise it stores `bAllow` (*threading
    allowed*, `0x306d`) and, on a change, calls `SetThreadMode(2, nServiceThread)` ("Queued Material System:
    ENABLED!") or forces single-threaded and `SetThreadMode(0)` ("DISABLED!"). The constructor (`0x18000e130`) starts
    the default at 0.
- **`EndFrame` then vetoes 2 back to 0** when the shader API's capability query (vtable `+0x580`, unnamed) says no,
  when *threading allowed* is false, when a pending "force off" flag (`0x306c`) or `0x2f0c` is set, or while a
  deferred-call list (`0x3098` count) is non-empty. Alt-tab (shader API not active) drops to 0 with "Handling alt-tab in
  queued mode!".

**Answer:** `-1` means **2 (queued multithreaded) on any PC with 2 or more physical cores**, provided the engine called
`AllowThreading(true)` and `-threads` is not 1; otherwise 0. The threshold is physical cores, not logical. Who calls
`AllowThreading` and with what is in `engine.dll` (**not read**).

## (2) When the game thread waits

Inside `EndFrame`, mode 2 path (**binary**, `0x180014bc0`, telemetry zone `"Mat_ThreadedEndframe"`):

1. The current context's own `EndFrame` (vtable `+0x6f8`) is called first, so in queued mode the swap (profiler label
   `"Swap_Buffers"`) is **recorded into the queue**, not executed on the game thread (**inferred** from where the call
   lands: the thread-local current context is the queued one in mode 2).
2. **If the previous frame's job (`0x3080`) is still pending or running, the game thread blocks**:
   `g_pThreadPool->YieldWait( &job, 1, bWaitAll=true, TT_INFINITE )` (vtable `+0x58`; the status test accepts the
   pending, in-progress and unserviced states). This is the one per-frame wait.
3. The two queued contexts swap: an index (`0x2f00`) flips 0↔1, the new current context is primed from the old one
   (`0x18002a8a0`), and the thread-local "current render context" is pointed at it.
4. A job named `"ThreadExecuteQueuedContext"` wrapping `0x18001e6d0` is created for the context **just recorded** and
   added to a dedicated pool created on first use by `0x1800130f0`: `CreateThreadPool()` started with the name
   `"MatQueue"`, one thread, a 0x40000-byte stack (**binary**; the start-params field mapping is **inferred** from
   `ThreadPoolStartParams_t` order).
5. `EndFrame` returns. The game thread starts the next frame immediately.

`0x18001e6d0` (the render thread's job) marks the render thread id (`0x3060`), sets the thread-local context to the
hardware context, plays the queue (`0x18002b9c0` with "execute"), restores, and reports a spike over 1 ms to
telemetry. **There is no wait at `BeginFrame`.**

## (3) How many frames in flight

**Two queued contexts, so exactly one frame playing while the next is recorded.** The constructor builds two queued
contexts (stride `0x3b0`), each with its own `CMemoryStack` (`0x1800773a0` with 8 MB maximum, 64 KB commit step, 256 KB
initial commit) (**binary**). Because step 2 waits for frame N-1's job before handing off frame N, the game thread can
never be more than one frame ahead of the render thread. Latency is bounded by that wait, not by a queue capacity:
there is no frame count to tune. What a queue overflow does mid-frame (the memory stack's full case) was **not read**.

## (4) What it means for input latency

**There is no held frame** (**inferred** from (2) and (3), not measured). Frame N's view, camera and input are sampled
on the game thread while recording N; at `EndFrame` N it is handed to the render thread, which starts playing it
**at once** (or as soon as N-1 finishes) and presents at the end of the play. Nothing waits for frame N+1 before
showing N. Input-to-present is therefore game(N) + render(N), the same work as serial mode, plus at most the time N
spends waiting for N-1 to finish when the render side is the slower of the two. What the queue buys is throughput
(game N+1 overlaps render N), not a deeper pipeline. **The owner's expectation holds: TF2 does not add a held frame.**
Driver-side frame queueing behind `Present` (D3D9's own render-ahead) is outside this DLL and not covered.

## (5) What forces a sync

- **`CMaterialSystem::Lock`** (`0x180018000`, its timing message `"*CMaterialSystem::Lock: %0.2fms"`) (**binary**):
  waits on the in-flight job (same `0x3080`, same `YieldWait`), takes the `"MatSysMutex"` fast mutex, makes the hardware
  context current on the calling thread, and **plays whatever the queued context has recorded so far** (`0x18001e910`)
  when its pending-call query (vtable `+0x718`) is non-zero. Lock is a full drain.
- **Forcing single-threaded** (`0x180016050`, `"Forcing queued mode off!"`): main thread only (`Error` otherwise),
  waits on the in-flight job, releases it, drains, plays both queued contexts, sets mode 0 and the "force off" flag
  that keeps `EndFrame` from going straight back to 2 on the next frame.
- **Leaving mode 2 in `EndFrame`** (any veto above) does the same wait and drain before switching.
- **`mat_queue_report`** reports stalls at those points; **`mat_report_queue_status`** prints per-queue call and byte
  counts. Read-backs (`ReadPixels`) inside the queued context were **not traced**; the SDK's callers already avoid
  them by requiring mode 0 (finding 98 section 1).

## (6) The boot-time worker pool

**Not read here.** `g_pThreadPool` is imported (it lives in `vstdlib`/`tier0`, not this DLL), so its thread count for
N cores and its spin-then-sleep policy need a read of that binary. The only pool this DLL creates is `MatQueue`, one
thread. Follow-up.

## (7) What it implies for D228

- **Depth: one.** Two snapshots (one being built, one being drawn), double-buffered exactly as the two queued
  contexts are; no ring, no tunable depth.
- **The hand-off blocks at the end of the game frame, on the previous frame's draw.** Our equivalent of `EndFrame`
  waits for draw N-1, swaps snapshots, and starts draw N; the game thread never waits at the start of a frame.
- **No latency is added and none should be visible**: frame N is presented as soon as its draw finishes. A design that
  delays showing N until N+1 is built would be a divergence.
- **Mode switch**: `-1` = 2 when physical cores ≥ 2, else 0; `1` = 0; `-2` = 2; re-read per frame, no callback.
- **Every sync point is a full drain**: a screenshot, seek, map load or teardown waits for the in-flight draw and runs
  serially, as `Lock` and the force-off path do.
- Finding 98 section 7's blocker stands: the saving above ~0.5 ms needs the snapshot, whatever it costs.
