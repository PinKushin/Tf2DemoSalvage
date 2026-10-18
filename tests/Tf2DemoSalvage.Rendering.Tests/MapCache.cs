using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

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

    /// <summary>One load per distinct request while any test holds it; released once none does (<see cref="WeakMemo{T}"/>).</summary>
    private static readonly WeakMemo<MapAssets> Loaded = new();

    /// <summary>The key of <see cref="Load"/> with every default, the request held strongly.</summary>
    private static readonly string DefaultKey = $"{DefaultMap}|{DefaultTextureSize}|||";

    /// <summary>Keeps the default request alive for the run; read by nothing.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S4487:Unread \"private\" fields should be removed",
        Justification = "Holding it is the purpose: the strong reference keeps the weak cache's entry alive.")]
    private static MapAssets? _default;

    /// <summary>A cached load: the assets, and everything the load said while producing them.</summary>
    /// <param name="Assets">The map, shared with every other caller asking the same thing.</param>
    /// <param name="Log">Every line the load wrote, with the area that wrote it.</param>
    /// <remarks>
    /// **The log rides along because it costs nothing and it was the missing instrument.** Loading
    /// a map with a real logger factory is the same work as loading it with a null one; keeping
    /// what it said turns "what did the loader report" from a question needing its own 30-second
    /// load into a question any test can ask of the shared one.
    ///
    /// It is also the only way to assert that a subsystem spoke AT ALL, which is what B229 needed:
    /// `MapAssets` was not passing `PropModels.Load` a logger, so the static-prop path wrote to a
    /// `NullLogger` and its two chequer warnings could never appear in any log the owner read.
    /// </remarks>
    internal sealed record LoadedMap(MapAssets Assets, RecordingLoggerFactory Log);

    /// <summary>The reference map, loaded once per distinct request.</summary>
    /// <param name="maximumTextureSize">Largest texture edge to decode.</param>
    /// <param name="entityModels">Models to load beyond the map's own, or null.</param>
    /// <param name="mapName">Which map, without folder or extension.</param>
    /// <param name="spriteMaterials">Sprite materials to load beside the map, or null.</param>
    /// <param name="wornModels">Models to load as worn (bone-merged) ones, or null.</param>
    /// <returns>The assets, shared with every other caller asking the same thing.</returns>
    /// <remarks>
    /// Skips the calling test when the game or the map is absent, which is checked BEFORE the cache
    /// is touched — a skip thrown inside the lazy would be cached and rethrown at every later
    /// caller, turning one missing file into a permanently poisoned entry.
    /// </remarks>
    public static MapAssets Load(
        int maximumTextureSize = DefaultTextureSize,
        IReadOnlyCollection<string>? entityModels = null,
        string mapName = DefaultMap,
        IReadOnlyCollection<string>? spriteMaterials = null,
        IReadOnlyCollection<string>? wornModels = null) =>
        Loaded_(maximumTextureSize, entityModels, mapName, spriteMaterials, wornModels).Assets;

    /// <summary>
    /// Every sprite material the effect-entity tests draw with — beam, halo, cable, trail — so they share ONE load.
    /// </summary>
    /// <remarks>
    /// A sprite material does not depend on the map it is loaded beside, and each distinct list is another whole map
    /// held for the life of the run; one list on the default map is one entry (D213).
    /// </remarks>
    public static readonly string[] EffectSprites =
    [
        "sprites/glow_test02.vmt",
        "sprites/light_glow03.vmt",
        "cable/cable.vmt",
        "cable/rope.vmt",

        // What `DemoModels.Sprites` adds for any rope: its `_back`, Pyrovision's solid, the two holiday light sprites.
        EntityRopes.BackMaterialPath("cable/cable.vmt"),
        EntityRopes.PureWhiteMaterial,
        RopeHolidayLights.BulbMaterial,
        RopeHolidayLights.FluffMaterial,
        "effects/repair_claw_trail_blue.vmt",
        "effects/beam001_white.vmt",

        // The default `env_sun` material, core and glow (`sun.cpp:141-160`) — B378.
        "sprites/light_glow02_add_noz.vmt",
    ];

    /// <summary>The default map with <see cref="EffectSprites"/> loaded beside it.</summary>
    public static MapAssets Effects() => Load(spriteMaterials: EffectSprites);

    /// <summary>The same load, with what it reported while producing the assets.</summary>
    /// <param name="maximumTextureSize">Largest texture edge to decode.</param>
    /// <param name="entityModels">Models to load beyond the map's own, or null.</param>
    /// <param name="mapName">Which map, without folder or extension.</param>
    /// <returns>The assets and the log, shared with every other caller asking the same thing.</returns>
    public static LoadedMap With(
        int maximumTextureSize = DefaultTextureSize,
        IReadOnlyCollection<string>? entityModels = null,
        string mapName = DefaultMap) =>
        Loaded_(maximumTextureSize, entityModels, mapName, null, null);

    private static LoadedMap Loaded_(
        int maximumTextureSize,
        IReadOnlyCollection<string>? entityModels,
        string mapName,
        IReadOnlyCollection<string>? spriteMaterials,
        IReadOnlyCollection<string>? wornModels)
    {
        // Both skips happen here, before the cache is touched.
        RequirePath(mapName);
        GameArchives archives = SharedGame.Archives;

        static string Listed(IReadOnlyCollection<string>? names) =>
            names is null ? string.Empty : string.Join(',', names.Order(StringComparer.Ordinal));

        string key = string.Join(
            '|', mapName, maximumTextureSize, Listed(entityModels), Listed(spriteMaterials), Listed(wornModels));

        MapAssets assets = Loaded.Get(
            key,
            () =>
            {
                RecordingLoggerFactory log = new();
                MapAssets made = MapAssets.Load(
                    Bytes(mapName),
                    archives,
                    maximumTextureSize,
                    entityModels,
                    wornModels,
                    spriteMaterials,
                    loggers: log);

                Logs.AddOrUpdate(made, log);

                return made;
            });

        // **The one request held for the run**: the default map with nothing added, asked by more fixtures than any
        // other. Held weakly it was loaded 19 times in one run, 2026-10-18 — most of the 43 reloads that cost the weak
        // cache 2.3 minutes. One map kept is about a gigabyte; nineteen loads of it are about two minutes.
        if (key == DefaultKey)
        {
            _default = assets;
        }

        return new LoadedMap(assets, Logs.TryGetValue(assets, out RecordingLoggerFactory? said)
            ? said
            : throw new InvalidOperationException($"the load of {key} recorded no log"));
    }

    /// <summary>Each load's log, alive exactly as long as its assets.</summary>
    /// <remarks>
    /// **Keyed on the assets, not cached beside them**, because callers keep only the assets: a memo of the pair was
    /// collectable while a test still drew the map, so the next ask loaded a second copy beside the first (review,
    /// 2026-10-18).
    /// </remarks>
    private static readonly ConditionalWeakTable<MapAssets, RecordingLoggerFactory> Logs = new();

    /// <summary>The memo itself, for a test of what it holds.</summary>
    internal static int LoadsSoFar => Loaded.Loads;

    /// <summary>The map's own bytes, for tests reading lumps rather than assets.</summary>
    /// <remarks>
    /// Shared for a smaller reason: a BSP is tens of megabytes and several tests read one only to
    /// pull a lump out of it — and held weakly like the maps, since 26 of them kept for the run
    /// are most of a gigabyte. Bytes are shared rather than copied, so a caller must not write to
    /// them — none does, and <see cref="ReadOnlyMemory{T}"/> at the call sites keeps it that way.
    /// </remarks>
    public static byte[] Bytes(string mapName = DefaultMap)
    {
        string path = RequirePath(mapName);

        return MapBytes.Get(mapName, () => File.ReadAllBytes(path));
    }

    private static readonly WeakMemo<byte[]> MapBytes = new();

    /// <summary>Whether a map is installed, for a test that walks several and skips the absent.</summary>
    /// <remarks>
    /// **Asks rather than skips**, which <see cref="RequirePath"/> cannot do: a test comparing maps
    /// across TF2's history has to report which ones it managed to read, and `Assert.Ignore` on the
    /// first missing one would abandon the maps that ARE installed.
    /// </remarks>
    public static bool Exists(string mapName) =>
        GameInstall.Root is { } game &&
        File.Exists(Path.Combine(game, "maps", mapName + ".bsp"));

    /// <summary>
    /// A map from the game or from the viewer's downloaded-maps folder, or null when the game or the map is absent.
    /// </summary>
    /// <param name="mapName">Which map, without folder or extension.</param>
    /// <param name="maximumTextureSize">Largest texture edge to decode.</param>
    /// <returns>The shared assets, or null — for a test that says in its own words what it could not run on.</returns>
    /// <remarks>
    /// The parity reference <c>cp_process_f12</c> is not a shipped map: the viewer downloads it into
    /// <c>%LocalAppData%\Tf2DemoSalvage\maps</c>, where seven fixtures were each loading it per TEST (D213).
    /// </remarks>
    public static MapAssets? TryLoad(string mapName, int maximumTextureSize = DefaultTextureSize) =>
        GameInstall.Root is null || Find(mapName) is null ? null : Load(maximumTextureSize, mapName: mapName);

    private static string? Find(string mapName)
    {
        string downloaded = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tf2DemoSalvage", "maps", mapName + ".bsp");

        if (GameInstall.Root is { } game && File.Exists(Path.Combine(game, "maps", mapName + ".bsp")))
        {
            return Path.Combine(game, "maps", mapName + ".bsp");
        }

        return File.Exists(downloaded) ? downloaded : null;
    }

    /// <summary>The map's path, or skips the calling test.</summary>
    /// <remarks>
    /// **This was fifteen lines and is now one**, which is the whole case for
    /// <see cref="GameInstall.RequireFile"/>: it already distinguishes "the game is absent" from
    /// "the game is here and this map is not", which is what those fifteen lines spelt out.
    ///
    /// It also removes a
    /// <c>throw new InvalidOperationException("unreachable; Assert.Ignore throws")</c> that stood
    /// here only to satisfy definite assignment. <see cref="Skip.Because"/> is
    /// <c>[DoesNotReturn]</c>, so the compiler now knows control stops at the skip.
    /// </remarks>
    private static string RequirePath(string mapName) =>
        Find(mapName) ?? GameInstall.RequireFile($"maps/{mapName}.bsp");
}
