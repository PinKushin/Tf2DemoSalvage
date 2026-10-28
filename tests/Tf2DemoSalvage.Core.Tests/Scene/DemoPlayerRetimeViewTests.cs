using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>demo_retimeview</c> (D238): the recorded origin interpolated on each packet's <c>net_Tick</c> rather than its
/// arrival stamp — an addition above <c>InterpolateViewpoint</c>, whose engine behaviour
/// <see cref="DemoPlayerConformanceTests"/> holds.
/// </summary>
/// <remarks>
/// **The fixture is finding 106's shape, built by hand**: a line moving four units per SERVER tick, one packet per
/// server tick, each stamped with its server tick minus 900 — except the late ones, stamped a tick later, which is what
/// arrival jitter does (a step of 2 into the late packet, 0 out of it). Ground truth is the line: on the server's clock
/// the origin at playback tick t is <c>4 (t + 900)</c> exactly.
/// </remarks>
public sealed class DemoPlayerRetimeViewTests
{
    /// <summary>The server tick minus the stamp of an on-time packet.</summary>
    private const int Delay = 900;

    private const float Tolerance = 1e-3f;

    /// <summary>The packets that arrived a tick late in <see cref="Jittered"/>.</summary>
    private static readonly int[] Late = [10, 20, 21, 33];

    private static readonly DemoViewConVars On = DemoViewConVars.Defaults with { RetimeView = true };
    private static readonly DemoViewConVars Off = DemoViewConVars.Defaults with { RetimeView = false };

    [Test]
    public void Build_APacketCarryingANetTick_KeepsItsServerTick()
    {
        // The fixture's control (B112): the encoder wrote it and the timeline's decode read it back.
        DemoViewCommand[] packets = Packets(DemoTimeline.Build(Jittered()));

        packets.Select(packet => packet.ServerTick).ShouldBe(Enumerable.Range(0, 60).Select(k => (int?)(1000 + k)));
        packets[10].Tick.ShouldBe(111, "packet 10 is stamped late");
    }

    [Test]
    public void Build_APacketWithoutANetTick_HasNoServerTick()
    {
        Packets(DemoTimeline.Build(Jittered(withServerTicks: false))).ShouldAllBe(packet => packet.ServerTick == null);
    }

    [Test]
    public void InterpolateViewpoint_OnAJitteredDemo_PlacesTheOriginOnTheServersLine()
    {
        // Packet 10 is stamped 111 and server-timed 110. At 110.5 the engine's pair is (109, 111) and gives
        // 4 (1009 + 0.75) = 4039; the server's pair is (110, 111) and gives 4 (1010.5) = 4042, the line.
        RecordedView on = Player(Jittered(), On).InterpolateViewpoint(110.5).ShouldNotBeNull();
        RecordedView off = Player(Jittered(), Off).InterpolateViewpoint(110.5).ShouldNotBeNull();

        on.Origin.X.ShouldBe(4042f, Tolerance);
        off.Origin.X.ShouldBe(4039f, Tolerance);
    }

    [Test]
    public void InterpolateViewpoint_OnAJitteredDemo_IsTheLineAtEveryQuarterTick()
    {
        DemoPlayer player = Player(Jittered(), On);

        for (double tick = 101d; tick <= 150d; tick += 0.25d)
        {
            player.InterpolateViewpoint(tick).ShouldNotBeNull().Origin.X.ShouldBe((float)(4d * (tick + Delay)), Tolerance, $"tick {tick}");
        }
    }

    [Test]
    public void InterpolateViewpoint_OnAJitteredDemo_KeepsTheEnginesAngles()
    {
        // The view angles are cl.viewangles, the stamp's own clock: retiming moves the origin only.
        DemoPlayer on = Player(Jittered(), On);
        DemoPlayer off = Player(Jittered(), Off);

        for (double tick = 101d; tick <= 150d; tick += 0.25d)
        {
            RecordedView retimed = on.InterpolateViewpoint(tick).ShouldNotBeNull();
            RecordedView engine = off.InterpolateViewpoint(tick).ShouldNotBeNull();

            retimed.Angles.ShouldBe(engine.Angles, $"tick {tick}");
            retimed.LocalAngles.ShouldBe(engine.LocalAngles, $"tick {tick}");
        }
    }

