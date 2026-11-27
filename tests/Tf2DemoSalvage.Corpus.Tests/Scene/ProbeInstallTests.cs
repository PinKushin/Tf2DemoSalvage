using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Probe;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A probe's timeline is the viewer's: its install, found as the viewer finds it, and the watcher's interp (B874).</summary>
public sealed class ProbeInstallTests
{
    /// <summary>
    /// **The probe builds what the viewer reads**: the install <see cref="ProbeInstall.Game"/> opens, handed to
    /// `DecodedDemo.Read` as the viewer hands it (`ViewerPresenter.LoadDemoAsync`: the interp, `game.ClassAnimation`,
    /// `game.ModelFacts`), and to the probe's helper, sample alike. Control: a build with the class scripts and no model
    /// facts samples differently, so the helper dropping the facts reddens this.
    /// </summary>
    /// <remarks>
    /// **The helper dropping the class scripts does not redden this one**: on this demo and on `z1800` (2026-11-26) a
    /// build with the facts and no scripts samples the same as the full build, jump activity included, because no shipped
    /// class script differs from the engine's default where either demo reaches. That half is
    /// <see cref="Timeline_WithALooseClassScript_TakesTheLegacyJump"/>'s, on a synthetic demo and a script that does differ.
    ///
    /// Gated on the helper's own answer, not another locator's: it runs wherever <see cref="ProbeInstall.Game"/> finds an
    /// install.
    /// </remarks>
    [Test]
    public void Timeline_WithTheInstall_MatchesTheViewersRead()
    {
        GameContent game = ProbeInstall.Game(output: null);

        if (game.Archives.IsEmpty)
        {
            Assert.Ignore("ProbeInstall found no install, so there is no install for the probe and the viewer to share.");
        }

        string path = Corpus.Demo(RealMatch);
        ClientInterp interp = ProbeInstall.Interp();
        byte[] file = File.ReadAllBytes(path);

        DemoTimeline viewer = DecodedDemo.Read(
            path, NullLogger.Instance, interp: interp, classes: game.ClassAnimation, models: game.ModelFacts).Timeline.ShouldNotBeNull();
        List<float> drawn = Sampled(viewer);

        Sampled(DemoTimeline.Build(file, client: interp, classes: game.ClassAnimation))
            .ShouldNotBe(drawn, "the control: the model facts change what this demo's tracks draw");

        DemoTimeline probe = ProbeInstall.Timeline(file, output: null);

        Sampled(probe).ShouldBe(drawn);
        TimelineFingerprint.Of(probe).ShouldBe(TimelineFingerprint.Of(viewer));
    }

    /// <summary>
    /// Every track's sequence, cycle and pose parameters as drawn half a tick after each of its keyframes.
    /// </summary>
    /// <remarks>
    /// **Sampled, because the install acts on the interpolation histories** — `OnNewModel` resizes the pose history and
    /// flags a static prop (B870) — which `TimelineFingerprint` cannot see: it hashes the stored keyframes, and those are
    /// the same with or without the install. Measured 2026-11-26 on this demo and on `z1800`: a build with the install and
    /// one without fingerprint the same; on this demo these samples differ.
    /// </remarks>
    private static List<float> Sampled(DemoTimeline timeline)
    {
        List<float> values = [];

        foreach (ScenePropTrack track in timeline.Props.Concat(timeline.PlayerTracks).Distinct())
        {
            foreach ((int tick, ScenePose _) in track.Keyframes)
            {
                if (track.At(tick + 0.5d) is { } pose)
                {
                    values.Add(pose.Sequence);
                    values.Add(pose.Cycle);
                    values.AddRange(pose.PoseParameters);
                }
            }
        }

        foreach (TimelineFrame frame in timeline.Frames)
        {
            foreach (ScenePlayer player in frame.Players)
            {
                values.Add(player.JumpActivity is { } jump ? (float)jump : -1f);
            }
        }

        values.ShouldNotBeEmpty("an instrument that sampled nothing would match anything");
        return values;
    }

