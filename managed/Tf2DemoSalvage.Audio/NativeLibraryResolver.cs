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

    /// <summary>
    /// Native libraries built by <c>tools/native-audio/build.ps1</c> and copied flat into the
    /// output directory by this project's own <c>.csproj</c> — no NuGet RID-fanout involved, so
    /// resolution here is simpler than the Opus case: the file just needs to be next to the
    /// assembly.
    /// </summary>
    private static readonly string[] BuiltLibraries = ["speex", "celt"];

    /// <summary>
    /// Registers the resolver. Every decoder class calls this from its own static constructor.
    /// </summary>
    /// <remarks>
    /// **The registration itself lives in this type's own static constructor, not here, and that
    /// is load-bearing.** <c>NativeLibrary.SetDllImportResolver</c> *throws*
    /// <see cref="InvalidOperationException"/> on a second call for the same assembly rather than
    /// replacing the resolver or no-op'ing, and xUnit v3 parallelises test classes across threads
    /// by default. A `bool _registered` guard checked-then-set in an ordinary method is a race
    /// under that concurrency: two decoder classes' static constructors can both observe
    /// "not yet registered" before either sets the flag, and the second call throws. A static
    /// constructor is different — the CLR runs a type's own type initializer exactly once and
    /// blocks any other thread that touches the type until it completes, so merely referencing
    /// this type from <see cref="EnsureRegistered"/> is what actually deduplicates the call,
    /// not a flag. Found by the race firing in practice: three decoder classes' tests in one
    /// process, only whichever class's static constructor happened to run first passing.
    /// </remarks>
    internal static void EnsureRegistered()
    {
        // The call itself does nothing; touching the type is what matters; see remarks.
    }

    static NativeLibraryResolver() =>
        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (string.Equals(libraryName, OpusLibrary, StringComparison.Ordinal))
        {
            return ResolveOpus(libraryName);
        }

        if (Array.Exists(BuiltLibraries, name => string.Equals(name, libraryName, StringComparison.Ordinal)))
        {
            return ResolveBuilt(libraryName);
        }

        // Not ours to resolve; returning zero tells the runtime to fall back to its own search,
        // which is correct for every other native import this assembly might ever gain.
        return 0;
    }

    private static nint ResolveOpus(string libraryName)
    {
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

    private static nint ResolveBuilt(string libraryName)
    {
        string candidate = Path.Combine(AppContext.BaseDirectory, libraryName + ".dll");

        if (!File.Exists(candidate))
        {
            // Deliberately not thrown here: returning zero lets the runtime raise its own
            // DllNotFoundException, and the caller-facing wrapper (SpeexVoiceDecoder,
            // CeltVoiceDecoder) is where a message pointing at build.ps1 belongs, not this
            // low-level resolver which every native import in this assembly shares.
            return 0;
        }

        return NativeLibrary.TryLoad(candidate, out nint handle) ? handle : 0;
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
