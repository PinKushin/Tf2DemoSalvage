using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>CDemoPlayer::InterpolateViewpoint</c>, read out of the x64 <c>engine.dll</c> (B56): the recorded view between
/// packets, as the running game draws it.
/// </summary>
/// <remarks>
/// <c>SCR_UpdateScreen</c> (<c>0x1800e8b40</c>) calls it every rendered frame of playback, before
/// <c>FRAME_RENDER_START</c> (<c>0x1800e8d36</c>). Its pieces, by address:
///
/// - <c>0x180072180</c> InterpolateViewpoint: target tick, fraction, snaps, lerp and slerp;
/// - <c>0x180072ee0</c> ReadPacket: every signon and packet read sets the current view and
///   <c>m_bInterpolateView</c> (+0x63c) from ParseAheadForInterval( tick, 8 ); <c>dem_synctick</c> sets +0x554;
/// - <c>0x180072af0</c> ParseAheadForInterval: false at a <c>dem_synctick</c> or <c>dem_stop</c> (or end of file,
///   which ReadCmdHeader <c>0x1800be370</c> answers as stop) before the first packet more than 8 ticks on;
/// - <c>0x180071fd0</c>: the pair <c>prev.tick &lt;= target &lt; next.tick</c>, or the pair ending at an
///   <c>FDEMO_NOINTERP</c> packet in <c>(lastInterpolatedTick, target]</c>.
///
/// Every case is a synthetic demo (D38) on TF2's 0.015 s tick.
/// </remarks>
public sealed class DemoPlayerConformanceTests
{
    private const float Tolerance = 1e-3f;

    /// <summary>The registrations' defaults with <c>demo_retimeview 0</c>: this suite is the engine's, and the addition is not (D238).</summary>
    private static readonly DemoViewConVars Engine = DemoViewConVars.Defaults with { RetimeView = false };

    [Test]
    public void InterpolateViewpoint_BeforeAnyViewIsRead_IsNull()
    {
        // The current democmdinfo_t is still the default one (the ServerInfo packet carries zeros), so the
        // engine sets nothing (0x1800722a5).
        DemoPlayer player = Player(SyntheticViews.Demo(SyntheticViews.Packet(100, (10f, 0f, 0f))));

        player.InterpolateViewpoint(50.5).ShouldBeNull();
    }

    [Test]
    public void InterpolateViewpoint_OnAPacketsOwnTick_IsThatPacketsView()
    {
        // Fraction zero: the earlier packet of the pair, whose tick the target is.
        DemoPlayer player = Player(SyntheticViews.Demo(
            SyntheticViews.Packet(100, (10f, 20f, 30f), (5f, 6f, 0f)),
            SyntheticViews.Packet(101, (20f, 20f, 30f), (5f, 6f, 0f)),
            SyntheticViews.Packet(120, (30f, 20f, 30f), (5f, 6f, 0f))));

        RecordedView view = player.InterpolateViewpoint(100d).ShouldNotBeNull();

        view.Origin.X.ShouldBe(10f, Tolerance);
        view.Angles.Yaw.ShouldBe(6f, Tolerance);
    }

    [Test]
    public void Build_TheServerInfoMaxPlayers_IsTheTimelinesMaxClients()
    {
        // `cl.m_nMaxClients` (DAT_180536be4) decides the rollback, and svc_ServerInfo is where it comes from.
        DemoTimeline.Build(SyntheticViews.Demo(1, [SyntheticViews.Packet(100, (1f, 0f, 0f))])).MaxClients.ShouldBe(1);
    }

    [Test]
    public void Build_WithTheDefaultInterp_ReportsGetClientInterpAmountAsATenth()
    {
        // max( cl_interp 0.1, cl_interp_ratio 2 / cl_updaterate 20 ), the rollback's other input.
        DemoTimeline.Build(SyntheticViews.Demo(SyntheticViews.Packet(100, (1f, 0f, 0f))))
            .ClientInterpAmount.ShouldBe(0.1d, 1e-6d);
    }

    [Test]
    public void InterpolateViewpoint_AQuarterIntoATick_LerpsTheOriginAQuarterOfTheWay()
    {
        // fraction = ((target - prev.tick) * TI + host_remainder) / ((next.tick - prev.tick) * TI) = 0.25, and the
        // origin is (next - prev) * fraction + prev (0x18007240a..0x18007265a).
        RecordedView view = Player(Line()).InterpolateViewpoint(105.25).ShouldNotBeNull();

        view.Origin.X.ShouldBe(421f, Tolerance);
    }

