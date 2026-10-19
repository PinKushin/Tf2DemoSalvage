using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Tests.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>A map a test can write: the smallest file <see cref="LoadedMap.Read"/> accepts, read with no install (D38, D214).</summary>
/// <remarks>
/// **A `LoadedMap` has no public constructor, and that is deliberate** — it is what a read produced, so a test gets one
/// the way production does: from bytes. The bytes are <see cref="SyntheticBsp"/>'s, a version 21 header and the lumps the
/// test names, and the install is the one a machine without TF2 has: no archives, so no texture, model or particle file
/// is found and every loader takes its absent path.
///
/// **What such a map HAS is the point of it**: a level with no faces, lighting that answers unlit, and the content record
/// with an empty lightmap atlas — so the parts that only ask "is a map loaded" run, and the ones that need a particle
/// file or a decal material do not. A test that needs more names the lumps.
/// </remarks>
internal static class SyntheticMap
{
    /// <summary>The install of a machine with no game on it.</summary>
    public static GameContent NoInstall() => GameContent.Open(folder: null, NullLoggerFactory.Instance);

    /// <summary>Reads a map of these lumps, for this demo, with no install.</summary>
    /// <param name="timeline">The demo opened with it, or null.</param>
    /// <param name="lumps">Lump payloads by index; none is a header alone.</param>
    public static LoadedMap Read(DemoTimeline? timeline = null, IReadOnlyDictionary<int, byte[]>? lumps = null) =>
        LoadedMap.Read(SyntheticBsp.Build(lumps ?? new Dictionary<int, byte[]>()), NoInstall(), timeline, textureQuality: 0, NullLoggerFactory.Instance, "synthetic");
}
