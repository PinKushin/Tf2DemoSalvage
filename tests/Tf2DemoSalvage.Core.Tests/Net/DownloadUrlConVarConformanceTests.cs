using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// <c>sv_downloadurl</c>'s declared default, against the engine's own registration.
/// </summary>
/// <remarks>
/// **Found while planning D162's map-version check.** `EngineConVars` declared the default as
/// `"0"` — copied from `sv_cheats` and `host_timescale`, the two entries above it, rather than read.
/// A URL ConVar defaulting to the string `"0"` never made sense on its own.
///
/// **The engine's registration**, decompiled: `engine.dll`'s x64 build has exactly one function
/// referencing the string `"sv_downloadurl"` (found by <c>ListStrings.java</c>). Decompiled, it is
/// <c>ConVar</c>'s own constructor call:
///
/// <code>
/// FUN_180283e80(&amp;DAT_180730090, "sv_downloadurl", &amp;DAT_18035d128, 0x2000,
///     "Location from which clients can download missing files");
/// </code>
///
/// `DAT_18035d128`, the default argument, is a single zero byte — the empty string. (Project
/// <c>tf2engine</c>, <c>D:\ghidra-proj</c>; the address is that build's.)
/// </remarks>
public sealed class DownloadUrlConVarConformanceTests
{
    [Test]
    public void Default_SvDownloadUrl_IsEmpty()
    {
        EngineConVars.ByName("sv_downloadurl").Default.ShouldBe(string.Empty);
    }
}