    [Test]
    public void InterpolateViewpoint_HalfwayAcrossTheYawWrap_TurnsTheShortWay()
    {
        // AngleQuaternion, QuaternionSlerp (align, then slerp: 0x180278c60), QuaternionAngles: 179 to -179 passes
        // through 180, not through zero.
        byte[] demo = SyntheticViews.Demo(
            [.. SyntheticViews.Run(100, 130, tick => SyntheticViews.Packet(
                tick, (tick * 4f, 0f, 0f), (0f, tick <= 110 ? 179f : -179f, 0f)))]);

        RecordedView view = Player(demo).InterpolateViewpoint(110.5).ShouldNotBeNull();

        MathF.Abs(MathF.Abs(view.Angles.Yaw) - 180f).ShouldBeLessThan(0.01f);
        MathF.Abs(MathF.Abs(view.LocalAngles.Yaw) - 180f).ShouldBeLessThan(0.01f);
    }

    [Test]
    public void InterpolateViewpoint_AnOriginFasterThanTheInterpLimit_SnapsToTheCurrentView()
    {
        // |next.GetViewOrigin() - current.GetViewOrigin()| / dt > demo_interplimit (4000): 100 units in a tick is
        // 6667 a second, so the current packet's view is used as it stands.
        RecordedView view = Player(Jump(100f)).InterpolateViewpoint(119.5).ShouldNotBeNull();

        view.Origin.X.ShouldBe(476f, Tolerance);
    }

    [Test]
    public void InterpolateViewpoint_AnOriginUnderTheInterpLimit_Interpolates()
    {
        // The control: 50 units in a tick is 3333 a second.
        RecordedView view = Player(Jump(50f)).InterpolateViewpoint(119.5).ShouldNotBeNull();

        view.Origin.X.ShouldBe(503f, Tolerance);
    }

    [Test]
    public void InterpolateViewpoint_LocalAnglesTurningFasterThanTheAvelLimit_SnapsToTheCurrentView()
    {
        // The largest |AngleNormalize(AngleNormalizePositive(next.local) - AngleNormalizePositive(prev.local))| / dt
        // over the three axes, against demo_avellimit (2000): 40 degrees in a tick is 2667 a second.
        RecordedView view = Player(Turn(localJumps: true)).InterpolateViewpoint(119.5).ShouldNotBeNull();

        view.Angles.Yaw.ShouldBe(10f, Tolerance);
        view.Origin.X.ShouldBe(476f, Tolerance);
    }

    [Test]
    public void InterpolateViewpoint_ViewAnglesTurningFastWithStillLocalAngles_Interpolates()
    {
        // The engine measures the LOCAL angles, so the same jump in the view angles alone does not snap.
        RecordedView view = Player(Turn(localJumps: false)).InterpolateViewpoint(119.5).ShouldNotBeNull();

        view.Angles.Yaw.ShouldBe(30f, 0.01f);
        view.Origin.X.ShouldBe(478f, Tolerance);
    }

    [Test]
    public void InterpolateViewpoint_CrossingANoInterpPacket_ShowsThatPacketExactly()
    {
        // A FDEMO_NOINTERP packet in (lastInterpolatedTick, target] takes the pair ending at it, whose fraction
        // clamps to one: the frame that crosses it shows the cut packet itself, not a blend toward the next.
        DemoPlayer player = Player(Cut());

        player.InterpolateViewpoint(119.5);
        RecordedView crossing = player.InterpolateViewpoint(120.5).ShouldNotBeNull();
        RecordedView after = player.InterpolateViewpoint(120.75).ShouldNotBeNull();

        crossing.Origin.X.ShouldBe(480f, Tolerance);
        after.Origin.X.ShouldBe(483f, Tolerance);
    }

    [Test]
    public void InterpolateViewpoint_WithinEightTicksOfTheStop_UsesTheCurrentView()
    {
        // ParseAheadForInterval meets dem_stop before a packet more than 8 ticks on: m_bInterpolateView is false.
        byte[] demo = SyntheticViews.Demo(
            [.. SyntheticViews.Run(100, 110, tick => SyntheticViews.Packet(tick, (tick * 4f, 0f, 0f))), SyntheticViews.Stop(110)]);

        Player(demo).InterpolateViewpoint(105.5).ShouldNotBeNull().Origin.X.ShouldBe(420f, Tolerance);
    }

