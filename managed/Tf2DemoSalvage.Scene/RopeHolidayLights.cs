using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// <c>CRopeManager::IsHolidayLightMode</c> and <c>GetHolidayLightStyle</c> (`c_rope.cpp:637-685`) — whether ropes carry
/// holiday lights, and which (B478).
/// </summary>
/// <remarks>
/// **One per process, like the engine's <c>s_RopeManager</c>**: <c>m_bHolidayInitialized</c> is set the first time the
/// question is asked with game rules present and never cleared, so Christmas is read once.
/// </remarks>
public sealed class RopeHolidayMode
{
    private bool _initialised;
    private bool _christmas;

    /// <summary>The style, or null out of holiday light mode.</summary>
    /// <param name="rulesPresent"><c>TFGameRules()</c> / <c>GameRules()</c> exists.</param>
    /// <param name="powerupMode"><c>IsPowerupMode()</c> — "we don't want to draw the lights for the grapple".</param>
    /// <param name="mapAllows"><c>GetRopesHolidayLightsAllowed()</c>.</param>
    /// <param name="christmas"><c>GameRules()-&gt;IsHolidayActive( kHoliday_Christmas )</c>, asked once.</param>
    /// <param name="pyrovision"><c>IsLocalPlayerUsingVisionFilterFlags( TF_VISION_FILTER_PYRO )</c>.</param>
    /// <returns>0 for Christmas bulbs, 1 for Pyrovision, null for none.</returns>
    /// <remarks><c>r_ropes_holiday_lights_allowed</c> is a development-only cvar at its default, 1.</remarks>
    public int? Style(bool rulesPresent, bool powerupMode, bool mapAllows, Func<bool> christmas, bool pyrovision)
    {
        ArgumentNullException.ThrowIfNull(christmas);

        if (rulesPresent && (powerupMode || !mapAllows))
        {
            return null;
        }

        if (!_initialised && rulesPresent)
        {
            _initialised = true;
            _christmas = christmas();
        }

        // "Turn them on in Pyro-vision too".
        if (pyrovision)
        {
            return 1;
        }

        return _christmas ? 0 : null;
    }
}

/// <summary>One holiday light as its temp entity holds it.</summary>
/// <param name="Origin">Where it is.</param>
/// <param name="Roll">Its roll, degrees.</param>
/// <param name="Colour">Its render colour.</param>
/// <param name="Scale"><c>m_flSpriteScale</c>.</param>
/// <param name="Material">The sprite it was made with.</param>
public readonly record struct RopeHolidayLight(
    Vector3 Origin, float Roll, (byte R, byte G, byte B, byte A) Colour, float Scale, string Material);

/// <summary>
/// The <c>TF_HolidayLight</c> client effect: <c>CHolidayLightManager</c> and <c>CreateHolidayLight</c>
/// (`tf_fx_christmaslights.cpp`) (B478).
/// </summary>
/// <remarks>
/// **A light is a temp entity that never dies** — <c>SpawnTempModel( …, 2.0f, FTENT_NEVERDIE )</c>, and
/// <c>IsActive</c> answers <c>die != 0</c> for one (`c_te_legacytempents.cpp:286-290`) — so it stays until the level ends,
/// moved whenever its rope dispatches it again.
///
/// ***Interpolated:*** the bulb's roll draws from a stream of this object's, not the client's global <c>RandomFloat</c>;
/// the 500-entity pool is this effect's alone, where the engine shares it with every other temp entity; and the sprite's
/// animation (<c>FTENT_SPRANIMATE</c> at 10 frames a second) is not drawn — this project draws a sprite's first frame.
/// </remarks>
public sealed class RopeHolidayLights
{
    /// <summary>Style 0's sprite.</summary>
    public const string BulbMaterial = "effects/christmas_bulb.vmt";

    /// <summary>Style 1's.</summary>
    public const string FluffMaterial = "effects/mtp_fluff.vmt";

    /// <summary>"Too far away?" — and "In the skybox?" — both 2000 units.</summary>
    private const float Range = 2000f;

    /// <summary><c>CTempEnts::MAX_TEMP_ENTITIES</c> (`c_te_legacytempents.h:139`).</summary>
    private const int MaximumTempEntities = 500;

    /// <summary><c>rgbaHolidayLightColors</c>: "red", "yellow", "green", "blue" by name — the values are these.</summary>
    private static readonly (byte R, byte G, byte B)[] Colours = [(255, 0, 0), (2, 110, 197), (117, 193, 8), (255, 151, 29)];

    private readonly List<RopeHolidayDispatch> _pending = [];
    private readonly Dictionary<(int Id, int SubId), int> _byId = [];
    private readonly List<Light> _lights = [];
    private readonly List<RopeHolidayLight> _view = [];
    private readonly UniformRandomStream _random = new();
    private readonly SpriteStripBatches _strips = new();
    private readonly List<DetailSpriteVertex> _corners = [];

    /// <summary><c>g_nHolidayLightColor</c>.</summary>
    private int _nextColour;

    /// <summary>Every light made so far, in creation order.</summary>
    public IReadOnlyList<RopeHolidayLight> Lights => _view;

