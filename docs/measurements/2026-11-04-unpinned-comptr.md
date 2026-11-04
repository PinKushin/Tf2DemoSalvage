# 2026-11-04 — does an unpinned `ref ComPtr` field crash the process when the collector moves it? (B706)

## Observation

CI, run 41805476486 attempt 1, job "Corpus + Audio (Windows)", commit `ac838c42`: `Capture_AtATickOnACorpusDemo_ExitsCleanlyAndWritesThePng`
exited -1073741819 (`0xC0000005`) with this stack, on the render thread:

```
ID3D11DeviceContext.PSSetConstantBuffers(UInt32, UInt32, ID3D11Buffer**)
D3D11DeviceContextVtblExtensions.PSSetConstantBuffers[[ID3D11Buffer]](ComPtr<ID3D11DeviceContext>, UInt32, UInt32, ComPtr<ID3D11Buffer> ByRef)
WorldRenderer.BindPipeline < DrawSky < Device3D.DrawFrame < SubmitScene job < QueuedRenderThread.Run
```

`VSSetConstantBuffers(0, 1, ref _camera)` on the line before had just succeeded with the same field. Attempt 2 of the same commit passed, and
both local gates passed on it. Reading Silk.NET 2.23.0: `ComPtr<T>.GetAddressOf()` is `(T**)Unsafe.AsPointer(ref Unsafe.AsRef(in this))`, and
the `ByRef` overload in the stack passes that pointer on. Nothing pins `WorldRenderer`, and `ViewerPresenter` runs an Aggressive compacting
collection after a load while a frame can be in flight on the render thread.

## Question

Does passing `ref` to a `ComPtr` field of a heap object, through Silk.NET's overload, crash a process when a collection moves that object, with
nothing else from this project present? And does the same call stop crashing when only the address changes?

## Method

A console program with no project code (source below). One thread loops `VSSetConstantBuffers` then `PSSetConstantBuffers`, 64 pairs at a time.
The main thread allocates 16 MB around a new holder object, publishes it to that thread, and runs
`GC.Collect(MaxGeneration, Aggressive, blocking: true, compacting: true)`. Each run is capped at 60 s. Three forms of the call:

- `field`: `ref holder.Camera`, as `BindPipeline` does.
- `stack`: the same overload, `ref` a local copy.
- `pinned`: `fixed (ComPtr<ID3D11Buffer>* p = &holder.Camera)` and the `ID3D11Buffer**` method, which is the form `PinnedComCalls` ships.

Settings: `DOTNET_TC_CallCounting=0`, `DOTNET_TieredPGO=0` (keeps tier 0, as the first frames after a load are), affinity two cores (the runner
logs "2 physical cores"), .NET 10, Silk.NET 2.23.0, `D3DDriverType.Warp` or `Hardware`, a device with no window.

Second instrument, the real path: `tf2demoview +volume 0 tf2-2008-build3420-spec-stv-cp_granary.dem --tick 1000 --shot x.png +mat_queue_mode 2`, the
capture CI crashed in, Release, alternating two binaries, with `TF2VIEW_WARP=1`, the same tier 0 settings, affinity two cores, `TF2_FOLDER` at an
empty folder (the runner has no game), under the desktop lock. "before" is the fix with the `PinnedComCalls` overloads compiled out, "after" is
the fix. The 19 `GetAddressOf` sites changed to `OMSetRenderTarget` are in both, so "before" is not quite `ac838c42`.

## Control

- `stack` is the control for `field`: one difference, where the address lives.
- The fatal stack is read from each crashing run, so a crash elsewhere would not count.
- Viewer loops: all 60 runs wrote a PNG and all 60 standard error files are empty. The 40 logs still present (the viewer prunes older ones) all say
  `mat_queue_mode 2: QueuedThreaded` and name the empty game folder.
- Stress loop: `DOTNET_GCgen0size=40000`, `DOTNET_GCConserveMemory=9`, `DOTNET_gcConcurrent=0`. One run's timeline reported 197 ms of collection pause against 60 ms
  unstressed, so the settings took effect; both reported 3 gen2 during that build, so **more compaction is not shown**.

## Environment

Windows 11 Pro 10.0.26300, 6 physical cores restricted to 2 by affinity, 2026-11-04, branch `fix/ci-render-crash` from `ac838c42`. Harness on
other agents' normal load; the viewer loops under `run-exclusive.ps1`.

## Result

Harness, tier 0, two cores, first sitting. Exit 139 is the runtime's `0xC0000005`:

| form | driver | crashed | seconds to crash, or survived 60 s |
|---|---|---|---|
| `field` | WARP | 4 of 5 | 4, 17, 5, survived, 57 |
| `stack` | WARP | 0 of 5 | all survived |
| `field` | hardware | 2 of 3 | 8, 9, survived |
| `stack` | hardware | 0 of 3 | all survived |

Second sitting, alternating, WARP:

| form | crashed | seconds to crash, or survived 60 s |
|---|---|---|
| `pinned` | 0 of 5 | all survived, 2437 to 3219 collections each |
| `field` | 5 of 5 | 33, 45, 24, 11, 33 |

Every crash's first frame was `ID3D11DeviceContext.PSSetConstantBuffers` or `VSSetConstantBuffers` with `ID3D11Buffer**`, the CI stack's top two.
One `field` run with default tiering (no `DOTNET_TC_CallCounting`) survived 60 s, 3163 collections; that is one run and rules nothing out.

Real viewer, `before` then `after` alternating:

| loop | `before` crashed | `after` crashed |
|---|---|---|
| plain | 0 of 20 | 0 of 20 |
| stress settings | 0 of 10 | 0 of 10 |

All 60 exited 0 with a PNG.

The harness:

```csharp
internal sealed class Holder { public long Pad1; public ComPtr<ID3D11Buffer> Camera; public long Pad2; }

// render thread, 64 at a time, holder = the current one:
context.VSSetConstantBuffers(0, 1, ref holder.Camera);   // field; stack copies to a local first
context.PSSetConstantBuffers(0, 1, ref holder.Camera);

// main thread, until the time is up:
for (int i = 0; i < keep.Length; i++) keep[i] = new byte[64 * 1024];   // keep has 256 slots
s_current = new Holder { Camera = buffer };
for (int i = 0; i < keep.Length; i++) keep[i] = new byte[64 * 1024];
Array.Clear(keep);
GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
```

## Verdict

**Yes to both, in the harness: the unpinned field overload crashes with CI's top frames, on WARP and on hardware, and neither a local copy nor `fixed` does (`field` 11 of 13, others 0 of 13).** The real viewer did not reproduce it in 30 `before` runs, so this establishes the defect and its mechanism but does not show that CI's crash was this one; `PinnedComCalls` removes the mechanism, and only further CI runs can speak to the crash.