    [Test]
    public void InterpolateViewpoint_NineTicksBeforeTheStop_Interpolates()
    {
        // The control in the same demo shape: packet 110 is nine ticks on from 101, so the scan ends before the stop.
        byte[] demo = SyntheticViews.Demo(
            [.. SyntheticViews.Run(100, 110, tick => SyntheticViews.Packet(tick, (tick * 4f, 0f, 0f))), SyntheticViews.Stop(110)]);

        Player(demo).InterpolateViewpoint(101.5).ShouldNotBeNull().Origin.X.ShouldBe(406f, Tolerance);
    }

    [Test]
    public void InterpolateViewpoint_WithinEightTicksBeforeASyncTick_UsesTheCurrentView()
    {
        byte[] demo = SyntheticViews.Demo(
        [
            .. SyntheticViews.Run(100, 105, tick => SyntheticViews.Packet(tick, (tick * 4f, 0f, 0f))),
            SyntheticViews.SyncTick(105),
            .. SyntheticViews.Run(106, 130, tick => SyntheticViews.Packet(tick, (tick * 4f, 0f, 0f))),
        ]);

        Player(demo).InterpolateViewpoint(103.5).ShouldNotBeNull().Origin.X.ShouldBe(412f, Tolerance);
    }

    [Test]
    public void InterpolateViewpoint_WithDemoInterpolateViewOff_UsesTheCurrentView()
    {
        DemoPlayer player = Player(Line(), Engine with { InterpolateView = false });

        player.InterpolateViewpoint(105.5).ShouldNotBeNull().Origin.X.ShouldBe(420f, Tolerance);
    }

    /// <remarks>
    /// <c>InterpolateViewpoint</c> (<c>0x180072180</c>) reads <c>demo_interpolateview</c>'s <c>m_nValue</c> on every
    /// call, so a change made while the demo plays applies on the next frame, not the next open (B450).
    /// </remarks>
    [Test]
    public void InterpolateViewpoint_DemoInterpolateViewChangedWhilePlaying_AppliesOnTheNextCall()
    {
        DemoViewConVars vars = Engine with { InterpolateView = false };
        DemoPlayer player = new(DemoTimeline.Build(Line())) { ConVars = () => vars };

        player.InterpolateViewpoint(105.5).ShouldNotBeNull().Origin.X.ShouldBe(420f, Tolerance);

        vars = Engine;

        player.InterpolateViewpoint(106.5).ShouldNotBeNull().Origin.X.ShouldBe(426f, Tolerance);
    }

    [Test]
    public void InterpolateViewpoint_TheUseOrigin2Flag_InterpolatesFromTheResampledOriginAndTheOriginalAngles()
    {
        // Every Get*() honours its own flag: packet 105 selects viewOrigin2 and leaves its angles original.
        byte[] demo = SyntheticViews.Demo(
            [.. SyntheticViews.Run(100, 130, tick => tick == 105
                ? SyntheticViews.Packet(
                    tick, (9999f, 0f, 0f), (0f, 10f, 0f), flags: SyntheticViews.UseOrigin2,
                    origin2: (420f, 0f, 0f), angles2: (0f, 90f, 0f))
                : SyntheticViews.Packet(tick, (tick * 4f, 0f, 0f), (0f, 10f, 0f)))]);

        RecordedView view = Player(demo).InterpolateViewpoint(105.5).ShouldNotBeNull();

        view.Origin.X.ShouldBe(422f, Tolerance);
        view.Angles.Yaw.ShouldBe(10f, 0.01f);
    }

    [Test]
    public void InterpolateViewpoint_TheUseAngles2Flag_InterpolatesFromTheResampledAnglesAndTheOriginalOrigin()
    {
        byte[] demo = SyntheticViews.Demo(
            [.. SyntheticViews.Run(100, 130, tick => tick == 105
                ? SyntheticViews.Packet(
                    tick, (420f, 0f, 0f), (0f, 90f, 0f), (0f, 90f, 0f), SyntheticViews.UseAngles2,
                    origin2: (9999f, 0f, 0f), angles2: (0f, 10f, 0f), localAngles2: (0f, 10f, 0f))
                : SyntheticViews.Packet(tick, (tick * 4f, 0f, 0f), (0f, 10f, 0f)))]);

        RecordedView view = Player(demo).InterpolateViewpoint(105.5).ShouldNotBeNull();

        view.Origin.X.ShouldBe(422f, Tolerance);
        view.Angles.Yaw.ShouldBe(10f, 0.01f);
        view.LocalAngles.Yaw.ShouldBe(10f, 0.01f);
    }

