using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Audio;

/// <summary>
/// Resolves this project's native libraries from the <c>libopus</c> NuGet package's RID-fanout
/// layout when no <c>RuntimeIdentifier</c> is pinned at build time.
/// </summary>
/// <remarks>
/// **The gap this closes.** The package's build targets copy every platform's binary to
/// <c>runtimes/&lt;rid&gt;/native/</c> — a flat, assembly-directory copy only happens when the
/// consuming build pins a <c>RuntimeIdentifier</c>. This project deliberately does not: it is a
/// library other projects (the CLI, the viewer) reference without necessarily publishing
/// RID-specific themselves, and forcing a RID onto every consumer to make one dependency's asset
/// layout work would be the tail deciding the shape of the dog. Measured directly: without this
/// resolver, <c>opus_decoder_create</c> throws <c>DllNotFoundException</c> under a plain
/// <c>dotnet test</c>, and the DLL is sitting one directory away the whole time.
///
/// This is also the more correct general answer for a package shaped like this one, and it keeps
/// <see cref="NativeOpus"/>'s <c>DefaultDllImportSearchPaths(AssemblyDirectory)</c> honest:
/// registering a resolver here means the runtime never falls through to the wider default search
/// order at all, because a successful resolution short-circuits it.
/// </remarks>
internal static class NativeLibraryResolver
{
    private const string OpusLibrary = "opus";

    /// <summary>Registers the resolver. Idempotent; safe to call more than once.</summary>
    internal static void EnsureRegistered()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, OpusLibrary, StringComparison.Ordinal))
        {
            // Not ours to resolve; returning zero tells the runtime to fall back to its own
            // search, which is correct for every other native import this assembly might ever
            // gain.
            return 0;
        }

        string? rid = CurrentWindowsRid();

        if (rid is null)
        {
            return 0;
        }

        string candidate = Path.Combine(
            AppContext.BaseDirectory, "runtimes", rid, "native", libraryName + ".dll");

        return File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out nint handle)
            ? handle
            : 0;
    }

    /// <summary>
    /// The RID folder name the <c>libopus</c> package ships for this process, or <c>null</c> off
    /// Windows or an unsupported architecture.
    /// </summary>
    /// <remarks>
    /// This project is Windows-only in practice (D20), so only the two Windows RIDs the package
    /// actually publishes are handled. Returning null for anything else means the fallback is
    /// "let the default resolver try and fail with its own clear error", not a wrong guess.
    /// </remarks>
    private static string? CurrentWindowsRid()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.Arm64 => "win-arm64",
            _ => null,
        };
    }
}