    /// <summary><c>CHolidayLightManager::AddHolidayLight</c>: queues a dispatch near the local player and outside the sky.</summary>
    /// <param name="dispatch">What <c>BuildRope</c> dispatched.</param>
    /// <param name="localPlayer">The local player's origin, or null without one.</param>
    /// <param name="skyOrigin">His <c>m_skybox3d.origin</c>.</param>
    public void Add(RopeHolidayDispatch dispatch, Vector3? localPlayer, Vector3 skyOrigin)
    {
        if (localPlayer is not { } player ||
            Vector3.Distance(player, dispatch.Origin) > Range ||
            Vector3.Distance(skyOrigin, dispatch.Origin) < Range)
        {
            return;
        }

        if (_pending.Count < MaximumTempEntities / 2)
        {
            _pending.Add(dispatch);
        }
    }

    /// <summary><c>CHolidayLightManager::Update</c>: each queued light made or moved, then the queue emptied.</summary>
    /// <param name="curtime"><c>gpGlobals-&gt;curtime</c>, which the blink cycle reads.</param>
    /// <param name="style"><c>GetHolidayLightStyle()</c> now.</param>
    public void Update(float curtime, int style)
    {
        foreach (RopeHolidayDispatch dispatch in _pending)
        {
            Create(dispatch, curtime, style);
        }

        _pending.Clear();

        _view.Clear();

        foreach (Light light in _lights)
        {
            _view.Add(new RopeHolidayLight(
                light.Origin, light.Roll, (light.Rgb.R, light.Rgb.G, light.Rgb.B, light.Alpha), light.Scale, light.Material));
        }
    }

    /// <summary><c>LevelShutdownPreEntity</c> and the temp entities' level shutdown: every light and queued dispatch gone.</summary>
    public void Clear()
    {
        _pending.Clear();
        _lights.Clear();
        _byId.Clear();
        _view.Clear();
    }

    /// <summary>The lights as sprites — <c>C_LocalTempEntity::DrawModel</c>'s <c>DrawSprite</c> at <c>kRenderNormal</c>.</summary>
    /// <param name="sprites">Each sprite material, by path.</param>
    /// <param name="viewForward">The view's forward.</param>
    /// <param name="viewRight">Its right.</param>
    /// <param name="viewUp">Its up.</param>
    /// <returns>One batch per material.</returns>
    public IReadOnlyList<ParticleBatch> Batches(
        IReadOnlyDictionary<string, EngineSprite> sprites, Vector3 viewForward, Vector3 viewRight, Vector3 viewUp)
    {
        ArgumentNullException.ThrowIfNull(sprites);

        _strips.Clear();

        foreach (Light light in _lights)
        {
            if (!sprites.TryGetValue(light.Material, out EngineSprite sprite) ||
                EntitySprites.Axes(sprite.Orientation, light.Origin, (0f, 0f, light.Roll), viewRight, viewUp, viewForward)
                    is not { } axes)
            {
                continue;
            }

            _corners.Clear();
            EntitySprites.Corners(
                light.Origin,
                axes.Right,
                axes.Up,
                sprite.Extents,
                EntitySprites.RenderScale(light.Scale, worldSpace: false, sprite.Width, sprite.Height),
                new Vector3(light.Rgb.R, light.Rgb.G, light.Rgb.B) / 255f,
                light.Alpha / 255f,
                _corners);

            _strips.Add(light.Material, sprite, RenderModes.Normal, _corners);
        }

        return _strips.Batches(sprites);
    }

    /// <summary><c>CreateHolidayLight</c> (`tf_fx_christmaslights.cpp:136-203`).</summary>
    private void Create(RopeHolidayDispatch dispatch, float curtime, int style)
    {
        // `FindTempEntByID`: skin and hitSound carry the two ids.
        if (_byId.TryGetValue((dispatch.RopeIndex, dispatch.SubId), out int found))
        {
            Light existing = _lights[found];

            existing.Origin = dispatch.Origin;

            // "Every 10 light strands have a blink cycle".
            if (dispatch.RopeIndex % 5 == 0)
            {
                int cycle = (dispatch.SubId + (int)(curtime * 2f)) % (existing.ColourIndex + Colours.Length + 1);

                existing.Alpha = cycle < Colours.Length ? (byte)255 : (byte)64;
            }

            existing.Scale = dispatch.Scale;

            return;
        }

        if (_lights.Count >= MaximumTempEntities)
        {
            return;
        }

        int colour = _nextColour;

        _nextColour = (_nextColour + 1) % Colours.Length;
        _byId[(dispatch.RopeIndex, dispatch.SubId)] = _lights.Count;
        _lights.Add(new Light
        {
            Origin = dispatch.Origin,
            Roll = style == 0 ? _random.RandomFloat(-180f, 180f) : 0f,
            ColourIndex = colour,
            Rgb = Colours[colour],
            Alpha = 255,
            Scale = dispatch.Scale,
            Material = style == 0 ? BulbMaterial : FluffMaterial,
        });
    }

    /// <summary>One <c>C_LocalTempEntity</c>'s state.</summary>
    private sealed class Light
    {
        public Vector3 Origin { get; set; }

        public float Roll { get; init; }

        /// <summary><c>m_nHitboxSet</c>, where the colour index is "smuggled".</summary>
        public int ColourIndex { get; init; }

        public (byte R, byte G, byte B) Rgb { get; init; }

        public byte Alpha { get; set; }

        public float Scale { get; set; }

        public required string Material { get; init; }
    }
}
