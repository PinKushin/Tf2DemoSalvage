using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Probe;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.SdkReference;

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
    /// **The helper dropping the class scripts cannot redden this, and that is measured, not assumed.** The timeline
    /// reads them in two places — the jump activity (`HandleJumping`'s class script) and a gesture's posture — and a build
    /// with the facts and no scripts samples the same as the full build on this demo and on `z1800` (2026-11-26), jump
    /// activity included: neither demo reaches a branch where a class's script differs from the engine's default. The
    /// scripts are still passed, as the viewer passes them; this test cannot see them.
    /// </remarks>
    [Test]
    public void Timeline_WithTheInstall_MatchesTheViewersRead()
    {
        _ = GameInstall.Require();
        string path = Corpus.Demo(RealMatch);
        GameContent game = ProbeInstall.Game(output: null);
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
    /// install <see cref="ProbeInstall.Game"/> opens reads a file only that folder holds. `MapLocator`, which the probes
    /// used, never reads the variable.
    /// </summary>
    [Test]
    [NonParallelizable]
    public void Game_WithTf2FolderSet_OpensThatFolder()
    {
        string folder = TemporaryFolder();
        File.WriteAllText(Path.Combine(folder, Marker), "here");

        try
        {
            WithEnvironment(
                [(SteamInstall.OverrideVariable, folder)],
                () => ProbeInstall.Game(output: null).Archives.Read(Marker).ShouldNotBeNull());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

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

    /// <summary>A 24-minute nine-versus-nine match: players of every class, carrying weapons and buildings.</summary>
    private const string RealMatch = "tf2-live-build11138802-spec-pov-cp_granary";
}
