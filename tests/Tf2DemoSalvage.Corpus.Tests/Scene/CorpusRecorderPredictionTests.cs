using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Prediction;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// D205 on real bytes: the recorder's predicted velocity against the networked one the next packet brings.
/// </summary>
/// <remarks>
/// **The next packet is the control, and it is the server's own answer.** Its acknowledgement covers exactly the
/// commands prediction re-ran, so a faithful port lands on the velocity it carries; holding the last packet's velocity —
/// what the timeline did before — is the baseline prediction has to beat. The 2009 POV badlands demo is chosen because
/// its packets arrive every three or four ticks, so there are commands to re-run; the 2013 one acknowledges every
/// command in the packet that follows it and prediction would add nothing.
///
/// *Interpolated:* the map is today's cp_badlands, not the 2009 build the demo names.
/// </remarks>
public sealed class CorpusRecorderPredictionTests
{
    [Test]
    public void VelocityAt_BetweenPackets_LandsCloserToTheNextPacketThanHoldingTheLastOne()
    {
        string game = SdkReference.GameInstall.Require();
        byte[] map = File.ReadAllBytes(Path.Combine(game, "maps", "cp_badlands.bsp"));
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(Corpus.Demo("tf2-2009-build3862-pov-cp_badlands")));
        MapLevel level = MapLevel.Read(map, NullLogger.Instance);

        // The viewer's wiring (B450): items_game.txt's attribute hooks, and the ground's surface data by the texinfo route.
        GameContent content = GameContent.Open(game, NullLoggerFactory.Instance);
        ImpactDecals decals = ImpactDecals.Load(map, content.Archives, content.Surfaces);
        AttributeHooks? hooks = content.Weapons.Items is { } items ? new AttributeHooks(items) : null;
        RecorderPrediction prediction = new(timeline, () => level)
        {
            Hooks = () => hooks,
            GroundSurface = trace => content.Surfaces.GetSurfaceData(decals.SurfacePropOfTrace(trace)),
        };

        // Controls: an empty wiring would leave the numbers unchanged and look like a measurement.
        hooks.ShouldNotBeNull();
        content.Surfaces.Surfaces.Count.ShouldBeGreaterThan(80);
        int recorder = timeline.RecorderEntityIndex.ShouldNotBeNull();

        IReadOnlyList<(int Tick, int Acknowledged)> packets = timeline.PacketAcknowledgements;
        IReadOnlyList<RecordedUserCommand> commands = timeline.UserCommands;

        List<float> predictedError = [];
        List<float> heldError = [];
        int declined = 0;

        for (int index = 0; index + 1 < packets.Count; index++)
        {
            (int tick, int acknowledged) = packets[index];
            (int nextTick, int nextAcknowledged) = packets[index + 1];
            // The usercmd at the next packet's tick is read before it, so the commands up to and including that tick
            // are the ones its acknowledgement covers.
            IReadOnlyList<RecordedUserCommand> pending = RecorderPrediction.Pending(commands, acknowledged, nextTick);

            if (pending.Count == 0 || pending[^1].Sequence != nextAcknowledged)
            {
                continue;
            }

            if (Velocity(timeline, recorder, tick) is not { } held ||
                Velocity(timeline, recorder, nextTick) is not { } truth)
            {
                continue;
            }

            if (prediction.PredictFrom((tick, acknowledged), nextTick) is not { } predicted)
            {
                declined++;
                continue;
            }

            predictedError.Add(Horizontal(predicted, truth));
            heldError.Add(Horizontal(held, truth));
        }

        string report = string.Create(
            CultureInfo.InvariantCulture,
            $"{predictedError.Count} packet gaps compared, {declined} declined; |Δv| horizontal, predicted: mean " +
            $"{predictedError.DefaultIfEmpty().Average():0.###} median {Median(predictedError):0.###} max " +
            $"{predictedError.DefaultIfEmpty().Max():0.###}; held: mean {heldError.DefaultIfEmpty().Average():0.###} median " +
            $"{Median(heldError):0.###} max {heldError.DefaultIfEmpty().Max():0.###}");

        TestContext.Out.WriteLine(report);