    [Test]
    public void InterpolateViewpoint_OnASinglePlayerServer_RollsBackOnePlusTheInterpTicks()
    {
        // cl.m_nMaxClients == 1 with demo_legacy_rollback: 1 + (int)(0.1 / 0.015 + 0.5) = 8 ticks back.
        RecordedView view = Player(Line(maxPlayers: 1)).InterpolateViewpoint(120.5).ShouldNotBeNull();

        view.Origin.X.ShouldBe(450f, Tolerance);
    }

    [Test]
    public void InterpolateViewpoint_OnASinglePlayerServerWithoutLegacyRollback_RollsBackOneTick()
    {
        DemoPlayer player = Player(Line(maxPlayers: 1), Engine with { LegacyRollback = false });

        player.InterpolateViewpoint(120.5).ShouldNotBeNull().Origin.X.ShouldBe(478f, Tolerance);
    }

    [Test]
    public void InterpolateViewpoint_ACutSharingItsPredecessorsTick_IsTheCutPartwayIntoTheTick()
    {
        // The cut pair has dt == 0: a positive numerator times 1/0 is +inf, clamped to one (MINSS).
        DemoPlayer player = Player(SameTickCut());

        player.InterpolateViewpoint(119.5);
        player.InterpolateViewpoint(120.5).ShouldNotBeNull().Origin.X.ShouldBe(800f, Tolerance);
    }

    [Test]
    public void InterpolateViewpoint_ACutSharingItsPredecessorsTick_IsThePredecessorOnTheTickItself()
    {
        // 0 * (1/0) is NaN, and MAXSS answers its source operand, zero (0x180072469): the earlier packet.
        DemoPlayer player = Player(SameTickCut());

        player.InterpolateViewpoint(119.5);
        player.InterpolateViewpoint(120d).ShouldNotBeNull().Origin.X.ShouldBe(480f, Tolerance);
    }

    [Test]
    public void InterpolateViewpoint_AfterResetDemoInterpolation_SnapsOnceThenInterpolates()
    {
        // m_bResetInterpolation (+0x63d) forces the current view and is cleared by doing so.
        DemoPlayer player = Player(Line());

        player.InterpolateViewpoint(119.5).ShouldNotBeNull().Origin.X.ShouldBe(478f, Tolerance);

        player.ResetDemoInterpolation();

        player.InterpolateViewpoint(119.5).ShouldNotBeNull().Origin.X.ShouldBe(476f, Tolerance);
        player.InterpolateViewpoint(119.75).ShouldNotBeNull().Origin.X.ShouldBe(479f, Tolerance);
    }

    [Test]
    public void InterpolateViewpoint_AfterARewind_AnswersAsAPlayerThatNeverWentPast()
    {
        // A rewind reloads and skips forward; the list it rebuilds is the one a cold read reaches.
        DemoPlayer played = Player(Cut());

        for (double tick = 100d; tick <= 128d; tick += 0.5d)
        {
            played.InterpolateViewpoint(tick);
        }

        DemoPlayer cold = Player(Cut());

        played.InterpolateViewpoint(110.25).ShouldBe(cold.InterpolateViewpoint(110.25));
        played.InterpolateViewpoint(110.25).ShouldNotBeNull().Origin.X.ShouldBe(441f, Tolerance);
    }

    /// <summary>
    /// `ViewsAt` is the seek scan's side-effect-free question, and must answer what the frame's `ReadPackets` reads: on a
    /// demo whose packet ticks run backwards, a binary search over the stream and the frame's linear read disagree.
    /// </summary>
    [Test]
    public void ViewsAt_PacketTicksRunningBackwards_AgreesWithAFreshFrameAtEveryTick()
    {
        byte[] demo = Backwards();
        DemoPlayer probe = Player(demo);

        for (int tick = 95; tick <= 145; tick++)
        {
            probe.ViewsAt(tick).ShouldBe(Player(demo).InterpolateViewpoint(tick) is not null, $"tick {tick}");
        }
    }

