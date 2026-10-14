using System;
using System.Collections.Generic;
using System.Globalization;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Render;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>One choice of a menu that offers several: a texture quality, a HUD.</summary>
/// <param name="Key">What the window hands back when it is picked; null is the stock HUD.</param>
/// <param name="Label">The item's text.</param>
/// <param name="Chosen">Whether it is the one in force.</param>
public readonly record struct MenuChoice(string? Key, string Label, bool Chosen);

/// <summary>What the menu's items open showing, from the settings: plain values, so a menu needs no settings type.</summary>
/// <param name="FullScreenExclusive">Whether full screen is the exclusive mode; borderless otherwise.</param>
/// <param name="TextureQualities">The texture quality levels, in menu order.</param>
/// <param name="ShowFrameRate">Whether `cl_showfps` is on.</param>
/// <param name="ShowPosition">Whether `cl_showpos` is on.</param>
/// <param name="Phong">Whether `mat_phong` is on.</param>
/// <param name="Huds">The HUDs the picker offers; empty for no picker.</param>
/// <param name="Volumes">The `volume` levels, in menu order; the nearest is chosen.</param>
/// <param name="MuteLoseFocus">Whether `snd_mute_losefocus` is on.</param>
public sealed record MenuState(
    bool FullScreenExclusive,
    IReadOnlyList<MenuChoice> TextureQualities,
    bool ShowFrameRate,
    bool ShowPosition,
    bool Phong,
    IReadOnlyList<MenuChoice> Huds,
    IReadOnlyList<MenuChoice> Volumes,
    bool MuteLoseFocus);

/// <summary>What choosing a game folder came to.</summary>
/// <param name="Accepted">Whether the folder is a TF2 `tf` folder and is now the setting.</param>
/// <param name="Status">The words for the status line.</param>
public readonly record struct GameFolderChoice(bool Accepted, string Status);

/// <summary>
/// Owns the viewer's settings: reading them, changing one, saving it and saying so (D214) [no engine counterpart: a
/// ConVar there is global state any code reads; here one record is replaced whole and one owner replaces it].
/// </summary>
/// <remarks>
/// **Moved out of the window, which held the record and rewrote it in eight handlers.** The window, its menu, its
/// folder dialog and its device setup now talk to this in plain values — a bool, an int, a string, a key a menu item
/// carries — and name no settings type. Other presenters read <see cref="Current"/>.
/// </remarks>
public sealed class SettingsPresenter
{
    private readonly ILogger _log;
    private readonly ILogger _renderLog;

    /// <summary>Preferences remembered between runs.</summary>
    private ViewerSettings _settings;

    /// <summary>Where a changed setting is saved; null is the settings file, wherever `TF2VIEW_SETTINGS` puts it.</summary>
    private readonly string? _path;

    /// <summary>Reads the settings file. Nothing is applied until something asks.</summary>
    /// <param name="loggers">The viewer's loggers, by area.</param>
    public SettingsPresenter(ILoggerFactory loggers)
        : this(loggers, ViewerSettings.Load())
    {
    }

    /// <summary>Starts from these settings and reads no file: for a test, which must not depend on the machine's.</summary>
    /// <param name="loggers">The viewer's loggers, by area.</param>
    /// <param name="settings">The settings to start from.</param>
    /// <param name="path">Where a changed setting is saved; null is the settings file.</param>
    public SettingsPresenter(ILoggerFactory loggers, ViewerSettings settings, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _path = path;
        ArgumentNullException.ThrowIfNull(loggers);

        _log = loggers.CreateLogger("viewer");
        _renderLog = loggers.CreateLogger("render");
    }

    /// <summary>Shown when a chosen folder is not a TF2 `tf` folder; the folder and the reason follow it.</summary>
    public const string NotAGameFolder = "Not a TF2 folder:";

    /// <summary>Shown when the display refuses the exclusive mode: the settings' own words, for a window that names no settings type.</summary>
    public const string ExclusiveFullScreenRefused = ViewerSettings.ExclusiveFullScreenRefused;

