using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The timeline builder snapshotted between any two commands and resumed must produce the timeline a straight build
/// produces (D217) — the property a checkpointed seek stands on.
/// </summary>
/// <remarks>
/// **Every cut, and every stop after it.** The snapshot is taken after each command in turn; the live builder runs on to
/// the END before the resume, so the resumed build meets output lists that have grown and been rewritten since; and the
/// resumed build stops at every later command and is compared with a straight build stopped there. A resume that
/// finished the demo would replay every later rewrite and hide one it had inherited — a seek does not finish the demo.
/// </remarks>
public sealed class TimelineBuilderSnapshotTests
{
    /// <summary>
    /// Scene slot 40 starts at 10 and stops at 14, after slot 41 started at 12 — a stop written behind the last run — and
    /// two packets on tick 12 each change the second player's soundscape, so the second drops the first's sample.
    /// </summary>
    private static byte[] ScenesDemo() =>
        SyntheticPlayer.DemoOfScenes(
            (10, 1, null, 4), (12, 1, 2, 5), (12, 1, 2, 6), (14, null, 2, 6), (16, null, null, 6), (18, 3, null, 6), (20, null, null, 6));

    private static IEnumerable<TestCaseData> Demos()
    {
        yield return new TestCaseData(SyntheticPlayer.DemoOverTicks(0.015f, (10, 0f, 0f), (11, 4f, 0f), (12, 8f, 2f), (14, 16f, 4f)))
            .SetArgDisplayNames("a player moving");
        yield return new TestCaseData(SyntheticPlayer.DemoOfALateTeam(0.015f, (10, null), (12, SceneTeams.Blu), (14, SceneTeams.Red)))
            .SetArgDisplayNames("a team stated late");
        yield return new TestCaseData(SyntheticRocket.DemoOverTicks(0.015f, ownerModel: SyntheticRocket.Model, [(20, 0f, 0f, 0f), (21, 10f, 0f, 0f), (22, 20f, 5f, 0f), (24, 40f, 5f, 2f)]))
            .SetArgDisplayNames("a rocket and its owner's track");
        yield return new TestCaseData(ScenesDemo())
            .SetArgDisplayNames("scenes stopping behind the last run, soundscapes contending");
    }

    /// <remarks>**The equivalence**: identical fingerprints for every cut and every stop, over something.</remarks>
    [TestCaseSource(nameof(Demos))]
    public void BuildThroughSnapshot_AtEveryCutAndStop_EqualsAStraightBuildToTheSameStop(byte[] demo)
    {
        int commands = DemoTimeline.CommandCount(demo);
        commands.ShouldBeGreaterThan(3);
        TimelineFingerprint.Of(DemoTimeline.Build(demo)).Values.ShouldBeGreaterThan(10);

        for (int stop = 0; stop <= commands; stop++)
        {
            TimelineFingerprint straight = TimelineFingerprint.Of(DemoTimeline.BuildThrough(demo, stop));

            for (int at = 0; at <= stop; at++)
            {
                TimelineFingerprint.Of(DemoTimeline.BuildThroughSnapshot(demo, at, stop))
                    .ShouldBe(straight, $"snapshot after {at}, stopped after {stop} of {commands} commands");
            }
        }
    }

    /// <remarks>
    /// **The install reaches the resume** (B874 review 3): a scout whose class script says `DontDoNewJump` jumps the single
    /// old <see cref="PlayerActivity.LegacyJump"/> (B437), and a build snapshotted at every command and resumed with those
    /// scripts equals the straight build with them and keeps that jump. Control: the straight build without the scripts
    /// never takes it, so a resume that dropped them would differ.
    /// </remarks>
    [Test]
    public void BuildThroughSnapshot_WithClassScriptsAtEveryCut_EqualsTheStraightBuildAndKeepsTheOldJump()
    {
        byte[] demo = SyntheticPlayer.ScoutJump();
        int commands = DemoTimeline.CommandCount(demo);
        ClientInterp client = new();
        DemoTimeline straight = DemoTimeline.Build(demo, client: client, classes: new CountingScripts());

        Jumps(DemoTimeline.Build(demo, client: client, classes: null))
            .ShouldNotContain(PlayerActivity.LegacyJump, "the control: with no scripts the engine's new jump holds");
        Jumps(straight).ShouldContain(PlayerActivity.LegacyJump);

        for (int at = 0; at <= commands; at++)
        {
            DemoTimeline resumed = DemoTimeline.BuildThroughSnapshot(demo, at, client: client, classes: new CountingScripts());

            TimelineFingerprint.Of(resumed).ShouldBe(TimelineFingerprint.Of(straight), $"snapshot after {at} of {commands} commands");
            Jumps(resumed).ShouldBe(Jumps(straight), $"snapshot after {at} of {commands} commands");
        }
    }