    /// <summary>
    /// **The viewer's locator, not the Steam library's**: `TF2_FOLDER` names a folder no Steam library lists, and the
    /// install <see cref="ProbeInstall.Game"/> opens reads a file only that folder holds, <see cref="ProbeInstall.Map"/>
    /// finds a map only its <c>maps</c> holds, and <see cref="ProbeInstall.Interp"/> takes the <c>cl_interp</c> only its
    /// <c>autoexec.cfg</c> sets. `MapLocator`, which the probes used, never reads the variable. The machine is hidden: the
    /// Steam root is the same bare folder, which lists no library, and the settings file does not exist, so the interp is
    /// the config's. (The Steam root is named rather than absent because `MapProvider.Locate`, the viewer's, answers null
    /// with no Steam library file at all, before it reads the install's <c>maps</c>.)
    /// </summary>
    [Test]
    [NonParallelizable]
    public void Game_WithTf2FolderSet_OpensThatFolder()
    {
        string folder = TemporaryFolder();
        File.WriteAllText(Path.Combine(folder, Marker), "here");
        Directory.CreateDirectory(Path.Combine(folder, "cfg"));
        File.WriteAllText(Path.Combine(folder, "cfg", "autoexec.cfg"), "cl_interp 0.033\n");
        Directory.CreateDirectory(Path.Combine(folder, "maps"));
        File.WriteAllText(Path.Combine(folder, "maps", MarkerMap + ".bsp"), "not a map, only a name");

        try
        {
            WithEnvironment(
                [
                    (SteamInstall.OverrideVariable, folder),
                    (SteamInstall.SteamRootVariable, folder),
                    (ViewerSettings.PathVariable, Path.Combine(folder, "absent.cfg")),
                    (ViewerSession.NoConfigsVariable, null),
                ],
                () =>
                {
                    ProbeInstall.Game(output: null).Archives.Read(Marker).ShouldNotBeNull();
                    ProbeInstall.Interp().Interp.ShouldBe(0.033f);
                    ProbeInstall.Map(MarkerMap).ShouldBe(Path.Combine(folder, "maps", MarkerMap + ".bsp"));
                });
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// **The interp the helper is handed is the one the build uses**: the same synthetic jump built through
    /// <see cref="ProbeInstall.Timeline(ReadOnlyMemory{byte}, GameContent, ClientInterp)"/> with no interpolation window
    /// and with TF2's default one answers differently, because the window delays when the jump event fires. A helper that
    /// built with any interp but the one passed would answer the same twice.
    /// </summary>
    [Test]
    public void Timeline_WithANonDefaultInterp_DiffersFromTheDefaultBuild()
    {
        GameContent none = ProbeInstall.Open(folder: null, output: null);
        byte[] demo = ScoutJump();

        List<PlayerActivity?> noWindow = JumpActivities(ProbeInstall.Timeline(demo, none, new ClientInterp(0f, 0f, 66f)));
        List<PlayerActivity?> byDefault = JumpActivities(ProbeInstall.Timeline(demo, none, new ClientInterp()));

        noWindow.ShouldContain(PlayerActivity.JumpStart, "the control: the jump reaches the timeline at all");
        byDefault.ShouldNotBe(noWindow);
    }

    /// <summary>
    /// **The class scripts the helper opens reach the build**: a loose install whose scout script sets
    /// <c>"DontDoNewJump" "1"</c> uncommented makes a scout's jump the single old <see cref="PlayerActivity.LegacyJump"/>
    /// (`HandleJumping`, B437, B523), where a build with no class scripts takes the engine's default new jump. No TF2: the
    /// install is a temporary folder of one file.
    /// </summary>
    [Test]
    public void Timeline_WithALooseClassScript_TakesTheLegacyJump()
    {
        string folder = TemporaryFolder();
        string scripts = Directory.CreateDirectory(Path.Combine(folder, "scripts", "playerclasses")).FullName;
        File.WriteAllText(Path.Combine(scripts, "scout.txt"), "\"PlayerClass\"\n{\n\t\"DontDoNewJump\"\t\"1\"\n}\n");

        try
        {
            GameContent game = ProbeInstall.Open(folder, output: null);
            byte[] demo = ScoutJump();

            game.ClassAnimation.ShouldNotBeNull("the loose script was read as the install's class scripts");
            JumpActivities(DemoTimeline.Build(demo, client: ProbeInstall.Interp(), classes: null))
                .ShouldNotContain(PlayerActivity.LegacyJump, "the control: with no scripts the engine's new jump holds");
            JumpActivities(ProbeInstall.Timeline(demo, game)).ShouldContain(PlayerActivity.LegacyJump);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A red scout who jumps at tick 101 and rises one unit a tick, too slowly to air-walk, until tick 130.</summary>
    private static byte[] ScoutJump() =>
        SyntheticPlayer.DemoOfGestures(
            0.015f,
            SceneTeams.Red,
            ScoutClass,
            rules: null,
            alwaysLoser: false,
            [
                new SyntheticPlayer.GestureSnapshot(100, 0f, PlayerActivityState.OnGround),
                new SyntheticPlayer.GestureSnapshot(101, 1f, 0) { Events = [PlayerAnimEvent.Jump] },
                .. Enumerable.Range(102, 29).Select(tick => new SyntheticPlayer.GestureSnapshot(tick, tick - 100f, 0)),
            ]);

    /// <summary>The one player's jump activity on every frame, in tick order.</summary>
    private static List<PlayerActivity?> JumpActivities(DemoTimeline timeline) =>
        [.. timeline.Frames.Select(frame => frame.Players.Single().JumpActivity)];

    /// <summary>
    /// **The watcher's interp, as the viewer has it**: the saved settings, then the TF2 configs over them, where
    /// `autoexec.cfg` wins (D264). Control: with no game folder there are no configs, and the saved value stands.
    /// </summary>
    [Test]
    public void WatcherInterp_WithAnAutoexec_TakesItsInterpOverTheSaved()
    {
        ViewerSettings saved = new() { Interp = new ClientInterp(0.05f, 1f, 66f) };
        string folder = TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(folder, "cfg"));
        File.WriteAllText(Path.Combine(folder, "cfg", "autoexec.cfg"), "cl_interp 0.033\n");

        try
        {
            ProbeInstall.WatcherInterp(folder: null, saved).ShouldBe(saved.Interp);
            ProbeInstall.WatcherInterp(folder, saved).ShouldBe(new ClientInterp(0.033f, 1f, 66f));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// **No install is said to the first writer that asks, and only once**: a first ask with no writer says nothing and
    /// does not spend the notice. The machine's install is hidden: no `TF2_FOLDER`, an empty Steam root, and a settings
    /// file that does not exist, so no saved `cl_game_folder`.
    /// </summary>
    [Test]
    [NonParallelizable]
    public void Game_WithNoInstall_TellsTheFirstWriterOnce()
    {
        string steam = TemporaryFolder();
        using StringWriter first = new();
        using StringWriter second = new();

        try
        {
            WithEnvironment(
                [
                    (SteamInstall.OverrideVariable, null),
                    (SteamInstall.SteamRootVariable, steam),
                    (ViewerSettings.PathVariable, Path.Combine(steam, "absent.cfg")),
                ],
                () =>
                {
                    ProbeInstall.Game(output: null).Archives.IsEmpty.ShouldBeTrue("the control: no install was found");
                    ProbeInstall.Game(first);
                    ProbeInstall.Game(second);
                });
        }
        finally
        {
            Directory.Delete(steam, recursive: true);
        }

        Lines(first).ShouldBe([ProbeInstall.NoInstall]);
        Lines(second).ShouldBeEmpty();
    }

    /// <summary>
    /// **A probe that asks only for the folder is told too**, once per process and shared with <see cref="ProbeInstall.Game"/>:
    /// the first non-null writer to ask either is told, and a later ask of the other says nothing. The machine is hidden
    /// as in <see cref="Game_WithNoInstall_TellsTheFirstWriterOnce"/>.
    /// </summary>
    [Test]
    [NonParallelizable]
    public void Folder_WithNoInstall_TellsTheFirstWriterOnceForGameToo()
    {
        string steam = TemporaryFolder();
        using StringWriter first = new();
        using StringWriter second = new();

        try
        {
            WithEnvironment(
                [
                    (SteamInstall.OverrideVariable, null),
                    (SteamInstall.SteamRootVariable, steam),
                    (ViewerSettings.PathVariable, Path.Combine(steam, "absent.cfg")),
                ],
                () =>
                {
                    ProbeInstall.Folder(output: null).ShouldBeNull("the control: no install was found");
                    ProbeInstall.Folder(first);
                    ProbeInstall.Game(second);
                });
        }
        finally
        {
            Directory.Delete(steam, recursive: true);
        }

        Lines(first).ShouldBe([ProbeInstall.NoInstall]);
        Lines(second).ShouldBeEmpty();
    }

    /// <summary>
    /// **No folder opens an empty install and says so**: one line naming what is missing, and no class scripts to build
    /// with. What a build with it makes is not asserted here: with no archives the scripts are null and the facts read
    /// nothing, which is the bare build by construction, so a comparison with one could not fail.
    /// </summary>
    [Test]
    public void Open_WithNoFolder_SaysSoAndHasNoClassScripts()
    {
        using StringWriter said = new();

        GameContent none = ProbeInstall.Open(folder: null, said);

        none.Archives.IsEmpty.ShouldBeTrue();
        none.ClassAnimation.ShouldBeNull();
        Lines(said).ShouldBe([ProbeInstall.NoInstall]);
    }

    private static string[] Lines(StringWriter writer) =>
        writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    private static string TemporaryFolder() => Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "probe-install-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>Runs <paramref name="act"/> with the variables set and the helper's finds forgotten, then puts both back.</summary>
    /// <remarks>Process-wide state, so only from a <c>[NonParallelizable]</c> test.</remarks>
    private static void WithEnvironment((string Name, string? Value)[] set, Action act)
    {
        string?[] before = [.. set.Select(variable => Environment.GetEnvironmentVariable(variable.Name))];

        ProbeInstall.Forget();

        try
        {
            foreach ((string name, string? value) in set)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            act();
        }
        finally
        {
            for (int index = 0; index < set.Length; index++)
            {
                Environment.SetEnvironmentVariable(set[index].Name, before[index]);
            }

            ProbeInstall.Forget();
        }
    }

    /// <summary>A loose file a bare <c>tf</c> folder, with no <c>gameinfo.txt</c>, serves as its only content.</summary>
    private const string Marker = "probe-install-marker.txt";

    /// <summary>A map name no Steam library or download folder holds, so only the install's own <c>maps</c> can answer.</summary>
    private const string MarkerMap = "probe_install_marker_map";

    /// <summary>`TF_CLASS_SCOUT`, the class whose script <c>scout.txt</c> is.</summary>
    private const int ScoutClass = 1;

    /// <summary>A 24-minute nine-versus-nine match: players of every class, carrying weapons and buildings.</summary>
    private const string RealMatch = "tf2-live-build11138802-spec-pov-cp_granary";
}
