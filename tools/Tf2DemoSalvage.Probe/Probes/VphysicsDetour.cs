using System;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// A routine of the loaded <c>vphysics.dll</c> sent to a managed callback instead, for as long as the detour lives — so a probe can
/// call one routine of the binary with the routines it calls replaced by recorders.
/// </summary>
/// <remarks>
/// **Twelve bytes at the routine's first instruction** become <c>MOV RAX, imm64; JMP RAX</c>, and are put back on disposal. Every
/// routine a probe detours starts with at least twelve bytes of whole instructions that nothing jumps into, which the probe that
/// uses it checks against the disassembly; the callback owns the routine's calling convention and must leave callee-saved state
/// as the routine would. Only the probe's own process is patched, and only the copy of the library it loaded.
/// </remarks>
internal sealed partial class VphysicsDetour : IDisposable
{
    private const int PatchLength = 12;
    private const uint ExecuteReadWrite = 0x40;

    private readonly nint _target;
    private readonly byte[] _original = new byte[PatchLength];
    private readonly Delegate _callback;

    /// <summary>Detours a routine.</summary>
    /// <param name="module">The loaded library's base.</param>
    /// <param name="address">The routine's address as read.</param>
    /// <param name="callback">The replacement, a delegate of the routine's signature marked with its calling convention.</param>
    /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The page could not be made writable.</exception>
    public VphysicsDetour(nint module, long address, Delegate callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        _target = VphysicsLibrary.Address(module, address);
        _callback = callback;
        Marshal.Copy(_target, _original, 0, PatchLength);

        byte[] patch = new byte[PatchLength];

        patch[0] = 0x48;
        patch[1] = 0xB8;
        BitConverter.TryWriteBytes(patch.AsSpan(2), (long)Marshal.GetFunctionPointerForDelegate(_callback));
        patch[10] = 0xFF;
        patch[11] = 0xE0;
        Write(patch);
    }

    /// <summary>Puts the routine's own bytes back.</summary>
    public void Dispose() => Write(_original);

    private void Write(byte[] bytes)
    {
        nint previous = Marshal.AllocHGlobal(sizeof(uint));

        try
        {
            if (VirtualProtect(_target, PatchLength, ExecuteReadWrite, previous) == 0)
            {
                throw new InvalidOperationException($"VirtualProtect refused the page at 0x{_target:x} (error {Marshal.GetLastPInvokeError()}).");
            }

            Marshal.Copy(bytes, 0, _target, PatchLength);

            if (VirtualProtect(_target, PatchLength, (uint)Marshal.ReadInt32(previous), previous) == 0)
            {
                throw new InvalidOperationException($"VirtualProtect could not restore the page at 0x{_target:x} (error {Marshal.GetLastPInvokeError()}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(previous);
        }
    }

    // Pinned to System32 so the loader cannot be pointed at a kernel32.dll dropped beside the probe (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int VirtualProtect(nint address, nuint size, uint protection, nint previous);
}
