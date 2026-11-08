using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.ProjectedShadows;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What a frame's shadows are worked out from.</summary>
/// <param name="Enabled">`r_shadowrendertotexture`.</param>
/// <param name="Timeline">The demo, for its <c>shadow_control</c>; null for none.</param>
/// <param name="Loaded">The map; null for none.</param>
/// <param name="World">The map as the decal walk sees it; null for none.</param>
/// <param name="Tick">The tick shown.</param>
/// <param name="Drawn">What the moment draws.</param>
/// <param name="Players">The players at that tick; null when none were sampled.</param>
/// <param name="Viewing">The camera.</param>
/// <param name="ViewportHeight">The viewport's height in pixels.</param>
internal readonly record struct ShadowStep(
    bool Enabled,
    DemoTimeline? Timeline,
    LoadedMap? Loaded,
    DecalWorld? World,
    int Tick,
    IReadOnlyList<SceneProp> Drawn,
    IReadOnlyList<ScenePlayer>? Players,
    FreeCamera Viewing,
    int ViewportHeight);

/// <summary>
/// The frame's render-to-texture shadows (B702, finding 107): the casters (<see cref="ShadowCasters"/>), the world
/// decals they land as (<see cref="ShadowReceivers"/>), and the <c>shadow_control</c> they are cast by — handed to the
/// device once a frame.
/// </summary>
/// <remarks>
/// **The client's own values before any controller**: direction (0.1, 0.1, −1), distance 50 (`clientshadowmgr.cpp:1287-1289`),
/// and the colour <see cref="LevelColour"/>.
/// </remarks>
internal sealed class ShadowEffects(EntityModelSet models)
{
    private static readonly Vector3 DefaultDirection = new(0.1f, 0.1f, -1f);

    /// <summary>Every leaf drawn, for a map the view cannot cull.</summary>
    private static readonly Func<int, bool> EveryLeaf = static _ => true;
    private const float DefaultDistance = 50f;

    /// <summary>
    /// The shadow colour a level starts with — <c>LevelInitPreEntity</c> (`clientshadowmgr.cpp:1513-1526`): the engine's
    /// ambient light × 3 + 0.3, each channel 255 above one and <c>(unsigned char)( 255 · c )</c> otherwise.
    /// </summary>
    /// <param name="lights">The level's world lights.</param>
    /// <returns>The colour.</returns>
    /// <remarks>
    /// <c>GetAmbientLightColor</c> (`engine.dll` `0x180071000`, IVEngineClient slot 46) is the intensity of the FIRST
    /// <c>emit_skyambient</c> world light (`FUN_1801ba290`, type 5), or zero for a map with none (disassembled).
    /// </remarks>
    internal static (byte Red, byte Green, byte Blue) LevelColour(IReadOnlyList<Content.Bsp.BspWorldLight> lights)
    {
        Vector3 ambient = Vector3.Zero;

        foreach (Content.Bsp.BspWorldLight light in lights)
        {
            if (light.Kind == Content.Bsp.WorldLightKind.SkyAmbient)
            {
                ambient = new Vector3(light.Intensity.Red, light.Intensity.Green, light.Intensity.Blue);
                break;
            }
        }

        ambient = (ambient * 3f) + new Vector3(0.3f);

        return (Channel(ambient.X), Channel(ambient.Y), Channel(ambient.Z));

        static byte Channel(float c) => c > 1f ? (byte)255 : (byte)(255f * c);
    }

    private readonly ShadowCasters _casters = new();
    private readonly ShadowEntities _gather = new();
    private readonly List<ShadowEntity> _entities = [];
    private readonly HashSet<int> _players = [];

    private readonly List<ShadowBrush> _brushes = [];

    /// <summary>Two lists each, one a frame: the device may still be drawing the last one while this one is filled.</summary>
    private readonly List<ShadowVertex>[] _vertices = [[], []];
    private readonly List<ShadowVertex>[] _brushVertices = [[], []];
    private int _half;

    private (LoadedMap Map, ShadowReceivers? Receivers)? _receivers;

    /// <summary>The two delegates the gather asks, made once.</summary>
    private Func<int, bool>? _isPlayer;
    private Func<SceneProp, (Vector3 Min, Vector3 Max)>? _bounds;
    private Func<SceneProp, bool>? _animates;
    private Func<SceneProp, byte>? _falloffBias;
    private Func<SceneProp, ShadowPhysics?>? _physics;

    /// <summary>How many shadows and decal corners — on the world, and on brush entities — the last frame handed over, for an instrument.</summary>
    public (int Shadows, int Corners, int BrushCorners) Last { get; private set; }

