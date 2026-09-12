---
name: a-native-api-wrapper-owns-a-library-handle
description: Disposing a Silk.NET API object calls FreeLibrary; GetApi loads a fresh copy every call, so per-instance ownership is either a crash or a leak — load once per process.
metadata:
  type: project
---

**`D3D11.Dispose()` unloads `d3d11.dll`.** Read from Silk.NET v2.23.0:
`NativeApiContainer.Dispose` → `_ctx.Dispose()` → `DefaultNativeContext.Dispose` →
`Library?.Dispose()` → `UnmanagedLibrary.Dispose` → `_loader.FreeNativeLibrary(Handle)`, whose own
doc comment reads *"Frees the native library. Function pointers retrieved from this library will be
void."* It looks like releasing a managed wrapper and it is a `FreeLibrary`.

**And `GetApi` is not a cache**: `D3D11.GetApi` runs `new D3D11(CreateDefaultContext(names))` on
every call, loading the library again. Those two facts together mean per-instance ownership has no
correct form — dispose it and you unload a library other code may still be executing; don't and you
leak a handle per instance.

**B402 was both, in order.** The shipped code disposed it, and every CI capture died with
`0xC000041D` — an exception inside a native callback, no managed stack, after the last line the
process wrote. Removing the dispose fixed the crash and the owner caught the other half
immediately: *"did you actually fix it? we need to call _d3d.Dispose dont we?"* `Rendering.Tests`
builds 34 offscreen targets in one process, so that would have been 34 leaked handles.

**The answer is one instance per process** (`Direct3DApi.Api`), disposed by nobody, freed by the
loader at exit — the only moment no driver thread can still be inside it.

**Why it was invisible here for six CI runs: the crash needs a GPU-less machine.** With a display
adapter you get a hardware driver; without one you get WARP, the software rasteriser, which is a
thread pool, and DXGI keeps a window hook that lives in the DLL. The CI log's last line was
`releasing the base, which destroys every child window` — destroying the swap chain's HWND called a
window procedure inside a library that had just been unloaded. `TF2VIEW_WARP=1` reproduces it here
in one command; D167 keeps it opt-in because WARP is far too slow for the suite.

**How to apply:** any `X.GetApi()` wrapper around a native library — D3D11, D3DCompiler, OpenAL —
is a library HANDLE, not just a managed object. Ask who owns it and for how long before writing
`Dispose`, and prefer one per process over one per user. A `using` on one of these is the shape to
distrust.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[ci-is-the-machine-without-tf2]],
[[the-first-step-of-a-sequence-is-not-the-culprit]], [[an-exception-type-can-be-load-bearing]].
