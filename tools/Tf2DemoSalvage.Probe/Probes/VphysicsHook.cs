using System;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>A detour a callback can call through: the routine's bytes put back, the routine called, the detour laid again.</summary>
/// <typeparam name="T">The routine's delegate type.</typeparam>
/// <remarks>Only sound single-threaded, which the probes' simulations are.</remarks>
internal sealed class VphysicsHook<T> : IDisposable
    where T : Delegate
{
    private readonly nint _module;
    private readonly long _address;
    private readonly T _callback;
    private VphysicsDetour? _detour;

    /// <summary>Lays the detour.</summary>
    /// <param name="module">The loaded <c>vphysics.dll</c>.</param>
    /// <param name="address">The routine's image address.</param>
    /// <param name="callback">What runs in its place.</param>
    public VphysicsHook(nint module, long address, T callback)
    {
        _module = module;
        _address = address;
        _callback = callback;
        _detour = new VphysicsDetour(module, address, callback);
    }

    /// <summary>Calls the routine itself, the detour lifted around the call.</summary>
    /// <param name="call">What to do with the routine.</param>
    public void CallThrough(Action<T> call)
    {
        _detour!.Dispose();
        _detour = null;

        try
        {
            call(Marshal.GetDelegateForFunctionPointer<T>(VphysicsLibrary.Address(_module, _address)));
        }
        finally
        {
            _detour = new VphysicsDetour(_module, _address, _callback);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _detour?.Dispose();
        _detour = null;
    }
}
