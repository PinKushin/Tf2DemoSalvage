using System;
using System.Threading;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary>The process's one <c>d3d11.dll</c>, loaded once and never unloaded (B402).</summary>
/// <remarks>
/// **Two facts force this shape, and both are read from Silk.NET's own source.**
///
/// `D3D11.GetApi` is not a cache: every call runs `new D3D11(CreateDefaultContext(names))`, which
/// loads the library again — so a program that calls it per device holds one handle per device.
/// And `D3D11.Dispose` reaches `UnmanagedLibrary.Dispose`, which is `FreeNativeLibrary(Handle)`,
/// documented as *"Frees the native library. Function pointers retrieved from this library will be
/// void."*
///
/// **Disposing it is what crashed every CI capture for six runs** (B402). A driver does not
/// necessarily have every thread stopped by the time the device's last reference goes, and
/// unloading the code those threads are running is an access violation on a thread with no managed
/// frame — `0xC000041D`, no stack, after the last line the process wrote. It never happens on a
/// machine with a display adapter, because that gets a hardware driver; a machine without one gets
/// WARP, which is a thread pool.
///
/// **Not disposing it at all was the first fix and it was wrong**, and the owner caught it: *"did
/// you actually fix it? we need to call _d3d.Dispose dont we?"* — with `GetApi` loading a fresh
/// library each time, skipping the call leaks one handle per creation, and `Rendering.Tests` builds
/// 34 offscreen targets in one process.
///
/// **One instance for the process answers both.** The library is loaded once however many devices
/// are built, and the loader frees it at process exit, which is the only moment no driver thread
/// can still be inside it. `Lazy` because a machine with no Direct3D at all should fail when a
/// device is asked for, not when this assembly is touched.
/// </remarks>
internal static class Direct3DApi
{
    private static readonly Lazy<D3D11> Loaded =
        new(() => D3D11.GetApi(null), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The shared API object. Never dispose it; see the type's own remarks.</summary>
    internal static D3D11 Api => Loaded.Value;
}
