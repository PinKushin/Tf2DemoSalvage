using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// What the engine does with a POV packet's stamp, written down before <c>demo_retimeview</c> departs from it (D238,
/// finding 106).
/// </summary>
/// <remarks>
/// **The writer**, read out of the x64 <c>engine.dll</c> (project <c>tf2enginex64</c>):
///
/// - <c>CDemoRecorder::RecordPacket</c> (<c>0x180073720</c>, recorder vtable slot 11 at <c>0x180368878</c>) calls
///   <c>0x1800748b0</c>, which writes the command header — <c>dem_signon</c> while the header is still being written,
///   else <c>dem_packet</c> — with the tick from vtable slot 1;
/// - that slot, <c>GetRecordingTick</c> (<c>0x180071f80</c>), is <c>(int)((float)net_time / interval + 0.5) -
///   m_nStartTick</c> when <c>cl.m_nMaxClients</c> (<c>0x180536be4</c>, the global <c>InterpolateViewpoint</c>'s
///   rollback tests at <c>0x1800722c4</c>) is above one: the client's WALL CLOCK at the moment of writing, rounded to a
///   tick. Single-player takes a tick counter instead (<c>0x1805369c4</c>, unidentified). <c>net_time</c> is the double
///   at <c>0x180779520</c>, zeroed by <c>NET_Init</c> (<c>0x1801d4850</c>);
/// - the <c>democmdinfo_t</c> beside it: <c>viewOrigin</c> from <c>IPrediction::GetViewOrigin</c> (slot +0x40,
///   <c>prediction.cpp:1820</c>, the local player's <c>GetLocalOrigin</c>), <c>localViewAngles</c> from
///   <c>GetLocalViewAngles</c> (+0x60, <c>pl.v_angle</c>), and <c>viewAngles</c> copied from <c>cl.viewangles</c>
///   (<c>0x18053f4e4</c>, which <c>IVEngineClient::SetViewAngles</c> at <c>0x180070fa0</c> writes).
///
/// So the stamp is when the client wrote the packet, and the <c>net_Tick</c> inside is the server's tick for it.
///
/// **The reader** pairs on the stamp alone: <c>InterpolateViewpoint</c> (<c>0x180072180</c>) and its pair search
/// (<c>0x180071fd0</c>) compare the commands' header ticks, and nothing in the view half reads a <c>net_Tick</c>. That
/// is the behaviour <c>demo_retimeview 0</c> must reproduce, bit for bit.
/// </remarks>
public sealed class DemoRecordingStampConformanceTests
{
    [Test]
    public void InterpolateViewpoint_WithRetimeOffOnAJitteredDemo_IsTheSameDemoWithoutItsNetTicks()
    {
        DemoViewConVars off = DemoViewConVars.Defaults with { RetimeView = false };
        DemoPlayer carrying = DemoPlayerConformanceTests.Player(DemoPlayerRetimeViewTests.Jittered(), off);
        DemoPlayer bare = DemoPlayerConformanceTests.Player(DemoPlayerRetimeViewTests.Jittered(withServerTicks: false), off);

        for (double tick = 95d; tick <= 165d; tick += 0.125d)
        {
            carrying.InterpolateViewpoint(tick).ShouldBe(bare.InterpolateViewpoint(tick), $"tick {tick}");
        }
    }

    [Test]
    public void InterpolateViewpoint_WithRetimeOffAcrossALateStamp_BlendsTheStampsPair()
    {
        // The engine's arithmetic on the stamps 109 and 111 around 110.5: ((110 - 109) + 0.5) / 2 = 0.75 of the way from
        // 4036 to 4040, though the server sent the two a tick apart.
        DemoPlayer player = DemoPlayerConformanceTests.Player(
            DemoPlayerRetimeViewTests.Jittered(), DemoViewConVars.Defaults with { RetimeView = false });

        player.InterpolateViewpoint(110.5).ShouldNotBeNull().Origin.X.ShouldBe(4039f, 1e-3f);
    }

    [Test]
    public void Declarations_DemoRetimeView_IsOursOnByDefaultAndTheWatchers()
    {
        EngineConVar declared = EngineConVars.ByName(DemoViewConVars.RetimeViewName);

        declared.Default.ShouldBe("1");
        declared.Replicated.ShouldBeFalse("a watcher's choice, as every demo_* ConVar is");
        declared.Cheat.ShouldBeFalse();
        DemoViewConVars.Defaults.RetimeView.ShouldBeTrue();
    }
}
