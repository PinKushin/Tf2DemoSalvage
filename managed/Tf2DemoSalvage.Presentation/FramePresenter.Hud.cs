using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Render;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What the window knows about the overlay it is asked for: the demo, the map, the size, three settings.</summary>
/// <param name="Timeline">The loaded demo, or null.</param>
/// <param name="Loaded">The loaded map, or null.</param>
/// <param name="MapName">The demo's map name, or null.</param>
/// <param name="Tick">The transport's whole tick.</param>
/// <param name="ViewportWidth">The viewport's width in pixels.</param>
/// <param name="ViewportHeight">The viewport's height in pixels.</param>
/// <param name="Crosshair">The watcher's crosshair settings.</param>
/// <param name="ShowFrameRate">`cl_showfps`.</param>
/// <param name="ScoresHeld">Whether `+showscores` is held.</param>
/// <param name="LastFrameSeconds">How long the last frame took.</param>
public readonly record struct OverlayFrame(
    DemoTimeline? Timeline,
    LoadedMap? Loaded,
    string? MapName,
    int Tick,
    int ViewportWidth,
    int ViewportHeight,
    CrosshairSettings Crosshair,
    int ShowFrameRate,
    bool ScoresHeld,
    double LastFrameSeconds);

/// <summary>What the HUD asks of the window while it builds: each is read at the moment the HUD needs it, as it was.</summary>
/// <param name="Gdi">GDI, as VGUI's fonts reach it.</param>
/// <param name="Game">The opened install, or null.</param>
/// <param name="MapName">The open demo's map name, or null.</param>
/// <param name="At">What the window knows that the HUD reads at a tick, its projection, and the frame.</param>
/// <param name="Position">`cl_showpos`' subject.</param>
/// <param name="RealSeconds">Seconds since the window opened, for VGUI's 250 ms ticks.</param>
/// <param name="Eye">The first-person camera.</param>
public sealed record HudWindow(
    IVguiGdi Gdi,
    Func<GameContent?> Game,
    Func<string?> MapName,
    Func<int, (HudView View, float[] WorldToScreen, ViewFrame Frame)> At,
    Func<PositionReadout> Position,
    Func<double> RealSeconds,
    Func<FreeCamera?> Eye);

/// <summary>The frame's VGUI: the client's HUD and the tools panel over it (D214, thin view slice 5).</summary>
/// <remarks>
/// **Moved out of `MainForm` whole**, where it was `BuildOverlay`, `SkipHudFrame`, `HudArchives`, `ResolveVguiMaterial`
/// and `PlayHudSound` with the surface host, the HUD, the tools panel and the HUD's scripts, hooks and event feed: the
/// same construction on first use, the same order every frame. In this presenter because the HUD and the frame need
/// each other — a seek runs HUD frames, and the HUD asks for the target ID and plays through the skip — and one owner
/// of both needs no callbacks between two.
/// </remarks>
public sealed partial class FramePresenter
{
    /// <summary>What the HUD asks of the window; null until the window attaches it.</summary>
    private HudWindow? _window;

    /// <summary>The overlay frame being built, for the methods <see cref="BuildOverlay"/> calls.</summary>
    private OverlayFrame _overlay;

    /// <summary>
    /// The HUD the user chose — a folder or `.vpk` — or null for TF2's stock HUD (D193). The HUD's files are remade the
    /// next time they are asked for, which is every frame — no restart needed.
    /// </summary>
    public string? ChosenHud { get; set; }

    /// <summary>Whether the HUD exists yet, for a seek that would run its skip frames.</summary>
    public bool HasHud => _vguiHud is not null && _vguiHost is not null;

    /// <summary>Hands over what the HUD asks of the window. Once, when the window is constructed.</summary>
    /// <param name="window">What the HUD asks.</param>
    public void AttachHud(HudWindow window) => _window = window;

    private GameContent? _game => _window?.Game();