    /// <summary>
    /// The frame itself must not depend on how it got there: `CDemoPlayer` rewinds only by `SkipToTick`'s reload, so a
    /// rewind lands in the state a fresh read does, even when the packet read last has a tick at or below the target.
    /// </summary>
    [Test]
    public void InterpolateViewpoint_RewoundOnABackwardsTickDemo_IsAFreshReadsAnswer()
    {
        byte[] demo = Backwards();
        DemoPlayer played = Player(demo);

        // Tick 120 reads past P120 into the backward run, whose last packet (tick 110, default view) is current.
        played.InterpolateViewpoint(120d).ShouldBeNull("the control: the backward run's default view is current");

        RecordedView? rewound = played.InterpolateViewpoint(115d);

        // The value itself is the parse-ahead's across the backward run; what is claimed is that history does not change it.
        rewound.ShouldNotBeNull("a fresh read at 115 has P115 current, a real view");
        rewound.ShouldBe(Player(demo).InterpolateViewpoint(115d));
    }

    /// <summary>
    /// A signon is read when reached, whatever its tick (`ReadPacket`'s mask, 0x180072ee0); only a `dem_packet` waits. A
    /// real POV demo's signons carry the server's tick — 146,124 on etf2l-12025 against packets from 0 — so a "first packet
    /// past the tick" that counted them would be 0 everywhere: `ViewsAt` always false, and the frame restarting each read.
    /// </summary>
    [Test]
    public void ViewsAt_SignonTickedPastEveryPacket_AgreesWithAFreshFrameAtEveryTick()
    {
        byte[] demo = SignonFirstCut();
        DemoPlayer probe = Player(demo);

        Player(demo).InterpolateViewpoint(110d).ShouldNotBeNull("the control: the frame reads the line's views");

        for (int tick = 95; tick <= 135; tick++)
        {
            probe.ViewsAt(tick).ShouldBe(Player(demo).InterpolateViewpoint(tick) is not null, $"tick {tick}");
        }
    }

    /// <summary>The same demo played through: a frame that restarted on every read would lose the cut window.</summary>
    [Test]
    public void InterpolateViewpoint_SignonTickedPastEveryPacket_StillCrossesTheCutExactly()
    {
        DemoPlayer player = Player(SignonFirstCut());

        player.InterpolateViewpoint(119.5);

        player.InterpolateViewpoint(120.5).ShouldNotBeNull().Origin.X.ShouldBe(480f, Tolerance);
    }

    /// <summary>
    /// **A rewind re-reads only the skip's tail** (`SkipToTick`'s reads land in the state a read from the start does,
    /// `0x180073b10`): a signon ticked past every packet stood at index 0, and the start-point loop stopped at the first
    /// command of any type past the target, so every rewind on etf2l-12025 re-read from the start.
    /// </summary>
    [Test]
    public void InterpolateViewpoint_RewoundPastASignonTickedPastEveryPacket_ReadsOnlyTheSkipsTail()
    {
        DemoPlayer played = Player(SignonFirstLongLine());

        played.InterpolateViewpoint(1000d);
        long before = played.CommandsRead;
        played.InterpolateViewpoint(900d);

        // From the last packet before 900 - 40 to 900: 41 or so; the unbounded loop read 801.
        (played.CommandsRead - before).ShouldBeLessThanOrEqualTo(42);
    }

    /// <summary>
    /// The bound must not change where a rewind lands: for every target, a rewound player answers what a fresh read does,
    /// bit for bit, over the next fifty ticks — long enough to cross the parse-ahead's 32 + 8 and a cut every 37 ticks.
    /// </summary>
    /// <remarks>
    /// The single-player case is the one that sees the skip's 32 + 8 back-off: its interpolation target is eight ticks
    /// behind the playback tick (`demo_legacy_rollback`), so a start nearer the target lacks the pair it needs.
    /// </remarks>
    [TestCase(false, (byte)24)]
    [TestCase(false, (byte)1)]
    [TestCase(true, (byte)24)]
    public void InterpolateViewpoint_RewoundToManyTargets_IsAFreshReadsAnswerThroughTheNextFiftyTicks(bool backwards, byte maxPlayers)
    {
        byte[] demo = backwards ? Backwards() : SignonFirstLongLine(maxPlayers);
        int[] targets = backwards ? [.. Enumerable.Range(95, 50)] : [.. Enumerable.Range(0, 60).Select(at => 101 + (at * 17))];
        DemoPlayer played = Player(demo);
        int compared = 0;

        foreach (int target in targets)
        {
            played.InterpolateViewpoint(backwards ? 145d : 1100d);
            DemoPlayer fresh = Player(demo);

            for (double tick = target; tick <= target + 50; tick += 0.25d)
            {
                played.InterpolateViewpoint(tick).ShouldBe(fresh.InterpolateViewpoint(tick), $"target {target}, tick {tick}");
                compared++;
            }
        }

        compared.ShouldBe(targets.Length * 201);
    }

