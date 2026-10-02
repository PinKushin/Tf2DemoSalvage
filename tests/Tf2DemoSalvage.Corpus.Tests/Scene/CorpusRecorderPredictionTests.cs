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