    private HudState HudStateAt(int hudTick)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_window is not { } window)
        {
            return default;
        }

        (HudView view, float[] worldToScreen, ViewFrame frame) = window.At(hudTick);

        return HudStateAt(view with { Scripts = _hudScripts, Hooks = _hudHooks }, worldToScreen, frame, window.Eye);
    }

    /// <summary>The class model panel's model set last handed to <see cref="EntityModelSet.Precache"/>.</summary>
    private string? _panelModelsPrecached;

    /// <summary>The engine's tools panel and `CFPSPanel` on it, drawn through VGUI; null until the game's files are open.</summary>
    /// <remarks>
    /// **Its scheme is `platform/resource/SourceScheme.res`, read live from the install**, which D85's table allows:
    /// that file is Valve-shipped game content, not user content under `tf/custom/`.
    /// </remarks>
    private VguiTools? _vguiTools;

    /// <summary>The surface, font manager and strings every VGUI root shares; null until the game's files are open.</summary>
    private VguiSurfaceHost? _vguiHost;

    /// <summary>The client's HUD viewport under `ClientScheme.res`; null until the game's files are open.</summary>
    private VguiHud? _vguiHud;

    // The HUD's stock view of the install, and the install it was made from.
    private GameArchives? _hudArchives;
    private GameContent? _hudArchivesOf;
    private string? _hudArchivesFor;

    // Which game events the HUD has heard, so each fires once as playback crosses it.
    private readonly HudEventFeed _hudEvents = new();

    /// <summary>The weapon and class scripts the HUD's ammo reads; null until the game's files are open.</summary>
    private TfWeaponData? _hudScripts;

    /// <summary>`CALL_ATTRIB_HOOK_*` over `items_game.txt`; null until the schema is loaded.</summary>
    private AttributeHooks? _hudHooks;

    /// <summary>The device the VGUI resolver was last handed to.</summary>
    private Device3D? _vguiResolverDevice;

    /// <summary>VGUI textures decoded at HUD load, held for the renderer's first draw (B595 part 6).</summary>
    private VguiMaterialLoads<MapTexture>? _vguiLoads;

    /// <summary>Whether the HUD is handling a seek's replayed events, which <see cref="PlayHudSound"/> keeps silent.</summary>
    private bool _hudReplaying;

    /// <summary>One skip frame of the HUD: its batch's game events and user messages, heard and not painted.</summary>
    private void SkipHudFrame(DemoTimeline timeline, int through)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_vguiHud is not { } hud)
        {
            return;
        }

        (bool reset, IReadOnlyList<Core.Scene.SceneGameEvent> crossed, IReadOnlyList<Core.Scene.SceneUserMessage> messages) = _hudEvents.Advance(timeline, through);

        hud.Frame(
            HudStateAt(through),
            HudEventFeed.Resolve(timeline, crossed, _window?.MapName() ?? string.Empty, _hudHooks),
            reset,
            messages,
            paint: false);
    }

    /// <summary>The files the HUD reads: the chosen HUD over the stock files, remade when the install or the choice changes.</summary>
    private GameArchives? HudArchives()
    {
        if (_game is null)
        {
            return null;
        }

        if (!ReferenceEquals(_hudArchivesOf, _game) || !string.Equals(_hudArchivesFor, ChosenHud, StringComparison.Ordinal))
        {
            _hudArchives = _game.Archives.WithHud(ChosenHud);
            _hudArchivesOf = _game;
            _hudArchivesFor = ChosenHud;
        }

        return _hudArchives;
    }

    /// <summary>This frame's VGUI: the client's HUD, then the tools panel and `CFPSPanel` on it above.</summary>
    /// <returns>The draw list.</returns>
    /// <remarks>
    /// Everything but the window size, the clock and the settings is <see cref="VguiHud"/> and <see cref="VguiTools"/>
    /// (D90). The meter is sampled every frame, drawn or not, because the frame-rate log reports it in exactly the
    /// headless case nobody watches. The HUD's state is `default` — not in game, so every element hidden — until the
    /// first element that reads the local player lands.
    /// </remarks>
    /// <param name="frame">What the window knows about the overlay.</param>
    /// <param name="device">The device, or null before there is one.</param>
    public VguiDrawList? BuildOverlay(in OverlayFrame frame, Device3D? device)
    {
        _overlay = frame;
        long partAt = Parts is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();

        // **How long a paint forked at the last overlay's end would have had, and whether it would have stalled** (finding
        // 105 B7): until the next writer of what it reads — this frame's `Pack`, or, on a frame with no rebuild, this
        // overlay itself. The two kinds are kept apart, since a frame with no `Pack` has a far longer window, and the stall
        // is taken per frame against that frame's own forkable span. Spans other phases: never added into `hud`.
        if (Parts is { } windowParts && _overlayEndedAt != 0)
        {
            bool packed = _moment.PackedAt > _overlayEndedAt;
            long forkWindow = (packed ? _moment.PackedAt : partAt) - _overlayEndedAt;

            windowParts.Add(packed ? "hud window packed" : "hud window unpacked", forkWindow);

            if (packed)
            {
                windowParts.Count("hud packed frames");
            }

            if (_forkableLast > forkWindow)
            {
                windowParts.Add("hud stall", _forkableLast - forkWindow);
                windowParts.Count("hud stalled frames");
            }
        }

        // A scheme read before the install is open is empty, and an empty scheme's `Panel.BgColor` falls back to opaque
        // white — the viewport would fill the screen with it and keep it, since only a new size loads the scheme again.
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_window is not { } window || window.Game() is not { } game)
        {
            return null;
        }

        // TF2's stock HUD by default (D193): a HUD in `custom/` is the user's to choose, not one the viewer picks up.
        // One decode per sheet: the load-time size query keeps the texture and the renderer's first draw takes it (B595 part 6).
        _vguiLoads ??= new VguiMaterialLoads<MapTexture>(ResolveVguiMaterial, HudArchives);
        _vguiHost ??= new VguiSurfaceHost(
            path => HudArchives()?.Read(path),
            path => HudArchives()?.FullPathOnDisk(path),
            window.Gdi,
            new VguiTextureSizes(
                material => _vguiLoads.Load(material) is { } texture ? (texture.MappingWidth, texture.MappingHeight) : (0, 0),
                HudArchives).Size);
        _vguiHud ??= new VguiHud(_vguiHost, _models);
        _vguiHud.Parts = Parts;
        _vguiTools ??= new VguiTools(_vguiHost);
        _vguiTools.Parts = Parts;

        if (device is not null && !ReferenceEquals(device, _vguiResolverDevice))
        {
            device.SetVguiResolver(_vguiLoads.Take);
            _vguiResolverDevice = device;
        }

        _vguiHost.BeginFrame(_overlay.ViewportWidth, _overlay.ViewportHeight);
        _hudScripts ??= new TfWeaponData(path => HudArchives()?.Read(path));

        if (_hudHooks is null && game.Weapons.Items is { } items)
        {
            _hudHooks = new AttributeHooks(items);

            // `CEconItemView::GetItemName`, under the HUD's own localisation.
            _vguiHud.Viewport.ItemName = (definition, quality) => definition is { } index
                ? TfItemName.Generate(items, index, quality, token => _vguiHud.Viewport.Context?.Localize?.Invoke(token))
                : null;
            _vguiHud.Viewport.PlayerAttribute = _hudHooks.OnPlayer;
            _vguiHud.Viewport.WeaponAttribute = _hudHooks.OnWeapon;
            _vguiHud.Viewport.Items = items;
        }

        partAt = Part("hud setup", partAt);
        int hudTick = _overlay.Tick;
        IReadOnlyList<HudGameEvent>? hudEvents = null;
        IReadOnlyList<Core.Scene.SceneUserMessage>? hudMessages = null;
        bool hudReset = false;

        if (_overlay.Timeline is { } hudTimeline)
        {
            (hudReset, IReadOnlyList<Core.Scene.SceneGameEvent> crossed, hudMessages) = _hudEvents.Advance(hudTimeline, hudTick);
            hudEvents = HudEventFeed.Resolve(hudTimeline, crossed, _overlay.MapName ?? string.Empty, _hudHooks);
        }

        partAt = Part("hud events", partAt);
        _vguiHud.Viewport.Scripts = _hudScripts;
        _vguiHud.Viewport.ClassModels = game.Classes;
        _vguiHud.Viewport.ParticleSystems = _overlay.Loaded?.Assets?.ParticleSystemsByName;
        _vguiHud.Viewport.ParticleMaterials = _overlay.Loaded?.Assets?.ParticleMaterials;

        // What the class model panel will draw, checked every frame so a model first needed mid-demo reaches the GPU
        // (`MomentScene.Pack` uploads whatever `Grown` says was added). Only a changed set is passed: `Precache` re-reads
        // a model that failed to load, which must not happen sixty times a second.
        string panelModels = string.Join('\n', _vguiHud.ModelsToPrecache());

        if (!string.Equals(panelModels, _panelModelsPrecached, StringComparison.Ordinal))
        {
            _panelModelsPrecached = panelModels;
            _models.Precache(panelModels.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }
        _vguiHud.Crosshair.Settings = _overlay.Crosshair;
        _vguiHud.DeathNotice.SoundEmitter ??= PlayHudSound;
        _vguiHud.Chat.SoundEmitter ??= PlayHudSound;
        _vguiHud.MatchStatus.SoundEmitter ??= PlayHudSound;
        _vguiHud.ItemEffectMeters.SoundEmitter ??= PlayHudSound;

        partAt = Part("hud precache", partAt);
        HudState hudState = HudStateAt(hudTick);
        partAt = Part("hud state", partAt);

        _hudReplaying = hudReset;

        try
        {
            _vguiHud.Frame(
                hudState,
                hudEvents,
                hudReset,
                hudMessages,
                _overlay.ScoresHeld);
        }
        finally
        {
            _hudReplaying = false;
        }

        partAt = Part("hud frame", partAt);
        _vguiTools.Frame(
            window.RealSeconds(),
            _overlay.LastFrameSeconds,
            _overlay.ShowFrameRate,
            window.Position(),
            _overlay.MapName);

        _overlayEndedAt = Part("hud tools", partAt);
        _forkableLast = _vguiHud.ForkableTicks + _vguiTools.PaintTicks;
        Parts?.Add("hud forkable", _forkableLast);

        return _vguiHost.List;
    }

    /// <summary>When the last overlay finished, for the `hud window` parts (finding 105 B7); 0 when not measuring.</summary>
    private long _overlayEndedAt;

    /// <summary>The last overlay's forkable span — HUD layout to paint end, plus the tools' paint — carried (finding 105 B7).</summary>
    private long _forkableLast;

    /// <summary>The parts of the frame's `camera` and `hud` phases for the rate line — set only for `--measure`, else null.</summary>
    public FrameParts? Parts { get; set; }

    /// <summary>Adds the time since <paramref name="since"/> to a part and returns now, to time the next; nothing unmeasured.</summary>
    internal long Part(string name, long since) => FrameParts.Lap(Parts, name, since);

    /// <summary>A VGUI material's texture, from the install — `DrawSetTextureFile`.</summary>
    private MapTexture? ResolveVguiMaterial(string material) =>
        HudArchives() is { } archives
            ? MapAssets.ResolveVguiMaterial(_renderLog, material, PakFile.Read(ReadOnlyMemory<byte>.Empty), archives, MaximumVguiTextureSize)
            : null;

    /// <summary>The decode cap for VGUI textures, which are small and never want reducing.</summary>
    private const int MaximumVguiTextureSize = 4096;

    /// <summary>
    /// Wired into the HUD as its <c>HudSoundEmitter</c>: resolves a `game_sounds.txt` name the same way a world sound
    /// does (<see cref="HudSounds"/>) and hands it to the presenter, at the current tick, from the local player.
    /// </summary>
    /// <param name="scriptName">The script the HUD element asked for.</param>
    /// <remarks>
    /// **Silent while a seek replays the events before it.** They fired before the tick the viewer landed on, so the
    /// sounds they ask for were over before then — the notices are rebuilt, and a burst of every kill in the window is not.
    /// </remarks>
    private void PlayHudSound(string scriptName) => HudSound(_overlay.Tick, scriptName, _hudReplaying);
}