    /// <remarks>
    /// **The scripts and the model facts are shared by a snapshot, never copied** (`SharedInputs`): a resume asks the very
    /// objects it was handed. Snapshotted before the first command, the live builder walks every command and the resume
    /// walks them all again, so each object is asked twice what a straight build asks it; a deep copy would be asked the
    /// resume's share itself, leaving the originals at the straight count. The scripts are asked on the jumping scout, the
    /// facts on a prop whose model is set twice (the scout's demo sets none).
    /// </remarks>
    [Test]
    public void BuildThroughSnapshot_WithTheInstallsInputs_AsksTheSameObjectsInTheResume()
    {
        byte[] scout = SyntheticPlayer.ScoutJump();
        byte[] prop = SyntheticAnimatingProp.Demo(
            [string.Empty, "models/buildables/sentry1.mdl", "models/props_gameplay/door.mdl"],
            (0, 2, [0.2f], null, null),
            (10, 2, [0.6f], null, null),
            (20, 1, [0.9f, 0.5f], null, null));
        CountingScripts straightScripts = new();
        CountingFacts straightFacts = new();
        CountingScripts scripts = new();
        CountingFacts facts = new();

        _ = DemoTimeline.Build(scout, client: new ClientInterp(), classes: straightScripts);
        _ = DemoTimeline.Build(prop, client: new ClientInterp(), models: straightFacts);
        _ = DemoTimeline.BuildThroughSnapshot(scout, 0, client: new ClientInterp(), classes: scripts);
        _ = DemoTimeline.BuildThroughSnapshot(prop, 0, client: new ClientInterp(), models: facts);

        (straightScripts.Asked, straightFacts.Asked).ShouldBe((31, 2), "the control: a straight build asks each");
        (scripts.Asked, facts.Asked).ShouldBe((62, 4));
    }

    /// <remarks>
    /// **The snapshot the `timeline-resume` probe measures is walked with the install's inputs** (B874 review 4): each of
    /// <c>MeasureSnapshot</c>'s three is observed. The scripts and the facts are counted, and walked through every command
    /// they are asked what a straight build asks them (31 and 2, the control the resume test also takes). The interp is seen
    /// in the builder's own state: the scout's one jump is queued to fire an interpolation window after it arrives (B437), so
    /// under TF2's tenth of a second it has fired before the demo ends and the queue holds nothing, while under a hundred
    /// seconds it is still waiting, and a snapshot copies it. Control: no interp at all is TF2's defaults.
    /// </remarks>
    [Test]
    public void MeasureSnapshot_WithTheInstallsInputs_WalksWithEachOfThem()
    {
        byte[] scout = SyntheticPlayer.ScoutJump();
        byte[] prop = SyntheticAnimatingProp.Demo(
            [string.Empty, "models/buildables/sentry1.mdl", "models/props_gameplay/door.mdl"],
            (0, 2, [0.2f], null, null),
            (10, 2, [0.6f], null, null),
            (20, 1, [0.9f, 0.5f], null, null));
        CountingScripts scripts = new();
        CountingFacts facts = new();

        // A first copy also allocates the copier's own reflection over each type, which the counter sees; not measured.
        _ = DemoTimeline.MeasureSnapshot(scout, DemoTimeline.CommandCount(scout));
        long defaults = Queued(DemoTimeline.MeasureSnapshot(scout, DemoTimeline.CommandCount(scout)));
        long tenth = Queued(DemoTimeline.MeasureSnapshot(scout, DemoTimeline.CommandCount(scout), client: new ClientInterp()));
        long late = Queued(DemoTimeline.MeasureSnapshot(
            scout, DemoTimeline.CommandCount(scout), client: new ClientInterp(Interp: 100f), classes: scripts));
        _ = DemoTimeline.MeasureSnapshot(prop, DemoTimeline.CommandCount(prop), models: facts);

        tenth.ShouldBe(defaults, "the control: no interp is TF2's defaults");
        late.ShouldBeGreaterThan(tenth, "a jump still waiting to fire is copied with the queue");
        (scripts.Asked, facts.Asked).ShouldBe((31, 2));
    }