    [Test]
    public void InterpolateViewpoint_RegularStampsWithServerTicks_IsTheEnginesAnswerBitForBit()
    {
        // The control: where every packet arrives on time the server's clock IS the stamp, so nothing may move.
        DemoPlayer on = Player(Jittered(late: []), On);
        DemoPlayer off = Player(Jittered(late: []), Off);

        for (double tick = 95d; tick <= 165d; tick += 0.125d)
        {
            on.InterpolateViewpoint(tick).ShouldBe(off.InterpolateViewpoint(tick), $"tick {tick}");
        }
    }

    [Test]
    public void InterpolateViewpoint_JitteredStampsWithoutServerTicks_IsTheEnginesAnswerBitForBit()
    {
        // The fallback: no net_Tick, no second clock.
        DemoPlayer on = Player(Jittered(withServerTicks: false), On);
        DemoPlayer off = Player(Jittered(withServerTicks: false), Off);

        for (double tick = 95d; tick <= 165d; tick += 0.125d)
        {
            on.InterpolateViewpoint(tick).ShouldBe(off.InterpolateViewpoint(tick), $"tick {tick}");
        }
    }

    [Test]
    public void InterpolateViewpoint_CrossingANoInterpPacket_IsTheEnginesFrameOnAndOff()
    {
        // The cut is late packet 33: stamped 134, server-timed 133. The engine crosses it at 134.5 and shows it there
        // (the pair ending at it, fraction clamped to one); with the origin paired on its own axis alone, that frame
        // would already be past it, blending 1034 to 1035.
        byte[] demo = Jittered(cutAt: 33);
        DemoPlayer on = Player(demo, On);
        DemoPlayer off = Player(demo, Off);

        foreach (double tick in new[] { 132.5, 133.5 })
        {
            on.InterpolateViewpoint(tick);
            off.InterpolateViewpoint(tick);
        }

        RecordedView? crossing = on.InterpolateViewpoint(134.5);

        crossing.ShouldBe(off.InterpolateViewpoint(134.5));
        crossing.ShouldNotBeNull().Origin.X.ShouldBe(4f * 1033f, Tolerance);
    }

    [Test]
    public void InterpolateViewpoint_RetimedAndRewoundToManyTargets_IsAFreshReadsAnswer()
    {
        // The rewind is the engine's reload on the stamp's axis; the second clock must not make history matter.
        byte[] demo = Jittered();
        DemoPlayer played = Player(demo, On);
        int compared = 0;

        for (int target = 100; target <= 150; target += 5)
        {
            played.InterpolateViewpoint(158d);
            DemoPlayer fresh = Player(demo, On);

            for (double tick = target; tick <= target + 10; tick += 0.25d)
            {
                played.InterpolateViewpoint(tick).ShouldBe(fresh.InterpolateViewpoint(tick), $"target {target}, tick {tick}");
                compared++;
            }
        }

        compared.ShouldBe(11 * 41);
    }