    /// <summary>Shown when a TF2 folder is chosen; the folder follows it.</summary>
    public const string GameFolderSet = "TF2 folder set to ";

    /// <summary>The settings as they are now, for the presenters that read them. Not for a window.</summary>
    public ViewerSettings Current => _settings;

    /// <summary>Reads the command line over the settings: a `+cvar value` applies to this run and is not saved.</summary>
    /// <param name="arguments">The command line.</param>
    /// <returns>What was asked for.</returns>
    public LaunchOptions ReadLaunch(IReadOnlyList<string> arguments)
    {
        LaunchOptions launch = LaunchOptionsReader.Read(arguments, _settings, _log);

        _settings = launch.Settings;

        // Detection reads the live cl_game_folder, so `+cl_game_folder` beats the cfg as in Source (D210).
        SteamInstall.ConfiguredFolder = () => _settings.GameFolder;

        return launch;
    }

    /// <summary>What the log accepts: `developer` as a level.</summary>
    public LogLevel Verbosity => _settings.Verbosity;

    /// <summary>`developer`.</summary>
    public int Developer => _settings.Developer;

    /// <summary>`cl_screenshot_folder`, or null for the default.</summary>
    public string? ScreenshotFolder => _settings.ScreenshotFolder;

    /// <summary>Whether full screen is the exclusive mode (`mat_fullscreen_mode 1`).</summary>
    public bool FullScreenExclusive => _settings.FullScreenMode == FullScreenMode.Exclusive;

    /// <summary>The texture quality in force, by name: `Full`, `High`, `Medium`, `Low`.</summary>
    public string TextureQuality => _settings.TextureQuality.ToString();

    /// <summary>`fps_max`; zero is no limit.</summary>
    public int FrameRateLimit => _settings.FrameRateLimit;

    /// <summary>`engine_no_focus_sleep`, in milliseconds.</summary>
    public int NoFocusSleep => _settings.NoFocusSleep;

    /// <summary>`cl_game_folder`, or null when none is set.</summary>
    public string? GameFolder => _settings.GameFolder;

    /// <summary>The watcher's own value for a ConVar this viewer keeps no setting for, or null where unset.</summary>
    /// <param name="name">The ConVar's name.</param>
    public string? ConVar(string name) => _settings.ConVars.TryGetValue(name, out string? value) ? value : null;

    /// <summary>What the menu's items open showing.</summary>
    /// <param name="customRoot">The program's own `custom` folder, whose HUDs the picker lists (D193), or null.</param>
    public MenuState Menu(string? customRoot)
    {
        List<MenuChoice> qualities = [];

        foreach (TextureQuality quality in new[]
        {
            Scene.TextureQuality.Full, Scene.TextureQuality.High, Scene.TextureQuality.Medium, Scene.TextureQuality.Low,
        })
        {
            int pixels = (int)quality;

            qualities.Add(new MenuChoice(
                quality.ToString(),
                pixels == 0
                    ? "&Full"
                    : string.Create(CultureInfo.InvariantCulture, $"{quality} ({pixels} px)"),
                _settings.TextureQuality == quality));
        }

        List<MenuChoice> huds = [];

        foreach (HudOption option in HudCatalog.Options(customRoot))
        {
            huds.Add(new MenuChoice(
                option.Path, option.Name, string.Equals(_settings.ChosenHud, option.Path, StringComparison.Ordinal)));
        }

        int nearest = (int)MathF.Round(_settings.Volume * 10f) * 10;
        List<MenuChoice> volumes = [];

        foreach (int percent in VolumeSteps)
        {
            string key = percent.ToString(CultureInfo.InvariantCulture);
            volumes.Add(new MenuChoice(key, key + "%", percent == nearest));
        }

        return new MenuState(
            FullScreenExclusive,
            qualities,
            _settings.ShowFrameRate != 0,
            _settings.ShowPosition != 0,
            _settings.Phong,
            huds,
            volumes,
            _settings.MuteLoseFocus);
    }

