using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The replicated ConVars as of a tick (B450): the client applies each <c>net_SetConVar</c> as it reads it, so a moment of
/// playback runs under what had been sent by then — not the last value of the whole recording.
/// </summary>
public sealed class ServerConVarsAtTests
{
    [Test]
    public void ServerConVarsAt_EachTick_IsWhatTheServerHadSentByThen()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithMessagesAt(
            (10, [SetConVar("sv_gravity", "400")]),
            (20, [SetConVar("sv_gravity", "200")])));

        timeline.ServerConVarsAt(5).Number("sv_gravity").ShouldBe(800f, "Valve's default before anything was sent");
        timeline.ServerConVarsAt(10).Number("sv_gravity").ShouldBe(400f);
        timeline.ServerConVarsAt(19).Number("sv_gravity").ShouldBe(400f);
        timeline.ServerConVarsAt(20).Number("sv_gravity").ShouldBe(200f);
        timeline.ServerConVarsAt(500).Number("sv_gravity").ShouldBe(200f);
    }

    [Test]
    public void ServerConVarsAt_ATickBeforeAChange_KeepsTheOtherNamesSentEarlier()
    {
        // A message carries only the names it changes; the rest stand.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithMessagesAt(
            (10, [SetConVar("sv_friction", "6")]),
            (20, [SetConVar("sv_gravity", "200")])));

        timeline.ServerConVarsAt(20).Number("sv_friction").ShouldBe(6f);
        timeline.ServerConVarsAt(10).Number("sv_gravity").ShouldBe(800f);
    }

    [Test]
    public void Number_TfClampAirDucks_IsItsDeclaredDefault()
    {
        // tf_gamemovement.cpp:49: ConVar tf_clamp_airducks( "tf_clamp_airducks", "1", FCVAR_REPLICATED ).
        new ServerConVars().Number("tf_clamp_airducks").ShouldBe(1f);
    }

    /// <remarks>
    /// **Each lookup is one delegate, made once** (B843 review 2, Gemini, Sonnet): the HUD asks for them every frame, so a
    /// fresh delegate per call is an allocation a frame the fix removed. The control: the delegates answer.
    /// </remarks>
    [Test]
    public void Lookup_AskedTwice_IsTheSameDelegate()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithMessagesAt((10, [SetConVar("sv_gravity", "400")])));
        ServerConVars atTen = timeline.ServerConVarsAt(10);

        atTen.Lookup().ShouldBeSameAs(atTen.Lookup());
        timeline.ServerLookupAt().ShouldBeSameAs(timeline.ServerLookupAt());
        timeline.ServerLookupAt()(10).ShouldBeSameAs(atTen.Lookup(), "a tick's lookup is its set's, not a new one (review 3, Fable)");
        timeline.ServerLookupAt()(10)!("sv_gravity").ShouldBe("400", "the control");
    }

    private static SetConVarMessage SetConVar(string name, string value) => new([new KeyValuePair<string, string>(name, value)]);
}