    [Test]
    public void InterpolateViewpoint_AcrossAServerHitchThatShiftsTheDelay_NeverJumps()
    {
        // The 2013 specimen's shape at stamp 6822: a 20-tick stamp gap over which the server ran 7 ticks, after which
        // every delay is 12 larger; the packet after the gap is a tick late in the new regime, so it is placed at 318
        // while the reader has not queued its successor. The line only moves forward: the camera must never step back,
        // and no frame may jump further than the engine's own worst across the same gap.
        List<DemoCommand> commands = [];

        for (int k = 0; k < 600; k++)
        {
            int server = 1000 + k + (k >= 200 ? 6 : 0);
            int late = k == 200 ? 1 : 0;
            int stamp = k < 200 ? 100 + k : 118 + k + late;

            commands.Add(SyntheticViews.Packet(stamp, (4f * server, 0f, 0f), (0f, 10f, 0f), serverTick: server));
        }

        byte[] demo = SyntheticViews.Demo([.. commands, SyntheticViews.Stop(800)]);

        (float back, float jump) = Steps(Player(demo, On));
        (_, float engineJump) = Steps(Player(demo, Off));

        back.ShouldBe(0f, "the line never moves back");
        jump.ShouldBeLessThanOrEqualTo(engineJump + Tolerance);

        static (float Back, float Jump) Steps(DemoPlayer player)
        {
            float back = 0f;
            float jump = 0f;
            float? last = null;

            for (double tick = 280d; tick < 340d; tick += 1d / 144d / SyntheticViews.Interval)
            {
                float x = player.InterpolateViewpoint(tick).ShouldNotBeNull().Origin.X;

                if (last is { } before)
                {
                    back = MathF.Max(back, before - x);
                    jump = MathF.Max(jump, x - before);
                }

                last = x;
            }

            return (back, jump);
        }
    }

    [Test]
    public void ServerTimed_OnAJitteredRun_IsEachServerTickPlusTheUsualDelay()
    {
        List<DemoViewCommand> commands = [.. Enumerable.Range(0, 60).Select(k => View(100 + k + (Late.Contains(k) ? 1 : 0), 1000 + k))];

        DemoPlayer.ServerTimed(commands, 20).ShouldBe(Enumerable.Range(0, 60).Select(k => 100 + k));
    }

    [Test]
    public void ServerTimed_APacketWithoutANetTick_KeepsItsStampAndSplitsTheRun()
    {
        List<DemoViewCommand> commands = [.. Enumerable.Range(0, 30).Select(k => View(100 + k + (k == 10 ? 1 : 0), k == 15 ? null : 1000 + k))];

        int[] ticks = DemoPlayer.ServerTimed(commands, 20);

        ticks[15].ShouldBe(115, "no net_Tick: the stamp");
        ticks[10].ShouldBe(110, "late, before the gap: placed");
        ticks[29].ShouldBe(129, "after the gap: placed in its own run");
    }

    [Test]
    public void ServerTimed_AServerTickSteppingBack_StartsANewRunAndNeverReorders()
    {
        // A server restart mid-recording: the server's tick drops to 10 while the stamps go on. The second run's delay
        // is its own (110 - 10 = 100), and the placement steps forward as the stamps do.
        List<DemoViewCommand> commands =
        [
            .. Enumerable.Range(0, 20).Select(k => View(100 + k, 1000 + k)),
            .. Enumerable.Range(0, 20).Select(k => View(120 + k + (k == 5 ? 1 : 0), 20 + k)),
        ];

        int[] ticks = DemoPlayer.ServerTimed(commands, 20);

        ticks.ShouldBe(Enumerable.Range(0, 40).Select(k => 100 + k));
    }

    [Test]
    public void ServerTimed_APlacementFurtherThanTheParseAheadFromItsStamp_KeepsItsStamp()
    {
        // A stall: packets 10-29 all arrived at stamp 130, then the client caught up. Packet 10's placement, 110, is 20
        // before its stamp, past the 8 the reader queues, so it keeps 130; the burst's later ones come within 8 and are
        // raised to 130 rather than placed before it. Every one is the engine's tick again.
        List<DemoViewCommand> commands = [.. Enumerable.Range(0, 40).Select(k => View(k is >= 10 and < 30 ? 130 : 100 + k, 1000 + k))];

        int[] ticks = DemoPlayer.ServerTimed(commands, 20);

        ticks[9].ShouldBe(109);
        ticks[10..31].ShouldAllBe(tick => tick == 130);
        ticks[31].ShouldBe(131);
    }

