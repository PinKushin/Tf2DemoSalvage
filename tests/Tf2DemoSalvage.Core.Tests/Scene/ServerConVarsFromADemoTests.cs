using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A vanilla server's <c>net_SetConVar</c>, and a demo that sends none, read through the timeline.
/// </summary>
/// <remarks>
/// **The vanilla list is a real server's.** The 2011 koth_viaduct SourceTV recording (retired from gcor 2026-10-29,
/// kept in lcor) carries exactly one <c>svc_setconvar</c>, of five values (Cli -t): <c>sv_skyname sky_morningsnow_01</c>,
/// <c>think_limit 0</c>, <c>sv_turbophysics 1</c>, <c>tf_gamemode_cp 1</c>, <c>tv_transmitall 1</c>. Every scripted
/// specimen's server sets <c>sv_cheats</c>, <c>mp_respawnwavetime</c> and <c>mp_disable_respawn_times</c>, so none of
/// them is vanilla; the solo 2013 badlands POV, the other retired witness, carries no <c>svc_setconvar</c> at all.
/// </remarks>
public sealed class ServerConVarsFromADemoTests
{
    private static readonly KeyValuePair<string, string>[] Vanilla =
    [
        new("sv_skyname", "sky_morningsnow_01"),
        new("think_limit", "0"),
        new("sv_turbophysics", "1"),
        new("tf_gamemode_cp", "1"),
        new("tv_transmitall", "1"),
    ];

    [Test]
    public void ServerConVars_AVanillaServersList_ChangesNoMovementConVar()
    {
        ServerConVars convars = TimelineOf(new SetConVarMessage(Vanilla)).ServerConVars;

        // The list arrived — and the pairing survived — or the assertions below say nothing.
        convars.Value("sv_skyname").ShouldBe("sky_morningsnow_01");

        convars.Changed.ShouldBeEmpty();
        convars.Number("sv_maxspeed").ShouldBe(320f);
        convars.Number("sv_specspeed").ShouldBe(3f);
        convars.Number("cl_forwardspeed").ShouldBe(450f);
    }

    [Test]
    public void ServerConVars_TheSameListWithOneMovementValueMoved_NamesIt()
    {
        // The control: an empty Changed must be reachable only by a server that moved nothing.
        ServerConVars convars =
            TimelineOf(new SetConVarMessage([.. Vanilla, new("sv_maxspeed", "400")])).ServerConVars;

        convars.Changed.ShouldBe(["sv_maxspeed"]);
        convars.Number("sv_maxspeed").ShouldBe(400f);
    }

    [Test]
    public void ServerConVars_ADemoThatSendsNoConVars_AreValvesDefaults()
    {
        // The path where no net_SetConVar ever arrives, which a null ServerConVars would throw on.
        ServerConVars convars = TimelineOf().ServerConVars;

        convars.Number("sv_maxspeed").ShouldBe(320f);
        convars.Changed.ShouldBeEmpty();
    }

    private static DemoTimeline TimelineOf(params INetMessage[] messages) =>
        DemoTimeline.Build(SyntheticPlayer.DemoWithMessages(messages));
}
