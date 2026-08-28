using System;
using System.Globalization;
using System.IO;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Logging;

namespace Tf2DemoSalvage.Scene;

/// <summary>A map, read and ready to draw.</summary>
/// <remarks>
/// **This is the domain half of <c>MainForm.ReadMap</c> and <c>ProjectMap</c>** (B188, D90). What
/// stayed behind is what genuinely needs a window: uploading to a device, and saying so in a status
/// line.
///
/// **Textures failing costs the textures, not the map**, which is why <see cref="Assets"/> is
/// nullable and <see cref="Problem"/> exists. A map whose content will not load still draws its
/// outline, still reports where every player stood, and says what went wrong — the salvage case this
/// project is for (D5). A map whose FACES will not read is a different thing and throws, because
/// continuing past that produces a black world rather than an error.
/// </remarks>
public sealed class LoadedMap
{
    private LoadedMap(
        MapOutline outline,
        MapLevel level,
        MapAssets? assets,
        LevelLighting lighting,
        GameContent game,
        string? problem)
    {
        Outline = outline;
        Level = level;
        Assets = assets;
        Lighting = lighting;
        Problem = problem;
        _game = game;
    }

    private readonly GameContent _game;

    // **`_colourByClass` went on 2026-08-27** (B219). It survived only to reach `MapWorldBuilder`,
    // which baked a flat colour per surface kind into every vertex — so the category view could not
    // be switched without rebuilding the map. The batch carries its category now and the renderer
    // picks the colour, so the world build no longer cares which view is on.
    //
    // `Read` still takes the flag, because brush ENTITY tints are baked separately and have not
    // moved yet.

    /// <summary>The play area's shape, for framing a camera on it.</summary>
    public MapOutline Outline { get; }

    /// <summary>Every lump the map carries.</summary>
    public MapLevel Level { get; }

    /// <summary>The textures and baked lighting, or null when the content would not load.</summary>
    public MapAssets? Assets { get; }

    /// <summary>What light this map casts, which the models and the asset loader both ask.</summary>
    public LevelLighting Lighting { get; }

    /// <summary>What went wrong loading the content, or null when nothing did.</summary>
    public string? Problem { get; }

    /// <summary>How high and how low the map goes, once the world has been built.</summary>
    /// <remarks>
    /// **Recorded during the build rather than after it**, because the camera projects height on the
    /// very first frame; taking it afterwards leaves one frame drawn with a pass-through depth.
    /// </remarks>
    public (float Lowest, float Highest)? HeightRange { get; private set; }