    /// <summary>
    /// **A bounded rewind lands in a fresh read's state, queue included** (batch-33 review): `SkipToTick` reloads, so the
    /// parse-ahead list after a rewind must be the one a read from the start builds — not just the views it happens to
    /// produce. Leading signons ticked past every packet, so the start is searched back from the stop. Three shapes: a gap
    /// wider than the 32 + 8 the skip backs off, where a start counted back from the TARGET rather than from the last
    /// packet read leaves that packet's predecessors out of the list; a run whose ticks go backwards, whose parse-ahead
    /// clears the list; and signons with no `dem_synctick` before the packets, whose parse-ahead runs to the end of the
    /// file because no packet is ever 8 ticks past 146,124.
    /// </summary>
    [TestCase("gapped")]
    [TestCase("backwards")]
    [TestCase("no synctick")]
    [TestCase("backwards after synctick")]
    public void InterpolateViewpoint_RewoundToManyTargets_QueuesWhatAFreshReadQueues(string shape)
    {
        byte[] demo = shape switch
        {
            "gapped" => SignonFirstGappedLine(synctick: true),
            "backwards" => SignonFirstBackwardsLine(syncBefore: false),
            "backwards after synctick" => SignonFirstBackwardsLine(syncBefore: true),
            _ => SignonFirstGappedLine(synctick: false),
        };
        DemoPlayer played = Player(demo);
        int compared = 0;

        for (int target = 100; target <= 600; target += 3)
        {
            played.InterpolateViewpoint(650d);
            DemoPlayer fresh = Player(demo);

            for (double tick = target; tick <= target + 50; tick += 0.5d)
            {
                RecordedView? expected = fresh.InterpolateViewpoint(tick);

                played.InterpolateViewpoint(tick).ShouldBe(expected, $"target {target}, tick {tick}");
                played.QueuedPackets.ShouldBe(fresh.QueuedPackets, $"target {target}, tick {tick}");
                compared++;
            }
        }

        compared.ShouldBe(167 * 101);
    }

    /// <summary>Two signons at tick 146,124, a synctick if asked, then packets every tick 100-300 and 360-650: a 60-tick gap.</summary>
    private static byte[] SignonFirstGappedLine(bool synctick) => SyntheticViews.Demo(
    [
        SyntheticViews.Signon(146124),
        SyntheticViews.Signon(146124),
        .. synctick ? [SyntheticViews.SyncTick(100)] : Array.Empty<DemoCommand>(),
        .. SyntheticViews.Run(100, 300, tick => SyntheticViews.Packet(tick, (tick * 4f, 0f, 0f), (0f, 10f, 0f))),
        .. SyntheticViews.Run(360, 650, tick => SyntheticViews.Packet(tick, (tick * 4f, 0f, 0f), (0f, 10f, 0f))),
    ]);

    /// <summary>
    /// Two signons at tick 146,124, a synctick, the line 100-400, a backward run 300-340 off the line (after a second
    /// synctick if asked), then 401-650.
    /// </summary>
    private static byte[] SignonFirstBackwardsLine(bool syncBefore) => SyntheticViews.Demo(
    [
        SyntheticViews.Signon(146124),
        SyntheticViews.Signon(146124),
        SyntheticViews.SyncTick(100),
        .. SyntheticViews.Run(100, 400, tick => SyntheticViews.Packet(tick, (tick * 4f, 0f, 0f), (0f, 10f, 0f))),
        .. syncBefore ? [SyntheticViews.SyncTick(300)] : Array.Empty<DemoCommand>(),
        .. SyntheticViews.Run(300, 340, tick => SyntheticViews.Packet(tick, ((tick * 4f) + 7f, 0f, 0f), (0f, 10f, 0f))),
        .. SyntheticViews.Run(401, 650, tick => SyntheticViews.Packet(tick, (tick * 4f, 0f, 0f), (0f, 10f, 0f))),
    ]);