    /// <summary>A new level: every cell free (`LevelInitPreEntity`) and the old map's receivers gone.</summary>
    public void ClearMap()
    {
        _casters.Reset();
        _receivers = null;
    }

    /// <summary>Works out this frame's shadows and hands them to the device.</summary>
    /// <param name="step">What they are worked out from.</param>
    /// <param name="device">The device that draws them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is null.</exception>
    /// <param name="parts">Where its own parts are timed, or null.</param>
    public void Step(in ShadowStep step, IFrameDevice device, FrameParts? parts = null)
    {
        ArgumentNullException.ThrowIfNull(device);

        long at = parts is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();

        SceneShadowControl? control = step.Timeline?.ShadowControl.At(step.Tick);

        if (!step.Enabled || control is { Disabled: true } || step.Loaded is not { } loaded || step.World is not { } world ||
            loaded.Level.Leaves is not { } tree)
        {
            Last = (0, 0, 0);
            device.SetShadows(ShadowFrame.None);
            return;
        }

        Vector3 direction = control is { } c ? new Vector3(c.Direction.X, c.Direction.Y, c.Direction.Z) : DefaultDirection;
        float distance = control?.Distance ?? DefaultDistance;
        (byte red, byte green, byte blue) = control?.Color ?? LevelColour(loaded.Level.WorldLights);

        _players.Clear();

        foreach (ScenePlayer player in step.Players ?? [])
        {
            _players.Add(player.EntityIndex);
        }

        _isPlayer ??= _players.Contains;
        _bounds ??= models.RenderBounds;
        _animates ??= models.AnimatesShadow;
        _falloffBias ??= models.ShadowFalloffBias;
        _physics ??= models.SimulatedShadow;
        _gather.Gather(step.Drawn, _isPlayer, _bounds, _animates, _falloffBias, _physics, _entities);
        at = FrameParts.Lap(parts, "camera particles/shadows/gather", at);

        // The view's leaves decide which shadows are considered (`EnumerateShadowsInLeaves`); a map the view cannot cull
        // draws every leaf.
        IReadOnlyList<CastShadow> casters = _casters.Frame(
            _entities,
            direction,
            distance,
            step.Viewing.Frustum(),
            step.Viewing.ScreenView(step.ViewportHeight),
            view: new ShadowLeafView(tree, device.WorldLeafDrawn ?? EveryLeaf));

        at = FrameParts.Lap(parts, "camera particles/shadows/casters", at);

        List<ShadowVertex> vertices = _vertices[_half];
        List<ShadowVertex> brushVertices = _brushVertices[_half];
        _half ^= 1;
        vertices.Clear();
        brushVertices.Clear();

        ShadowReceivers receivers = ReceiversOf(loaded, world, tree);

        ShadowEntities.Brushes(step.Drawn, loaded.Level.BrushModels ?? [], _brushes);
        receivers.Begin(_brushes);

        foreach (CastShadow shadow in casters)
        {
            receivers.Project(shadow);
        }

        at = FrameParts.Lap(parts, "camera particles/shadows/receivers", at);

        (IReadOnlyList<int>? surfaces, IReadOnlyList<ReachedDisplacement>? displacements) = device.WorldDrawn;

        receivers.Build(surfaces, displacements, vertices);
        receivers.BuildBrushes(brushVertices);
        FrameParts.Lap(parts, "camera particles/shadows/decals", at);
        Last = (casters.Count, vertices.Count, brushVertices.Count);
        device.SetShadows(new ShadowFrame(casters, vertices, new Vector3(Linear(red), Linear(green), Linear(blue)), brushVertices)
        {
            Blob = loaded.Assets?.ShadowBlob,
        });
    }

    /// <summary>`SetPixelShaderConstantGammaToLinear`: a colour byte as the shader's linear constant, `pow( c, 2.2 )`.</summary>
    private static float Linear(byte value) => MathF.Pow(value / 255f, 2.2f);

    private ShadowReceivers ReceiversOf(LoadedMap loaded, DecalWorld world, Content.Bsp.BspLeafTree tree)
    {
        if (_receivers is { } held && ReferenceEquals(held.Map, loaded) && held.Receivers is { } built)
        {
            return built;
        }

        IReadOnlyList<MapTexture?> textures = loaded.Assets?.Textures ?? [];
        ShadowReceivers receivers = ShadowReceivers.ForMap(
            tree,
            world,
            loaded.Level.Surfaces,
            texdata => texdata >= 0 && texdata < textures.Count ? ShadowReceivers.MaterialBits(textures[texdata]) : SurfaceShadowBits.None);

        _receivers = (loaded, receivers);
        return receivers;
    }
}
