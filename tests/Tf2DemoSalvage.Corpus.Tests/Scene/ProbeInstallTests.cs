using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Probe;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.SceneDecode;

/// <summary>A probe's timeline is the viewer's: built with the install's class scripts and model facts (B874).</summary>
public sealed class ProbeInstallTests
{
    /// <summary>
    /// **The probe builds what the viewer reads**: the same install handed to `DecodedDemo.Read` as the viewer hands it
    /// (`ViewerPresenter.LoadDemoAsync`: `game.ClassAnimation`, `game.ModelFacts`) and to the probe's helper fingerprint
    /// alike. Control: a build without the install fingerprints differently, so the match is not a demo the install leaves
    /// unchanged.
    /// </summary>
    [Test]
    public void Timeline_WithTheInstall_MatchesTheViewersRead()
    {
        string path = Corpus.Demo(RealMatch);
        GameContent game = ProbeInstall.Open(GameInstall.Require(), output: null);
        byte[] file = File.ReadAllBytes(path);

        DemoTimeline viewer =
            DecodedDemo.Read(path, NullLogger.Instance, classes: game.ClassAnimation, models: game.ModelFacts).Timeline.ShouldNotBeNull();
        List<float> drawn = Sampled(viewer);

        Sampled(DemoTimeline.Build(file)).ShouldNotBe(drawn, "the control: the install changes what this demo's tracks draw");

        DemoTimeline probe = ProbeInstall.Timeline(file, game);

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

        values.ShouldNotBeEmpty("an instrument that sampled nothing would match anything");
        return values;
    }

    /// <summary>
    /// **No install builds as the viewer builds with none, and says so**: one line naming what is missing, and the
    /// timeline the viewer's read makes from the same empty install.
    /// </summary>
    [Test]
    public void Open_WithNoFolder_SaysSoAndBuildsAsTheViewer()
    {
        string path = Corpus.Demo(AnyEraDemo);
        using StringWriter said = new();

        GameContent none = ProbeInstall.Open(folder: null, said);

        TimelineFingerprint viewer = TimelineFingerprint.Of(
            DecodedDemo.Read(path, NullLogger.Instance, classes: none.ClassAnimation, models: none.ModelFacts).Timeline.ShouldNotBeNull());

        TimelineFingerprint.Of(ProbeInstall.Timeline(File.ReadAllBytes(path), none)).ShouldBe(viewer);
        said.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .ShouldBe([ProbeInstall.NoInstall]);
    }

    /// <summary>A 24-minute nine-versus-nine match: players of every class, carrying weapons and buildings.</summary>
    private const string RealMatch = "tf2-live-build11138802-spec-pov-cp_granary";

    /// <summary>A demo the committed corpus always has, so CI runs the no-install test.</summary>
    private const string AnyEraDemo = "tf2-2009-build3862-spec-pov-cp_granary";
}
