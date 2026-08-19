using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Loads a real map once and hands the same result to every test that asks for it.
/// </summary>
/// <remarks>
/// **Not an NUnit fixture, deliberately, and the distinction is the whole design.** A shared
/// fixture serialises the tests inside it, and this assembly declares
/// <c>[assembly: Parallelizable(ParallelScope.All)]</c> and
/// <c>[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]</c> for reasons its policy file
/// argues at length. Giving that up to save a map load would trade one cost for another.
///
/// A static memoised cache gives both. Tests stay in their own classes, get their own fixture
/// instance, and run in parallel exactly as before; they simply receive the same already-loaded
/// <see cref="MapAssets"/> instead of building it again.
///
/// **The cost being removed, measured 2026-08-19.** The viewer suite took 1m49s of a 3m41s gate,
/// and its slowest tests were all one thing:
///
/// <code>
/// OneFileOnTheCommandLineIsOpenedNotJustListed   51 s
/// AnEntityModelsMaterialsExtendTheTable          50 s
/// ReportTheDarkestMaterialsByArea                43 s
/// TheCensusExaminesPropsAndNotOnlyBrushwork      30 s
/// </code>
///
/// Every one decodes the BSP, resolves 422 materials, decodes their textures and loads the props
/// from scratch. Around a dozen tests each paid that toll independently, and one of them paid it
/// twice to get its own control.
///
/// **Sharing is safe because <see cref="MapAssets"/> is read-only once built** — every collection
/// on it is exposed as <c>IReadOnlyList</c>, its properties are <c>init</c>-only, and the pixel
/// data is <c>ReadOnlyMemory</c>. This is shared IMMUTABLE state, which is what the parallelism
/// policy objects to sharing only when it is mutable.
///
/// **A test that needs a map nothing else uses still gets one**, at the cost of its own load. The
/// cache is keyed on everything that changes the result, so asking for a different texture size or
/// a different model list is a different entry rather than a wrong answer.
/// </remarks>
internal static class MapCache
{
    /// <summary>The map every test uses unless it says otherwise.</summary>
    public const string DefaultMap = "cp_process_final";

    /// <summary>
    /// Texture size for tests that do not care about texture size, which is most of them.
    /// </summary>
    /// <remarks>
    /// **One size, so they share one entry.** Tests were asking for 4, 16, 32, 64, 256 and 512 —
    /// six loads where one would do, because the number was chosen per test to be "small enough"
    /// rather than to match anything. A test asserting on pixels or on decode still names its own.
    /// </remarks>
    public const int DefaultTextureSize = 64;

    /// <summary>
    /// One <see cref="Lazy{T}"/> per distinct request, so two tests asking at once load once.
    /// </summary>
    /// <remarks>
    /// <c>Lazy</c> rather than <c>GetOrAdd</c> with a factory: <c>ConcurrentDictionary</c> does not
    /// promise the factory runs only once, and a map load is expensive enough that running it twice
    /// under contention is exactly what this exists to prevent. The lazy is per key, so a test
    /// wanting a different map is not blocked behind one wanting this map.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, Lazy<MapAssets>> Loaded = new();

    /// <summary>The reference map, loaded once per distinct request.</summary>
    /// <param name="maximumTextureSize">Largest texture edge to decode.</param>
    /// <param name="entityModels">Models to load beyond the map's own, or null.</param>
    /// <param name="mapName">Which map, without folder or extension.</param>
    /// <returns>The assets, shared with every other caller asking the same thing.</returns>
    /// <remarks>
    /// Skips the calling test when the game or the map is absent, which is checked BEFORE the cache
    /// is touched — a skip thrown inside the lazy would be cached and rethrown at every later
    /// caller, turning one missing file into a permanently poisoned entry.
    /// </remarks>
    public static MapAssets Load(
        int maximumTextureSize = DefaultTextureSize,
        IReadOnlyCollection<string>? entityModels = null,
        string mapName = DefaultMap)
    {
        string path = RequirePath(mapName);

        string key = string.Join(
            '|',
            mapName,
            maximumTextureSize,
            entityModels is null ? string.Empty : string.Join(',', entityModels.Order(StringComparer.Ordinal)));

        return Loaded.GetOrAdd(
            key,
            _ => new Lazy<MapAssets>(
                () => MapAssets.Load(
                    File.ReadAllBytes(path),
                    GameArchives.Open(Tf2Install.Folder),
                    maximumTextureSize,
                    entityModels),
                LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;
    }

    /// <summary>The map's own bytes, for tests reading lumps rather than assets.</summary>
    /// <remarks>
    /// Cached separately and for a smaller reason: a BSP is tens of megabytes and several tests
    /// read one only to pull a lump out of it. Bytes are shared rather than copied, so a caller
    /// must not write to them — none does, and <see cref="ReadOnlyMemory{T}"/> at the call sites
    /// keeps it that way.
    /// </remarks>
    public static byte[] Bytes(string mapName = DefaultMap) =>
        MapBytes.GetOrAdd(
            mapName,
            name => new Lazy<byte[]>(
                () => File.ReadAllBytes(RequirePath(name)),
                LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;

    private static readonly ConcurrentDictionary<string, Lazy<byte[]>> MapBytes = new();

    /// <summary>The map's path, or skips the calling test.</summary>
    private static string RequirePath(string mapName)
    {
        if (Tf2Install.Folder is not { } game)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");

            throw new InvalidOperationException("unreachable; Assert.Ignore throws");
        }

        string path = Path.Combine(game, "maps", mapName + ".bsp");

        if (!File.Exists(path))
        {
            Assert.Ignore($"{mapName} is not installed.");
        }

        return path;
    }
}