    /// <summary>Chooses how full screen is entered, and remembers it.</summary>
    /// <param name="exclusive">The exclusive mode; borderless otherwise.</param>
    /// <param name="switchDevice">
    /// Switches the device now, answering whether it could — given only while the window is already full screen, so the
    /// choice can be judged by making it rather than by restarting.
    /// </param>
    /// <returns>What to say on the status line, or null for nothing.</returns>
    public string? SetFullScreenExclusive(bool exclusive, Func<bool, bool>? switchDevice)
    {
        _settings = _settings with { FullScreenMode = exclusive ? FullScreenMode.Exclusive : FullScreenMode.Borderless };

        string? status = null;

        if (switchDevice is not null)
        {
            bool wanted = exclusive;

            if (!switchDevice(wanted) && wanted)
            {
                status = ViewerSettings.ExclusiveFullScreenRefused;
            }
        }

        string? failure = _settings.Save(_path);

        if (failure is not null)
        {
            // Reported rather than swallowed: a preference that silently does not stick is worse
            // than one that says so.
            status = ViewerSettings.SavedForThisSessionOnly(failure);
        }

        return status;
    }

    /// <summary>Chooses how much texture detail to load, and remembers it.</summary>
    /// <param name="key">The level's name, as <see cref="Menu"/> gave it.</param>
    /// <returns>What to say on the status line.</returns>
    /// <remarks>
    /// The map is not reloaded here. Textures are decoded when a map is opened, so the change
    /// applies to the next one — which is honest about what it costs rather than pretending a
    /// setting is free.
    /// </remarks>
    public string SetTextureQuality(string key)
    {
        TextureQuality quality = Enum.Parse<TextureQuality>(key);

        _settings = _settings with { TextureQuality = quality };

        string? failure = _settings.Save(_path);

        return failure is null
            ? "Texture quality: " + quality + ". Applies to the next map opened."
            : ViewerSettings.SavedForThisSessionOnly(failure);
    }

    /// <summary>Shows or hides Valve's frame rate meter.</summary>
    /// <param name="on">Whether to draw it.</param>
    /// <remarks>
    /// **Two, not one**, because `cl_showfps 2` is the form that names the worst and best frame as
    /// well as the average, and the smoothed single number is the one this project has repeatedly
    /// found hides a stall.
    /// </remarks>
    public void SetFrameRateMeter(bool on)
    {
        _settings = _settings with { ShowFrameRate = on ? 2 : 0 };

        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        _renderLog.LogInformation(
            "{Message}",
            string.Create(CultureInfo.InvariantCulture, $"cl_showfps {_settings.ShowFrameRate}"));
    }

    /// <summary>Shows or hides Valve's position readout.</summary>
    /// <param name="on">Whether to draw it.</param>
    /// <remarks>
    /// **One, not two.** `cl_showpos 1` reports the VIEW's own origin and angles, which is the
    /// question a screenshot is taken to answer — where the camera is. Mode 2 reports the watched
    /// player instead, and for a free-camera shot of a piece of geometry that says nothing about
    /// what is being looked at. Someone who wants the player can still write `cl_showpos 2` in a
    /// config; the menu offers the useful one.
    /// </remarks>
    public void SetPositionReadout(bool on)
    {
        _settings = _settings with { ShowPosition = on ? PositionReadout.View : 0 };

        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        _renderLog.LogInformation(
            "{Message}",
            string.Create(CultureInfo.InvariantCulture, $"cl_showpos {_settings.ShowPosition}"));
    }

    /// <summary>The gain over the whole mix: `volume`, or silence while unfocused under `snd_mute_losefocus`.</summary>
    /// <param name="focused">Whether the window has focus.</param>
    public float MasterGain(bool focused) => _settings.MuteLoseFocus && !focused ? 0f : _settings.Volume;

