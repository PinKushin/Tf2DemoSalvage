using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Render;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What one frame's light styles are stepped against.</summary>
/// <param name="Timeline">The loaded demo, or null.</param>
/// <param name="Loaded">The loaded map, or null.</param>
/// <param name="Tick">The whole tick being shown.</param>
/// <param name="WorldLights">The dynamic lights as the frame drew them.</param>
/// <param name="Drawn">The entities the frame drew, for the brush models the dynamic lights reach.</param>
/// <param name="Walk">The map as the engine's light walk sees it, built once per map by whoever owns it.</param>
public readonly record struct LightStyleFrame(
    DemoTimeline? Timeline,
    LoadedMap? Loaded,
    int Tick,
    DynamicLights WorldLights,
    IReadOnlyList<SceneProp> Drawn,
    Func<LoadedMap, DecalWorld> Walk);

/// <summary>What a loaded map holds that the light styles read, as data (D214): a test supplies it by hand.</summary>
/// <param name="Lightmaps">The lightmap atlas, or null when the map's assets would not load.</param>
/// <param name="Lighting">The level's lights.</param>
/// <param name="Level">The map's geometry.</param>
/// <param name="Walk">The map as the engine's light walk sees it.</param>
internal readonly record struct LightStyleMap(
    LightmapAtlas? Lightmaps,
    LevelLighting Lighting,
    MapLevel Level,
    Func<DecalWorld> Walk);

/// <summary>
/// Each light style's value at a tick, the world's lightmaps rebuilt where one changed, and the faces under the dynamic
/// lights (B424, B425).
/// </summary>
/// <remarks>
/// **Moved out of `MainForm` whole** (D214, thin view slice 3), where it was `StepLightStyles` and its three fields:
/// the same arithmetic and the same order. The view's side is the constructor, one <see cref="Step"/> a frame,
/// <see cref="Scale"/> for whoever else reads a style, and <see cref="ClearMap"/>.
/// </remarks>
public sealed class LightStyleEffects
{
    private readonly ILogger _renderLog;

    /// <summary>Every light style's value, animated once a frame.</summary>
    private LightStyleValues _lightStyles = new();

    /// <summary>The world's faces under the dlights, built with the map's decal world.</summary>
    private WorldDynamicLights? _worldLights;

    /// <summary>The atlas regions rebuilt this frame, reused.</summary>
    private readonly List<AtlasRegion> _lightmapRegions = [];

    /// <summary>The frame's walk bound to its map, made once a map.</summary>
    private Func<DecalWorld>? _walk;

    /// <summary>The map <see cref="_walk"/> is bound to.</summary>
    private LoadedMap? _walkOf;

    /// <summary>Light styles for one view.</summary>
    /// <param name="render">The render log.</param>
    public LightStyleEffects(ILogger render) => _renderLog = render;

    /// <summary>A style's value now, as a scale of the stored light — what everything else lit by a style multiplies by.</summary>
    /// <param name="style">The style.</param>
    /// <returns>The scale.</returns>
    public float Scale(int style) => _lightStyles.Scale(style);

    /// <summary>Forgets what belonged to the map that is going: its dynamic light walk and its styles' values.</summary>
    public void ClearMap()
    {
        _worldLights = null;
        _lightStyles = new();
    }

    /// <summary>Each light style's value at this tick, and the world's lightmaps rebuilt where one changed.</summary>
    /// <param name="frame">What this frame is stepped against.</param>
    /// <param name="device">The device that holds the lightmaps.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is null.</exception>
    /// <remarks>
    /// `R_AnimateLight` (`engine.dll` `0x1800d3ec0`) once a frame from the `lightstyles` table, then each face answering
    /// to a changed style rebuilt and uploaded, as `R_BuildLightMap` rebuilds a face whose style was stamped. The time is
    /// the tick's, so a seek lands on the same step however it was reached.
    /// </remarks>
    public void Step(in LightStyleFrame frame, ILightmapUpload device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (frame.Timeline is null || frame.Loaded is null)
        {
            return;
        }

        if (_walk is null || !ReferenceEquals(frame.Loaded, _walkOf))
        {
            LoadedMap loaded = frame.Loaded;
            Func<LoadedMap, DecalWorld> walk = frame.Walk;

            _walkOf = loaded;
            _walk = () => walk(loaded);
        }

        StepOver(
            frame.Timeline,
            new LightStyleMap(frame.Loaded.Assets?.Lightmaps, frame.Loaded.Lighting, frame.Loaded.Level, _walk),
            frame.Tick,
            frame.WorldLights,
            frame.Drawn,
            device);
    }

    /// <summary>The same, over what the map holds as data and any device (D214).</summary>
    /// <param name="timeline">The loaded demo.</param>
    /// <param name="map">What the map holds.</param>
    /// <param name="tick">The whole tick being shown.</param>
    /// <param name="worldLights">The dynamic lights as the frame drew them.</param>
    /// <param name="drawn">The entities the frame drew.</param>
    /// <param name="device">The device that holds the lightmaps.</param>
    internal void StepOver(
        DemoTimeline timeline,
        in LightStyleMap map,
        int tick,
        DynamicLights worldLights,
        IReadOnlyList<SceneProp> drawn,
        ILightmapUpload device)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (map.Lightmaps is not { } lightmaps)
        {
            return;
        }

        for (int style = 0; style < LightStyleValues.Count; style++)
        {
            _lightStyles.Set(style, timeline.LightStyles.PatternAt(style, tick));
        }

        IReadOnlyList<int> changed = _lightStyles.Advance(tick * timeline.IntervalPerTick);

        // The world lights answer to the same values (`0x1801b8e20`), so a model under a switched lamp samples again.
        LevelLighting lighting = map.Lighting;

        // Also what a baked static prop's styled lights are drawn at, on top of its colours (B424, `FUN_1801b5400`).
        lighting.StyleScale ??= style => _lightStyles.Scale(style);

        if (changed.Count > 0 && lighting.Answers(changed))
        {
            // Per style, so a static prop's ranked styled lights stand until a style it lists changes (B607).
            lighting.StylesChanged(changed);
        }

        _lightmapRegions.Clear();
        lightmaps.Recompose(_lightStyles.Scale, changed, _lightmapRegions);

        // B425: after the styles, so a dlit styled face is rebuilt with both; from the lights as the frame drew them.
        _worldLights ??= new WorldDynamicLights(map.Walk());
        _worldLights.Frame(
            worldLights,
            lightmaps,
            _lightStyles.Scale,
            _lightmapRegions,
            WorldDynamicLights.BrushesOf(drawn, map.Level.BrushModels ?? []));

        device.UpdateLightmap(lightmaps.Pixels, lightmaps.Width, _lightmapRegions);

        // Once per style change past the first frame, so a toggle is visible in the log with what it rebuilt.
        if (_lightmapRegions.Count > 0 && _renderLog.IsEnabled(LogLevel.Information))
        {
            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            _renderLog.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"light styles at {tick}: {changed.Count} changed ({string.Join(",", changed.Take(8))}), {_lightmapRegions.Count} lightmaps rebuilt"));
        }
    }
}