    /// <summary>What copying the builder's queue of gestures waiting to fire allocates.</summary>
    private static long Queued((long Total, IReadOnlyList<(string Field, long Bytes)> Fields) cost) =>
        cost.Fields.Single(field => field.Field == "queuedGestures").Bytes;

    /// <summary>The one player's jump activity on every frame, in tick order.</summary>
    private static List<PlayerActivity?> Jumps(DemoTimeline timeline) =>
        [.. timeline.Frames.Select(frame => frame.Players.Single().JumpActivity)];

    /// <summary>Class scripts in which the scout jumps the old way, counting every ask.</summary>
    private sealed class CountingScripts : IClassAnimationScripts
    {
        public int Asked { get; private set; }

        public ClassAnimationScript ScriptOf(int? playerClass)
        {
            Asked++;
            return new ClassAnimationScript(DontDoAirwalk: false, DontDoNewJump: playerClass == SyntheticPlayer.ScoutClass);
        }
    }

    /// <summary>Model facts that know no model, counting every ask.</summary>
    private sealed class CountingFacts : IModelFacts
    {
        public int Asked { get; private set; }

        public ModelFacts? FactsOf(string modelPath)
        {
            Asked++;
            return null;
        }
    }

    /// <remarks>**Forward, then back**: resuming a later snapshot and then an earlier one is a seek backwards, and is exact.</remarks>
    [Test]
    public void BuildResumingTwice_LaterThenEarlier_EqualsAStraightBuild()
    {
        byte[] demo = ScenesDemo();
        int commands = DemoTimeline.CommandCount(demo);

        TimelineFingerprint.Of(DemoTimeline.BuildResumingTwice(demo, earlier: 4, later: 7, backFirst: false, walk: 1, stopAfter: commands))
            .ShouldBe(TimelineFingerprint.Of(DemoTimeline.Build(demo)));
    }

    /// <remarks>
    /// **Back, then forward, refused**: the earlier resume cut the shared lists below the later snapshot's marks, and
    /// marks cannot restore what was cut — resuming it silently left a gap (S1 at 100 frames, S2 at 500, gave 101).
    /// </remarks>
    [Test]
    public void BuildResumingTwice_EarlierThenLaterNotWalkedPast_Refuses()
    {
        byte[] demo = ScenesDemo();
        int commands = DemoTimeline.CommandCount(demo);

        Should.Throw<InvalidOperationException>(() =>
            DemoTimeline.BuildResumingTwice(demo, earlier: 4, later: 8, backFirst: true, walk: 1, stopAfter: commands));
    }

    /// <remarks>**Back, then forward again past the later snapshot**: the lists have grown back, so the later one resumes exactly.</remarks>
    [Test]
    public void BuildResumingTwice_EarlierThenLaterWalkedPast_EqualsAStraightBuild()
    {
        byte[] demo = ScenesDemo();
        int commands = DemoTimeline.CommandCount(demo);

        TimelineFingerprint.Of(DemoTimeline.BuildResumingTwice(demo, earlier: 4, later: 7, backFirst: true, walk: commands, stopAfter: commands))
            .ShouldBe(TimelineFingerprint.Of(DemoTimeline.Build(demo)));
    }

    /// <remarks>**After Finish, refused**: the shared lists are in a timeline a caller holds, and `sounds` was rewritten for it.</remarks>
    [Test]
    public void ResumeAfterFinish_AnySnapshot_Refuses()
    {
        Should.Throw<InvalidOperationException>(() => DemoTimeline.ResumeAfterFinish(ScenesDemo(), 5));
    }

    /// <remarks>
    /// **The control for the demo above**: it does write a stop behind the last run, and it does make the soundscape
    /// sampler drop a sample within a tick — or the equivalence would hold for want of anything to get wrong.
    /// </remarks>
    [Test]
    public void Build_ScenesDemo_StopsAnEarlierRunAndKeepsOneSoundscapePerTick()
    {
        DemoTimeline timeline = DemoTimeline.Build(ScenesDemo());

        timeline.Scenes.Select(scene => (scene.Tick, scene.EntityIndex, scene.StoppedTick))
            .ShouldBe([(10, 40, (int?)14), (12, 41, (int?)16), (18, 40, (int?)20)]);
        timeline.Soundscapes.Select(sample => (sample.Tick, sample.Soundscape.Index)).ShouldBe([(10, 4), (12, 6)]);
    }
}
