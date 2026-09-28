---
name: a-native-api-wrapper-owns-a-library-handle
description: Disposing a Silk.NET API object calls FreeLibrary; GetApi loads a fresh copy every call, so per-instance ownership is either a crash or a leak — load once per process.
metadata:
  type: project
---

**`D3D11.Dispose()` unloads `d3d11.dll`.** Silk.NET v2.23.0: `NativeApiContainer.Dispose` →
`_ctx.Dispose()` → `DefaultNativeContext.Dispose` → `Library?.Dispose()` →
`UnmanagedLibrary.Dispose` → `_loader.FreeNativeLibrary(Handle)` — *"Frees the native library.
Function pointers retrieved from this library will be void."* Looks like releasing a wrapper; it's a
`FreeLibrary`.

**`GetApi` is not a cache**: `D3D11.GetApi` runs `new D3D11(CreateDefaultContext(names))` every call,
reloading the library. Together: per-instance ownership has no correct form — dispose it and you
unload a library other code may still execute in; don't and you leak a handle per instance.

**B402 was both, in order.** Shipped code disposed it; every CI capture died with `0xC000041D` (native
callback exception, no managed stack). Removing the dispose fixed the crash; owner caught the other
half: *"did you actually fix it? we need to call _d3d.Dispose dont we?"* — `Rendering.Tests` builds 34
offscreen targets in one process, so that's 34 leaked handles without it.

**Answer: one instance per process** (`Direct3DApi.Api`), disposed by nobody, freed by the loader at
exit — the only moment no driver thread can be inside it.

**Why invisible for six CI runs:** the crash needs a GPU-less machine — WARP (software rasteriser) is
a thread pool and DXGI keeps a window hook living in the DLL; destroying the swap chain's HWND called
a window procedure inside a just-unloaded library. `TF2VIEW_WARP=1` reproduces it (D167 keeps it
opt-in — WARP is too slow for the suite).

**How to apply:** any `X.GetApi()` native wrapper (D3D11, D3DCompiler, OpenAL) is a library HANDLE —
ask who owns it and for how long before `Dispose`; prefer one per process. A `using` on one of these
is the shape to distrust.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[ci-is-the-machine-without-tf2]],
[[the-first-step-of-a-sequence-is-not-the-culprit]], [[an-exception-type-can-be-load-bearing]].