    /// <summary>Reads a map and everything drawing it needs.</summary>
    /// <param name="bytes">The whole BSP.</param>
    /// <param name="game">What the install provides.</param>
    /// <param name="timeline">The open demo, whose models are loaded with the map.</param>
    /// <param name="textureQuality">The largest texture edge to decode to.</param>
    /// <param name="loggers">Where each stage reports itself, by category (D83).</param>
    /// <returns>The map.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidDataException">The map's faces or lighting would not read.</exception>
    public static LoadedMap Read(
        ReadOnlyMemory<byte> bytes,
        GameContent game,
        DemoTimeline? timeline,
        int textureQuality,
        ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(loggers);

        ILogger assetLog = loggers.CreateLogger("assets");
        ILogger mapLog = loggers.CreateLogger("map");
        ILogger renderLog = loggers.CreateLogger("render");

        BspGeometry geometry = BspGeometry.Read(bytes);
        MapOutline outline = MapOutline.FromFaces(geometry.OverheadFaces);

        mapLog.LogInformation(
            "{Message}",
            $"{geometry.Faces.Count.ToString(CultureInfo.InvariantCulture)} faces, " +
            $"{geometry.OverheadFaces.Count.ToString(CultureInfo.InvariantCulture)} overhead, " +
            $"{outline.Segments.Count.ToString(CultureInfo.InvariantCulture)} outline segments");

        // **Every lump this viewer keeps, read by one type that knows how.** The engine's own
        // arrangement is that each system initialises itself from the level
        // (`IGameSystem::LevelInitPreEntity`, `igamesystem.h:39`) rather than the caller unpacking
        // lumps into fields for everybody.
        MapLevel level;

        using (assetLog.Time("reading the map's lumps"))
        {
            level = MapLevel.Read(bytes, assetLog);
        }

        LevelLighting lighting = LevelLighting.From(level, renderLog);

        // **The early `LoadedMap` went with the baked tint** (B219). It existed only so
        // `BrushModels.Build` could call `map.EntityTint` while the geometry was being built —
        // a map constructed purely to answer a question during its own construction. The colours
        // are per-instance now, so nothing needs an answer this early.

        // **The textured world is its own failure, and losing it costs the textures rather than the
        // map.** The outline still draws and the demo still plays.
        try
        {
            MapAssets assets;

            using (assetLog.Time("reading textures"))
            {
                // **Every model the demo will ever show, loaded WITH the map.** The timeline is
                // already built, so the whole set is known before anything is drawn — and loading
                // them here means their materials join the map's table and the textures upload once.
                // Loading during playback would grow that table and force a re-upload mid-match.
                assets = MapAssets.Load(
                    bytes,
                    game.Archives,
                    textureQuality,
                    DemoModels.Needed(timeline, game),
                    DemoModels.Worn(timeline, game),

                    // **A factory rather than finished geometry, because the atlas is packed inside
                    // Load.** A door's faces carry baked lightmap samples in the same atlas as the
                    // wall's, so the geometry cannot be built before it exists (B131).
                    //
                    // Built from the surfaces just read rather than from a second pass over the
                    // file: the models lump names face RANGES, so it needs the same surface list the
                    // world was built from and nothing else.
                    atlas => BrushModels.Build(
                        level.BrushModels ?? [],
                        level.Surfaces,
                        atlas,
                        // **No tint baked in any more** (B219). Valve's per-class brush entity
                        // colours travel on the instance now, so the geometry is the same whichever
                        // view is on and switching costs a constant write instead of a rebuild.
                        null),

                    // **The light cache, for props whose baked lighting is absent or refused**
                    // (B123). Usable here because the level was read above, before any asset is
                    // loaded — the ordering is what makes this a delegate rather than a second pass.
                    lighting.LightingAt,

                    // **Passed explicitly, and forgetting it is silent (D83).** The parameter
                    // defaults to a null logger so tests need not supply one, which means an
                    // omission here costs every asset line in the run and nothing reports it.
                    loggers);
            }

            Report(level, assets, textureQuality, assetLog);

            return new LoadedMap(outline, level, assets, lighting, game, null);
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            assetLog.LogWarning(failure, "{Message}", "reading the map's content");

            return new LoadedMap(
                outline,
                level,
                null,
                lighting,
                game,
                "Map content unavailable: " + failure.Message);
        }
    }

    /// <summary>Builds the drawable world, in world space.</summary>
    /// <param name="loggers">Where the build reports itself.</param>
    /// <returns>The world, ready to upload.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loggers"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The content did not load, so there is none.</exception>
    /// <remarks>
    /// **Rebuilt on a resize because the projection is baked into the vertices**, which is what keeps
    /// the shader a sample and a multiply. Moving the CAMERA does not need this — that is a
    /// sixty-four byte upload — and the difference is what took a viewport change from a third of a
    /// second to nothing.
    ///
    /// **No play-area cull, and the 3D skybox stays** (owner's direction). Passing the main bounds
    /// discarded every surface and prop outside the map's main cluster — the miniature scenery room
    /// a TF2 map keeps far outside the level. That was right for a camera framed to the play area
    /// and wrong for one that can go anywhere: "you cannot see it from here" stopped being true when
    /// the free camera arrived. Drawing it raw puts a miniature copy of the surroundings far outside
    /// the level; the `sky_camera` transform that makes it read correctly is separate work, and a
    /// visible wrong-looking skybox is a better starting point than an invisible one.
    ///
    /// **No decal bias is set here, and its removal is the point (B135).** This once called
    /// `SetDecalBias` with the map's height range, which DISPOSED the rasteriser state built at load
    /// and replaced it with one computed from 2^24 / range — so every experiment that edited the
    /// constant measured nothing, and zero and Valve's -262144 produced identical pictures because
    /// neither was ever in effect.
    /// </remarks>
    public MapWorld BuildWorld(ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(loggers);

        if (Assets is not { } assets)
        {
            throw new InvalidOperationException(
                "the map's content did not load, so there is no world to build");
        }

        // Recorded before the build so a camera can project height on the very first frame; taking
        // it afterwards leaves one frame drawn with a pass-through depth.
        HeightRange = MapWorldBuilder.HeightRange(Level.Surfaces, Outline.MainBounds);

        // **A `TopDownCamera` was threaded through here into `Build` until 2026-08-26** (D98), and
        // `Build` never read it. The world is built in world space; what points it is the view
        // matrix, set separately and per frame.
        return MapWorldBuilder.Build(
            Level.Terrain,
            Level.Surfaces,
            assets.Materials,
            assets.Lightmaps,
            assets.Props,
            area: null,
            Level.Overlays,
            Level.BrushModels,
            loggers);
    }

