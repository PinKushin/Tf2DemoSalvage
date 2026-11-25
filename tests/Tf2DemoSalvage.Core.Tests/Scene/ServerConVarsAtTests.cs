using System.Collections.Generic;
using System.Linq;

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
    /// **A set the signon carries holds from the first tick** (B843 review 4, Fable): a signon command's tick is the server's
    /// when recording began, not a playback tick (`z1800` stamps its signon 70,718 and its first packet 6), and the engine
    /// applies the set before any packet. So tick 1 reads 400, not Valve's 800, and a packet's later set at 5 still lands
    /// in tick order after it.
    /// </remarks>
    [Test]
    public void ServerConVarsAt_ASetFromTheSignon_HoldsFromTheFirstTick()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            [
                SyntheticDemo.DataTables(new Core.Schema.DemoSchema([], [])),
                SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 500, SetConVar("sv_gravity", "400")) with
                {
                    Type = Core.Container.DemoCommandType.Signon,
                },
                .. Enumerable.Range(1, 3).Select(tick => SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, tick)),
                SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 5, SetConVar("sv_gravity", "200")),
                .. Enumerable.Range(6, 5).Select(tick => SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, tick)),
            ]));

        // A negative tick too (review 5, Fable, Sonnet): a stamp of 0 would answer every tick asked above.
        (timeline.ServerConVarsAt(int.MinValue).Number("sv_gravity"), timeline.ServerConVarsAt(-1000).Number("sv_gravity"),
            timeline.ServerConVarsAt(1).Number("sv_gravity"), timeline.ServerConVarsAt(5).Number("sv_gravity"),
            timeline.ServerConVarsAt(600).Number("sv_gravity"))
            .ShouldBe((400f, 400f, 400f, 200f, 200f));
    }

    /// <remarks>
    /// **A signon after a packet's set is applied when read, not from the start** (B843 review 5, Fable, Sonnet): it takes
    /// the tick after the last packet read (reviews 6 and 7, Fable, Sonnet), since it applies after that packet's events.
    /// Sets of 300 at 2 and 200 at 5, packets on to 7, then 100 by a signon stamped 900: tick 4 reads 300, tick 7 still
    /// reads 200, and tick 8 on reads 100. Two sets before it and packets between catch a stamp from either set.
    /// </remarks>
    [Test]
    public void ServerConVarsAt_ASignonAfterAPacketsSet_HoldsFromWhereItWasRead()
    {
        DemoTimeline timeline = DemoTimeline.Build(LateSignonDemo());

        (timeline.ServerConVarsAt(4).Number("sv_gravity"), timeline.ServerConVarsAt(7).Number("sv_gravity"),
            timeline.ServerConVarsAt(8).Number("sv_gravity"), timeline.ServerConVarsAt(9).Number("sv_gravity"))
            .ShouldBe((300f, 200f, 100f, 100f));
    }

    /// <remarks>
    /// **A builder resumed from any cut stamps the late signon the same** (B843 review 7, Fable, Sonnet): the last packet
    /// tick read is a builder field a snapshot must carry. The fingerprint cannot see the ConVar history, so the history is
    /// read directly. A field the copy dropped would restart at its default and stamp the signon from the wrong tick.
    /// </remarks>
    [Test]
    public void ServerConVarsAt_ALateSignonAfterAResumeFromAnyCut_IsStampedAsAStraightBuild()
    {
        byte[] demo = LateSignonDemo();
        int commands = DemoTimeline.CommandCount(demo);

        for (int at = 0; at <= commands; at++)
        {
            DemoTimeline resumed = DemoTimeline.BuildThroughSnapshot(demo, at, commands);

            (resumed.ServerConVarsAt(7).Number("sv_gravity"), resumed.ServerConVarsAt(8).Number("sv_gravity"))
                .ShouldBe((200f, 100f), $"resumed after command {at} of {commands}");
        }
    }

    /// <remarks>
    /// **The history stays in tick order whatever the stream does** (B843 review 8, Fable, Sonnet): a packet that repeats
    /// tick 7 after a late signon sets 50; stamped 7 it would sit behind the signon's 8. Clamped, it takes 8. The search
    /// answers this unsorted `[2, 8, 7]` the same as the sorted history at every tick asked (review 10, run), so the
    /// stored stamps are asserted directly: they are what the clamp, to the LAST stamp, decides. The values stand as what
    /// a reader gets: 300 at tick 7 (the set at 2), 50 at tick 9.
    /// </remarks>
    [Test]
    public void ServerConVarsAt_APacketRepeatingATickAfterALateSignon_KeepsTheHistoryInOrder()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            [
                SyntheticDemo.DataTables(new Core.Schema.DemoSchema([], [])),
                SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 1),
                SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 2, SetConVar("sv_gravity", "300")),
                .. Enumerable.Range(3, 5).Select(tick => SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, tick)),
                SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 900, SetConVar("sv_gravity", "100")) with
                {
                    Type = Core.Container.DemoCommandType.Signon,
                },
                SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 7, SetConVar("sv_gravity", "50")),
                .. Enumerable.Range(8, 3).Select(tick => SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, tick)),
            ]));

        (timeline.ServerConVarsAt(7).Number("sv_gravity"), timeline.ServerConVarsAt(9).Number("sv_gravity")).ShouldBe((300f, 50f));

        // The stamps themselves (review 10): an unsorted tail is often answered the same by the search, so the order is
        // read directly. A clamp to the first stamp instead of the last leaves the repeat at 7, behind the signon's 8.
        timeline.ConVarChanges.Select(static change => change.Tick).ShouldBe([2, 8, 8]);
    }

    /// <summary>Sets of 300 at 2 and 200 at 5, packets on to 7, a signon setting 100 stamped 900, then packets 8 to 10.</summary>
    private static byte[] LateSignonDemo() =>
        SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            [
                SyntheticDemo.DataTables(new Core.Schema.DemoSchema([], [])),
                SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 1),
                SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 2, SetConVar("sv_gravity", "300")),
                .. Enumerable.Range(3, 2).Select(tick => SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, tick)),
                SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 5, SetConVar("sv_gravity", "200")),
                .. Enumerable.Range(6, 2).Select(tick => SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, tick)),
                SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 900, SetConVar("sv_gravity", "100")) with
                {
                    Type = Core.Container.DemoCommandType.Signon,
                },
                .. Enumerable.Range(8, 3).Select(tick => SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, tick)),
            ]);

    /// <remarks>
    /// **A copy makes its own lookup, and making one changes no digest** (B843 review 4, Fable, Sonnet): the cache is
    /// `StateCopy.NotCopied`, so a copy does not keep a delegate bound to the original (which a later `Apply` to the
    /// original would leak into), and a read is not a write to the checkpoint the digest guards.
    /// </remarks>
    [Test]
    public void Lookup_OfACopiedSet_IsTheCopysOwnAndChangesNoDigest()
    {
        ServerConVars original = new();
        original.Apply(SetConVar("sv_gravity", "400"));
        HashSet<object> none = [];
        string before = StateCopy.Digest(original, none);
        System.Func<string, string?> lookup = original.Lookup();

        StateCopy.Digest(original, none).ShouldBe(before);

        ServerConVars copy = StateCopy.Of(original, none);
        original.Apply(SetConVar("sv_gravity", "200"));

        copy.Lookup().ShouldNotBeSameAs(lookup);
        copy.Lookup()("sv_gravity").ShouldBe("400");
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