        predictedError.Count.ShouldBeGreaterThan(100, report);
        // Measured 2026-10-02: predicted 10.1 against held 25.7, median 0 against 0.7 — unchanged to 0.001 once items and the
        // ground's surfaceprop were wired (B450); scaling friction by 1.0 instead of 1.25 takes the mean to 13.1, so they are read. Under half is the bound: inverting
        // StepMove's road choice took the mean to 16.4, which "better than holding" alone let through.
        predictedError.Average().ShouldBeLessThan(heldError.Average() / 2f, report);
        Median(predictedError).ShouldBe(0f, report);
    }

    [Test]
    public void PlayersAt_ThroughTheMomentSource_DrawsTheRecorderWithPredictionsVelocity()
    {
        // B450, the output: the body TimelineMoments hands the scene carries prediction's velocity as its Velocity —
        // the one GetAbsVelocity() every reader asks, motion cloak included — wherever it differs from the networked.
        string game = SdkReference.GameInstall.Require();
        MapLevel level = MapLevel.Read(File.ReadAllBytes(Path.Combine(game, "maps", "cp_badlands.bsp")), NullLogger.Instance);
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(Corpus.Demo("tf2-2009-build3862-pov-cp_badlands")));
        int recorder = timeline.RecorderEntityIndex.ShouldNotBeNull();
        RecorderPrediction prediction = new(timeline, () => level);
        TimelineMoments moments = new(timeline) { Player = new DemoPlayer(timeline), Prediction = prediction };

        int compared = 0;

        for (int tick = timeline.FirstTick; tick < timeline.LastTick && compared < 50; tick++)
        {
            if (Velocity(timeline, recorder, tick) is not { } networked ||
                new RecorderPrediction(timeline, () => level).VelocityAt(tick) is not { } predicted ||
                Horizontal(predicted, networked) < 1f)
            {
                continue;
            }

            List<ScenePlayer> players = [];
            moments.PlayersAt(tick, players);

            players.Single(player => player.EntityIndex == recorder).Velocity.ShouldBe(predicted);
            compared++;
        }

        // The control: ticks where the two differ exist, or the assertion above never ran.
        compared.ShouldBe(50);
    }

    [Test]
    public void TakeLandings_PlayingThroughAPovDemo_SoundsEachHardLandingTheServerSawOnce()
    {
        // B172, the output: played tick by tick as the viewer does, prediction's CheckFalling hands out landing sounds, each
        // command's once. The control is the server's own m_flFallVelocity (DT_Local): it is zeroed by the server's
        // CheckFalling on the landing, so a packet with it at or past 350 followed by one on the ground with it zero is a
        // hard landing the server made from the same commands.
        string game = SdkReference.GameInstall.Require();
        byte[] map = File.ReadAllBytes(Path.Combine(game, "maps", "cp_badlands.bsp"));
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(Corpus.Demo("tf2-2009-build3862-pov-cp_badlands")));
        MapLevel level = MapLevel.Read(map, NullLogger.Instance);
        GameContent content = GameContent.Open(game, NullLoggerFactory.Instance);
        ImpactDecals decals = ImpactDecals.Load(map, content.Archives, content.Surfaces);
        int recorder = timeline.RecorderEntityIndex.ShouldNotBeNull();
        RecorderPrediction prediction = new(timeline, () => level)
        {
            GroundSurface = trace => content.Surfaces.GetSurfaceData(decals.SurfacePropOfTrace(trace)),
        };
        TimelineMoments moments = new(timeline) { Player = new DemoPlayer(timeline), Prediction = prediction };

        List<PredictedLanding> landings = [];
        List<ScenePlayer> players = [];

        for (int tick = timeline.FirstTick; tick < timeline.LastTick; tick++)
        {
            players.Clear();
            moments.PlayersAt(tick, players);
            prediction.TakeLandings(landings);
        }

        // The server's hard landings: the field is a low-precision float, so its zero arrives as 0.03125.
        List<(int Tick, float Volume)> server = [];
        float lastFall = 0f;

        foreach ((int tick, int _) in timeline.PacketAcknowledgements)
        {
            if (Find(timeline, recorder, tick) is not { Movement: { } movement } player)
            {
                continue;
            }

            if (lastFall >= 350f && movement.FallVelocity < 1f && ((player.Flags ?? 0) & 1) != 0 &&
                (player.PlayerClass != 1 || lastFall > 580f))
            {
                server.Add((tick, lastFall > 580f ? 1f : 0.85f));
            }

            lastFall = movement.FallVelocity;
        }

        List<(int Tick, float Volume)> predicted =
            [.. landings.Select(landing => (timeline.UserCommands.First(command => command.Sequence == landing.Sequence).Tick, landing.Volume))];

        string report = string.Create(
            CultureInfo.InvariantCulture,
            $"predicted (command tick, volume): {string.Join(", ", predicted)}; the server's (packet tick, volume): {string.Join(", ", server)}");

        TestContext.Out.WriteLine(report);

        // Measured 2026-10-07: 4 of the server's 7. The other three land in a command read on the tick the packet that
        // acknowledges it arrives, so no frame ever predicts it — the engine's order too, if CL_RunPrediction follows the
        // tick's messages (D205), so TF2 plays none of those three either.
        predicted.Count.ShouldBeGreaterThan(0, report);
        landings.Select(landing => landing.Sequence).ShouldBeUnique(report);
        landings.ShouldAllBe(landing => landing.Surface != null, report);

        foreach ((int tick, float volume) in predicted)
        {
            // The first server landing at or after the command — the packet acknowledging it, up to 8 ticks on (18333 → 18341).
            (int Tick, float Volume) next = server.FirstOrDefault(landing => landing.Tick >= tick);

            (next.Tick - tick).ShouldBeInRange(0, 10, report);
            next.Volume.ShouldBe(volume, report);
        }
    }

    private static ScenePlayer? Find(DemoTimeline timeline, int recorder, int tick) =>
        timeline.PlayersAt(tick).FirstOrDefault(player => player.EntityIndex == recorder && player.IsAlive);

    private static (float X, float Y, float Z)? Velocity(DemoTimeline timeline, int recorder, int tick)
    {
        foreach (ScenePlayer player in timeline.PlayersAt(tick))
        {
            if (player.EntityIndex == recorder && player.IsAlive)
            {
                return player.Velocity;
            }
        }

        return null;
    }

    private static float Horizontal((float X, float Y, float Z) a, (float X, float Y, float Z) b) =>
        MathF.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private static float Median(List<float> values)
    {
        if (values.Count == 0)
        {
            return 0f;
        }

        List<float> sorted = [.. values.Order()];
        return sorted[sorted.Count / 2];
    }
}
