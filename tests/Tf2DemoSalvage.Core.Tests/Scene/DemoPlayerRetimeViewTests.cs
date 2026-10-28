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
    public void InterpolateViewpoint_AJitteredLineUnderTheInterpLimit_IsTheLineAndNeverSnaps()
    {
        // 40 units a server tick is 2667 a second, under demo_interplimit's 4000. Across late packet 10 the server's pair
        // is (10, 11) while packet 9 is still the one read: measured from 9 over the pair's one tick it would be 5333 and
        // snap, holding packet 9 for a tick. Over the two ticks 9 to 11 actually span, it is the line.
        DemoPlayer player = Player(Jittered(speed: 40f), On);

        for (double tick = 101d; tick <= 150d; tick += 0.25d)
        {
            player.InterpolateViewpoint(tick).ShouldNotBeNull().Origin.X.ShouldBe((float)(40d * (tick + Delay)), 1e-2f, $"tick {tick}");
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
    public void InterpolateViewpoint_EveryFrameANoInterpPacketTouches_IsTheEnginesOnAndOff()
    {
        // The cut is late packet 33: stamped 134, server-timed 133. The engine crosses it at 134.5 and shows it there
        // (the pair ending at it, fraction clamped to one). On the server's axis alone the origin would cross it a
        // frame early, at 133.5, and then hold it; with the origin paired on its own axis after the stamp's cut, 134.5
        // would already be past it. Every frame the cut touches must be the engine's. 131.5 and 135.5 are unaffected
        // frames: no late packet is near them, so both axes give one pair with or without the rule, and they cannot fail.
        byte[] demo = Jittered(cutAt: 33);
        DemoPlayer on = Player(demo, On);
        DemoPlayer off = Player(demo, Off);

        double[] ticks = [131.5, 132.5, 133.5, 134.5, 135.5];
        RecordedView?[] frames = new RecordedView?[ticks.Length];

        for (int at = 0; at < ticks.Length; at++)
        {
            frames[at] = on.InterpolateViewpoint(ticks[at]);
            frames[at].ShouldBe(off.InterpolateViewpoint(ticks[at]), $"tick {ticks[at]}");
        }

        RecordedView? crossing = frames[3];

        // The control: the frame that crosses the cut shows the cut packet itself.
        crossing.ShouldNotBeNull().Origin.X.ShouldBe(4f * 1033f, Tolerance);
    }

    [Test]
    public void InterpolateViewpoint_AFreshPlayerAskedOnceBesideAnEarlyCut_IsTheEnginesFrame()
    {
        // No history, so no cut window on either axis: at 133.5 the server's pair is (cut 33, 34), already past the
        // cut, while the engine's is (32, cut 33) — the pair that starts AT a cut, which only its ends can show.
        byte[] demo = Jittered(cutAt: 33);

        Player(demo, On).InterpolateViewpoint(133.5).ShouldBe(Player(demo, Off).InterpolateViewpoint(133.5));
    }

    [Test]
    public void InterpolateViewpoint_ACutTwoTicksEarly_IsTheEnginesFrameThroughout()
    {
        // Cut 33 is stamped 135 and placed 133, its successors 34 and 35 stamped 135 and placed 134 and 135. At 134.5 the
        // server's pair is (34, 35), wholly past the cut, while the engine still blends (32, cut 33) and has not taken
        // its cut branch: no end and no cut branch of the server's pair shows it.
        byte[] demo = Jittered(cutAt: 33, lateness: k => k switch { 33 => 2, 34 => 1, _ => 0 });
        DemoPlayer on = Player(demo, On);
        DemoPlayer off = Player(demo, Off);

        for (double tick = 131.5; tick <= 137.5; tick += 1d)
        {
            on.InterpolateViewpoint(tick).ShouldBe(off.InterpolateViewpoint(tick), $"tick {tick}");
        }
    }

    [Test]
    public void InterpolateViewpoint_ACutHeldPastAGapWiderThanTheParseAhead_IsTheEnginesFrame()
    {
        // An 11-tick gap, then cut packet 20, stamped 131 and placed 130. The reader queues nothing past it until it is
        // read, so at 130 the server's axis has no pair and the origin would hold the cut packet while the angles still
        // blend toward it.
        List<DemoCommand> commands = [];

        for (int k = 0; k < 200; k++)
        {
            int stamp = k < 20 ? 100 + k : 111 + k;
            int server = k < 20 ? 1000 + k : 1011 + k - Convert.ToInt32(k == 20);

            commands.Add(SyntheticViews.Packet(
                stamp, (4f * server, 0f, 0f), (0f, stamp * 0.5f, 0f), flags: k == 20 ? SyntheticViews.NoInterpolation : 0, serverTick: server));
        }

        byte[] demo = SyntheticViews.Demo([.. commands, SyntheticViews.Stop(400)]);
        DemoPlayer on = Player(demo, On);
        DemoPlayer off = Player(demo, Off);

        for (double tick = 127.5; tick <= 133.5; tick += 1d)
        {
            on.InterpolateViewpoint(tick).ShouldBe(off.InterpolateViewpoint(tick), $"tick {tick}");
        }
    }

    [Test]
    public void InterpolateViewpoint_ACutLiftedAboveItsStampByTheRaise_IsTheEnginesFrame()
    {
        // Packet 32 arrived a tick early (stamp 132, placed 133). Cut 33 carries no net_Tick, so it starts from its stamp,
        // 132, and is raised to 133 past packet 32 — a cut placed LATE. At 133 the server's pair starts at it while the
        // engine blends (cut 33, 34) from 132.
        List<DemoCommand> commands = [];

        for (int k = 0; k < 120; k++)
        {
            int stamp = k == 33 ? 132 : 100 + k;
            int? server = k switch { 32 => 1033, 33 => null, _ => 1000 + k };

            commands.Add(SyntheticViews.Packet(
                stamp, (4f * (server ?? 1033), 0f, 0f), (0f, stamp * 0.5f, 0f), flags: k == 33 ? SyntheticViews.NoInterpolation : 0, serverTick: server));
        }

        byte[] demo = SyntheticViews.Demo([.. commands, SyntheticViews.Stop(300)]);
        DemoPlayer on = Player(demo, On);
        DemoPlayer off = Player(demo, Off);

        // The fixture's control: the cut really is placed above its stamp.
        on.ServerTimedTicks[Index(DemoTimeline.Build(demo), 33)].ShouldBe(133);

        // Before 132 neither pair reaches the cut, and the origin is retimed: at 131.5 it blends toward packet 32 at its
        // placement, 133, where the engine blends toward its stamp, 132 — the control that the setting is on here.
        on.InterpolateViewpoint(131.5).ShouldNotBe(off.InterpolateViewpoint(131.5));

        for (double tick = 132d; tick <= 136.5; tick += 0.25d)
        {
            on.InterpolateViewpoint(tick).ShouldBe(off.InterpolateViewpoint(tick), $"tick {tick}");
        }
    }

    [Test]
    public void InterpolateViewpoint_FirstFramesOnAPacketPlacedAfterItsStamp_AreTheEnginesThenTheLine()
    {
        // The first packet arrived a tick early: stamp 99, placed 100. Until 100 the target lies before every queued
        // placement — the server's axis has no pair and nothing to hold — so the origin is exactly the engine's blend;
        // from 100 the server's pair takes over and the origin is exactly the line. Played from the start, and again after
        // seeking back from 104 to 99, which lands the same way.
        List<DemoCommand> commands = [.. Enumerable.Range(0, 60).Select(k => SyntheticViews.Packet(
            k == 0 ? 99 : 100 + k, (4f * (1000 + k), 0f, 0f), (0f, 10f, 0f), serverTick: 1000 + k))];
        byte[] demo = SyntheticViews.Demo([.. commands, SyntheticViews.Stop(200)]);
        DemoPlayer on = Player(demo, On);
        DemoPlayer off = Player(demo, Off);

        void Play()
        {
            for (double tick = 99d; tick <= 104d; tick += 0.125d)
            {
                RecordedView now = on.InterpolateViewpoint(tick).ShouldNotBeNull();
                RecordedView engine = off.InterpolateViewpoint(tick).ShouldNotBeNull();

                if (tick < 100d)
                {
                    now.ShouldBe(engine, $"tick {tick}: before the hand-over, the engine's blend");
                }
                else
                {
                    now.Origin.X.ShouldBe((float)(4d * (tick + 900d)), Tolerance, $"tick {tick}: after it, the server's line");
                }
            }
        }

        Play();

        // The control: the hand-over is real — at 100.5 the engine blends (99, 101) and the line does not.
        on.InterpolateViewpoint(100.5).ShouldNotBe(off.InterpolateViewpoint(100.5));

        // The first Play ended at 104, so this one starts with a seek back to 99.
        Play();
    }

    private static int Index(DemoTimeline timeline, int packet) =>
        Enumerable.Range(0, timeline.ViewCommands.Count)
            .Where(at => timeline.ViewCommands[at].Type == DemoCommandType.Packet && !timeline.ViewCommands[at].View.IsDefault)
            .ElementAt(packet);

    [Test]
    public void InterpolateViewpoint_DemoRetimeViewChangedWhilePaused_AppliesOnTheNextCall()
    {
        // B450's rule for every demo_* ConVar, and a pause asks one moment again and again: the repeat may not answer
        // from before the change.
        DemoViewConVars vars = On;
        DemoPlayer player = new(DemoTimeline.Build(Jittered())) { ConVars = () => vars };

        player.InterpolateViewpoint(110.5).ShouldNotBeNull().Origin.X.ShouldBe(4042f, Tolerance);

        vars = Off;

        player.InterpolateViewpoint(110.5).ShouldNotBeNull().Origin.X.ShouldBe(4039f, Tolerance);
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
    public void ServerTimed_APacketWithoutANetTick_StartsFromItsStampAndSplitsTheRun()
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
    public void ServerTimed_APlacementFurtherThanTheParseAheadFromItsStamp_FallsBackAndIsNeverPlacedBeforeTheOneBefore()
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
    public void ServerTimed_TwoDelaysAlternatingAtATie_KeepsOneAndNeverFlips()
    {
        // Every odd packet of the first 400 a tick late, then 100 on time: across a 101-packet window the two delays lead
        // by one in turn as it slides, which flips the bare commonest delay nearly every packet and, taken, would step the
        // placements by 0 and 2 in turn. Every one of those flips comes back within the window. The on-time tail puts the
        // near-tie mid-run; the twin below ends the run in it.
        List<DemoViewCommand> commands = [.. Enumerable.Range(0, 500).Select(k => View(100 + k + (k < 400 ? k % 2 : 0), 1000 + k))];
        DemoPlayer.ServerTimedChanges changes = new();

        int[] ticks = DemoPlayer.ServerTimed(commands, 50, changes);

        changes.Commonest.Count.ShouldBeGreaterThan(100, "the control: the bare commonest delay does flip");
        changes.Applied.ShouldBeEmpty();
        ticks.ShouldBe(Enumerable.Range(0, 500).Select(k => 100 + k));
    }

    [Test]
    public void ServerTimed_TwoDelaysAlternatingAtATieToTheRunsEnd_KeepsOneThroughTheTail()
    {
        // The twin of the test above without its on-time tail. The tail kept the end unambiguous there. Here the run ENDS
        // in the near-tie: packet 399's window [349, 399] holds 26 late against 25 on time, so its commonest delay flips,
        // and nothing ahead could bring the other back. A change with less than a window ahead of it is held.
        List<DemoViewCommand> commands = [.. Enumerable.Range(0, 400).Select(k => View(100 + k + (k % 2), 1000 + k))];
        DemoPlayer.ServerTimedChanges changes = new();

        int[] ticks = DemoPlayer.ServerTimed(commands, 50, changes);

        changes.Commonest[^1].Index.ShouldBe(399, "the control: the tail's last packet does flip");
        changes.Applied.ShouldBeEmpty();
        ticks.ShouldBe(Enumerable.Range(0, 400).Select(k => 100 + k));
    }

    [Test]
    public void HoldShortChanges_ANoisyStep_IsTakenAfterTheOldDelaysLastReturn()
    {
        // The step at 500, but the old delay is the commonest once more at 600. Every packet 500-599 has that return
        // within its window, so the change is held; 601 has none ahead, so it is taken there — after the last return,
        // however many come in a chain.
        long[] commonest =
            [.. Enumerable.Repeat(-900L, 500), .. Enumerable.Repeat(-899L, 100), -900L, .. Enumerable.Repeat(-899L, 600)];

        long[] held = DemoPlayer.HoldShortChanges(commonest, 333);

        held[..601].ShouldAllBe(delay => delay == -900L);
        held[601..].ShouldAllBe(delay => delay == -899L);
    }

    [Test]
    public void HoldShortChanges_AChangeRevertingWithinTheWindow_IsNotTaken()
    {
        // Ten packets of the other delay, and a window of ten: the delay in force is back at the tenth packet after.
        long[] commonest = [.. Enumerable.Repeat(-900L, 100), .. Enumerable.Repeat(-899L, 10), .. Enumerable.Repeat(-900L, 100)];

        DemoPlayer.HoldShortChanges(commonest, 10).ShouldAllBe(delay => delay == -900L);
    }

    [Test]
    public void HoldShortChanges_AChangeLastingLongerThanTheWindow_IsTakenAtItsFirstPacket()
    {
        // Eleven packets against a window of ten: taken at the very packet it starts, and left at the packet it ends.
        long[] commonest = [.. Enumerable.Repeat(-900L, 100), .. Enumerable.Repeat(-899L, 11), .. Enumerable.Repeat(-900L, 100)];

        DemoPlayer.HoldShortChanges(commonest, 10).ShouldBe(commonest);
    }

    [Test]
    public void ServerTimed_AOneTickStepAtTheProductionWindow_IsFollowedAtTheStepWithNoLag()
    {
        // From packet 500 every packet arrives a tick later, for good. At the 333-packet window the commonest delay
        // changes at packet 500 exactly, and that change is taken there: every packet, before and after, sits on its stamp.
        // A clean step: the bare commonest delay with no hold passes this too. The noisy step is
        // HoldShortChanges_ANoisyStep_IsTakenAfterTheOldDelaysLastReturn.
        List<DemoViewCommand> commands = [.. Enumerable.Range(0, 1000).Select(k => View(100 + k + (k >= 500 ? 1 : 0), 1000 + k))];
        DemoPlayer.ServerTimedChanges changes = new();

        int[] ticks = DemoPlayer.ServerTimed(commands, 333, changes);

        changes.Applied.ShouldBe([(500, -900L, -899L)]);
        ticks.ShouldBe(commands.Select(command => command.Tick));
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
    internal static byte[] Jittered(
        bool withServerTicks = true, int[]? late = null, int cutAt = -1, float speed = 4f, Func<int, int>? lateness = null)
    {
        int[] lateOnes = late ?? Late;
        lateness ??= k => lateOnes.Contains(k) ? 1 : 0;

        return SyntheticViews.Demo(
        [
            .. Enumerable.Range(0, 60).Select(k =>
            {
                int server = 1000 + k;
                int stamp = server - Delay + lateness(k);

                return SyntheticViews.Packet(
                    stamp,
                    (speed * server, 0f, 0f),
                    (0f, stamp * 0.5f, 0f),
                    flags: k == cutAt ? SyntheticViews.NoInterpolation : 0,
                    serverTick: withServerTicks ? server : null);
            }),
            SyntheticViews.Stop(160),
        ]);
    }

    private static DemoPlayer Player(byte[] demo, DemoViewConVars conVars) => DemoPlayerConformanceTests.Player(demo, conVars);
}
