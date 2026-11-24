using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Tests.Bsp;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Render;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The viewer without its window (D214, slice 9) [no engine counterpart: the idle loop, the capture countdown, the
/// measurement and the key dispatch are this viewer's], over a window with no controls, a transport with no slider, a
/// device that draws nothing and a process that is two strings and a number.
/// </summary>
/// <remarks>
/// **Every test here was impossible while the code was in the form**: reaching any of it needed a window, a message
/// pump and, for most, a GPU. The demos are written by the test (D213) and the map is the one empty BSP
/// <see cref="ViewerPresenterTests"/> uses.
/// </remarks>
[NonParallelizable]
public sealed class ViewerSessionTests
{
    private const string Map = "ctf_written_by_a_test";

    private string _folder = string.Empty;

    /// <summary>How many times a session asked whether the tree is dirty; the rig's tree always is.</summary>
    private int _dirtyReads;

    /// <summary>The configuration the rig's process claims; a test sets it before <c>Build</c>.</summary>
    private string _hostBuild = "Debug";

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "tf2salvage-tests", Path.GetRandomFileName());
        _gameFolder = null;
        _environmentAtStart = [];
        _dirtyReads = 0;
        _hostBuild = "Debug";
        Directory.CreateDirectory(Path.Combine(_folder, "maps"));
        File.WriteAllBytes(Path.Combine(_folder, "maps", Map + ".bsp"), SyntheticBsp.Build(new Dictionary<int, byte[]>()));
    }

    [TearDown]
    public void RemoveFolder()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException held)
        {
            // A disposable temp folder: a file still held open is reported and left, and fails no test.
            TestContext.Out.WriteLine($"left {_folder} behind: {held.Message}");
        }
    }

    private string WriteDemo(string name, int ticks = 12345)
    {
        DemoHeader header = new()
        {
            DemoProtocol = 3,
            NetworkProtocol = 24,
            ServerName = "test server",
            ClientName = "tester",
            MapName = Map,
            GameDirectory = "tf",
            PlaybackTimeSeconds = ticks * 0.015f,
            PlaybackTicks = ticks,
            PlaybackFrames = ticks,
            SignonLengthBytes = 0,
        };

        string path = Path.Combine(_folder, name + ".dem");
        File.WriteAllBytes(path, DemoWriter.Write(header, [new DemoCommand(DemoCommandType.Stop, 0, default)]));

        return path;
    }

    /// <summary>A session and everything it talks to.</summary>
    private sealed class Rig : IDisposable
    {
        public required ViewerSession Session { get; init; }

        public required FakeViewerWindow Window { get; init; }

        public required FakePlaybackView Transport { get; init; }

        public required RecordingLoggers Log { get; init; }

        public required StringWriter Out { get; init; }

        public required StringWriter Error { get; init; }

        public required SettingsPresenter Settings { get; init; }

        public List<int> ExitCodes { get; } = [];

        public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal);

        public FakeRenderDevice Device { get; } = new();

        /// <summary>Gives the session the stand-in device, as a created one would be.</summary>
        public Rig WithDevice()
        {
            Session.OpenDevice(() => Device, NoAttach, 1280, 720).ShouldBeTrue();

            return this;
        }

        public void Dispose()
        {
            Session.Dispose();
            Out.Dispose();
            Error.Dispose();
            Log.Dispose();
        }
    }

    private Rig Build(params string[] arguments) => Build(new ViewerSettings { FrameRateLimit = 0 }, 0.5, arguments);

    private Rig Build(ViewerSettings settings, double frameSeconds = 0.5, params string[] arguments)
    {
        FakeViewerWindow window = new();
        FakePlaybackView transport = new();
        RecordingLoggers log = new();
        StringWriter output = new();
        StringWriter error = new();
        List<int> exits = [];
        Dictionary<string, string> environment = new(_environmentAtStart, StringComparer.Ordinal);
#pragma warning disable CA2000 // The session disposes the provider; the rig disposes the session.
        MapProvider maps = new(
            Path.Combine(_folder, "no-steam-library.vdf"),
            Path.Combine(_folder, "maps"),
            static () => throw new InvalidOperationException("no test here downloads a map"),
            gameFolder: () => _gameFolder);
#pragma warning restore CA2000
        SettingsPresenter owner = new(log, settings, Path.Combine(_folder, "settings.cfg"));

        ViewerSession session = new(
            window,
            transport,
            log,
            arguments,
            new FpsPanelTests.SolidGdi(),
            new ViewerHost(output, error, exits.Add, name => environment.GetValueOrDefault(name), () => new FixedTime(frameSeconds))
            {
                Build = _hostBuild,
                TreeDirty = () =>
                {
                    _dirtyReads++;

                    return true;
                },
            },
            maps,
            owner);

        window.SurfaceColoursTicked = session.SetSurfaceColours;

        Rig rig = new()
        {
            Session = session, Window = window, Transport = transport, Log = log, Out = output, Error = error, Settings = owner,
        };

        // The rig's lists are the ones the host writes to, so they are shared rather than copied.
        exits.Clear();
        _exits = exits;
        _environment = environment;

        return rig;
    }

    private List<int> _exits = [];

    /// <summary>The TF2 folder the session's configs are read from; null is no install.</summary>
    private string? _gameFolder;

    /// <summary>A session whose TF2 install carries this autoexec.cfg.</summary>
    private Rig BuildWithAutoexec(string autoexec, params string[] arguments)
    {
        _gameFolder = Path.Combine(_folder, "tf");
        Directory.CreateDirectory(Path.Combine(_gameFolder, "cfg"));
        File.WriteAllText(Path.Combine(_gameFolder, "cfg", "autoexec.cfg"), autoexec);

        return Build(arguments);
    }

    private Dictionary<string, string> _environment = [];

    /// <summary>What the session finds in its environment as it is built, for what it reads only then.</summary>
    private Dictionary<string, string> _environmentAtStart = [];

    private static KeyPress Key(
        string name,
        Func<string?, bool>? chord = null,
        FocusedWidget focus = FocusedWidget.None,
        bool escape = false,
        bool controlOrAlt = false,
        bool repeat = false) =>
        new(
            name,
            controlOrAlt,
            focus,
            chord ?? (binding => string.Equals(binding, name, StringComparison.OrdinalIgnoreCase)),
            escape,
            name,
            repeat);

    // ---- the render switches ----

    [Test]
    public void SetWireframe_OnThenOff_LogsTheCvarTellsTheDeviceAndAsksForARepaintEachTime()
    {
        using Rig rig = Build().WithDevice();
        int redraws = rig.Window.Count("Redraw");

        rig.Session.SetWireframe(true);
        rig.Device.Wireframe.ShouldBe(1);
        rig.Session.SetWireframe(false);

        rig.Device.Wireframe.ShouldBe(0);
        // The settings record each for this run too (B791), on their own log line.
        rig.Log.Lines.Where(static line => line.StartsWith("[render] mat_wireframe", StringComparison.Ordinal))
            .ShouldBe(["[render] mat_wireframe 1", "[render] mat_wireframe 0"]);
        rig.Log.Count("mat_wireframe 0 (set at run time)").ShouldBe(1);
        (rig.Window.Count("Redraw") - redraws).ShouldBe(2);
    }

    /// <remarks>
    /// B720: a `mat_phong` change reloads every model in TF2 (`materialsystem.dll` `FUN_180018fd0` →
    /// `MATERIAL_RESTORE_VERTEX_FORMAT_CHANGED` → `datacache.dll` `FUN_18000fbe0`), so the models take it as well as the device.
    /// </remarks>
    [Test]
    public void SetPhong_Off_ReachesTheModels()
    {
        using Rig rig = Build().WithDevice();
        bool before = rig.Session.MaterialSwitches.Phong;

        rig.Session.SetPhong(false);

        (before, rig.Session.MaterialSwitches.Phong).ShouldBe((true, false));
    }

    /// <remarks>
    /// B720, D69: a bound key's `mat_phong 0` or `mat_bumpmap 0` takes the menu's route, because in TF2 the ConVar is the
    /// setting and its change reloads every model whoever set it (`materialsystem.dll` `FUN_180018fd0`). The console and an
    /// exec'd cfg announce their sets through the same `ConVarSet` as a key.
    /// </remarks>
    [Test]
    public void KeyPressed_AKeyBoundToEachMaterialSwitch_ReachesTheModels()
    {
        using Rig rig = BuildWithAutoexec("bind \"F7\" \"mat_phong 0\"\nbind \"F8\" \"mat_bumpmap 0\"\n").WithDevice();
        Scene.MaterialSwitches before = rig.Session.MaterialSwitches;

        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue();
        Scene.MaterialSwitches afterPhong = rig.Session.MaterialSwitches;
        rig.Session.KeyPressed(Key("F8", static _ => false)).ShouldBeTrue();

        (before, afterPhong, rig.Session.MaterialSwitches).ShouldBe(
            (Scene.MaterialSwitches.Default, new Scene.MaterialSwitches(true, false), new Scene.MaterialSwitches(false, false)));
        rig.Log.Lines.Where(static line => line.StartsWith("[render] mat_", StringComparison.Ordinal))
            .ShouldBe(["[render] mat_phong 0", "[render] mat_bumpmap 0"]);
    }

    /// <remarks>
    /// B671: a key's, the console's or an exec'd cfg's `mat_specular 0` and `mat_fullbright 1` are ConVar sets (D215), so
    /// they reached the settings and stopped there; the device kept drawing what it had. In TF2 the ConVar is the switch:
    /// the shaders hold `mat_fullbright` themselves (`BaseVSShader.cpp:50`, `lightmappedgeneric_dx9_helper.cpp:21`) and
    /// read `mat_specular` in the params pass (`lightmappedgeneric_dx9_helper.cpp:166-170`).
    /// </remarks>
    [Test]
    public void KeyPressed_AKeyBoundToSpecularAndFullbright_ReachesTheDevice()
    {
        using Rig rig = BuildWithAutoexec("sv_cheats 1\nbind \"F7\" \"mat_specular 0\"\nbind \"F8\" \"mat_fullbright 1\"\n").WithDevice();
        rig.Window.SpecularTicked = rig.Session.SetSpecular;
        (bool, Fullbright) before = (rig.Device.Specular, rig.Device.Fullbright);

        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue();
        rig.Session.KeyPressed(Key("F8", static _ => false)).ShouldBeTrue();

        (before, (rig.Device.Specular, rig.Device.Fullbright)).ShouldBe(((true, Fullbright.Off), (false, Fullbright.NoLighting)));
    }

    /// <remarks>
    /// B791: `mat_wireframe` is a cheat ConVar, so a config's bind to it is refused with `sv_cheats 0`, and with cheats on it
    /// is the switch, ticking the menu's item, which then reaches the device by the menu's own route; `sv_cheats 0` reverts
    /// it, as TF2 reverts every cheat ConVar it holds (B718). The menu itself stays ungated (D253). `sv_cheats 0` sits on
    /// F8, which has a viewer default (`cl_showfps`): before, a bind to `sv_cheats` lost its key to the default, because
    /// `sv_cheats` is replicated and so not counted as a set the bind makes.
    /// </remarks>
    [Test]
    public void KeyPressed_AConfigsBindToMatWireframe_IsRefusedWithoutCheatsAndIsTheSwitchWithThem()
    {
        using Rig rig = BuildWithAutoexec(
            "bind \"F6\" \"sv_cheats 1\"\nbind \"F7\" \"mat_wireframe 1\"\nbind \"F8\" \"sv_cheats 0\"\n").WithDevice();
        rig.Window.WireframeTicked = rig.Session.SetWireframe;

        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue("a refused cheat is still the console's answer");
        int refused = rig.Device.Wireframe;
        rig.Session.KeyPressed(Key("F6", static _ => false));
        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue();
        int cheated = rig.Device.Wireframe;
        rig.Session.KeyPressed(Key("F8", static _ => false)).ShouldBeTrue();

        (refused, cheated, rig.Device.Wireframe).ShouldBe((0, 1, 0));
        rig.Window.Told.Where(static told => told.StartsWith("ShowWireframe", StringComparison.Ordinal))
            .ShouldBe(["ShowWireframe True", "ShowWireframe False"]);
    }

    /// <remarks>
    /// **A bind's `mat_wireframe 2` keeps its value** (B791 review 13, Fable): the menu's tick it causes finds the setting
    /// already on and records nothing over it, so `toggle mat_wireframe 2 0` later compares against "2", as TF2's would.
    /// The device takes the 2 too (B803): `engine.dll` `FUN_1800dc720` draws 2 with the z-buffered material.
    /// </remarks>
    [Test]
    public void KeyPressed_ABindOfMatWireframeTwoWithCheats_KeepsTheValueTwo()
    {
        using Rig rig = BuildWithAutoexec("sv_cheats 1\nbind \"F7\" \"mat_wireframe 2\"\n").WithDevice();
        rig.Window.WireframeTicked = rig.Session.SetWireframe;

        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue();

        rig.Device.Wireframe.ShouldBe(2);
        rig.Settings.Current.ConVarText("mat_wireframe").ShouldBe("2");
    }

    /// <remarks>
    /// **`mat_wireframe` 1 then 2 reaches the device as 2** (B803): the menu's item is already ticked, so it reports no change,
    /// and the value has to reach the device by the ConVar's own route. Without it the overlay stays the value 1's.
    /// </remarks>
    [Test]
    public void KeyPressed_MatWireframeTwoAfterOne_TellsTheDeviceTwo()
    {
        using Rig rig = BuildWithAutoexec(
            "sv_cheats 1\nbind \"F6\" \"mat_wireframe 1\"\nbind \"F7\" \"mat_wireframe 2\"\n").WithDevice();
        rig.Window.WireframeTicked = rig.Session.SetWireframe;

        rig.Session.KeyPressed(Key("F6", static _ => false)).ShouldBeTrue();
        int first = rig.Device.Wireframe;
        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue();

        (first, rig.Device.Wireframe).ShouldBe((1, 2));
    }

    /// <remarks>
    /// **A config's own `mat_wireframe 2` keeps its value when the device opens** (B791 review 14, Fable): the hand-over's
    /// tick finds the console's value already on and records nothing over it, so `toggle mat_wireframe 2 0` goes 2 to 0,
    /// as TF2's would. Before, the settings recorded "1", which shadowed the console's "2", and the toggle set 2 again.
    /// </remarks>
    [Test]
    public void KeyPressed_ATwoZeroToggleAfterAConfigsWireframeTwo_TurnsItOff()
    {
        using Rig rig = BuildWithAutoexec("sv_cheats 1\nmat_wireframe 2\nbind \"F7\" \"toggle mat_wireframe 2 0\"\n");
        rig.Window.WireframeTicked = rig.Session.SetWireframe;
        rig.WithDevice();
        rig.Device.Wireframe.ShouldBe(2, "the control: the config's line reached the device");

        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue();

        rig.Device.Wireframe.ShouldBe(0);
    }

    /// <remarks>
    /// **`sv_cheats 0` reverts a wireframe a watcher config turned on** (B791 review 15, Fable): the config's line lives in
    /// the console, not the settings, and the revert reads both, as the console's own value does; TF2 reverts every cheat
    /// ConVar wherever it was set.
    /// </remarks>
    [Test]
    public void KeyPressed_SvCheatsZeroAfterAConfigsWireframe_ClearsTheDevice()
    {
        using Rig rig = BuildWithAutoexec("sv_cheats 1\nmat_wireframe 2\nbind \"F8\" \"sv_cheats 0\"\n");
        rig.Window.WireframeTicked = rig.Session.SetWireframe;
        rig.WithDevice();
        rig.Device.Wireframe.ShouldBe(2, "the control: the config's line reached the device");

        rig.Session.KeyPressed(Key("F8", static _ => false)).ShouldBeTrue();

        rig.Device.Wireframe.ShouldBe(0);
    }

    /// <remarks>
    /// **`sv_cheats 0` reverts a wireframe the menu turned on, too** (B791 review 11, Sonnet): the menu records its value for
    /// the run, so the revert finds `mat_wireframe 1` and announces `0`, which unticks the item and clears the device.
    /// </remarks>
    [Test]
    public void KeyPressed_SvCheatsZeroAfterTheMenuSetWireframe_ClearsTheDevice()
    {
        using Rig rig = BuildWithAutoexec("sv_cheats 1\nbind \"F8\" \"sv_cheats 0\"\n").WithDevice();
        rig.Window.WireframeTicked = rig.Session.SetWireframe;
        rig.Window.ShowWireframe(true);
        rig.Device.Wireframe.ShouldBe(1, "the control: the menu's tick reached the device");

        rig.Session.KeyPressed(Key("F8", static _ => false)).ShouldBeTrue();

        rig.Device.Wireframe.ShouldBe(0);
    }

    /// <remarks>
    /// B791: the menu's wireframe is recorded for this run, as its fullbright is (B671 review, D215), so a `toggle
    /// mat_wireframe` after it reads what the menu set and turns it off; without the record it read 0 and set 1.
    /// </remarks>
    [Test]
    public void KeyPressed_AToggleAfterTheMenuSetWireframe_ActsOnTheMenusValue()
    {
        using Rig rig = BuildWithAutoexec("sv_cheats 1\nbind \"F7\" \"toggle mat_wireframe\"\n").WithDevice();
        rig.Window.WireframeTicked = rig.Session.SetWireframe;

        rig.Window.ShowWireframe(true);
        rig.Device.Wireframe.ShouldBe(1, "the control: the menu's tick reached the device");

        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue();

        rig.Device.Wireframe.ShouldBe(0);
    }

    /// <remarks>
    /// B725 glint review 2: `r_glint_alwaysdraw` and `r_glint_procedural` are read by the studio render at each eye draw
    /// (`studiorender.dll` `0x18001e071`, `0x18001e0ad`), so a set from a key, the console or an exec'd cfg mid-demo is the
    /// switch, as B671's are. Before, only the hand-over at device creation gave them to the device.
    /// </remarks>
    [Test]
    public void KeyPressed_AKeyBoundToEachGlintConVar_ReachesTheDevice()
    {
        using Rig rig = BuildWithAutoexec("bind \"F7\" \"r_glint_alwaysdraw 1\"\nbind \"F8\" \"r_glint_procedural 1\"\n").WithDevice();
        GlintSwitches before = rig.Device.EyeGlint;
        int redraws = rig.Window.Count("Redraw");

        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue();
        GlintSwitches afterAlways = rig.Device.EyeGlint;
        int afterFirst = rig.Window.Count("Redraw") - redraws;
        rig.Session.KeyPressed(Key("F8", static _ => false)).ShouldBeTrue();

        (before, afterAlways, rig.Device.EyeGlint).ShouldBe(
            (default(GlintSwitches), new GlintSwitches(true, false), new GlintSwitches(true, true)));
        (afterFirst, rig.Window.Count("Redraw") - redraws).ShouldBe((1, 2), "a paused viewer repaints once per set");
        rig.Log.Lines.Where(static line => line.StartsWith("[render] r_glint_", StringComparison.Ordinal))
            .ShouldBe(["[render] r_glint_alwaysdraw 1", "[render] r_glint_procedural 1"]);
    }

    /// <remarks>
    /// B790: each of these the engine reads where it uses it (cited at `SettingsPresenter.LiveDeviceConVars`) — so a set from a key, the console or an exec'd
    /// cfg mid-demo is the switch. Before, only the hand-over at device creation gave them to the device. The control is
    /// the hand-over itself: it set the property, and not to the key's value.
    /// </remarks>
    [TestCase("mat_disable_bloom 1", "DisableBloom True")]
    [TestCase("r_lightmap_bicubic 1", "BicubicLightmap True")]
    [TestCase("r_shader_srgb 1", "ShaderSrgb 1")]
    [TestCase("mat_force_tonemap_scale 2", "ForceToneMapScale 2")]
    [TestCase("cl_drawhud 0", "DrawHud False")]
    [TestCase("r_3dsky 0", "Draw3dSky 0")]
    [TestCase("cl_detaildist 500", "DetailDistance 500")]
    [TestCase("cl_detailfade 100", "DetailFadeWidth 100")]
    [TestCase("cl_drawmonitors 0", "DrawMonitors False")]
    public void KeyPressed_AKeyBoundToADeviceConVar_ReachesTheDevice(string set, string handed)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(handed);

        // `sv_cheats 1` because `cl_drawhud` and `mat_force_tonemap_scale` are cheats, which a bind cannot set without it
        // (D253); the route is the question here, not that gate.
        using Rig rig = BuildWithAutoexec($"sv_cheats 1\nbind \"F7\" \"{set}\"\n");
        FakeViewerDevice device = new();
        rig.Session.OpenDevice(() => rig.Device, (_, specularOff, setFullbright) => rig.Settings.ApplyOver(device, specularOff, setFullbright), 1280, 720)
            .ShouldBeTrue();
        string property = handed.Split(' ')[0] + " ";
        (device.Set.Count(entry => entry.StartsWith(property, StringComparison.Ordinal)), device.Set.Contains(handed))
            .ShouldBe((1, false), "the control: the hand-over set it once, to the config's value");
        int sets = device.Set.Count;
        int redraws = rig.Window.Count("Redraw");

        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue();

        device.Set.Skip(sets).ShouldBe([handed]);
        (rig.Window.Count("Redraw") - redraws).ShouldBe(1, "a paused viewer repaints once per set");
        rig.Log.Lines.ShouldContain($"[render] {set}");
    }

    /// <remarks>
    /// B790 review 1: a device let go — released at shutdown, or one whose opening failed after the hand-over — takes no
    /// later set. The control is the same key before the release, which reaches it.
    /// </remarks>
    [TestCase(false)]
    [TestCase(true)]
    public void KeyPressed_ADeviceConVarAfterTheDeviceIsLetGo_ReachesNothing(bool failedOpening)
    {
        using Rig rig = BuildWithAutoexec("bind \"F7\" \"r_3dsky 0\"\nbind \"F8\" \"r_3dsky 1\"\n");
        FakeViewerDevice device = new();
        bool opened = rig.Session.OpenDevice(
            () => rig.Device,
            (_, specularOff, setFullbright) =>
            {
                rig.Settings.ApplyOver(device, specularOff, setFullbright);

                if (failedOpening)
                {
                    throw new InvalidOperationException("no adapter");
                }
            },
            1280,
            720);

        if (!failedOpening)
        {
            rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue();
            device.Set[^1].ShouldBe("Draw3dSky 0", "the control: a held device takes the set");
            rig.Session.ReleaseDevice();
        }

        int sets = device.Set.Count;
        rig.Session.KeyPressed(Key("F8", static _ => false));

        (opened, device.Set.Count - sets).ShouldBe((!failedOpening, 0));
    }

    /// <remarks>
    /// **The menu writes the ConVar too** (B671 review, D215), as `SetPhong` does: unticked from the menu, `mat_specular`
    /// must read 0 to a later `toggle`, which then turns reflections back on; and a menu fullbright must be in the settings
    /// for `sv_cheats 0` to put it back to 0, as TF2 reverts every cheat ConVar it holds. Before, the toggle read 1 and set
    /// 0 again, and the revert found the default and skipped the device.
    /// </remarks>
    [Test]
    public void KeyPressed_AToggleAndACheatsOffAfterTheMenuSetSpecularAndFullbright_ActOnTheMenusValues()
    {
        using Rig rig = BuildWithAutoexec("sv_cheats 1\nbind \"F7\" \"sv_cheats 0\"\nbind \"F9\" \"toggle mat_specular\"\n").WithDevice();
        rig.Window.SpecularTicked = rig.Session.SetSpecular;

        rig.Session.SetSpecular(false);
        rig.Session.SetFullbright(Fullbright.NoLighting);
        (rig.Device.Specular, rig.Device.Fullbright).ShouldBe((false, Fullbright.NoLighting), "the control: the menu reached the device");

        rig.Session.KeyPressed(Key("F9", static _ => false)).ShouldBeTrue();
        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue();

        (rig.Device.Specular, rig.Device.Fullbright).ShouldBe((true, Fullbright.Off));
    }

    [Test]
    public void SetRenderSwitches_EachOne_ReachesItsOwnDevicePropertyUnderValvesName()
    {
        using Rig rig = Build().WithDevice();

        rig.Session.SetDrawWorld(false);
        rig.Session.SetDrawEntities(false);
        rig.Session.SetSpecular(false);
        rig.Session.SetPhong(false);

        // `mat_phong` reaches the device by the material switches' route with `mat_bumpmap` (B720): ViewerPresenterTests.
        (rig.Device.DrawWorld, rig.Device.DrawEntities, rig.Device.Specular).ShouldBe((false, false, false));
        rig.Device.Wireframe.ShouldBe(0, "no switch throws its neighbour");
        rig.Log.Lines.Where(static line => line.StartsWith("[render] r_draw", StringComparison.Ordinal) || line.StartsWith("[render] mat_", StringComparison.Ordinal))
            .ShouldBe(["[render] r_drawworld 0", "[render] r_drawentities 0", "[render] mat_specular 0", "[render] mat_phong 0"]);
    }

    [Test]
    public void SetWireframe_BeforeThereIsADevice_StillLogsAndRepaints()
    {
        using Rig rig = Build();

        rig.Session.SetWireframe(true);

        rig.Log.Count("[render] mat_wireframe 1").ShouldBe(1);
        rig.Window.Count("Redraw").ShouldBe(1);
    }

    [Test]
    public void SetFullbright_LightingOnly_TicksItsItemLogsTheValueAndTellsTheDevice()
    {
        using Rig rig = Build().WithDevice();

        rig.Session.SetFullbright(Fullbright.LightingOnly);

        rig.Session.Fullbright.ShouldBe(Fullbright.LightingOnly);
        rig.Device.Fullbright.ShouldBe(Fullbright.LightingOnly);
        rig.Window.Told.ShouldContain("ShowFullbright 2");
        rig.Log.Count("[render] mat_fullbright 2").ShouldBe(1);
    }

    [Test]
    public void SetDebugMode_ASecondView_KeepsTheFirstAndHandsTheDeviceBoth()
    {
        using Rig rig = Build().WithDevice();

        rig.Session.SetDebugMode(static (modes, on) => modes with { Luxels = on }, true);
        rig.Session.SetDebugMode(static (modes, on) => modes with { DrawFlat = on }, true);

        rig.Device.Debug.ShouldBe(DebugModes.None with { Luxels = true, DrawFlat = true });

        rig.Session.SetDebugMode(static (modes, on) => modes with { Luxels = on }, false);

        rig.Device.Debug.ShouldBe(DebugModes.None with { DrawFlat = true });
        rig.Log.Count("[render] debug views: ").ShouldBe(3);
    }

    [Test]
    public void SetSurfaceColours_OnThenOff_WritesTheLegendOnceAndThenSaysOff()
    {
        using Rig rig = Build();

        rig.Session.SetSurfaceColours(true);
        rig.Session.SetSurfaceColours(false);

        rig.Log.Count("[render] surface colours on — grey-blue brushwork").ShouldBe(1);
        rig.Log.Count("[render] surface colours off").ShouldBe(1);
        rig.Window.Count("Redraw").ShouldBe(2);
    }

    [Test]
    public void SetFullScreenExclusive_WhileWindowed_TicksTheItemAndLeavesTheDeviceAlone()
    {
        using Rig rig = Build().WithDevice();

        rig.Session.SetFullScreenExclusive(true);

        rig.Session.FullScreenExclusive.ShouldBeTrue();
        rig.Window.Told.ShouldContain("ShowFullScreenExclusive True");
        rig.Device.Exclusive.ShouldBeEmpty("the mode is entered when the window goes full screen, not when it is chosen");
    }

    [Test]
    public void SetFullScreenExclusive_WhileFullScreenAndRefused_AsksTheDeviceAndSaysItWasRefused()
    {
        using Rig rig = Build().WithDevice();
        rig.Window.IsFullScreen = true;
        rig.Device.GrantsExclusive = false;

        rig.Session.SetFullScreenExclusive(true);

        rig.Device.Exclusive.ShouldBe([true]);
        rig.Window.Status.ShouldBe(SettingsPresenter.ExclusiveFullScreenRefused);
    }

    [Test]
    public void EnteredFullScreen_WithExclusiveChosen_TakesTheDisplayAndLeavingGivesItBack()
    {
        using Rig rig = Build(new ViewerSettings { FullScreenMode = FullScreenMode.Exclusive }).WithDevice();

        rig.Session.EnteredFullScreen();
        rig.Session.LeavingFullScreen();

        rig.Device.Exclusive.ShouldBe([true, false]);
        rig.Window.Told.ShouldNotContain("Status: " + SettingsPresenter.ExclusiveFullScreenRefused);
    }

    [Test]
    public void EnteredFullScreen_WithBorderlessChosen_AsksTheDeviceNothing()
    {
        using Rig rig = Build().WithDevice();

        rig.Session.EnteredFullScreen();

        rig.Device.Exclusive.ShouldBeEmpty();
    }

    [Test]
    public void EnteredFullScreen_ExclusiveRefused_SaysSoInTheStatus()
    {
        using Rig rig = Build(new ViewerSettings { FullScreenMode = FullScreenMode.Exclusive }).WithDevice();
        rig.Device.GrantsExclusive = false;

        rig.Session.EnteredFullScreen();

        rig.Window.Status.ShouldBe(SettingsPresenter.ExclusiveFullScreenRefused);
    }

    [Test]
    public void SetTextureQuality_ALevel_TicksItRemembersItAndSaysWhenItApplies()
    {
        using Rig rig = Build();

        rig.Session.SetTextureQuality(nameof(TextureQuality.Low));

        rig.Session.TextureQuality.ShouldBe(nameof(TextureQuality.Low));
        rig.Window.Told.ShouldContain("ShowTextureQuality " + nameof(TextureQuality.Low));
        rig.Window.Status.ShouldNotBeNullOrEmpty();
        rig.Window.Status.ShouldNotBe("No demo loaded.");
    }

    // ---- the device ----

    [Test]
    public void OpenDevice_WhenTheMachineGivesOne_SaysTheFrameRateLineThenTheSizeAndThatDirect3DIsReady()
    {
        using Rig rig = Build();

        rig.Session.HasDevice.ShouldBeFalse();
        rig.Session.OpenDevice(() => rig.Device, NoAttach, 800, 600).ShouldBeTrue();

        rig.Session.HasDevice.ShouldBeTrue();
        rig.Window.Status.ShouldBe("Direct3D ready.");
        string[] render = [.. rig.Log.Lines.Where(static line => line.StartsWith("[render] ", StringComparison.Ordinal))];
        render[^1].ShouldBe("[render] device created for a 800x600 viewport");
        render[^2].ShouldBe("[render] " + rig.Session.Settings.FrameRateLine);
    }

    [Test]
    public void OpenDevice_WhenTheMachineRefuses_SaysWhyInTheStatusLogsItAndAnswersFalse()
    {
        using Rig rig = Build();

        rig.Session.OpenDevice(static () => throw new InvalidOperationException("no adapter"), NoAttach, 800, 600).ShouldBeFalse();

        rig.Session.HasDevice.ShouldBeFalse();
        rig.Window.Status.ShouldBe("Direct3D unavailable: no adapter");
        rig.Log.Count("[render] creating the Direct3D device").ShouldBe(1);
        rig.Log.Count("device created for").ShouldBe(0);
    }

    /// <remarks>
    /// B671 review: the device is held before the hand-over now, so a hand-over that throws must let it go again. Kept, the
    /// session would draw frames through a device whose status line says it is unavailable.
    /// </remarks>
    [Test]
    public void OpenDevice_WhenTheHandOverThrows_HoldsNoDeviceAndAnswersFalse()
    {
        using Rig rig = Build();

        rig.Session.OpenDevice(
            () => rig.Device,
            static (_, _, _) => throw new InvalidOperationException("hand-over failed"),
            800,
            600).ShouldBeFalse();

        rig.Session.HasDevice.ShouldBeFalse();
        rig.Window.Status.ShouldBe("Direct3D unavailable: hand-over failed");
    }

    [Test]
    public async Task OpenDevice_WithADemoAlreadyOpen_LeavesTheDemosStatusAlone()
    {
        using Rig rig = Build();
        (await rig.Session.Presenter.LoadDemoAsync(WriteDemo("first")).ConfigureAwait(false)).Loaded.ShouldBeTrue();
        string? before = rig.Window.Status;

        rig.Session.OpenDevice(() => rig.Device, NoAttach, 800, 600).ShouldBeTrue();

        rig.Window.Status.ShouldBe(before);
    }

    /// <remarks>
    /// B671: `+mat_specular 0` and `+mat_fullbright 1` were logged as set and reached no device, so a `--shot` with either
    /// was its control's picture. The hand-over runs the menu's own two calls (`SettingsPresenter.ApplyOver`), and both
    /// reached for the session's device while it was still unassigned. The window here routes an unticked Reflections
    /// item back to `SetSpecular`, as the real menu's `CheckedChanged` does.
    /// </remarks>
    [Test]
    public void OpenDevice_WithSpecularOffAndFullbright1InTheSettings_HandsBothToTheDevice()
    {
        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 0, Specular = false, Fullbright = 1 });
        rig.Window.SpecularTicked = rig.Session.SetSpecular;

        rig.Device.Specular.ShouldBeTrue("the control: a new device starts with reflections on");
        rig.Device.Fullbright.ShouldBe(Fullbright.Off, "the control: a new device starts lit");

        rig.Session.OpenDevice(
            () => rig.Device,
            (_, specularOff, setFullbright) => rig.Settings.ApplyOver(new FakeViewerDevice(), specularOff, setFullbright),
            800,
            600).ShouldBeTrue();

        rig.Device.Specular.ShouldBeFalse();
        rig.Device.Fullbright.ShouldBe(Fullbright.NoLighting);
    }

    /// <remarks>
    /// B791: `+sv_cheats 1 +mat_wireframe 1` stores the ConVar (`LaunchOptionsTests`), and the hand-over ticks the menu's
    /// item from it, as a set at run time does, so the device draws wireframe from its first frame. Before, nothing at
    /// startup read `mat_wireframe` and the launch line's set reached nothing.
    /// </remarks>
    [Test]
    public void OpenDevice_WithMatWireframe1InTheSettings_TicksTheMenuWhichTellsTheDevice()
    {
        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 0 }.Set("mat_wireframe", "1"));
        rig.Window.WireframeTicked = rig.Session.SetWireframe;

        rig.Device.Wireframe.ShouldBe(0, "the control: a new device starts solid");

        rig.Session.OpenDevice(() => rig.Device, NoAttach, 800, 600).ShouldBeTrue();

        rig.Device.Wireframe.ShouldBe(1);
        rig.Window.Told.ShouldContain("ShowWireframe True", "the menu is ticked, so its first click turns wireframe off");
    }

    /// <remarks>
    /// **A watcher config's own `mat_wireframe 1`, under `sv_cheats 1`, reaches the device it opens** (B791 review 9, Fable):
    /// the console keeps a config's line, not the settings, so the hand-over reads the client value the console holds too.
    /// </remarks>
    [Test]
    public void OpenDevice_AfterAConfigSetMatWireframeWithCheats_DrawsWireframe()
    {
        using Rig rig = BuildWithAutoexec("sv_cheats 1\nmat_wireframe 1\n");
        rig.Window.WireframeTicked = rig.Session.SetWireframe;

        rig.WithDevice();

        rig.Device.Wireframe.ShouldBe(1);
    }

    /// <remarks>
    /// **A launch cheat runs after the configs, so an autoexec's `sv_cheats 1` admits it** (B811). TF2's shipped
    /// `cfg/valve.rc` (read from `tf2_misc_dir.vpk`) runs `exec autoexec.cfg` and only then `stuffcmds`, which executes the
    /// command line's `+` commands. The control: the same launch line with an autoexec that leaves cheats off is refused.
    /// </remarks>
    [Test]
    public void OpenDevice_ALaunchMatWireframeAfterAnAutoexecsSvCheats_DrawsWireframe()
    {
        using (Rig refused = BuildWithAutoexec("cl_showfps 0\n", "+mat_wireframe", "1"))
        {
            refused.Window.WireframeTicked = refused.Session.SetWireframe;
            refused.WithDevice();
            refused.Device.Wireframe.ShouldBe(0, "the control: cheats off in the configs, the launch line is refused");
        }

        using Rig rig = BuildWithAutoexec("sv_cheats 1\n", "+mat_wireframe", "1");
        rig.Window.WireframeTicked = rig.Session.SetWireframe;

        rig.WithDevice();

        rig.Device.Wireframe.ShouldBe(1);
    }

    /// <remarks>
    /// **A typed cheat ConVar set after the configs reaches the settings its readers read** (B811 review 1, Fable):
    /// `cl_drawhud` is read from `ViewerSettings.DrawHud`, not from the console. An autoexec's `sv_cheats 1` and a launch
    /// `+cl_drawhud 0`: the run's settings say no HUD. The control: without `sv_cheats`, they still draw it.
    /// </remarks>
    [Test]
    public void Build_ALaunchClDrawhudAfterAnAutoexecsSvCheats_ReachesTheSettings()
    {
        using (Rig refused = BuildWithAutoexec("cl_showfps 0\n", "+cl_drawhud", "0"))
        {
            refused.Settings.Current.DrawHud.ShouldBeTrue("the control: cheats off, the launch line is refused");
        }

        using Rig rig = BuildWithAutoexec("sv_cheats 1\n", "+cl_drawhud", "0");

        rig.Settings.Current.DrawHud.ShouldBeFalse();
    }

    /// <remarks>
    /// **A launch cheat nothing admits is refused with TF2's line** (B811 review 1, Fable): no `sv_cheats` anywhere.
    /// </remarks>
    [Test]
    public void Build_ALaunchCheatNothingAdmits_LogsTf2sRefusal()
    {
        using Rig rig = BuildWithAutoexec("cl_showfps 0\n", "+mat_wireframe", "1");

        rig.Log.Count("Can't use cheat cvar mat_wireframe in multiplayer").ShouldBe(1);
    }

    /// <remarks>
    /// **An autoexec's typed ConVars reach the run's settings, where their readers look** (B816): TF2 runs `autoexec.cfg`
    /// (`cfg/valve.rc`), so an autoexec's `fps_max 123` and, under its `sv_cheats 1`, `cl_drawhud 0` are the run's values.
    /// The control: an autoexec that sets neither leaves both at the rig's saved values.
    /// </remarks>
    [Test]
    public void Build_AnAutoexecsTypedConVars_ReachTheSettings()
    {
        using (Rig control = BuildWithAutoexec("cl_showfps 0\n"))
        {
            control.Settings.Current.FrameRateLimit.ShouldBe(0, "the control: the rig's saved value");
            control.Settings.Current.DrawHud.ShouldBeTrue("the control: nothing hides the HUD");
        }

        using Rig rig = BuildWithAutoexec("fps_max 123\nsv_cheats 1\ncl_drawhud 0\n");

        rig.Settings.Current.FrameRateLimit.ShouldBe(123);
        rig.Settings.Current.DrawHud.ShouldBeFalse();
    }

    /// <remarks>
    /// **TF2's `config.cfg` ranks below the viewer's own saved settings, which play its part** (D264, the owner's pick):
    /// a saved `fps_max 144` survives a `config.cfg` that says 77. The control: the same line in `autoexec.cfg`, the
    /// player's hand-written layer, wins over the saved value, as in TF2.
    /// </remarks>
    [Test]
    public void Build_ASavedConVarAlsoInTf2sConfigCfg_KeepsTheSavedValue()
    {
        _gameFolder = Path.Combine(_folder, "tf");
        Directory.CreateDirectory(Path.Combine(_gameFolder, "cfg"));
        ViewerSettings saved = new ViewerSettings { FrameRateLimit = 0 }.Set("fps_max", "144");

        File.WriteAllText(Path.Combine(_gameFolder, "cfg", "autoexec.cfg"), "fps_max 77\n");
        using (Rig control = Build(saved))
        {
            control.Settings.Current.FrameRateLimit.ShouldBe(77, "the control: autoexec.cfg wins over the saved value");
        }

        File.Delete(Path.Combine(_gameFolder, "cfg", "autoexec.cfg"));
        File.WriteAllText(Path.Combine(_gameFolder, "cfg", "config.cfg"), "fps_max 77\n");
        using Rig rig = Build(saved);

        rig.Settings.Current.FrameRateLimit.ShouldBe(144);
    }

    /// <remarks>
    /// **What is read once at startup is read after the configs** (B816 review 1, Fable, Sonnet): an autoexec's
    /// `mat_hdr_level 0` is the lighting every loader takes, as the settings say. The control: without it, the saved 2.
    /// </remarks>
    [Test]
    [NonParallelizable]
    public void Build_AnAutoexecsMatHdrLevel_IsTheLightingTheLoadersTake()
    {
        // The level is process-wide: put back what this found, so no later test loads a map at 0 (review 7, Grok).
        int before = Content.Bsp.BspHdr.MatHdrLevel;

        try
        {
            using (Rig control = BuildWithAutoexec("cl_showfps 0\n"))
            {
                Content.Bsp.BspHdr.MatHdrLevel.ShouldBe(control.Settings.Current.HdrLevel, "the control: the saved level");
                control.Settings.Current.HdrLevel.ShouldNotBe(0);
            }

            using Rig rig = BuildWithAutoexec("mat_hdr_level 0\n");

            rig.Settings.Current.HdrLevel.ShouldBe(0);
            Content.Bsp.BspHdr.MatHdrLevel.ShouldBe(0);
        }
        finally
        {
            Content.Bsp.BspHdr.MatHdrLevel = before;
        }
    }

    /// <remarks>
    /// **A folder chosen before any map reads its autoexec's `mat_hdr_level` into the lighting too** (B816 review 2, Grok):
    /// no map has been read, so the first one must take the new value. The control: before the folder, the saved level.
    /// </remarks>
    [Test]
    [NonParallelizable]
    public void GameFolderChosen_WhoseAutoexecSetsMatHdrLevel_IsTheLightingTheLoadersTake()
    {
        string tf = Path.Combine(_folder, "chosen", "tf");
        Directory.CreateDirectory(Path.Combine(tf, "cfg"));
        File.WriteAllText(Path.Combine(tf, SteamInstall.Recogniser), string.Empty);
        File.WriteAllText(Path.Combine(tf, "cfg", "autoexec.cfg"), "mat_hdr_level 0\n");
        int before = Content.Bsp.BspHdr.MatHdrLevel;

        try
        {
            using Rig rig = Build();

            Content.Bsp.BspHdr.MatHdrLevel.ShouldNotBe(0, "the control: the saved level before the folder");

            _gameFolder = tf;
            rig.Session.GameFolderChosen(tf);

            rig.Window.Status.ShouldNotBeNull().ShouldStartWith(SettingsPresenter.GameFolderSet, Case.Sensitive, "the folder taken");
            Content.Bsp.BspHdr.MatHdrLevel.ShouldBe(0);
        }
        finally
        {
            // Process-wide: put back what this found (review 7, Grok).
            Content.Bsp.BspHdr.MatHdrLevel = before;
        }
    }

    /// <remarks>
    /// **An instrument's run reads none of the machine's configs** (B816 review 5): `TF2VIEW_NO_CONFIGS=1` leaves an
    /// autoexec's `fps_max 123` out of the run. The control: without it, 123.
    /// </remarks>
    [Test]
    public void Build_WithNoConfigs_ReadsNoAutoexec()
    {
        using (Rig control = BuildWithAutoexec("fps_max 123\n"))
        {
            control.Settings.Current.FrameRateLimit.ShouldBe(123, "the control: the autoexec is read");
        }

        _environmentAtStart[ViewerSession.NoConfigsVariable] = "1";
        using Rig rig = BuildWithAutoexec("fps_max 123\n");

        rig.Settings.Current.FrameRateLimit.ShouldBe(0, "the rig's saved value");
        rig.Log.Count("no config is read").ShouldBe(1);
    }

    /// <remarks>
    /// **The launch line runs as `stuffcmds`, not through a file's line reader** (B820 review 9, Sonnet): the reader would
    /// make `\\` into `\`. The control: the same value from the autoexec, a file, loses one.
    /// </remarks>
    [Test]
    public void Build_ALaunchValueWithTwoBackslashes_ReachesTheConVarWhole()
    {
        using (Rig control = BuildWithAutoexec("cl_voice_filter \"a\\\\b\"\n"))
        {
            control.Settings.ConVar("cl_voice_filter").ShouldBe("a\\b", "the control: a file line is unescaped");
        }

        using Rig rig = BuildWithAutoexec(string.Empty, "+cl_voice_filter", "a\\\\b");

        rig.Settings.ConVar("cl_voice_filter").ShouldBe("a\\\\b");
    }

    /// <remarks>
    /// **The launch line's ConVar wins over the configs', as `stuffcmds` running after `exec` gives** (B816; B811 review 3):
    /// an autoexec's `fps_max 123` and a launch `+fps_max 60` end at 60. The control: without the launch option, 123.
    /// </remarks>
    [Test]
    public void Build_ALaunchConVarAlsoInTheAutoexec_EndsAtTheLaunchValue()
    {
        using (Rig control = BuildWithAutoexec("fps_max 123\n"))
        {
            control.Settings.Current.FrameRateLimit.ShouldBe(123, "the control: the autoexec's value");
        }

        using Rig rig = BuildWithAutoexec("fps_max 123\n", "+fps_max", "60");

        rig.Settings.Current.FrameRateLimit.ShouldBe(60);
    }

    /// <remarks>
    /// **A bind a key's alias makes at run time reaches the bindings the window shows** (B821): `F7` runs an alias that
    /// binds `F8` to the screenshot. TF2 has one bind table, which its binding menu reads (*not read*; the menu is closed),
    /// so the projection must follow the console. The control: before `F7`, `F8` takes no screenshot.
    /// </remarks>
    [Test]
    public void Bindings_AfterAKeysAliasBindsAtRunTime_NameTheNewKey()
    {
        string screenshot = KeyBindings.Commands[ViewerAction.Screenshot];
        using Rig rig = BuildWithAutoexec($"unbindall\nalias rebind \"bind F8 {screenshot}\"\nbind F7 rebind\n");
        rig.Session.Bindings.KeyFor(ViewerAction.Screenshot).ShouldNotBe("F8", "the control: F8 is not bound yet");

        rig.Session.KeyPressed(Key("F7")).ShouldBeTrue();
        rig.Session.KeyUp("F7");

        rig.Session.Bindings.KeyFor(ViewerAction.Screenshot).ShouldBe("F8");
    }

    /// <remarks>
    /// **An alias defined at run time retires the default it now claims** (B821 review 1, Fable): after `unbindall`, the
    /// screenshot's default key answers only as this viewer's fallback; `F8` is bound to `shot`, which `F7` defines as the
    /// screenshot. Before, nothing claims the screenshot, so the fallback keeps it; after, the claim is `F8`'s and the
    /// fallback retires. The control: before `F7`, the default's key.
    /// </remarks>
    [Test]
    public void Bindings_AfterAnAliasClaimsADefaultsAction_NoLongerNameTheDefaultKey()
    {
        string screenshot = KeyBindings.Commands[ViewerAction.Screenshot];
        using Rig rig = BuildWithAutoexec($"unbindall\nbind F8 shot\nalias define \"alias shot {screenshot}\"\nbind F7 define\n");
        string defaultKey = rig.Session.Bindings.KeyForCommand(screenshot).ShouldNotBeNull("the control: the default holds it");

        rig.Session.KeyPressed(Key("F7")).ShouldBeTrue();
        rig.Session.KeyUp("F7");

        rig.Session.Bindings.KeyForCommand(screenshot).ShouldNotBe(defaultKey);
    }

    /// <remarks>
    /// **The window is told the bindings again when a key changed them, and only then** (B821 review 1, Sonnet): its menu
    /// carries each item's shortcut key, set when it was built. `F6` redefines an alias no key reaches, as a null-cancel
    /// script does on every press (review 2, Grok): its table is unchanged, so the window is told nothing.
    /// </remarks>
    [Test]
    public void KeyPressed_AKeyWhoseAliasBinds_TellsTheWindowTheBindingsOnce()
    {
        string screenshot = KeyBindings.Commands[ViewerAction.Screenshot];
        using Rig rig = BuildWithAutoexec(
            $"unbindall\nalias rebind \"bind F8 {screenshot}\"\nbind F7 rebind\nalias noise \"alias unused echo\"\nbind F6 noise\n");
        int before = rig.Window.Told.Count(told => told == "ShowBindings");

        rig.Session.KeyPressed(Key("F6"));
        rig.Session.KeyUp("F6");
        rig.Window.Told.Count(told => told == "ShowBindings").ShouldBe(before, "the control: F6 changed no key's meaning");

        rig.Session.KeyPressed(Key("F7"));
        rig.Session.KeyUp("F7");

        rig.Window.Told.Count(told => told == "ShowBindings").ShouldBe(before + 1);
        rig.Window.ShownBindings.ShouldNotBeNull().KeyFor(ViewerAction.Screenshot).ShouldBe("F8", "the table it was told");
    }

    /// <remarks>
    /// **A key's release and a mouse button's press and release tell the window too** (B821 review 2, Fable, Sonnet): a
    /// `+` alias whose `-` half binds runs on the release, and a mouse button reaches the console by its own route. Each
    /// half of `+sw` binds `F8`, so the press and the release each tell the window once.
    /// </remarks>
    [TestCase("F7", false)]
    [TestCase("MOUSE4", true)]
    public void KeyUp_AButtonWhoseReleaseBinds_TellsTheWindowTheBindings(string button, bool mouse)
    {
        string screenshot = KeyBindings.Commands[ViewerAction.Screenshot];
        using Rig rig = BuildWithAutoexec(
            $"unbindall\nalias +sw \"bind F8 {screenshot}\"\nalias -sw \"bind F8 none\"\nbind {button} +sw\n");
        int before = rig.Window.Told.Count(told => told == "ShowBindings");

        if (mouse)
        {
            rig.Session.PointerDown(button, drags: false, 0, 0);
            rig.Window.Told.Count(told => told == "ShowBindings").ShouldBe(before + 1, "the press");
            rig.Session.PointerUp(button);
        }
        else
        {
            rig.Session.KeyPressed(Key(button));
            rig.Window.Told.Count(told => told == "ShowBindings").ShouldBe(before + 1, "the press");
            rig.Session.KeyUp(button);
        }

        rig.Window.Told.Count(told => told == "ShowBindings").ShouldBe(before + 2, "the release");
    }

    /// <remarks>
    /// **An alias a key defines at run time reaches the bindings too** (B821): `F8` is bound to `shot`, which nothing defines
    /// until `F7` runs `alias shot` with the screenshot command. The projection expands aliases, so it changes with no `bind` at all. The
    /// control: before `F7`, `F8` reaches nothing.
    /// </remarks>
    [Test]
    public void Bindings_AfterAKeyDefinesAnAliasAtRunTime_NameTheKeyThatReachesIt()
    {
        string screenshot = KeyBindings.Commands[ViewerAction.Screenshot];
        using Rig rig = BuildWithAutoexec($"unbindall\nbind F8 shot\nalias define \"alias shot {screenshot}\"\nbind F7 define\n");
        rig.Session.Bindings.KeyFor(ViewerAction.Screenshot).ShouldNotBe("F8", "the control: shot is not defined yet");

        rig.Session.KeyPressed(Key("F7")).ShouldBeTrue();
        rig.Session.KeyUp("F7");

        rig.Session.Bindings.KeyFor(ViewerAction.Screenshot).ShouldBe("F8");
    }

    /// <remarks>
    /// **The launch line runs in its own order** (B811 review 1, Fable): with no cheats in the configs,
    /// `+mat_wireframe 1 +sv_cheats 1` refuses the wireframe before the line admits cheats; a reversed replay would draw it.
    /// </remarks>
    [Test]
    public void OpenDevice_ALaunchMatWireframeBeforeItsSvCheatsOne_DrawsSolid()
    {
        using Rig rig = BuildWithAutoexec("cl_showfps 0\n", "+mat_wireframe", "1", "+sv_cheats", "1");
        rig.Window.WireframeTicked = rig.Session.SetWireframe;

        rig.WithDevice();

        rig.Device.Wireframe.ShouldBe(0);
    }

    /// <remarks>
    /// **The last set on the line wins, as `stuffcmds` running the whole line gives** (B811 review 2, Sonnet): an autoexec's
    /// `sv_cheats 1`, then `+mat_fullbright 1 +sv_cheats 1 +mat_fullbright 0` ends at 0. Before, the line's admitted 0 was
    /// stored when it was read and the deferred 1 replayed over it. The control: the same line ending in 1 ends at 1.
    /// </remarks>
    [Test]
    public void Build_ALaunchCheatSetTwiceAroundItsSvCheats_EndsAtTheLastValue()
    {
        using (Rig control = BuildWithAutoexec("sv_cheats 1\n", "+mat_fullbright", "0", "+sv_cheats", "1", "+mat_fullbright", "1"))
        {
            control.Settings.Current.ConVarText("mat_fullbright").ShouldBe("1", "the control: the last value is 1");
        }

        using Rig rig = BuildWithAutoexec("sv_cheats 1\n", "+mat_fullbright", "1", "+sv_cheats", "1", "+mat_fullbright", "0");

        rig.Settings.Current.ConVarText("mat_fullbright").ShouldBe("0");
    }

    /// <remarks>
    /// **The launch line runs before the window is told of any set** (B811 review 2, Fable and Sonnet): the session is built
    /// before the real window's menu and viewport exist, so a switch applied then crashed it. The switches reach the window
    /// from the settings when the device opens. The control: the same set after the device opens is told.
    /// </remarks>
    [Test]
    public void Build_ALaunchCheatAdmittedByAnAutoexec_TellsTheWindowNothingUntilTheDeviceOpens()
    {
        using Rig rig = BuildWithAutoexec("sv_cheats 1\n", "+mat_wireframe", "1");
        rig.Window.WireframeTicked = rig.Session.SetWireframe;

        rig.Window.Told.ShouldNotContain(said => said.StartsWith("ShowWireframe", StringComparison.Ordinal));

        rig.WithDevice();

        rig.Window.Told.ShouldContain("ShowWireframe True", "the control: the device's hand-over tells it");
    }

    /// <remarks>
    /// **`sv_cheats 0` later on the line puts the cheat back** (B811): an autoexec's `sv_cheats 1`, then the line's
    /// `+mat_wireframe 1 +sv_cheats 0`, ends with wireframe off, as `sv_cheats 0` reverts every cheat ConVar.
    /// </remarks>
    [Test]
    public void OpenDevice_ALaunchSvCheatsZeroAfterItsMatWireframe_DrawsSolid()
    {
        using Rig rig = BuildWithAutoexec("sv_cheats 1\n", "+mat_wireframe", "1", "+sv_cheats", "0");
        rig.Window.WireframeTicked = rig.Session.SetWireframe;

        rig.WithDevice();

        rig.Device.Wireframe.ShouldBe(0);
    }

    /// <remarks>
    /// **`mat_wireframe` is read as `WireFrameMode()` reads it, its integer** (`view.h:68`; B788 review 6, Fable): 2 and
    /// `incrementvar`'s `1.000000` draw wireframe, `0.5` does not. The device takes the integer (B803): 3 stays 3.
    /// </remarks>
    [TestCase("2", 2)]
    [TestCase("3", 3)]
    [TestCase("1.000000", 1)]
    [TestCase("0.5", 0)]
    public void OpenDevice_WithAMatWireframeValue_DrawsWireframeByItsInteger(string value, int wireframe)
    {
        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 0 }.Set("mat_wireframe", value));
        rig.Window.WireframeTicked = rig.Session.SetWireframe;

        rig.Session.OpenDevice(() => rig.Device, NoAttach, 800, 600).ShouldBeTrue();

        rig.Device.Wireframe.ShouldBe(wireframe);
    }

    /// <remarks>
    /// **A device opened again with the menu already ticked draws wireframe too** (B788 review 3, Sonnet): the real menu
    /// reports on `CheckedChanged`, so a second tick of an item already ticked reaches nothing, and the new device must be
    /// told by the session itself.
    /// </remarks>
    [Test]
    public void OpenDevice_AgainWithTheWireframeMenuAlreadyTicked_TellsTheNewDevice()
    {
        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 0 }.Set("mat_wireframe", "1"));
        rig.Window.WireframeTicked = rig.Session.SetWireframe;
        rig.Session.OpenDevice(() => rig.Device, NoAttach, 800, 600).ShouldBeTrue();
        FakeRenderDevice second = new();

        rig.Session.OpenDevice(() => second, NoAttach, 800, 600).ShouldBeTrue();

        second.Wireframe.ShouldBe(1);
    }

    /// <summary>An attach that hands nothing over, for the tests about the opening itself.</summary>
    private static void NoAttach(IRenderDevice device, Action specularOff, Action<int> setFullbright)
    {
        // Nothing to hand over: these tests are about the opening, and a device's settings are the next test's.
    }

    [Test]
    public void ViewportResized_ToARealSize_ResizesTheDeviceAndAnEmptyOneDoesNot()
    {
        using Rig rig = Build().WithDevice();

        rig.Window.Viewport = (640, 480);
        rig.Session.ViewportResized();
        rig.Window.Viewport = (0, 480);
        rig.Session.ViewportResized();
        rig.Window.Viewport = (640, 0);
        rig.Session.ViewportResized();

        rig.Device.Sizes.ShouldBe([(640, 480)]);
        rig.Session.Presenter.WorldNeedsProjecting.ShouldBeTrue("a new size is a new projection");
    }

    // ---- keys and the pointer ----

    [Test]
    public void KeyPressed_WhileATextBoxHasTheKeyboard_IsLeftToTheTextBox()
    {
        using Rig rig = Build();
        string reset = rig.Session.Bindings.KeyFor(ViewerAction.ResetSpeed).ShouldNotBeNull();

        rig.Session.KeyPressed(Key(reset, focus: FocusedWidget.Text)).ShouldBeFalse();

        rig.Transport.Speeds.ShouldBeEmpty();
    }

    [Test]
    public void KeyPressed_TheResetSpeedBinding_PutsTheTransportBackToRealTime()
    {
        using Rig rig = Build();
        string reset = rig.Session.Bindings.KeyFor(ViewerAction.ResetSpeed).ShouldNotBeNull();

        rig.Session.KeyPressed(Key(reset)).ShouldBeTrue();

        // ONCE (B547, B825): the key goes to the console, whose one-shot `OnConsoleAction` performs and the key is taken.
        rig.Transport.Speeds.ShouldBe([1d]);
    }

    /// <remarks>
    /// **A bare key on a menu one-shot performs it, once** (B825): WinForms registers no shortcut for `p`, so the menu item
    /// never fired, and the console raised the one-shot for nobody. TF2 runs every bound key through the console; here the
    /// session asks the window to perform the item's action, and the console takes the key, so a registered menu shortcut
    /// on the same key does not fire it a second time. The control: before the press, nothing was performed.
    /// </remarks>
    [TestCase("opendemo", ViewerAction.OpenDemo)]
    [TestCase("togglefullscreen", ViewerAction.FullScreen)]
    [TestCase("cl_showfps", ViewerAction.FrameRate)]
    [TestCase("mat_surfacecolours", ViewerAction.SurfaceColours)]
    public void KeyPressed_ABareKeyOnAMenuOneShot_PerformsItsMenuActionOnce(string command, ViewerAction action)
    {
        using Rig rig = BuildWithAutoexec($"unbindall\nbind p {command}\n");
        string performed = $"PerformMenuAction {action}";
        rig.Window.Told.ShouldNotContain(performed, "the control: nothing performed before the press");

        rig.Session.KeyPressed(Key("p")).ShouldBeTrue("the console takes the key");

        rig.Window.Told.Count(told => told == performed).ShouldBe(1);
    }

    /// <remarks>
    /// **A `+` bind that also resets the camera still gets its release** (B825 review 1, Sonnet): `resetcamera` releases
    /// every held key, and it now runs inside the console's `KeyDown`, so the key it was pressed by was forgotten and its
    /// up never reached the console. Source sends a `+` bind's whole command with the `-` on release, so `-sw` must run.
    /// The control: the press ran `+sw`.
    /// </remarks>
    [Test]
    public void KeyUp_APlusBindThatResetsTheCamera_RunsItsMinusHalf()
    {
        using Rig rig = BuildWithAutoexec(
            "unbindall\nalias +sw \"fps_max 10\"\nalias -sw \"fps_max 20\"\nbind r \"+sw; resetcamera\"\n");

        rig.Session.KeyPressed(Key("r"));
        rig.Settings.Current.FrameRateLimit.ShouldBe(10, "the control: the press ran +sw");

        rig.Session.KeyUp("r");

        rig.Settings.Current.FrameRateLimit.ShouldBe(20);
    }

    /// <remarks>
    /// **The play/pause key pauses** (B825): `demo_togglepause` on `o` reached the console and nothing performed it, since no
    /// menu item carries play/pause. The control: playing before the press.
    /// </remarks>
    [Test]
    public void KeyPressed_ThePlayPauseBinding_PausesPlayback()
    {
        using Rig rig = Build();
        rig.Transport.Playing = true;
        string playPause = rig.Session.Bindings.KeyFor(ViewerAction.PlayPause).ShouldNotBeNull();

        rig.Session.KeyPressed(Key(playPause)).ShouldBeTrue();

        rig.Transport.Playing.ShouldBeFalse();
    }

    /// <remarks>
    /// **A chord bound to play/pause pauses** (B825 review 1, Gemini): a CTRL chord never reaches the console, and no menu
    /// item carries play/pause, so `KeyPressed`'s chord checks are its only route. The control: playing before.
    /// </remarks>
    [Test]
    public void KeyPressed_AChordBoundToPlayPause_PausesPlayback()
    {
        using Rig rig = Build();
        rig.Session.Bindings.Bind(ViewerAction.PlayPause, "CTRL+o");
        rig.Transport.Playing = true;

        rig.Session.KeyPressed(Key("o", binding => binding == "CTRL+o", controlOrAlt: true)).ShouldBeTrue();

        rig.Transport.Playing.ShouldBeFalse();
    }

    /// <remarks>
    /// **A held play/pause chord toggles once** (B825 review 3, Fable): a chord never enters the bound-key set the repeat
    /// guard reads, and a toggle is not idempotent as the resets are, so each repeat paused and played. The view is set
    /// playing again before the repeat, so a toggling repeat would pause it. The control: the press paused.
    /// </remarks>
    [Test]
    public void KeyPressed_AHeldPlayPauseChordRepeating_TogglesOnce()
    {
        using Rig rig = Build();
        rig.Session.Bindings.Bind(ViewerAction.PlayPause, "CTRL+o");
        rig.Transport.Playing = true;

        rig.Session.KeyPressed(Key("o", binding => binding == "CTRL+o", controlOrAlt: true));
        rig.Transport.Playing.ShouldBeFalse("the control: the press paused");

        rig.Transport.Playing = true;
        rig.Session.KeyPressed(Key("o", binding => binding == "CTRL+o", controlOrAlt: true, repeat: true))
            .ShouldBeTrue("the repeat is taken, so the toolkit does not read it as a mnemonic (review 4, Fable)");

        rig.Transport.Playing.ShouldBeTrue("a repeat does not toggle");
    }

    /// <remarks>
    /// **A held screenshot chord writes one file** (B825 review 4, Fable): a matched chord's repeat is taken and runs
    /// nothing, so a held chord is one press, as Source binds it. The control: the press wrote one.
    /// </remarks>
    [Test]
    public void KeyPressed_AHeldScreenshotChordRepeating_CapturesOnce()
    {
        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 0, ScreenshotFolder = Path.Combine(_folder, "shots") }).WithDevice();
        rig.Session.Bindings.Bind(ViewerAction.Screenshot, "CTRL+k");

        rig.Session.KeyPressed(Key("k", binding => binding == "CTRL+k", controlOrAlt: true)).ShouldBeTrue();
        rig.Device.Captured.Count.ShouldBe(1, "the control: the press wrote one");

        rig.Session.KeyPressed(Key("k", binding => binding == "CTRL+k", controlOrAlt: true, repeat: true)).ShouldBeTrue();

        rig.Device.Captured.Count.ShouldBe(1);
    }

    /// <remarks>
    /// **Escape bound to play/pause still leaves full screen** (B825 review 4, Sonnet): Escape never reaches the console,
    /// so the play/pause chord check was its one route to the toggle, ahead of the full-screen exit. The control: full
    /// screen and playing before the press.
    /// </remarks>
    [Test]
    public void KeyPressed_EscapeBoundToPlayPauseInFullScreen_LeavesFullScreenAndKeepsPlaying()
    {
        using Rig rig = Build();
        rig.Session.Bindings.Bind(ViewerAction.PlayPause, "ESCAPE");
        rig.Window.IsFullScreen = true;
        rig.Transport.Playing = true;

        rig.Session.KeyPressed(Key("ESCAPE", binding => binding == "ESCAPE", escape: true)).ShouldBeTrue();

        rig.Window.IsFullScreen.ShouldBeFalse();
        rig.Transport.Playing.ShouldBeTrue();
    }

    /// <remarks>
    /// **Every chord check takes its repeat and runs nothing** (B825 review 5, Sonnet, Fable): a repeat the toolkit saw
    /// again would be a menu mnemonic or shortcut. Every case asserts the repeat is taken. The camera calls always answer
    /// true, so the return cannot tell a guarded repeat from one that cycled again; the log can, since each camera call
    /// writes a line. Reset speed, screenshot and play/pause log nothing, so their run-nothing half is held by their own
    /// tests (`…ResetSpeedChordRepeating_ResetsOnce`, `…ScreenshotChordRepeating_CapturesOnce`,
    /// `…PlayPauseChordRepeating_TogglesOnce`). The control: an unbound chord's repeat is left.
    /// </remarks>
    [TestCase(ViewerAction.SwitchCameraMode)]
    [TestCase(ViewerAction.FirstPersonView)]
    [TestCase(ViewerAction.ThirdPersonView)]
    [TestCase(ViewerAction.ResetSpeed)]
    [TestCase(ViewerAction.ResetCamera)]
    [TestCase(ViewerAction.Screenshot)]
    [TestCase(ViewerAction.PlayPause)]
    public void KeyPressed_AHeldChordRepeating_IsTaken(ViewerAction action)
    {
        // The test's own folder, so the screenshot case writes nothing into the user's captures (review 6, Fable).
        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 0, ScreenshotFolder = Path.Combine(_folder, "shots") });
        rig.Session.KeyPressed(Key("k", binding => binding == "CTRL+k", controlOrAlt: true, repeat: true))
            .ShouldBeFalse("the control: an unbound chord's repeat is left");
        rig.Session.Bindings.Bind(action, "CTRL+k");

        rig.Session.KeyPressed(Key("k", binding => binding == "CTRL+k", controlOrAlt: true));
        int logged = rig.Log.Lines.Count;

        rig.Session.KeyPressed(Key("k", binding => binding == "CTRL+k", controlOrAlt: true, repeat: true)).ShouldBeTrue();

        rig.Log.Lines.Skip(logged).Where(static line => !line.StartsWith("[render] key ", StringComparison.Ordinal))
            .ShouldBeEmpty("a repeat runs nothing; every key logs its own arrival");
    }

    /// <remarks>
    /// **A held reset-speed chord resets once** (B825 review 5, Fable): the bare-key test's repeat is taken by the bound-key
    /// guard ahead of the chord checks, so only a chord reaches this one. The control: the press reset.
    /// </remarks>
    [Test]
    public void KeyPressed_AHeldResetSpeedChordRepeating_ResetsOnce()
    {
        using Rig rig = Build();
        rig.Session.Bindings.Bind(ViewerAction.ResetSpeed, "CTRL+k");

        rig.Session.KeyPressed(Key("k", binding => binding == "CTRL+k", controlOrAlt: true));
        rig.Transport.Speeds.ShouldBe([1d], "the control: the press reset");

        rig.Session.KeyPressed(Key("k", binding => binding == "CTRL+k", controlOrAlt: true, repeat: true));

        rig.Transport.Speeds.ShouldBe([1d]);
    }

    /// <remarks>
    /// **Escape bound to any chord action still leaves full screen** (B825 review 5, Sonnet, Fable): every chord check
    /// takes its key whatever it does, so without the guard each stranded the user. The control: full screen before the
    /// press.
    /// </remarks>
    [TestCase(ViewerAction.SwitchCameraMode)]
    [TestCase(ViewerAction.FirstPersonView)]
    [TestCase(ViewerAction.ThirdPersonView)]
    [TestCase(ViewerAction.ResetSpeed)]
    [TestCase(ViewerAction.ResetCamera)]
    [TestCase(ViewerAction.Screenshot)]
    [TestCase(ViewerAction.PlayPause)]
    public void KeyPressed_EscapeBoundToAnActionInFullScreen_LeavesFullScreen(ViewerAction action)
    {
        // The test's own folder, so a screenshot case reached under sabotage or mutation writes nothing into the user's
        // captures (review 6, Fable).
        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 0, ScreenshotFolder = Path.Combine(_folder, "shots") });
        rig.Session.Bindings.Bind(action, "ESCAPE");
        rig.Window.IsFullScreen = true;

        rig.Session.KeyPressed(Key("ESCAPE", binding => binding == "ESCAPE", escape: true)).ShouldBeTrue();

        rig.Window.IsFullScreen.ShouldBeFalse();
    }

    /// <remarks>
    /// **A held one-shot key does not re-fire on auto-repeat** (B825 review 1, Gemini, Fable): the console took the press,
    /// and Source binds the press, not its repeats. Reset speed, since every call is recorded. The control: the press
    /// itself reset once.
    /// </remarks>
    [Test]
    public void KeyPressed_AHeldResetSpeedKeyRepeating_ResetsOnce()
    {
        using Rig rig = Build();
        string reset = rig.Session.Bindings.KeyFor(ViewerAction.ResetSpeed).ShouldNotBeNull();

        rig.Session.KeyPressed(Key(reset));
        rig.Transport.Speeds.ShouldBe([1d], "the control: the press reset");

        rig.Session.KeyPressed(Key(reset, repeat: true)).ShouldBeTrue();
        rig.Session.KeyPressed(Key(reset, repeat: true));

        rig.Transport.Speeds.ShouldBe([1d], "a repeat does not reset again");
    }

    /// <remarks>
    /// **An alias reaches a session one-shot** (B825): the reset was matched by key in `KeyPressed`, so `alias go resetspeed`
    /// on `p` raised it through the console for nobody. The control: no speed set before the press.
    /// </remarks>
    [Test]
    public void KeyPressed_AnAliasNamingResetSpeed_ResetsTheSpeedOnce()
    {
        using Rig rig = BuildWithAutoexec("unbindall\nalias go resetspeed\nbind p go\n");
        rig.Transport.Speeds.ShouldBeEmpty("the control");

        rig.Session.KeyPressed(Key("p")).ShouldBeTrue();

        rig.Transport.Speeds.ShouldBe([1d]);
    }

    [Test]
    public void KeyPressed_TheScreenshotBinding_CapturesAStampedFileInTheScreenshotFolder()
    {
        string shots = Path.Combine(_folder, "shots");
        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 0, ScreenshotFolder = shots }).WithDevice();
        string screenshot = rig.Session.Bindings.KeyFor(ViewerAction.Screenshot).ShouldNotBeNull();

        rig.Session.KeyPressed(Key(screenshot)).ShouldBeTrue();

        string written = rig.Device.Captured.ShouldHaveSingleItem();
        Path.GetDirectoryName(written).ShouldBe(shots);
        Path.GetFileName(written).ShouldStartWith("shot-");
        Path.GetExtension(written).ShouldBe(".png");
        rig.Session.CaptureFolder.ShouldBe(shots);
    }

    [Test]
    public void KeyPressed_AKeyNothingIsBoundTo_IsNotTaken()
    {
        using Rig rig = Build().WithDevice();

        rig.Session.KeyPressed(Key("F24", static _ => false)).ShouldBeFalse();

        rig.Device.Captured.ShouldBeEmpty();
        rig.Transport.Speeds.ShouldBeEmpty();
    }

    [Test]
    public void KeyPressed_AKeyboardKeyBoundToToggle_RunsItAndTakesTheKey()
    {
        // B547: a keyboard key reached the console only in the free camera, and only for flight; a mouse button
        // always did. The engine's `Key_Event` gives every key to its binding.
        using Rig rig = BuildWithAutoexec("bind \"F7\" \"toggle cl_showpos\"\n");

        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue("a key whose bind ran is the bind's");
        rig.Session.KeyUp("F7").ShouldBeTrue("the bind took the press, so the release is the bind's too");
        rig.Settings.Current.ShowPosition.ShouldBe(1);
        rig.Log.Count("cl_showpos 1 (set at run time)").ShouldBe(1);

        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue();
        rig.Settings.Current.ShowPosition.ShouldBe(0);
    }

    [Test]
    public void KeyPressed_AnAutoRepeat_RunsNothingButIsStillTaken()
    {
        // The engine binds a key's press, not Windows' repeat; a held toggle key would otherwise flicker. The repeat is
        // still the bind's, so a viewer shortcut on the same key cannot fire on it either.
        using Rig rig = BuildWithAutoexec("bind \"F7\" \"toggle cl_showpos\"\n");

        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue();
        // ONE repeat: an even number of extra toggles would land back on 1 and hide a repeat that ran.
        rig.Session.KeyPressed(Key("F7", static _ => false, repeat: true)).ShouldBeTrue();

        rig.Settings.Current.ShowPosition.ShouldBe(1);
    }

    [Test]
    public void KeyPressed_WithControlHeld_IsNotTheBareKeysBind()
    {
        // Source's `bind` has no modifier syntax, so CTRL+F7 is not F7: the viewer's own CTRL chords live there.
        using Rig rig = BuildWithAutoexec("bind \"F7\" \"toggle cl_showpos\"\n");

        rig.Session.KeyPressed(Key("F7", static _ => false, controlOrAlt: true)).ShouldBeFalse();

        rig.Settings.Current.ShowPosition.ShouldBe(0);
    }

    [Test]
    public void KeyPressed_AViewerDefaultKeyReboundByTheConfig_RunsTheConfigsBindInstead()
    {
        // D101: the user's config wins a collision. HOME is the viewer's reset-speed default.
        using Rig rig = BuildWithAutoexec("bind \"HOME\" \"toggle cl_showpos\"\n");

        rig.Session.KeyPressed(Key("HOME")).ShouldBeTrue();

        rig.Settings.Current.ShowPosition.ShouldBe(1);
        rig.Transport.Speeds.ShouldBeEmpty();
    }

    [Test]
    public void KeyUp_AKeyAFocusedControlKept_RunsNoReleaseHalf()
    {
        // The engine releases only what it pressed (its keys keep their own down state): typing `x` in the search box
        // must not run `-x`.
        using Rig rig = BuildWithAutoexec("alias +x \"\"\nalias -x \"cl_showpos 1\"\nbind \"x\" \"+x\"\n");

        rig.Session.KeyPressed(Key("x", focus: FocusedWidget.Text)).ShouldBeFalse();
        rig.Session.KeyUp("x").ShouldBeFalse();

        rig.Settings.Current.ShowPosition.ShouldBe(0);
    }

    [Test]
    public void KeyUp_AKeyTheConsolePressed_RunsItsReleaseHalf()
    {
        // The control on the test above: the same bind, pressed with nothing focused.
        using Rig rig = BuildWithAutoexec("alias +x \"\"\nalias -x \"cl_showpos 1\"\nbind \"x\" \"+x\"\n");

        rig.Session.KeyPressed(Key("x")).ShouldBeTrue();
        rig.Session.KeyUp("x").ShouldBeTrue();

        rig.Settings.Current.ShowPosition.ShouldBe(1);
    }

    [Test]
    public void KeyPressed_EscapeBoundByTheConfigInFullScreen_StillLeavesAndRunsNoBind()
    {
        // Escape is the game UI's, ahead of the bindings, and here it is the way out of full screen.
        using Rig rig = BuildWithAutoexec("bind \"ESCAPE\" \"toggle cl_showpos\"\n");
        rig.Window.IsFullScreen = true;

        rig.Session.KeyPressed(Key("ESCAPE", static _ => false, escape: true)).ShouldBeTrue();

        rig.Window.Told.ShouldContain("LeaveFullScreen");
        rig.Settings.Current.ShowPosition.ShouldBe(0);
    }

    [Test]
    public void KeyPressed_ARepeatOfABoundKeyWithControlTapped_IsStillTheBinds()
    {
        // Hold F7, tap CTRL: the repeats arrive as CTRL+F7, and must not become the viewer's CTRL chord.
        using Rig rig = BuildWithAutoexec("bind \"F7\" \"toggle cl_showpos\"\n");

        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue();

        bool shortcutAsked = false;

        rig.Session.KeyPressed(Key("F7", _ => shortcutAsked = true, controlOrAlt: true, repeat: true)).ShouldBeTrue();

        shortcutAsked.ShouldBeFalse("no viewer shortcut may even be considered for the bind's repeat");
    }

    [Test]
    public void KeyPressed_ALoneAltBoundByTheConfig_IsTakenAndItsReleaseIsTheBindsToo()
    {
        // Its release is what the toolkit turns into menu mode (B542); the window consumes it when this says so.
        using Rig rig = BuildWithAutoexec("bind \"ALT\" \"toggle cl_showpos\"\n");

        rig.Session.KeyPressed(Key("ALT", static _ => false, controlOrAlt: true)).ShouldBeTrue();
        rig.Session.KeyUp("ALT").ShouldBeTrue();

        rig.Settings.Current.ShowPosition.ShouldBe(1);
    }

    [Test]
    public void KeyPressed_EscapeInFullScreen_LeavesItAndSaysTheKeyArrived()
    {
        using Rig rig = Build();
        rig.Window.IsFullScreen = true;

        rig.Session.KeyPressed(Key("ESCAPE", static _ => false, escape: true)).ShouldBeTrue();

        rig.Window.Told.ShouldContain("LeaveFullScreen");
        rig.Log.Count("[render] ESCAPE reached the form; full screen is True").ShouldBe(1);
    }

    [Test]
    public void KeyPressed_EscapeWhileWindowed_IsNotTakenButIsStillLogged()
    {
        using Rig rig = Build();

        rig.Session.KeyPressed(Key("ESCAPE", static _ => false, escape: true)).ShouldBeFalse();

        rig.Window.Told.ShouldNotContain("LeaveFullScreen");
        rig.Log.Count("[render] ESCAPE reached the form; full screen is False").ShouldBe(1);
    }

    [Test]
    public void PointerMoved_WithNoMapDrawn_TurnsNothingWhetherOrNotAButtonIsDown()
    {
        // The camera refuses a drag until a map with an outline is drawn, and the synthetic map has none: so this holds
        // the refusal, and that a refused drag leaves the world as projected. The turn itself is `CameraPresenter.Drag`'s,
        // tested there; the distance handed to it needs a drawn map and is not reached by a unit test.
        using Rig rig = Build();
        rig.Session.Presenter.WorldProjected();
        (float Pitch, float Yaw) before = rig.Session.Presenter.Camera.Free.Angles;

        rig.Session.PointerMoved(50, 60);
        rig.Session.PointerDown("MOUSE1", drags: true, 10, 10);
        rig.Session.PointerMoved(30, 10);
        rig.Session.PointerUp("MOUSE1");

        rig.Session.Presenter.WorldNeedsProjecting.ShouldBeFalse();
        rig.Session.Presenter.Camera.Free.Angles.ShouldBe(before);
    }

    [Test]
    public void PointerDown_ANonDragButton_StartsNoDrag()
    {
        using Rig rig = Build();
        rig.Session.Presenter.WorldProjected();

        rig.Session.PointerDown("MOUSE2", drags: false, 10, 10);
        rig.Session.PointerMoved(200, 10);

        rig.Session.Presenter.WorldNeedsProjecting.ShouldBeFalse();
    }

    // ---- the idle loop ----

    [Test]
    public void Idle_WithNoFrameLimit_DrawsUntilAMessageWaitsAndSleepsForNothing()
    {
        using Rig rig = Build();
        FakeIdleHost host = new();
        host.Queue.Enqueue(null);
        host.Queue.Enqueue(null);
        host.Queue.Enqueue(0x0200);

        rig.Session.Idle(host);

        host.Looks.ShouldBe(3, "one look after each frame, and the third finds the mouse move");
        host.Sleeps.ShouldBeEmpty();
        host.Waits.ShouldBeEmpty("nothing ended a burst before this one");
    }

    [Test]
    public void Idle_WithoutFocusAndANoFocusSleepSet_SleepsThatLongBeforeEachFrame()
    {
        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 0, NoFocusSleep = 50 });
        rig.Window.IsActiveApp = false;
        FakeIdleHost host = new();
        host.Queue.Enqueue(null);
        host.Queue.Enqueue(0x0200);

        rig.Session.Idle(host);

        host.Sleeps.ShouldBe([50, 50]);
    }

    /// <remarks>
    /// **A measurement is never clamped by the focus sleep, from wherever the value came** — the saved settings (50 here) or a
    /// `.cfg` executed after launch (the `SetConVar` below, the console's route). Held where the sleep is READ, so nothing set
    /// later can put it back; said once, not once a frame. The control is the test above: the same settings, no `--measure`, sleep.
    /// </remarks>
    [Test]
    public void Idle_WhileMeasuringWithANoFocusSleepSet_NeverSleepsAndSaysSoOnce()
    {
        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 0, NoFocusSleep = 50 }, 0.5, "--measure", "5");
        rig.Window.IsActiveApp = false;
        rig.Settings.SetConVar(ViewerSettings.NoFocusSleepCommand, "50");
        FakeIdleHost host = new();
        host.Queue.Enqueue(null);
        host.Queue.Enqueue(0x0200);

        rig.Session.Idle(host);

        host.Sleeps.ShouldBeEmpty();
        rig.Log.Count("engine_no_focus_sleep 50 ignored while measuring").ShouldBe(1);
    }

    [Test]
    public void SetPhong_FromTheMenu_IsWhatTheMenuReadsBack()
    {
        // The menu re-reads its checks on opening (D215); a Phong the menu set must be what it reads, or opening
        // View would put the check back.
        using Rig rig = Build(new ViewerSettings { Phong = true });

        rig.Session.SetPhong(false);

        rig.Session.Menu.Phong.ShouldBeFalse();
    }

    [Test]
    public void Idle_WhileTheApplicationIsActive_DoesNotSleep()
    {
        // CEngine::Frame (engine.dll FUN_18020cbd0) sleeps on `!IsActiveApp()` alone: one of our own dialogs holding
        // the keyboard is still the active application, so the window's focus is not consulted at all.
        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 0, NoFocusSleep = 50 });
        rig.Window.IsActiveApp = true;
        FakeIdleHost host = new();
        host.Queue.Enqueue(null);
        host.Queue.Enqueue(0x0200);

        rig.Session.Idle(host);

        host.Sleeps.ShouldBeEmpty();
    }

    [Test]
    public void Idle_BeforeTheNextFrameIsDue_WaitsRatherThanDraws()
    {
        // A clock stuck at one millisecond: the first frame is always allowed, and at 10 fps the second is 99 ms away,
        // far enough to sleep rather than spin.
        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 10 }, frameSeconds: 0.001);
        FakeIdleHost host = new();
        host.Queue.Enqueue(null);
        host.Queue.Enqueue(null);
        host.Queue.Enqueue(0x0200);

        rig.Session.Idle(host);

        host.Sleeps.ShouldBe([1, 1], "the first pass draws; the two after it wait a millisecond each");
    }

    [Test]
    public void Idle_AfterACallEndedTheLastBurst_WaitsForTrafficAndYieldsWhenItComes()
    {
        using Rig rig = Build();
        FakeIdleHost host = new();
        host.Queue.Enqueue(0x0400);
        rig.Session.Idle(host);
        int looksAfterFirst = host.Looks;
        host.Waits.ShouldBeEmpty("the first burst follows nothing, so it waits for nothing");
        host.Arrives = true;

        rig.Session.Idle(host);

        host.Waits.ShouldBe([FramePacer.TrafficWaitSeconds]);
        (host.Looks - looksAfterFirst).ShouldBe(1, "it looks once to learn what arrived, and draws nothing");
    }

    /// <remarks>
    /// **With a UI Automation client attached, a paint waits, the frame's own included** (finding 88): the rule before
    /// finding 88, restored after narrowing it timed a CI UI test out.
    /// </remarks>
    [Test]
    public void Idle_AfterAPaintWithAnAutomationClient_WaitsForTraffic()
    {
        using Rig rig = Build();
        FakeIdleHost host = new() { AutomationListening = true };
        host.Queue.Enqueue(0x000F);
        rig.Session.Idle(host);
        host.Arrives = true;

        rig.Session.Idle(host);

        host.Waits.ShouldBe([FramePacer.TrafficWaitSeconds]);
    }

    /// <remarks>
    /// **With no UI Automation client, nothing waits** (finding 88): the transport bar repaints after every frame, and
    /// waiting a millisecond after each was 1.0-1.5 ms of every frame for a caller that does not exist.
    /// </remarks>
    [Test]
    public void Idle_WithNoAutomationClient_NeverWaitsForTraffic()
    {
        using Rig rig = Build();
        FakeIdleHost host = new() { AutomationListening = false };
        host.Queue.Enqueue(0x0400);
        host.Queue.Enqueue(0x000F);
        rig.Session.Idle(host);
        host.Arrives = true;

        rig.Session.Idle(host);
        rig.Session.Idle(host);

        host.Waits.ShouldBeEmpty("a call or a paint is only followed by traffic when a client is attached");
    }

    [Test]
    public void Idle_AfterAKeyEndedTheLastBurst_DoesNotWaitForTraffic()
    {
        using Rig rig = Build();
        FakeIdleHost host = new();
        host.Queue.Enqueue(0x0100);
        rig.Session.Idle(host);
        host.Arrives = true;

        rig.Session.Idle(host);

        host.Waits.ShouldBeEmpty("input is answered with a frame, not a wait");
    }

    // ---- startup ----

    [Test]
    public async Task WindowShown_WithOneDemoNamed_OpensItAndKeepsTheLoad()
    {
        string demo = WriteDemo("named", ticks: 2000);
        using Rig rig = Build(demo);

        rig.Session.Paths.ShouldBe([demo]);
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        DemoLoadResult result = await rig.Session.Loading.ConfigureAwait(false);

        result.Loaded.ShouldBeTrue(result.Message);
        rig.Session.Presenter.DemoPath.ShouldBe(demo);
        rig.Transport.DemoLength.ShouldBe(2000);
    }

    [Test]
    public void WindowShown_AMissingFileOnACaptureRun_SaysWhichOnStderrExitsOneAndCloses()
    {
        string absent = Path.Combine(_folder, "absent.dem");
        using Rig rig = Build(absent, "--shot", Path.Combine(_folder, "out.png"));

        rig.Session.PlanStartup();
        rig.Log.Count($"[demo] {absent} is not a file that exists; nothing was opened").ShouldBe(1);
        _exits.ShouldBeEmpty("nothing ends before the window is up");

        rig.Session.WindowShown();

        rig.Error.ToString().Trim().ShouldBe($"{absent} is not a file that exists; nothing was opened");
        _exits.ShouldBe([1]);
        rig.Window.Told.ShouldContain("Close posted");
        rig.Log.Count($"[viewer] {absent} is not a file that exists; nothing was opened; the headless run stops").ShouldBe(1);
    }

    [Test]
    public void WindowShown_AMissingFileForAPerson_LeavesTheWindowOpen()
    {
        using Rig rig = Build(Path.Combine(_folder, "absent.dem"));

        rig.Session.PlanStartup();
        rig.Session.WindowShown();

        rig.Error.ToString().ShouldBeEmpty();
        _exits.ShouldBeEmpty();
        rig.Window.Count("Close").ShouldBe(0);
    }

    [Test]
    public void WindowShown_SeveralPathsOnAMeasureRun_SaysACaptureTakesOneAndStops()
    {
        string first = WriteDemo("first");
        string second = WriteDemo("second");
        using Rig rig = Build(first, second, "--measure", "5");

        rig.Session.PlanStartup();
        rig.Session.WindowShown();

        rig.Log.Count("[demo] 2 paths listed in the playlist; none opened on its own").ShouldBe(1);
        rig.Error.ToString().Trim().ShouldBe(
            $"2 paths were given where a capture takes one demo ({first} | {second}); a path with a space in it must arrive as ONE argument");
        _exits.ShouldBe([1]);
        rig.Session.Presenter.HasDemo.ShouldBeFalse();
    }

    [Test]
    public void PlanStartup_WithNoPaths_PlansNothingAndSaysNothing()
    {
        using Rig rig = Build();

        rig.Session.PlanStartup();
        rig.Session.WindowShown();

        rig.Log.Count("[demo]").ShouldBe(0);
        rig.Session.Loading.Result.Outcome.ShouldBe(DemoLoadOutcome.Superseded);
    }

    [Test]
    public void LoadSelected_WithNothingSelected_AnswersThatNoDemoIsSelected()
    {
        using Rig rig = Build();

        DemoLoadResult result = rig.Session.LoadSelected(null).Result;

        result.Outcome.ShouldBe(DemoLoadOutcome.Superseded);
        result.Message.ShouldBe("No demo is selected.");
    }

    [Test]
    public async Task Activate_ARow_LoadsItAndKeepsTheLoadWhereItCanBeAwaited()
    {
        string demo = WriteDemo("row");
        using Rig rig = Build();

        rig.Session.Activate(demo);

        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();
        rig.Session.Presenter.DemoPath.ShouldBe(demo);
    }

    /// <remarks>
    /// **The window's own map-read projection says it is the read's** (B779 review 29 item 2): `ILoadView.MapRead` passes
    /// `fromTheRead`, so the barrier's guard lets it project. Without it the read's own projection would be held off like a
    /// window's mid-read projection — and with a real map the world would wait for a frame instead of going up with the
    /// read. Here a session with a device opens a demo through the playlist (the async load): the read's projection
    /// **ran** — it asked the window for the viewport's size, inside the read, before the read reported its models
    /// (review 30 item 3) — and was not held off. The control: a projection made while a map reads, from outside the read,
    /// is held off and says so.
    /// </remarks>
    [Test]
    public async Task Activate_ADemoWithItsMap_TheReadsOwnProjectionRunsAndIsNotHeldOff()
    {
        string demo = WriteDemo("projected");
        using Rig rig = Build().WithDevice();
        bool? controlHeldOff = null;
        int readsBefore = rig.Window.ViewportReads;
        int? readsInTheRead = null;
        rig.Window.ProjectingDuringMapRead = () => readsInTheRead = rig.Window.ViewportReads - readsBefore;

        rig.Session.Activate(demo);
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        readsInTheRead.ShouldBe(1, "the read's own projection ran: one viewport size asked, by `MapRead`");
        rig.Log.Count(ViewerPresenter.HeldOffWhileAMapReads).ShouldBe(0, "and was not held off");

        // The control: the same projection made from outside a read in flight is held off, and the line is written.
        string second = WriteDemo("control");
        rig.Window.ProjectingDuringMapRead = () =>
        {
            rig.Session.Presenter.ProjectMap(surfaceColours: false, (1280, 720));
            controlHeldOff = rig.Log.Count(ViewerPresenter.HeldOffWhileAMapReads) == 1;
        };
        rig.Session.Activate(second);
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        controlHeldOff.ShouldBe(true);
    }

    /// <remarks>
    /// **A world lost during a load says so once** (B779 review 30 item 3): the reload waits for the load, and two frames'
    /// "lost" answers during it write one line, not one a frame. Here a session with a demo open, a second load, and two
    /// such answers while it reads. The control: the line was written at all.
    /// </remarks>
    [Test]
    public async Task Activate_AWorldLostTwiceDuringALoad_SaysSoOnce()
    {
        using Rig rig = Build().WithDevice();
        rig.Session.Activate(WriteDemo("open"));
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();
        Scene.WorldUpload lost = new(Uploaded: false, Problem: "Reloading the map…", NeedsReload: true);
        rig.Window.ProjectingDuringMapRead = () =>
        {
            Scene.LoadedMap map = rig.Session.Presenter.Map.ShouldNotBeNull();
            rig.Session.Presenter.FollowUpload(lost, map);
            rig.Session.Presenter.FollowUpload(lost, map);
        };

        rig.Session.Activate(WriteDemo("second"));
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        rig.Log.Count("world textures lost while a load is in flight").ShouldBe(1);
    }

    [Test]
    public void ReportStartup_WithNothingOpening_LogsTheBootTimeAndAddsItToTheStatus()
    {
        using Rig rig = Build();

        rig.Session.ReportStartup(TimeSpan.FromMilliseconds(1234));

        rig.Log.Count("[viewer] startup: window shown 1234 ms after the process started").ShouldBe(1);
        rig.Window.Status.ShouldBe("No demo loaded. Started in 1.23 s.");
    }

    [Test]
    public async Task ReportStartup_WithADemoOpen_LogsButLeavesTheDemosStatus()
    {
        using Rig rig = Build();
        (await rig.Session.Presenter.LoadDemoAsync(WriteDemo("open")).ConfigureAwait(false)).Loaded.ShouldBeTrue();
        string? before = rig.Window.Status;

        rig.Session.ReportStartup(TimeSpan.FromSeconds(2));

        rig.Window.Status.ShouldBe(before);
        rig.Log.Count("startup: window shown 2000 ms").ShouldBe(1);
    }

    [Test]
    public void ApplyLogVerbosity_WithAListener_HandsItTheSettingsLevelAndSaysSo()
    {
        using Rig rig = Build(new ViewerSettings { Developer = 1 });
        List<Microsoft.Extensions.Logging.LogLevel> set = [];
        rig.Session.SetLogVerbosity = set.Add;

        rig.Session.ApplyLogVerbosity();

        set.ShouldBe([Microsoft.Extensions.Logging.LogLevel.Debug]);
        rig.Log.Count("[viewer] developer 1; the log accepts Debug and above").ShouldBe(1);
    }

    [Test]
    public void ApplyLogVerbosity_WithNobodyListening_SaysNothing()
    {
        using Rig rig = Build();

        rig.Session.ApplyLogVerbosity();

        rig.Log.Count("the log accepts").ShouldBe(0);
    }

    [Test]
    public void ShouldAskForGameFolder_ForAPersonWithNoInstall_SaysNothingWasFoundAndAsks()
    {
        using Rig rig = Build();

        rig.Session.ShouldAskForGameFolder().ShouldBeTrue();

        rig.Log.Count("[viewer] " + ViewerSession.GameFolderLog + ViewerSession.GameFolderNotFound).ShouldBe(1);
    }

    [Test]
    public void ShouldAskForGameFolder_OnACaptureRun_SaysWhatWasFoundAndDoesNotAsk()
    {
        using Rig rig = Build(WriteDemo("shot"), "--shot", Path.Combine(_folder, "out.png"));

        rig.Session.ShouldAskForGameFolder().ShouldBeFalse("a dialog nobody sees would hang the run");

        rig.Log.Count(ViewerSession.GameFolderLog + ViewerSession.GameFolderNotFound).ShouldBe(1);
    }

    [Test]
    public void GameFolderChosen_AFolderThatIsNotTf2_SaysSoAndRereadsNoBindings()
    {
        using Rig rig = Build();

        rig.Session.GameFolderChosen(_folder);

        rig.Window.Status.ShouldNotBeNull().ShouldStartWith(SettingsPresenter.NotAGameFolder);
        rig.Window.Told.ShouldNotContain("ShowBindings");
    }

    /// <remarks>
    /// **The configs read from a folder chosen later are followed by the launch line again** (B811 review 2, Fable), as
    /// `stuffcmds` follows `exec`: the new folder's autoexec admits cheats, so the line's `+mat_fullbright 1` refused at
    /// startup is applied. The control: before the folder, it was refused.
    /// </remarks>
    [Test]
    public void GameFolderChosen_WhoseAutoexecAdmitsCheats_RunsTheLaunchLineAgain()
    {
        using Rig rig = Build("+mat_fullbright", "1");
        string tf = Path.Combine(_folder, "chosen", "tf");
        Directory.CreateDirectory(Path.Combine(tf, "cfg"));
        File.WriteAllText(Path.Combine(tf, SteamInstall.Recogniser), string.Empty);
        File.WriteAllText(Path.Combine(tf, "cfg", "autoexec.cfg"), "sv_cheats 1\n");

        rig.Settings.Current.ConVarText("mat_fullbright").ShouldNotBe("1", "the control: refused with no config");

        _gameFolder = tf;
        rig.Session.GameFolderChosen(tf);

        rig.Settings.Current.ConVarText("mat_fullbright").ShouldBe("1");
    }

    /// <remarks>
    /// **The folder's configs and the line run from `sv_cheats 0`, as a fresh start does** (B811 review 3, Fable, Sonnet):
    /// `+mat_fullbright 1 +sv_cheats 1` refuses the fullbright and leaves cheats on; a chosen folder whose autoexec sets no
    /// `sv_cheats` must refuse it again, as the next launch would. The control: the same cheat after the line's
    /// `+sv_cheats 1` is admitted through the same reload.
    /// </remarks>
    [Test]
    public void GameFolderChosen_AfterALineThatTurnedCheatsOnLast_StillRefusesItsEarlierCheat()
    {
        string tf = Path.Combine(_folder, "chosen", "tf");
        Directory.CreateDirectory(Path.Combine(tf, "cfg"));
        File.WriteAllText(Path.Combine(tf, SteamInstall.Recogniser), string.Empty);
        File.WriteAllText(Path.Combine(tf, "cfg", "autoexec.cfg"), "cl_showfps 0\n");

        using (Rig control = Build("+sv_cheats", "1", "+mat_fullbright", "1"))
        {
            _gameFolder = tf;
            control.Session.GameFolderChosen(tf);
            control.Settings.Current.ConVarText("mat_fullbright").ShouldBe("1", "the control: admitted after its sv_cheats");
            _gameFolder = null;
        }

        string? unset;

        using (Rig plain = Build())
        {
            unset = plain.Settings.Current.ConVarText("mat_fullbright");
        }

        using Rig rig = Build("+mat_fullbright", "1", "+sv_cheats", "1");

        _gameFolder = tf;
        rig.Session.GameFolderChosen(tf);

        rig.Window.Status.ShouldNotBeNull().ShouldStartWith(SettingsPresenter.GameFolderSet, Case.Sensitive, "the folder taken");
        rig.Settings.Current.ConVarText("mat_fullbright").ShouldBe(unset);
        rig.Log.Count("Can't use cheat cvar mat_fullbright").ShouldBe(2, "refused at startup and again after the folder");
    }

    /// <remarks>
    /// **A cheat the saved settings hold survives a folder chosen later, as it survives a fresh start** (B811 review 4,
    /// Fable, Sonnet, Grok): `settings.cfg`'s `mat_fullbright 1` with `+sv_cheats 1` on the line; the reload's `sv_cheats`
    /// starts at 0 without reverting it. The control: the same settings without the reload are fullbright too.
    /// </remarks>
    [Test]
    public void GameFolderChosen_WithASavedCheatAndCheatsOn_KeepsTheSavedValue()
    {
        string tf = Path.Combine(_folder, "chosen", "tf");
        Directory.CreateDirectory(Path.Combine(tf, "cfg"));
        File.WriteAllText(Path.Combine(tf, SteamInstall.Recogniser), string.Empty);
        File.WriteAllText(Path.Combine(tf, "cfg", "autoexec.cfg"), "cl_showfps 0\n");

        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 0 }.Set("mat_fullbright", "1"), 0.5, "+sv_cheats", "1");

        rig.Settings.Current.ConVarText("mat_fullbright").ShouldBe("1", "the control: the saved value at startup");

        _gameFolder = tf;
        rig.Session.GameFolderChosen(tf);

        rig.Window.Status.ShouldNotBeNull().ShouldStartWith(SettingsPresenter.GameFolderSet, Case.Sensitive, "the folder taken");
        rig.Settings.Current.ConVarText("mat_fullbright").ShouldBe("1");
    }

    /// <remarks>
    /// **A config's refused cheat is logged once, as TF2 prints it once** (B811 review 5, Sonnet): after the settings are
    /// attached, their `CheatRefused` handler prints TF2's line, and the load's own list printed it a second time. A chosen
    /// folder's autoexec sets `mat_wireframe 1` with cheats off.
    /// </remarks>
    [Test]
    public void GameFolderChosen_WhoseAutoexecSetsACheatWithCheatsOff_LogsTheRefusalOnce()
    {
        string tf = Path.Combine(_folder, "chosen", "tf");
        Directory.CreateDirectory(Path.Combine(tf, "cfg"));
        File.WriteAllText(Path.Combine(tf, SteamInstall.Recogniser), string.Empty);
        File.WriteAllText(Path.Combine(tf, "cfg", "autoexec.cfg"), "mat_wireframe 1\n");
        using Rig rig = Build();

        _gameFolder = tf;
        rig.Session.GameFolderChosen(tf);

        rig.Window.Status.ShouldNotBeNull().ShouldStartWith(SettingsPresenter.GameFolderSet, Case.Sensitive, "the folder taken");
        rig.Log.Count("Can't use cheat cvar mat_wireframe in multiplayer").ShouldBe(1);
    }

    [Test]
    public void GameFolderDeclined_AtStartup_PutsTheSettingsOwnersWordsInTheStatus()
    {
        using Rig rig = Build();

        rig.Session.GameFolderDeclined();

        rig.Window.Status.ShouldNotBeNullOrEmpty();
        rig.Window.Status.ShouldNotBe("No demo loaded.");
        rig.Session.ShouldAskForGameFolder().ShouldBeFalse("declining once stops the asking");
    }

    // ---- what a load asks ----

    [Test]
    public void AutoPlay_ByTheFlagByTheVariableAndByNeither_SaysWhichAsked()
    {
        using Rig flagged = Build("--autoplay");
        ((ILoadView)flagged.Session).AutoPlay.ShouldBe((true, "--autoplay"));

        using Rig variable = Build();
        _environment[ViewerSession.AutoPlayVariable] = "1";
        ((ILoadView)variable.Session).AutoPlay.ShouldBe((true, ViewerSession.AutoPlayVariable + " is set"));

        using Rig neither = Build();
        ((ILoadView)neither.Session).AutoPlay.Play.ShouldBeFalse();
    }

    [Test]
    public async Task DemoOpened_WithATimescaleOnTheCommandLine_SetsTheTransportsSpeed()
    {
        using Rig rig = Build("+demo_timescale", "2");

        (await rig.Session.Presenter.LoadDemoAsync(WriteDemo("fast")).ConfigureAwait(false)).Loaded.ShouldBeTrue();

        rig.Transport.Speeds.ShouldBe([2d]);
    }

    [Test]
    public async Task DemoOpened_WithNoTimescaleAsked_LeavesTheSpeedAlone()
    {
        using Rig rig = Build();

        (await rig.Session.Presenter.LoadDemoAsync(WriteDemo("plain")).ConfigureAwait(false)).Loaded.ShouldBeTrue();

        rig.Transport.Speeds.ShouldBeEmpty();
    }

    [Test]
    public void LoadViewMembers_StatusLoadingAndTheWorld_GoOnToTheWindowAndTheDevice()
    {
        using Rig rig = Build().WithDevice();
        ILoadView load = rig.Session;
        ICameraView camera = rig.Session;

        load.Status = "reading";
        load.Status.ShouldBe("reading");
        load.ReportLoading("Decoding", 0.5);
        load.HideLoading();
        load.ClearWorld();
        camera.Say(null);
        load.Status.ShouldBe(string.Empty, "a null status reads back as empty");
        rig.Session.Presenter.WorldProjected();
        int redraws = rig.Window.Count("Redraw");
        camera.CameraChanged();

        rig.Window.Told.ShouldContain("ReportLoading Decoding");
        rig.Window.Told.ShouldContain("HideLoading");
        rig.Device.Cleared.ShouldBe(1);
        rig.Session.Presenter.WorldNeedsProjecting.ShouldBeTrue();
        (rig.Window.Count("Redraw") - redraws).ShouldBe(1);
    }

    [Test]
    public async Task Scrubbing_WithADemoOpen_ShowsTheMomentAndAsksForARepaint()
    {
        using Rig rig = Build();
        rig.Transport.Scrub(5);
        rig.Window.Count("Redraw").ShouldBe(0, "with no demo a scrub draws nothing");
        (await rig.Session.Presenter.LoadDemoAsync(WriteDemo("scrubbed")).ConfigureAwait(false)).Loaded.ShouldBeTrue();
        int redraws = rig.Window.Count("Redraw");

        rig.Transport.Scrub(40);

        (rig.Window.Count("Redraw") - redraws).ShouldBe(1);
    }

    // ---- the playlist ----

    [Test]
    public void AddToLibrary_AFolderOfTwoDemos_ShowsTwoRowsThenSaysTheCount()
    {
        WriteDemo("alpha");
        WriteDemo("beta");
        using Rig rig = Build();

        rig.Session.AddToLibrary(string.Empty, _folder);

        rig.Window.Told.TakeLast(2).ShouldBe(["ShowPlaylist 2", "Status: 2 demos."]);
        rig.Session.PlaylistRow(0).ShouldNotBeNull().Name.ShouldBe("alpha.dem");
        rig.Session.PlaylistRow(1).ShouldNotBeNull().Path.ShouldBe(Path.Combine(_folder, "beta.dem"));
        rig.Session.PlaylistRow(2).ShouldBeNull();
    }

    [Test]
    public void FilterLibrary_ToOneName_ShowsOneRowAndLeavesTheStatus()
    {
        WriteDemo("alpha");
        WriteDemo("beta");
        using Rig rig = Build();
        rig.Session.AddToLibrary(string.Empty, _folder);
        string? status = rig.Window.Status;

        rig.Session.FilterLibrary("bet");

        rig.Window.Told[^1].ShouldBe("ShowPlaylist 1");
        rig.Session.PlaylistRow(0).ShouldNotBeNull().Name.ShouldBe("beta.dem");
        rig.Window.Status.ShouldBe(status);
    }

    // ---- export and compile ----

    [Test]
    public void OfferExport_WithNoDemoOpen_SaysToOpenOneAndOffersNothing()
    {
        using Rig rig = Build();

        rig.Session.OfferExport().ShouldBeNull();

        rig.Window.Status.ShouldBe("Open a demo first.");
        rig.Session.FileWork.Result.ShouldBe("Open a demo first.");
        rig.Log.Count("[viewer] export: pressed with no demo open").ShouldBe(1);
    }

    [Test]
    public async Task Export_ThenCompile_SayWhatEachIsDoingThenWhatItDidAndRebuildTheSameBytes()
    {
        string demo = WriteDemo("round");
        string text = Path.Combine(_folder, "round.txt");
        string rebuilt = Path.Combine(_folder, "rebuilt.dem");
        using Rig rig = Build();
        (await rig.Session.Presenter.LoadDemoAsync(demo).ConfigureAwait(false)).Loaded.ShouldBeTrue();

        rig.Session.OfferExport().ShouldBe("round.txt");
        rig.Session.Export(text);
        string exported = await rig.Session.FileWork.ConfigureAwait(false);
        ViewerSession.OfferCompile(text).ShouldBe("round.dem");
        rig.Session.Compile(text, rebuilt);
        string compiled = await rig.Session.FileWork.ConfigureAwait(false);

        rig.Window.Told.ShouldContain("Status: Exporting round.dem...");
        rig.Window.Told.ShouldContain("Status: Compiling round.txt...");
        exported.ShouldStartWith("Exported ");
        compiled.ShouldStartWith("Compiled ");
        rig.Window.Status.ShouldBe(compiled);
        rig.Log.Count("[viewer] " + exported).ShouldBe(1);
        rig.Log.Count($"[viewer] compile: {text} to {rebuilt}").ShouldBe(1);
        (await File.ReadAllBytesAsync(rebuilt).ConfigureAwait(false)).ShouldBe(await File.ReadAllBytesAsync(demo).ConfigureAwait(false));
    }

    /// <remarks>
    /// **The path a dialog returned is logged as the work starts** (B558): a UI test whose save dialog stayed open after OK
    /// left the viewer's log with the press and nothing after it, so whether the dialog ever answered was unreadable. The
    /// press alone, with no dialog answer, is the control: it logs the press and no path.
    /// </remarks>
    [Test]
    public async Task Export_AfterTheDialogAnswers_LogsThePathItWritesTo()
    {
        string demo = WriteDemo("round");
        string text = Path.Combine(_folder, "round.txt");
        using Rig rig = Build();
        (await rig.Session.Presenter.LoadDemoAsync(demo).ConfigureAwait(false)).Loaded.ShouldBeTrue();

        rig.Session.OfferExport();
        rig.Log.Count("[viewer] export: to ").ShouldBe(0);

        rig.Session.Export(text);
        await rig.Session.FileWork.ConfigureAwait(false);

        rig.Log.Count("[viewer] export: to " + text).ShouldBe(1);
    }

    [Test]
    public async Task Compile_AFileThatIsNotThere_SaysFailedRatherThanThrowing()
    {
        using Rig rig = Build();

        rig.Session.Compile(Path.Combine(_folder, "absent.txt"), Path.Combine(_folder, "out.dem"));
        string result = await rig.Session.FileWork.ConfigureAwait(false);

        result.ShouldStartWith("Failed: ");
        rig.Window.Status.ShouldBe(result);
    }

    // ---- the capture ----

    private static void Frames(ViewerSession session, int count)
    {
        for (int frame = 0; frame < count; frame++)
        {
            session.RenderFrame().ShouldBeTrue();
        }
    }

    [Test]
    public async Task RenderFrame_OnACaptureRun_SeeksToTheTickThenCapturesOnceAndCloses()
    {
        string shot = Path.Combine(_folder, "out.png");
        using Rig rig = Build(WriteDemo("shot"), "--shot", shot, "--tick", "40").WithDevice();
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        Frames(rig.Session, 44);
        rig.Device.Captured.ShouldBeEmpty("forty frames settle before the opening state, and forty-five more before the shot");
        Frames(rig.Session, 60);

        rig.Transport.ShownTicks.ShouldContain(40);
        rig.Session.PlaybackPosition.ShouldBe(40d, "the clock too, not just the transport: a reprojection re-reads the moment from it");
        rig.Log.Count("[viewer] opening state applied at tick 40").ShouldBe(1);
        rig.Log.Count("[viewer] first frame after the opening state draws the camera placed at tick 40").ShouldBe(1);
        rig.Device.Captured.ShouldBe([shot]);
        rig.Window.Count("Close posted").ShouldBe(1);
    }

    /// <remarks>
    /// **A shot taken with cheats records `sv_cheats`, though it is replicated** (B811 review 7, Fable): without it `--like`
    /// replays the shot's cheats with cheats off and every one is refused. The control: a cheat ConVar recorded beside it.
    /// </remarks>
    [Test]
    public async Task RenderFrame_OnACaptureRunWithCheats_RecordsSvCheatsInTheSidecar()
    {
        string shot = Path.Combine(_folder, "out.png");
        using Rig rig = Build(WriteDemo("shot"), "--shot", shot, "--tick", "40", "+sv_cheats", "1", "+mat_fullbright", "1").WithDevice();
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        Frames(rig.Session, 104);

        System.Text.Json.Nodes.JsonObject convars = System.Text.Json.Nodes.JsonNode.Parse(
            await File.ReadAllTextAsync(shot + ".json").ConfigureAwait(false))!["convars"]!.AsObject();
        convars["mat_fullbright"]!.GetValue<string>().ShouldBe("1", "the control: the cheat it admitted");
        convars["sv_cheats"]!.GetValue<string>().ShouldBe("1");
    }

    [TestCase("Debug")]
    [TestCase("Release")]
    public async Task RenderFrame_OnACaptureRun_WritesTheD226SidecarBesideTheShotInEveryBuild(string build)
    {
        _hostBuild = build;
        string shot = Path.Combine(_folder, "out.png");
        string demo = WriteDemo("shot");
        using Rig rig = Build(demo, "--shot", shot, "--tick", "40", "+mat_phong", "0").WithDevice();
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        Frames(rig.Session, 104);

        System.Text.Json.Nodes.JsonObject json =
            System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(shot + ".json").ConfigureAwait(false))!.AsObject();
        json["side"]!.GetValue<string>().ShouldBe("viewer");
        json["demo"]!.GetValue<string>().ShouldBe("shot.dem");
        json["demoSha256"]!.GetValue<string>().ShouldBe(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(demo).ConfigureAwait(false))));
        json["map"]!.GetValue<string>().ShouldBe(Map);
        json["tick"]!.GetValue<int>().ShouldBe(40);
        json["camera"].ShouldBeNull("this device holds no world, so no frame handed it a camera: null, not a guess");
        json["viewport"]!.ToJsonString().ShouldBe($"[{rig.Window.Viewport.Width},{rig.Window.Viewport.Height}]");
        json["args"]!.AsArray().Select(arg => arg!.GetValue<string>()).ShouldBe(
            ["shot.dem", "--shot", "out.png", "--tick", "40", "+mat_phong", "0"], "no user paths: file names only");
        json["convars"]!["mat_phong"]!.GetValue<string>().ShouldBe("0", "read back, so the +cvar given is the value recorded");
        json["convars"]!.AsObject().ContainsKey("mat_specular").ShouldBeFalse("at its default 1, so not recorded");
        json["gitSha"]!.GetValue<string>().ShouldEndWith("-dirty");
        _dirtyReads.ShouldBe(1, "read once, at the shot");
        (await File.ReadAllLinesAsync(shot + ".json").ConfigureAwait(false)).ShouldContain(line => line.Contains("\"+mat_phong\"", StringComparison.Ordinal));
        json["like"].ShouldBeNull();
    }

    /// <remarks>
    /// The sidecar's camera is the one a device holding a world was handed, carried (B243): the session rig's own device
    /// holds none, so a frame is drawn over one that does, the route `LastViewing_AfterAFrame_IsTheCameraTheDeviceWasGiven`
    /// proves is the presenter's.
    /// </remarks>
    [Test]
    public async Task RenderFrame_OnACaptureRunAfterAFrameWithAWorld_WritesTheCameraTheDeviceWasGiven()
    {
        string shot = Path.Combine(_folder, "out.png");
        using Rig rig = Build(WriteDemo("shot"), "--shot", shot, "--tick", "40").WithDevice();
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();
        FakeViewerDevice world = new();
        rig.Session.Presenter.UploadCameraOver(world, new FakeFrameDevice(), flyTicks: 0, exposureSeconds: 0f, surfaceColours: false);

        Frames(rig.Session, 104);

        FreeCamera handed = world.Camera.Camera.ShouldNotBeNull();
        System.Text.Json.Nodes.JsonObject json =
            System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(shot + ".json").ConfigureAwait(false))!.AsObject();
        json["camera"]!.AsArray().Select(n => n!.GetValue<float>()).ShouldBe(
            [handed.Origin.X, handed.Origin.Y, handed.Origin.Z, handed.Angles.Pitch, handed.Angles.Yaw]);
        json["camera"]!.AsArray().ShouldContain(n => n!.GetValue<float>() != 0f, "a camera of zeros could not tell a carried one from a default");
    }

    [Test]
    public async Task RenderFrame_LikeASidecarOfAnotherDemo_WarnsAndNotesItButStillShoots()
    {
        string shot = Path.Combine(_folder, "out.png");
        string like = Path.Combine(_folder, "tf2.png.json");
        await File.WriteAllTextAsync(
            like,
            """{ "demo": "shot.dem", "demoSha256": "00", "tick": 40, "camera": [0, 0, 0, 0, 0], "viewport": [8, 8] }""")
            .ConfigureAwait(false);
        using Rig rig = Build(WriteDemo("shot"), "--like", like, "--shot", shot, "--tick", "40").WithDevice();
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        Frames(rig.Session, 104);

        rig.Device.Captured.ShouldBe([shot]);
        rig.Log.Count("is not the demo tf2.png.json was taken of").ShouldBe(1);
        System.Text.Json.Nodes.JsonObject json =
            System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(shot + ".json").ConfigureAwait(false))!.AsObject();
        json["notes"]!.GetValue<string>().ShouldContain("demoSha256 00");
        json["like"]!["file"]!.GetValue<string>().ShouldBe("tf2.png.json");
    }

    [TestCase("Debug", true)]
    [TestCase("Release", false)]
    public async Task CaptureViewportToFile_TheScreenshotKey_WritesASidecarOnlyInADebugBuild(string build, bool written)
    {
        _hostBuild = build;
        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 0, ScreenshotFolder = _folder }, 0.5, WriteDemo("key")).WithDevice();
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        rig.Session.CaptureViewportToFile();

        string png = rig.Device.Captured.ShouldHaveSingleItem();
        Path.GetDirectoryName(png).ShouldBe(_folder);
        File.Exists(png + ".json").ShouldBe(written);
    }

    [Test]
    public async Task CaptureViewportToFile_PastTheLimit_PrunesAShotWithItsSidecarAndKeepsAKeptShotsSidecar()
    {
        // One more old shot than the limit, each with its sidecar: the oldest must go, PNG and sidecar together.
        List<string> old = [];

        for (int age = 0; age <= Captures.Kept; age++)
        {
            string png = Path.Combine(_folder, Captures.Name(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Local).AddMinutes(age)));
            await File.WriteAllBytesAsync(png, [1]).ConfigureAwait(false);
            await File.WriteAllTextAsync(png + ".json", "{}").ConfigureAwait(false);
            old.Add(png);
        }

        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 0, ScreenshotFolder = _folder }, 0.5, WriteDemo("prune")).WithDevice();
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        rig.Session.CaptureViewportToFile();

        File.Exists(old[0]).ShouldBeFalse("the oldest is past the limit");
        File.Exists(old[0] + ".json").ShouldBeFalse("a pruned shot leaves no orphan sidecar");
        File.Exists(old[^1]).ShouldBeTrue();
        File.Exists(old[^1] + ".json").ShouldBeTrue("a kept shot keeps its sidecar");
    }

    [Test]
    public async Task RenderFrame_OnACaptureRunWhileThePostProcessingWalks_WaitsAndSaysWhatItWaitsOn()
    {
        using Rig rig = Build(WriteDemo("walks"), "--shot", Path.Combine(_folder, "out.png")).WithDevice();
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();
        Frames(rig.Session, 44);
        rig.Device.PostProcessingSettled = false;

        Frames(rig.Session, 200);

        rig.Device.Captured.ShouldBeEmpty();
        rig.Log.Count("[viewer] capture waiting on post-processing: bloom 1.00 of 0.50").ShouldBeGreaterThan(0);
        rig.Log.Count("capture waiting on post-processing").ShouldBeLessThan(5, "said once a second, not once a frame");

        rig.Device.PostProcessingSettled = true;
        Frames(rig.Session, 60);

        rig.Device.Captured.Count.ShouldBe(1);
    }

    /// <remarks>
    /// B753: a held freeze frame draws no world (`CFreezeFrameView::Draw`, viewrender.cpp:5192), so the post-processing it
    /// would wait on never moves again; a `--shot` inside one waited for ever. The held picture is final, and is taken.
    /// </remarks>
    [Test]
    public async Task RenderFrame_OnACaptureRunInAHeldFreezeFrame_TakesTheShotWithoutWaiting()
    {
        using Rig rig = Build(WriteDemo("held"), "--shot", Path.Combine(_folder, "out.png")).WithDevice();
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();
        Frames(rig.Session, 44);
        rig.Device.PostProcessingSettled = false;
        rig.Device.ShowingFreezeFrame = true;

        Frames(rig.Session, 60);

        rig.Device.Captured.Count.ShouldBe(1);
    }

    [Test]
    public void RenderFrame_OnACaptureRunWhoseDemoNeverComes_GivesUpAfterItsPatienceAndWritesNothing()
    {
        using Rig rig = Build("--shot", Path.Combine(_folder, "out.png")).WithDevice();

        Frames(rig.Session, 30_000);
        rig.Window.Count("Close").ShouldBe(0);
        Frames(rig.Session, 2);

        rig.Log.Count("[viewer] no demo loaded after 30000 frames, so --shot wrote nothing").ShouldBe(1);
        rig.Device.Captured.ShouldBeEmpty();
        rig.Window.Count("Close posted").ShouldBe(1);
    }

    [Test]
    public async Task RenderFrame_WithColoursAndFirstPersonAsked_TicksTheColoursItemWhenTheOpeningStateApplies()
    {
        using Rig rig = Build(WriteDemo("colours"), "--colours", "--tick", "7");
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        Frames(rig.Session, 60);

        rig.Window.Told.ShouldContain("ShowSurfaceColours True");
        rig.Log.Count("surface colours on").ShouldBe(1, "the tick reports back as a click would");
        rig.Log.Count("opening state applied at tick 7").ShouldBe(1);
        rig.Window.Count("Close").ShouldBe(0, "no shot was asked for, so nothing closes");
    }

    [Test]
    public async Task RenderFrame_WithSeekThroughSet_VisitsThoseTicksBeforeItsOwn()
    {
        using Rig rig = Build(WriteDemo("through"), "--tick", "9");
        _environment["TF2VIEW_SEEK_THROUGH"] = "600 50";
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();
        int shownBefore = rig.Transport.ShownTicks.Count;

        Frames(rig.Session, 60);

        rig.Transport.ShownTicks.Skip(shownBefore).Take(3).ShouldBe([600, 50, 9]);
        rig.Log.Lines.Where(static line => line.StartsWith("[viewer] opening state", StringComparison.Ordinal)).ShouldBe(
        [
            "[viewer] opening state sought through tick 600",
            "[viewer] opening state sought through tick 50",
            "[viewer] opening state applied at tick 9",
        ]);
    }

    [Test]
    public void CaptureViewport_ABlankPath_IsRefused()
    {
        using Rig rig = Build().WithDevice();

        Should.Throw<ArgumentException>(() => rig.Session.CaptureViewport(" "));

        rig.Device.Captured.ShouldBeEmpty();
    }

    // ---- the measurement ----

    [Test]
    public async Task RenderFrame_OnAMeasureRun_PrintsTheSecondsTheFramesAndTheirMeanThenClosesAtOnce()
    {
        // Every frame takes the fixed clock's half second, so two seconds of playback is four counted frames.
        using Rig rig = Build(WriteDemo("measured"), "--measure", "2", "--autoplay");
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        for (int frame = 0; frame < 20 && rig.Window.Count("Close") == 0; frame++)
        {
            rig.Session.RenderFrame();
        }

        string printed = rig.Out.ToString();
        printed.ShouldContain("measured 2 seconds of playback, ");
        printed.ShouldContain("; 4 frames, mean 500.00 ms");
        printed.ShouldContain("  phases, mean ms: sound ");
        rig.Window.Told.ShouldContain("Close now");
        rig.Window.Count("Close posted").ShouldBe(0);
    }

    [Test]
    public async Task RenderFrame_OnAMeasureRunWithAnOpeningTick_CountsNoFrameBeforeTheSeekAndTheOneAfterIt()
    {
        // Finding 82: the opening seek and the frame whose interval holds its tail are not playback. Every frame is the
        // fixed clock's half second, so two seconds is four counted frames, and the close comes two frames after the seek.
        using Rig rig = Build(WriteDemo("opened"), "--measure", "2", "--autoplay", "--tick", "7");
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        int frames = 0;
        int? appliedAt = null;

        for (; frames < 200 && rig.Window.Count("Close") == 0; frames++)
        {
            rig.Session.RenderFrame();
            appliedAt ??= rig.Log.Count("opening state applied at tick 7") > 0 ? frames : null;
        }

        appliedAt.ShouldNotBeNull();
        // Applied the frame after the seek's landing (B708), which is also the one not counted: its interval holds the seek's tail.
        frames.ShouldBe(appliedAt.Value + 1 + 4, "the frame it is applied in, then four counted");
        rig.Out.ToString().ShouldContain("; 4 frames, mean 500.00 ms");
        rig.Out.ToString().ShouldContain(", between ");
    }

    [Test]
    public async Task RenderFrame_OnAMeasureRunWithThenSeek_PrintsPlaybackThenPausesSeeksAndMeasuresAgain()
    {
        using Rig rig = Build(WriteDemo("twice"), "--measure", "1", "--then-seek", "300", "--autoplay");
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        for (int frame = 0; frame < 40 && rig.Window.Count("Close") == 0; frame++)
        {
            rig.Session.RenderFrame();
        }

        string printed = rig.Out.ToString();
        printed.ShouldContain("measured 1 seconds of playback, ");
        printed.ShouldContain("measured 1 seconds paused at 300 after the seek, ");
        printed.IndexOf("of playback", StringComparison.Ordinal).ShouldBeLessThan(printed.IndexOf("paused at 300", StringComparison.Ordinal));
        rig.Transport.Playing.ShouldBeFalse();
        rig.Transport.ShownTicks.ShouldContain(300);
        rig.Window.Count("Close now").ShouldBe(1);
    }

    /// <summary>The session turns rewind on, not a test's setter (D246): its opening walks and a step back after it does not.</summary>
    [Test]
    public async Task RenderFrame_AStepBackAfterTheOpening_ShowsItsTickWithNoWalk()
    {
        using Rig rig = Build(WriteDemo("back"), "--measure", "1", "--tick", "300", "--then-seek", "100", "--autoplay");
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        for (int frame = 0; frame < 200 && rig.Window.Count("Close") == 0; frame++)
        {
            rig.Session.RenderFrame();
        }

        rig.Log.Count($"{FramePresenter.SkipLandedLine} 300").ShouldBe(1, "the control: the opening seek forward walks");
        rig.Transport.ShownTicks.ShouldContain(100);
        rig.Log.Count($"{FramePresenter.SkipLandedLine} 100").ShouldBe(0, "a step back must not walk from the start");
    }

    /// <summary>The session's own frames are what a skip spreads over (B708): this is what makes every other skip test here mean anything.</summary>
    [Test]
    public void Construct_AnySession_SpreadsASkipOverItsFrames()
    {
        using Rig rig = Build();

        rig.Session.Presenter.SkipsAcrossFrames.ShouldBeTrue();
    }

    /// <remarks>
    /// `_Host_RunFrame` runs the read that reaches the target and then renders it: the frame it lands on is the tick asked for, not that tick
    /// plus the frame's time, and a `--shot` and `--measure` take it from there.
    /// </remarks>
    [Test]
    public async Task RenderFrame_ASkipWhilePlaying_LandsOnTheTickAskedFor()
    {
        using Rig rig = Build(WriteDemo("landing"), "--autoplay", "--tick", "700");
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        for (int frame = 0; frame < 200 && (rig.Session.Presenter.Skipping || !rig.Transport.ShownTicks.Contains(700)); frame++)
        {
            rig.Session.RenderFrame();
        }

        rig.Transport.ShownTicks.ShouldContain(700, "the control: the opening sought it");
        rig.Session.Presenter.Skipping.ShouldBeFalse("the control: it has landed");
        rig.Transport.LastShownTick.ShouldBe(700, "the frame it landed on has not moved the clock past it");

        rig.Session.RenderFrame();

        rig.Transport.LastShownTick.ShouldBeInRange(701, 707, "and the next frame plays on from there by at most one frame's time");
    }

    [Test]
    public async Task RenderFrame_WithoutAShot_TheOpeningLandingClearsAfterTheSkip()
    {
        using Rig rig = Build(WriteDemo("noshot"), "--tick", "700");
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        Frames(rig.Session, 30);

        rig.Log.Count("opening state applied at tick 700").ShouldBe(1);
    }

    /// <summary>`TF2VIEW_PLAY_SHOTS` reads the back buffer on a skip's frames too, paused or not: the walk's only instrument of what it drew.</summary>
    [Test]
    public async Task RenderFrame_APlayShotAtATickOnASkipsWay_IsCapturedWhilePaused()
    {
        string demo = WriteDemo("walked");

        _environmentAtStart = new(StringComparer.Ordinal) { ["TF2VIEW_PLAY_SHOTS"] = $"{_folder} 0" };

        using Rig rig = Build(demo, "--tick", "700").WithDevice();
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        Frames(rig.Session, 60);

        rig.Transport.Playing.ShouldBeFalse("the control: no frame here played");
        rig.Device.Captured.Count.ShouldBe(1);
    }

    /// <summary>Records what a frame asked of the sound device.</summary>
    private sealed class AskedSink : IAudioSink
    {
        public float MasterGain { get; set; } = 1f;

        public int Reclaims { get; private set; }

        public int Silences { get; private set; }

        public int Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel) => 0;

        public bool SetVoiceGain(int voice, float gain) => false;

        public bool SetVoicePitch(int voice, float pitch) => false;

        public bool SetGain(int entity, int channel, float gain) => false;

        public bool SetPitch(int entity, int channel, float pitch) => false;

        public void SilenceAll() => Silences++;

        public int Reclaim() => Reclaims++;

        public void Silence(int entity, int channel)
        {
            // Nothing is held.
        }

        public int Silence(int entity, int channel, SoundSample sample) => 0;
    }

    /// <remarks>
    /// `_Host_RunFrame_Sound` (engine.dll FUN_18002fbe0) has no skip test, so losing focus mutes through a skip (`snd_mute_losefocus`), and
    /// what is playing keeps following the ears. **But the move is the landing's**: taken here, the landing would not see it was a jump, and
    /// what is in flight would go on over the new place.
    /// </remarks>
    [Test]
    public async Task PlaySoundsOver_DuringASkip_FollowsFocusAndLeavesTheJumpToTheLanding()
    {
        using Rig rig = Build();
        (await rig.Session.Presenter.LoadDemoAsync(WriteDemo("heard")).ConfigureAwait(false)).Loaded.ShouldBeTrue();
        AskedSink sink = new();
        FreeCamera ears = new() { Origin = (0f, 0f, 0f), Angles = (0f, 0f, 0f) };

        // A schedule's first call is the demo's `StartPlayback`, which stops everything (B550): the demo's opening move takes it.
        rig.Session.Presenter.FinishSkip();
        rig.Session.Presenter.PlaySoundsOver(sink, () => ears, static () => null, 100, focused: true);
        int reclaims = sink.Reclaims;
        int silences = sink.Silences;

        // Behind, so the landing is a reload — the skip that stops what plays; one ahead keeps it playing (B550).
        rig.Transport.Scrub(50);
        rig.Session.Presenter.Skipping.ShouldBeTrue("the control: the scrub began a walk");

        rig.Session.Presenter.PlaySoundsOver(sink, () => ears, static () => null, 50, focused: false);
        rig.Session.Presenter.PlaySoundsOver(sink, () => null, static () => null, 50, focused: false);

        sink.MasterGain.ShouldBe(0f, "snd_mute_losefocus ships at 1");
        sink.Reclaims.ShouldBe(reclaims + 1, "the frame with ears followed them");
        sink.Silences.ShouldBe(silences, "a frame of a skip starts and stops nothing");

        rig.Session.Presenter.FinishSkip();
        rig.Session.Presenter.PlaySoundsOver(sink, () => ears, static () => null, 50, focused: true);

        sink.MasterGain.ShouldBe(1f);
        sink.Silences.ShouldBe(silences + 1, "the landing took the jump back, and silenced what was in flight");
    }

    /// <summary>The close path: a load that fails during a walk leaves nothing skipping, or every frame after it would wait for the landing.</summary>
    [Test]
    public async Task LoadDemo_AFailureDuringASkip_LeavesNothingSkipping()
    {
        using Rig rig = Build();
        (await rig.Session.Presenter.LoadDemoAsync(WriteDemo("open")).ConfigureAwait(false)).Loaded.ShouldBeTrue();
        rig.Transport.Scrub(700);
        rig.Session.Presenter.Skipping.ShouldBeTrue("the control: the scrub began a walk and no frame has stepped it");

        string broken = Path.Combine(_folder, "broken.dem");

        await File.WriteAllBytesAsync(broken, [1, 2, 3]).ConfigureAwait(false);
        (await rig.Session.Presenter.LoadDemoAsync(broken).ConfigureAwait(false)).Loaded.ShouldBeFalse();

        rig.Session.Presenter.Skipping.ShouldBeFalse();
    }

    /// <summary>The load path: a demo opened during a walk ends it, and the frame after the read does not wait on it.</summary>
    [Test]
    public async Task LoadDemo_ANewDemoDuringASkip_EndsTheWalk()
    {
        using Rig rig = Build();
        (await rig.Session.Presenter.LoadDemoAsync(WriteDemo("open")).ConfigureAwait(false)).Loaded.ShouldBeTrue();
        rig.Transport.Scrub(700);
        rig.Session.Presenter.Skipping.ShouldBeTrue("the control: the scrub began a walk and no frame has stepped it");

        (await rig.Session.Presenter.LoadDemoAsync(WriteDemo("next")).ConfigureAwait(false)).Loaded.ShouldBeTrue();

        rig.Log.Count(FramePresenter.SkipLandedLine + " 700").ShouldBe(0);
    }

    /// <summary>What `--then-seek` measures is the paused frame at its tick, and the playback before it says where it ended.</summary>
    [Test]
    public async Task RenderFrame_ThenSeekOnAMeasureRun_EndsAtTheTickSoughtAndSaysWhere()
    {
        using Rig rig = Build(WriteDemo("then"), "--measure", "1", "--then-seek", "700", "--autoplay");
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        for (int frame = 0; frame < 60 && rig.Window.Count("Close") == 0; frame++)
        {
            rig.Session.RenderFrame();
        }

        string printed = rig.Out.ToString();

        printed.ShouldContain(", ending at tick ");
        printed.ShouldContain("measured 1 seconds paused at 700 after the seek, ");
        rig.Transport.LastShownTick.ShouldBe(700);
    }

    [Test]
    public void RenderFrame_WithNoDemoOnAMeasureRun_NeverCountsAFrameAndNeverCloses()
    {
        using Rig rig = Build("--measure", "1");

        Frames(rig.Session, 20);

        rig.Out.ToString().ShouldBeEmpty();
        rig.Window.Count("Close").ShouldBe(0);
    }

    [Test]
    public void FullScreenChanging_ThenAFrame_SaysHowLongTheChangeTookOnce()
    {
        using Rig rig = Build();
        rig.Window.IsFullScreen = true;

        rig.Session.FullScreenChanging();
        Frames(rig.Session, 2);

        rig.Log.Lines.Count(static line => line.StartsWith("[render] full screen on took ", StringComparison.Ordinal) && line.EndsWith(" ms to the first frame at 1280x720", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    // ---- shutdown ----

    [Test]
    public void ReleaseServices_Once_AnnouncesTheMapsThenTheShutdownTokenAndASecondTimeNothing()
    {
        using Rig rig = Build();
        List<string> announced = [];

        rig.Session.StopBackgroundWork();
        rig.Session.ReleaseDevice();
        rig.Session.ReleaseServices(announced.Add);
        rig.Session.ReleaseServices(announced.Add);

        announced.ShouldBe(["maps", "shutdown"]);
    }

    [Test]
    public void Dispose_Twice_ReleasesOnce()
    {
        Rig rig = Build().WithDevice();

        rig.Session.Dispose();
        Should.NotThrow(rig.Session.Dispose);
        rig.Session.HasDevice.ShouldBeFalse();
        rig.Dispose();
    }
}
