using System;

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

    internal static DemoPlayer Player(byte[] demo, DemoViewConVars? conVars = null) =>
        new(DemoTimeline.Build(demo)) { ConVars = conVars ?? DemoViewConVars.Defaults };
}
