using System;
using System.IO;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>The game's own <c>vphysics.dll</c>, loaded into the probe's process so its routines can be called at their addresses.</summary>
/// <remarks>
/// **Shared by every probe that uses the binary as its oracle**, so "which library" and "which address" are answered once. The
/// addresses are the ones `docs/findings/51` reads, against the image base Ghidra loads the library at.
/// </remarks>
internal static class VphysicsLibrary
{
    /// <summary>The image base the addresses in the findings were read at.</summary>
    public const long ImageBase = 0x180000000;

    /// <summary>Loads the installed game's x64 <c>vphysics.dll</c>.</summary>
    /// <param name="output">Where to say what was loaded, or why nothing was.</param>
    /// <param name="module">The loaded module's handle, which is its base address; zero when nothing was loaded.</param>
    /// <returns>Whether the library was loaded.</returns>
    /// <remarks>Never freed: the process ends with the probe, and unloading runs the library's own teardown for nothing.</remarks>
    public static bool TryLoad(TextWriter output, out nint module)
    {
        module = 0;

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } folder ||
            Path.GetDirectoryName(folder) is not { } root)
        {
            output.WriteLine("The game folder could not be found.");
            return false;
        }

        string library = Path.Combine(root, "bin", "x64", "vphysics.dll");

        if (!File.Exists(library))
        {
            output.WriteLine($"No library at {library}.");
            return false;
        }

        module = NativeLibrary.Load(library);
        output.WriteLine($"{library} loaded at 0x{module:x}");

        return true;
    }

    /// <summary>Where an address read at <see cref="ImageBase"/> is in the loaded module.</summary>
    /// <param name="module">The module's base.</param>
    /// <param name="address">The address as read.</param>
    /// <returns>The address in this process.</returns>
    public static nint Address(nint module, long address) => module + (nint)(address - ImageBase);

    /// <summary>A routine in the loaded module, as a delegate.</summary>
    /// <typeparam name="T">The delegate type, matching the routine's arguments.</typeparam>
    /// <param name="module">The module's base.</param>
    /// <param name="address">The routine's address as read.</param>
    /// <returns>The delegate.</returns>
    public static T Function<T>(nint module, long address)
        where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Address(module, address));

    /// <summary>SplitMix64: a fixed, reproducible stream of bits, so a sweep can be run again on the same arguments.</summary>
    /// <param name="state">The stream's state, advanced.</param>
    /// <returns>The next 64 bits.</returns>
    public static ulong SplitMix(ref ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong mixed = state;
            mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
            mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
            return mixed ^ (mixed >> 31);
        }
    }

    /// <summary>The top 53 bits of a draw as a double in [0, 1).</summary>
    /// <param name="bits">The draw.</param>
    /// <returns>The fraction.</returns>
    public static double Unit(ulong bits) => (bits >> 11) * (1d / (1UL << 53));
}