    /// <summary>Valve's colour for a brush entity's class, or null for ordinary brushwork.</summary>
    /// <param name="model">The submodel index a <c>*N</c> reference names.</param>
    /// <returns>The colour, or null.</returns>
    /// <remarks>
    /// **Only in the category view.** A brush entity is a door, a lift, an areaportal or a trigger,
    /// and drawn as plain brushwork none of that is visible — the one view whose whole job is "what
    /// is this" was the one view that could not say. The numbers are Valve's, out of the FGDs the
    /// game ships, so a capture reads the same way as Hammer's own colouring rather than needing a
    /// second legend (B156).
    ///
    /// **Null rather than a default at every step**, and each null means something different: the
    /// map may not name this model, the class may state no colour and inherit none, or the FGDs may
    /// not be readable at all. A fallback colour at any of those points would report "Valve says
    /// grey" where the truth is "nobody said", which is the sentinel-shaped mistake this project has
    /// made before.
    /// </remarks>
    public (float Red, float Green, float Blue)? EntityTint(int model)
    {
        if (_game.EntityClasses is not { } classes ||
            !Level.BrushModelClasses.TryGetValue(model, out string? classname))
        {
            // Not an entity at all, or the map named no class for it. Ordinary brushwork.
            return null;
        }

        return classes.Colour(classname) is { } colour
            ? (colour.Red / 255f, colour.Green / 255f, colour.Blue / 255f)
            : HammerDefaultEntityColour;
    }

    /// <summary>Valve's colour for the brush entity a <c>*N</c> path names, or null (B219).</summary>
    /// <param name="modelPath">A scene prop's model path.</param>
    /// <returns>The colour, or null for anything that is not a brush entity.</returns>
    /// <remarks>
    /// **The path form, because that is what the scene has.** A <c>SceneProp</c> carries <c>*N</c>
    /// and the colour is keyed by submodel index, so the parse belongs beside the table it feeds
    /// rather than in whatever holds the props. Anything not shaped like <c>*N</c> is an ordinary
    /// model and has no class colour.
    /// </remarks>
    public (float Red, float Green, float Blue)? EntityTintFor(string modelPath) =>
        modelPath is { Length: > 1 } &&
        modelPath[0] == '*' &&
        int.TryParse(modelPath.AsSpan(1), CultureInfo.InvariantCulture, out int model)
            ? EntityTint(model)
            : null;

    /// <summary>What Hammer draws an entity with no colour of its own in, which is magenta.</summary>
    /// <remarks>
    /// 58 of the 598 classes in the shipped FGDs state no colour and inherit none, and Hammer draws
    /// every one of them magenta. Reporting that rather than inventing a grey is the honest answer.
    /// </remarks>
    private static (float Red, float Green, float Blue) HammerDefaultEntityColour => (1f, 0f, 1f);

    /// <summary>Says what the map turned out to hold, once per read.</summary>
    private static void Report(
        MapLevel level, MapAssets assets, int textureQuality, ILogger assetLog)
    {
        int displacements = 0;

        foreach (BspSurface surface in level.Surfaces)
        {
            displacements += surface.IsDisplacement ? 1 : 0;
        }

        assetLog.LogInformation(
            "{Message}",
            $"{level.Surfaces.Count.ToString(CultureInfo.InvariantCulture)} surfaces " +
            $"({displacements.ToString(CultureInfo.InvariantCulture)} displacements), " +
            $"{assets.Resolved.ToString(CultureInfo.InvariantCulture)} materials resolved, " +
            $"{assets.Missing.ToString(CultureInfo.InvariantCulture)} missing, " +
            $"lightmap atlas {assets.Lightmaps.Width.ToString(CultureInfo.InvariantCulture)}x" +
            $"{assets.Lightmaps.Height.ToString(CultureInfo.InvariantCulture)}, " +
            $"texture quality {textureQuality.ToString(CultureInfo.InvariantCulture)}");

        (double seconds, long count) = Tf2DemoSalvage.Content.Assets.VtfTexture.DecodeCost;

        assetLog.LogInformation(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"VTF decode so far: {seconds:F2}s CPU over {count} textures " +
                $"(decoded in parallel, so wall clock is less); " +
                $"baking {PropModels.BakeSeconds:F2}s"));
    }
}
