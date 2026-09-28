using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>The game events a timeline keeps for the HUD, each with the roster as it stood when it arrived.</summary>
/// <remarks>
/// A listener resolves `userid` through `engine->GetPlayerForUserID` and names through `GetPlayerInfo` WHILE handling the
/// event (hud_basedeathnotice.cpp:460), so a slot reused later must not rename an earlier death.
/// </remarks>
public sealed class SceneGameEventTests
{
    [Test]
    public void GameEvents_ADeath_IsKeptWithItsFields()
    {
        DemoTimeline timeline = Watching(Roster("Alice", 7), Death(7, 3));

        SceneGameEvent death = timeline.GameEvents.ShouldHaveSingleItem();

        (death.Name, death.GetInt("userid"), death.GetInt("attacker"), death.Tick).ShouldBe(("player_death", 7, 3, 0));
    }

    [Test]
    public void GameEvents_AChaseShot_IsNotAmongThem() =>
        Watching(new GameEventMessage(2, "hltv_chase", new Dictionary<string, object?> { ["target1"] = (short)1 })).GameEvents.ShouldBeEmpty();

    [Test]
    public void PlayerForUserId_ASlotReusedAfterTheEvent_StillNamesTheFirstOccupant()
    {
        DemoTimeline timeline = Watching(
            Roster("Alice", 7),
            Death(7, 0),
            SyntheticDemo.UpdateTable(0, [("0", Record("Bob", 9))]),
            Death(9, 0));

        timeline.GameEvents.Count.ShouldBe(2);
        SceneGameEvent first = timeline.GameEvents[0];
        SceneGameEvent second = timeline.GameEvents[1];

        first.PlayerForUserId(7).ShouldBe(1, "entry 0 is entity 1");
        first.Roster[1].Name.ShouldBe("Alice");
        second.PlayerForUserId(7).ShouldBe(0, "user 7 no longer holds a slot");
        second.Roster[1].Name.ShouldBe("Bob");
    }

    [Test]
    public void GetInt_EveryWholeNumberTheCodecProduces_IsReadAsAnInt()
    {
        // `GameEventCodec` hands back each wire type as its own CLR type; `IGameEvent::GetInt` reads any of them.
        SceneGameEvent values = Event(new Dictionary<string, object?>
        {
            ["long"] = 5L, ["short"] = (short)-6, ["byte"] = (byte)7, ["int"] = 8, ["true"] = true, ["false"] = false, ["float"] = 9.75f,
            ["text"] = "10",
        });

        (values.GetInt("long"), values.GetInt("short"), values.GetInt("byte"), values.GetInt("int")).ShouldBe((5, -6, 7, 8));
        (values.GetInt("true"), values.GetInt("false"), values.GetInt("float")).ShouldBe((1, 0, 9), "a float truncates as a C cast does");
        values.GetInt("text", -1).ShouldBe(-1, "a string is not a number to GetInt");
        values.GetInt("absent", 42).ShouldBe(42);
    }

    [Test]
    public void GetString_ATextFieldOrAnything_ReturnsTheTextOrTheFallback()
    {
        SceneGameEvent values = Event(new Dictionary<string, object?> { ["name"] = "Alice", ["number"] = 3 });

        values.GetString("name").ShouldBe("Alice");
        values.GetString("number", "none").ShouldBe("none");
        values.GetString("absent").ShouldBe(string.Empty);
    }

    private static SceneGameEvent Event(Dictionary<string, object?> values) => new(0, "test", values, new Dictionary<int, PlayerInfo>());

    private static GameEventMessage Death(int userId, int attacker) =>
        new(1, "player_death", new Dictionary<string, object?> { ["userid"] = (short)userId, ["attacker"] = (short)attacker });

    /// <summary>`userinfo` holding one player in entry 0 — entity 1.</summary>
    private static CreateStringTableMessage Roster(string name, int userId) => SyntheticDemo.StringTable("userinfo", [("0", Record(name, userId))]);

    private static byte[] Record(string name, int userId)
    {
        byte[] record = new byte[PlayerInfo.RecordBytes];
        Encoding.UTF8.GetBytes(name).CopyTo(record, 0);
        BitConverter.GetBytes((uint)userId).CopyTo(record, 32);

        return record;
    }

    private static GameEventListMessage Declaration { get; } = new(
    [
        new GameEventDefinition(1, "player_death", [new GameEventField("userid", GameEventValueType.Short), new GameEventField("attacker", GameEventValueType.Short)]),
        new GameEventDefinition(2, "hltv_chase", [new GameEventField("target1", GameEventValueType.Short)]),
    ]);

    private static DemoTimeline Watching(params INetMessage[] messages) =>
        DemoTimeline.Build(SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.DataTables(new Core.Schema.DemoSchema([], [])),
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, [Declaration, .. messages])));
}