    /// <summary>
    /// A signon at tick 146,124, a synctick as a real recording has after its signons, then packets every tick 100-1100,
    /// every 37th tick a cut. Without the synctick the signon's parse-ahead queues the whole file and no bounded rewind
    /// can land where a fresh read does (the "no synctick" case above).
    /// </summary>
    private static byte[] SignonFirstLongLine(byte maxPlayers = 24) => SyntheticViews.Demo(
        maxPlayers,
    [
        SyntheticViews.Signon(146124),
        SyntheticViews.SyncTick(100),
        .. SyntheticViews.Run(100, 1100, tick => SyntheticViews.Packet(
            tick, (tick * 4f, 0f, 0f), (0f, 10f, 0f), flags: tick % 37 == 0 ? SyntheticViews.NoInterpolation : 0)),
    ]);

    /// <summary>The cut line with a default-view signon at tick 146,124 before it, as etf2l-12025 has.</summary>
    private static byte[] SignonFirstCut() => SyntheticViews.Demo([SyntheticViews.Signon(146124), .. CutCommands()]);

    /// <summary>The line with tick 120 flagged FDEMO_NOINTERP.</summary>
    private static IEnumerable<DemoCommand> CutCommands() =>
        SyntheticViews.Run(100, 130, tick => SyntheticViews.Packet(
            tick, (tick * 4f, 0f, 0f), (0f, 10f, 0f), flags: tick == 120 ? SyntheticViews.NoInterpolation : 0));

    /// <summary>The line to 120, a backward run of default views at ticks 100-110, then the line on from 121.</summary>
    private static byte[] Backwards() => SyntheticViews.Demo(
    [
        .. SyntheticViews.Run(100, 120, tick => SyntheticViews.Packet(tick, (tick * 4f, 0f, 0f), (0f, 10f, 0f))),
        .. SyntheticViews.Run(100, 110, tick => SyntheticViews.Packet(tick, (0f, 0f, 0f))),
        .. SyntheticViews.Run(121, 140, tick => SyntheticViews.Packet(tick, (tick * 4f, 0f, 0f), (0f, 10f, 0f))),
    ]);

    /// <summary>Packets every tick from 100 to 130, four units a tick along X, looking along yaw 10.</summary>
    private static byte[] Line(byte maxPlayers = 24) => SyntheticViews.Demo(
        maxPlayers,
        [.. SyntheticViews.Run(100, 130, tick => SyntheticViews.Packet(tick, (tick * 4f, 0f, 0f), (0f, 10f, 0f)))]);

    /// <summary>The line, with everything from tick 120 on moved forward by <paramref name="distance"/>.</summary>
    private static byte[] Jump(float distance) => SyntheticViews.Demo(
        [.. SyntheticViews.Run(100, 130, tick => SyntheticViews.Packet(
            tick, ((tick * 4f) + (tick >= 120 ? distance : 0f), 0f, 0f), (0f, 10f, 0f)))]);

    /// <summary>The line turning 40 degrees at tick 120, in the view angles and, when asked, the local ones.</summary>
    private static byte[] Turn(bool localJumps) => SyntheticViews.Demo(
        [.. SyntheticViews.Run(100, 130, tick => SyntheticViews.Packet(
            tick,
            (tick * 4f, 0f, 0f),
            (0f, tick >= 120 ? 50f : 10f, 0f),
            (0f, localJumps && tick >= 120 ? 50f : 10f, 0f)))]);

    /// <summary>The line with tick 120 flagged FDEMO_NOINTERP.</summary>
    private static byte[] Cut() => SyntheticViews.Demo([.. CutCommands()]);

    /// <summary>The line with a second packet at tick 120, flagged FDEMO_NOINTERP and 320 units on.</summary>
    private static byte[] SameTickCut() => SyntheticViews.Demo(
    [
        .. SyntheticViews.Run(100, 120, tick => SyntheticViews.Packet(tick, (tick * 4f, 0f, 0f), (0f, 10f, 0f))),
        SyntheticViews.Packet(120, (800f, 0f, 0f), (0f, 10f, 0f), flags: SyntheticViews.NoInterpolation),
        .. SyntheticViews.Run(121, 130, tick => SyntheticViews.Packet(tick, ((tick * 4f) + 320f, 0f, 0f), (0f, 10f, 0f))),
    ]);

    internal static DemoPlayer Player(byte[] demo, DemoViewConVars? conVars = null) =>
        new(DemoTimeline.Build(demo)) { ConVars = () => conVars ?? Engine };
}