    /// <summary>The Volume menu's levels, a tenth apart as TF2's Audio slider is read.</summary>
    private static readonly int[] VolumeSteps = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100];

    /// <summary>Sets `volume` from the menu, and remembers it — `volume` is `archive`, so TF2 keeps it too.</summary>
    /// <param name="key">The level's key, as <see cref="Menu"/> gave it: the percentage.</param>
    /// <returns>What to say on the status line, or null for nothing.</returns>
    public string? SetVolume(string key) =>
        Remember(_settings.Set(ViewerSettings.VolumeCommand, (int.Parse(key, CultureInfo.InvariantCulture) / 100f).ToString(CultureInfo.InvariantCulture)));

    /// <summary>Sets `snd_mute_losefocus` from the menu, and remembers it.</summary>
    /// <param name="on">Whether to go silent while another window has focus.</param>
    /// <returns>What to say on the status line, or null for nothing.</returns>
    public string? SetMuteLoseFocus(bool on) => Remember(_settings with { MuteLoseFocus = on });

    private string? Remember(ViewerSettings settings)
    {
        _settings = settings;

        return _settings.Save(_path) is { } failure ? ViewerSettings.SavedForThisSessionOnly(failure) : null;
    }

    /// <summary>Applies a ConVar a command set at run time, by the launch option's route; saves nothing (D215).</summary>
    /// <param name="name">The ConVar's name.</param>
    /// <param name="value">Its value, as the command wrote it.</param>
    public void SetConVar(string name, string value) => _settings = _settings.Set(name, value);

    /// <summary>Makes these settings the console's ConVar values: it reads them, and a key's set lands in them (D215).</summary>
    /// <param name="console">The console; attach after the watcher's configs load, which keep their own lines.</param>
    public void Attach(ConfigConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        console.ReadSettingsFrom(name => _settings.ConVarText(name));
        console.ConVarSet += (_, set) => SetConVar(set.Name, set.Value);
    }

    /// <summary>Remembers the HUD the picker chose.</summary>
    /// <param name="hud">The HUD's folder or archive, or null for TF2's stock one.</param>
    /// <returns>What to say on the status line, or null for nothing.</returns>
    public string? SetChosenHud(string? hud)
    {
        _settings = _settings with { ChosenHud = hud };

        string? failure = _settings.Save(_path);

        return failure is not null ? ViewerSettings.SavedForThisSessionOnly(failure) : null;
    }

    /// <summary>Whether a missing install is worth a dialog: not when one was found, declined before, or nobody is there.</summary>
    /// <param name="found">The install that was found, or null.</param>
    /// <param name="headless">Whether this run is a capture or a measurement.</param>
    public bool ShouldAskForGameFolder(string? found, bool headless) => _settings.ShouldAskForGameFolder(found, headless);

    /// <summary>The startup dialog was cancelled: do not ask again.</summary>
    /// <returns>What to say on the status line.</returns>
    public string DeclineGameFolder()
    {
        _settings = _settings with { AskForGameFolder = false };

        return _settings.Save(_path) is { } declineFailure
            ? ViewerSettings.SavedForThisSessionOnly(declineFailure)
            : "No TF2 folder chosen. File > TF2 folder sets it any time.";
    }

    /// <summary>A folder was picked in the dialog: accept it if it is a TF2 `tf` folder, remember it, and say what happened.</summary>
    /// <param name="selected">The folder picked.</param>
    /// <param name="installOpen">Whether an install is already open, so the change waits for the next start.</param>
    /// <returns>Whether it was accepted, and the status line's words. Both outcomes are logged here.</returns>
    public GameFolderChoice ChooseGameFolder(string selected, bool installOpen)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (SteamInstall.AsGameFolder(selected) is not { } game)
        {
            string refused = $"{NotAGameFolder} {selected} holds no {SteamInstall.Recogniser}. "
                + "Choose the tf folder inside Team Fortress 2.";
            _log.LogWarning("{Message}", refused);

            return new GameFolderChoice(false, refused);
        }

        _settings = _settings with { GameFolder = game };

        string outcome = !installOpen
            ? $"{GameFolderSet}{game}."
            : $"{GameFolderSet}{game}. It takes effect the next time the viewer starts.";

        if (_settings.Save(_path) is { } failure)
        {
            outcome = $"TF2 folder could not be saved: {failure}";
        }

        _log.LogInformation("{Message}", outcome);

        return new GameFolderChoice(true, outcome);
    }

    /// <summary>Whether `mat_specular` is on.</summary>
    public bool Specular => _settings.Specular;

    /// <summary>`mat_fullbright`: 0, 1 or 2.</summary>
    public int Fullbright => _settings.Fullbright;

    /// <summary>The log's line about the frame rate limit and vertical sync, said once the device exists.</summary>
    public string FrameRateLine =>
        $"frame rate limit {(_settings.FrameRateLimit > 0 ? _settings.FrameRateLimit + " a second" : "none")}, " +
        $"vertical sync {(_settings.VerticalSync ? "on" : "off")}";

    /// <summary>Hands a new device every setting it takes, in the order the window did.</summary>
    /// <param name="device">The device, just created.</param>
    /// <param name="specularOff">Unticks the menu's Reflections item, which is what switches `mat_specular` off.</param>
    /// <param name="setFullbright">Sets `mat_fullbright` through the menu's own method, so the check marks follow.</param>
    public void ApplyTo(Device3D device, Action specularOff, Action<int> setFullbright) =>
        ApplyOver(device, specularOff, setFullbright);

    /// <summary><see cref="ApplyTo"/> over a device a test can read.</summary>
    internal void ApplyOver(IViewerDevice device, Action specularOff, Action<int> setFullbright)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(specularOff);
        ArgumentNullException.ThrowIfNull(setFullbright);

        device.VerticalSync = _settings.VerticalSync;

        // **`mat_phong` from the config, applied where `mat_vsync` is and for the same reason.**
        // `SetRenderToggle` no-ops while there is no device, so a config read before this point
        // reaches nothing — and `Device3D._phong` defaults to ON, so `mat_phong 0` would be
        // silently dropped rather than visibly failing. That is the null-object shape recorded
        // in `docs/memory/logs-are-the-debugger.md#a-null-object-default-hides-a-missed-wiring`.
        device.Phong = _settings.Phong;

        // `cl_drawmonitors` from the config, for the same reason (B511).
        device.DrawMonitors = _settings.DrawMonitors;

        // The golden comparison's three switches, applied where the others are (B161).
        device.DisableBloom = _settings.DisableBloom;
        device.ForceToneMapScale = _settings.ForceToneMapScale;
        device.DrawHud = _settings.DrawHud;

        // **`mat_specular` and `mat_fullbright` from the config or the command line** (B521). Both were menu items
        // only: a `+mat_specular 0` was logged as set by the launch options and reached nothing, so a picture taken
        // to switch reflections off was byte-identical to its control. Through the menu's own item and method, so
        // the check marks follow: unchecking Reflections is what calls `SetSpecular`.
        if (!_settings.Specular)
        {
            specularOff();
        }

        if (_settings.Fullbright != 0)
        {
            setFullbright(_settings.Fullbright);
        }

        // **`r_3dsky`, applied here for exactly the reason `mat_phong` is.** The device
        // defaults it to Valve's 1, so a config saying `r_3dsky 0` would be silently dropped —
        // the null-object shape in `docs/memory/logs-are-the-debugger.md#a-null-object-default-hides-a-missed-wiring`,
        // where the default and the ignored setting agree and nothing looks wrong.
        device.Draw3dSky = _settings.ThreeDimensionalSky;

        // **`cl_detaildist` and `cl_detailfade`, applied for the reason `r_3dsky` above is.**
        // The device defaults them to Valve's 1200 and 400, so a config saying
        // `cl_detaildist 0` — which `tf/cfg/low.cfg` ships, and which means draw no grass at
        // all — would be silently dropped: the default and the ignored setting would agree
        // everywhere except on the one machine that set it.
        device.DetailDistance = _settings.DetailDistance;
        device.DetailFadeWidth = _settings.DetailFade;
    }
}