    [Test]
    public void ServerTimed_ADelayStepWiderThanTheParseAhead_StartsANewRunWithItsOwnDelay()
    {
        // The server lost 12 ticks at packet 100: the stamp steps 13 where the server steps 1. Without a run break the old
        // delay is the commonest across the window, every later placement lands 12 from its stamp and falls back — and
        // late packet 150 stays late.
        List<DemoViewCommand> commands = [.. Enumerable.Range(0, 200).Select(k => View(k < 100 ? 100 + k : 112 + k + Convert.ToInt32(k == 150), 1000 + k))];

        int[] ticks = DemoPlayer.ServerTimed(commands, 333);

        ticks.ShouldBe(Enumerable.Range(0, 200).Select(k => k < 100 ? 100 + k : 112 + k));
    }

    [Test]
    public void ServerTimed_ADelayDriftingByATick_FollowsTheDrift()
    {
        // The two clocks part by a tick halfway: within a few packets the usual delay is the new one, and every
        // on-time packet sits on its stamp again.
        List<DemoViewCommand> commands = [.. Enumerable.Range(0, 60).Select(k => View(100 + k + (k >= 30 ? 1 : 0), 1000 + k))];

        int[] ticks = DemoPlayer.ServerTimed(commands, 5);

        ticks[0..25].ShouldBe(Enumerable.Range(100, 25));
        ticks[36..60].ShouldBe(Enumerable.Range(136, 24).Select(tick => tick + 1));
    }

    [Test]
    public void ServerTimed_ASignonBetweenPackets_IsLeftOnItsStampAndEndsTheRun()
    {
        List<DemoViewCommand> commands =
        [
            .. Enumerable.Range(0, 10).Select(k => View(100 + k, 1000 + k)),
            new DemoViewCommand(DemoCommandType.Signon, 146124, default, 5000),
            .. Enumerable.Range(10, 10).Select(k => View(100 + k + (k == 12 ? 1 : 0), 1000 + k)),
        ];

        int[] ticks = DemoPlayer.ServerTimed(commands, 20);

        ticks[10].ShouldBe(146124);
        ticks[13].ShouldBe(112, "the late packet after the signon is still placed, in the run after it");
    }

    private static DemoViewCommand View(int stamp, int? server) =>
        new(DemoCommandType.Packet, stamp, new RecordedView { ViewOrigin = (stamp, 0f, 0f) }, server);

    private static DemoViewCommand[] Packets(DemoTimeline timeline) =>
        [.. timeline.ViewCommands.Where(command => command.Type == DemoCommandType.Packet && !command.View.IsDefault)];

    /// <summary>Sixty packets, server ticks 1000-1059, stamped server tick - 900 (+1 when late), origin 4 per server tick.</summary>
    /// <remarks>The yaw turns with the STAMP, as <c>cl.viewangles</c> does; a cut flag on <paramref name="cutAt"/> if asked.</remarks>
    internal static byte[] Jittered(bool withServerTicks = true, int[]? late = null, int cutAt = -1)
    {
        int[] lateOnes = late ?? Late;

        return SyntheticViews.Demo(
        [
            .. Enumerable.Range(0, 60).Select(k =>
            {
                int server = 1000 + k;
                int stamp = server - Delay + (lateOnes.Contains(k) ? 1 : 0);

                return SyntheticViews.Packet(
                    stamp,
                    (4f * server, 0f, 0f),
                    (0f, stamp * 0.5f, 0f),
                    flags: k == cutAt ? SyntheticViews.NoInterpolation : 0,
                    serverTick: withServerTicks ? server : null);
            }),
            SyntheticViews.Stop(160),
        ]);
    }

    private static DemoPlayer Player(byte[] demo, DemoViewConVars conVars) => DemoPlayerConformanceTests.Player(demo, conVars);
}
