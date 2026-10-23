# 100 — The viewer's helper threads were the opening, not the frame

**The question.** `measurements/2026-10-22-threads-f12.md` found two unnamed threads beside the viewer's render thread on
f12 first person: a managed one at 30-36% of a core (4.9-5.5 ms a frame) and a runtime one at 15-16% (2.1-2.9 ms). If either
were steady background work, removing it would be cheaper than any render-thread change. Measured in
`measurements/2026-10-22-viewer-helper-threads.md`.

## The wrong turn

The first reading attributed those costs to the steady frame. Two things were wrong with it, both instrument faults:

- **The names were never read.** The sampler opened each thread with access `0x1000` (`THREAD_RESUME`) to call
  `GetThreadDescription`, which needs `0x0800` (`THREAD_QUERY_LIMITED_INFORMATION`); every name came back `?`. .NET names its
  own threads, so the identity was one access mask away all along. A control with a named spinner caught it.
- **The window held the opening.** The 20 s `--measure` window starts the moment playback starts, and two pieces of start-up
  work run into it. Measured, not deduced: split at 10 s, both threads are busy before and idle after.

## What they are (measured on the corpus demo, names from the runtime, work from a sample-profiler trace)

| thread | identity, evidence | first ~10 s of playback | steady (10-30 s), ms a frame | does |
|---|---|---:|---:|---|
| main | `tf2demoview.exe` entry | 92-97% | **12.85-13.74** (95.8-96.9%) | everything per frame |
| `.NET TP Worker` (`coreclr+0xd22f0`) | trace: `StartCorpseRecord` -> `CorpseRecord.Run` -> `CorpsePhysics.Advance` -> `IvpRagdollWorld.Simulate` | 57-68% | **under 1%** | the corpse record pass, once; then parked |
| `.NET Tiered Compilation Worker` (`coreclr+0x17abf0`) | runtime's own name | 30-34% | **0.17-0.20** (1.2-1.5%) | tier-1 rejit of methods hot in the first seconds |
| NVIDIA D3D11 driver worker | `nvwgf2umx.dll` | 11-15% | 1.75-2.20 | driver |
| DirectSound mixer | `dsound.dll` | 2% | 0.25 or less | sound |
| `.NET BGC`, EventPipe dispatch, `measure heartbeat` | names and trace | small | under 1% each | gen2 GC (none ran), `--measure` instruments |

Process in steady state: **114-116% of a core**, of which 96-97 is the render thread and 13-16 the driver.

**GC** (40 s from playback start, runtime counters; measurements/2026-10-22-viewer-helper-threads.md): workstation, concurrent GC; 538-599 gen0 and 111-125 gen1 collections,
**0 gen2**; paused 0.28-0.35 ms a frame (about 2% of the frame); 1.47-1.55 MB allocated a frame, 88% of it on the render
thread; no heartbeat gap over 50 ms in any run, so no collection stopped the process long enough to count.

## What it implies

- **There is no background work to remove from the steady frame** (measurements/2026-10-22-viewer-helper-threads.md). The viewer's steady cost is the render thread (12.9-13.7 ms)
  plus the driver; the 4.9-5.5 ms and 2-2.9 ms per frame of the earlier record were the corpse pass and JIT warm-up landing
  inside a window that began at playback start. Leave both.
- **The corpse pass is start-up latency, not frame cost.** If the opening seconds matter (they show as the slow first samples
  of every `--measure`), it could start earlier (during asset loading) or be cut by finding 89/91's allocation work; it does not
  touch the steady frame.
- **Concurrent GC's background thread is idle here** (no gen2). The GC cost that remains is the gen0/gen1 pauses on the render
  thread, about 0.3 ms a frame, which only allocation reduction removes (findings 88-91).
- **A `--measure` window that starts at playback start includes the opening.** A steady reading needs the first ~10 s
  excluded; this record did it by splitting the sampler series, not by changing `--measure`.
- Compared with TF2's main 4.9 + driver 3.7 + one `CThread` 3.6 ms: the viewer's gap is on its one thread, as finding 92 said.

**Unverified:** the `0x17abf0` thread was named only by the runtime's description, not traced at work (the sample profiler
does not show native JIT frames); the corpse pass's exact end time was not logged (the `corpse record:` log line goes to the
render log, not the `--measure` report).
