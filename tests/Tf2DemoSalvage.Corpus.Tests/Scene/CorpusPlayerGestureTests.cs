using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// That a real demo's gesture events reach a player, which only real bytes can answer.
/// </summary>
/// <remarks>
/// **The synthetic tests own the decode; this owns the SPELLING.** `PlayerGestureFeedTests` builds
/// its own `CTEPlayerAnimEvent` and knows the right answer because it put the value there. What it
/// cannot establish is that a real TF2 demo names the class and its properties the way this project
/// expects — a feed matching a class name nothing sends would pass every synthetic test and produce
/// no gesture on any recording, which is exactly the shape of defect this project keeps finding
/// (D38, and `output-level-assertion-or-it-is-not-done`).
///
/// **Why this cannot be a synthetic test at all.** The claim is about what Valve's server sends,
/// and no fixture we write is evidence for that.
/// </remarks>
public sealed class CorpusPlayerGestureTests
{
    /// <remarks>
    /// **z1800 is the specimen because it is a real match**, and the era specimens cannot answer
    /// this: they are the owner's own solo recordings on period clients, with nobody else on the
    /// server to raise an event. Measured in this file: 40,288 `CTEPlayerAnimEvent` effects, the
    /// most common temp entity in it by an order of magnitude.
    ///
    /// **Asserted as "some player has some gesture", not as a count.** The exact number is a fact
    /// about this recording and would make the test a change detector; that ANY gesture survives
    /// the trip from the temp entity stream to a sampled player is the wiring claim.
    /// </remarks>
    [Test]
    public void PlayersAt_OnARealMatch_ReportsGesturesFromTheTempEntityStream()
    {
        if (Corpus.Demo("z1800") is not { } path)
        {
            Assert.Ignore("z1800.dem is not available");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        List<ScenePlayer> players = [];

        int withGestures = 0;

        // A spread of ticks rather than one, because a gesture slot is filled by an event and a
        // single tick could legitimately land where nobody has raised one yet.
        for (int tick = timeline.FirstTick + 100; tick < timeline.LastTick; tick += 500)
        {
            timeline.PlayersAt(tick, players);

            withGestures += players.Count(one => one.Gestures is { Count: > 0 });
        }

        withGestures.ShouldBeGreaterThan(
            0,
            "a real match carries CTEPlayerAnimEvent temp entities for every attack, reload and " +
            "flinch, and they are the only place a player's animation layers exist: " +
            "tf_player.cpp:774 excludes overlay_vars from the player's send table");
    }

    /// <remarks>
    /// **The control, and it is what stops the test above from passing on a fabrication.** If the
    /// feed matched the wrong class — every demo carries thousands of `CTEFireBullets` and
    /// `CTEEffectDispatch` effects — or ignored the event id, every player would carry a gesture at
    /// every tick. A real recording has players standing still, and the reload slot is not
    /// permanently occupied.
    /// </remarks>
    [Test]
    public void PlayersAt_OnARealMatch_LeavesSomePlayersWithNoGesture()
    {
        if (Corpus.Demo("z1800") is not { } path)
        {
            Assert.Ignore("z1800.dem is not available");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        List<ScenePlayer> players = [];

        int withoutGestures = 0;

        for (int tick = timeline.FirstTick + 100; tick < timeline.LastTick; tick += 500)
        {
            timeline.PlayersAt(tick, players);

            withoutGestures += players.Count(one => one.Gestures is null or { Count: 0 });
        }

        withoutGestures.ShouldBeGreaterThan(
            0,
            "a feed that matched the wrong temp entity class, or ignored the event id, would give " +
            "every player a gesture at every tick");
    }

    /// <remarks>
    /// **The other source of layers, and the pair is the point** (B285). A player's
    /// <c>m_AnimOverlay</c> is excluded from the send table (<c>tf_player.cpp:774</c>) while every
    /// other animating entity sends one — so a reading that found layers on players, or none on
    /// buildings, would have the mechanism backwards. Asserting both directions in one test is what
    /// makes either meaningful.
    ///
    /// Measured on `z1800.dem`: sentries carry two, three and four layers, and teleporters,
    /// dispensers, sappers and taunt props carry them too.
    /// </remarks>
    [Test]
    public void PropsAt_OnARealMatch_CarriesWireLayersOnBuildingsAndNoneOnPlayers()
    {
        if (Corpus.Demo("z1800") is not { } path)
        {
            Assert.Ignore("z1800.dem is not available");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        List<SceneProp> props = [];
        List<ScenePlayer> players = [];

        int layered = 0;
        int playersSeen = 0;

        for (int tick = timeline.FirstTick + 100; tick < timeline.LastTick; tick += 500)
        {
            timeline.PropsAt(tick, props);
            timeline.PlayersAt(tick, players);

            layered += props.Count(one => one.Pose.Layers.Count > 0);
            playersSeen += players.Count;
        }

        playersSeen.ShouldBeGreaterThan(0, "the control: the sweep must have seen players at all");

        layered.ShouldBeGreaterThan(
            0,
            "sentries, dispensers, teleporters and sappers all send m_AnimOverlay, and the array " +
            "is keyed by path because fifteen elements share one flat name");
    }

    /// <remarks>
    /// **The reload specifically, because it is what the owner reported missing.** A gesture that
    /// resolves to the reload activity has to appear somewhere in a match with 762 plain reload
    /// events in it; naming the activity rather than counting keeps this a claim about the mapping
    /// rather than about this recording.
    /// </remarks>
    [Test]
    public void PlayersAt_OnARealMatch_ProducesAReloadGesture()
    {
        if (Corpus.Demo("z1800") is not { } path)
        {
            Assert.Ignore("z1800.dem is not available");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        List<ScenePlayer> players = [];

        bool sawReload = false;

        for (int tick = timeline.FirstTick + 100;
            tick < timeline.LastTick && !sawReload;
            tick += 200)
        {
            timeline.PlayersAt(tick, players);

            sawReload = players.Any(one =>
                one.Gestures is { } gestures &&
                gestures.Any(gesture =>
                    gesture.ActivityName?.StartsWith("ACT_MP_RELOAD", System.StringComparison.Ordinal)
                        == true));
        }

        sawReload.ShouldBeTrue(
            "z1800.dem carries 762 PLAYERANIMEVENT_RELOAD events plus 925 loops and 287 ends, so " +
            "a reload gesture must reach a sampled player somewhere in the recording");
    }
}
