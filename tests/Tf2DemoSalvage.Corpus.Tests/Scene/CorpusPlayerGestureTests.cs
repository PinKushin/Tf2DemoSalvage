using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// That a real demo's gesture events reach a player, which only real bytes can answer.
/// </summary>
/// <remarks>
/// **The synthetic tests own the decode; this owns the SPELLING.** `PlayerGestureFeedTests` builds
/// its own `CTEPlayerAnimEvent` and knows the right answer because it put the value there. What it
/// cannot establish is that a real TF2 demo names the class and its properties the way this project
/// expects — a feed matching a class name nothing sends would pass every synthetic test and produce
/// no gesture on any recording, which is exactly the shape of defect this project keeps finding
/// (D38, and `output-level-assertion-or-it-is-not-done`).
///
/// **Why this cannot be a synthetic test at all.** The claim is about what Valve's server sends,
/// and no fixture we write is evidence for that.
/// </remarks>
public sealed class CorpusPlayerGestureTests
{
    /// <remarks>
    /// **z1800 is the specimen because it is a real match**, and the era specimens cannot answer
    /// this: they are the owner's own solo recordings on period clients, with nobody else on the
    /// server to raise an event. Measured in this file: 40,288 `CTEPlayerAnimEvent` effects, the
    /// most common temp entity in it by an order of magnitude.
    ///
    /// **Asserted as "some player has some gesture", not as a count.** The exact number is a fact
    /// about this recording and would make the test a change detector; that ANY gesture survives
    /// the trip from the temp entity stream to a sampled player is the wiring claim.
    /// </remarks>
    [Test]
    public void PlayersAt_OnARealMatch_ReportsGesturesFromTheTempEntityStream()
    {
        if (Corpus.Demo("z1800") is not { } path)
        {
            Assert.Ignore("z1800.dem is not available");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        List<ScenePlayer> players = [];

        int withGestures = 0;

        // A spread of ticks rather than one, because a gesture slot is filled by an event and a
        // single tick could legitimately land where nobody has raised one yet.
        for (int tick = timeline.FirstTick + 100; tick < timeline.LastTick; tick += 500)
        {
            timeline.PlayersAt(tick, players);

            withGestures += players.Count(one => one.Gestures is { Count: > 0 });
        }

        withGestures.ShouldBeGreaterThan(
            0,
            "a real match carries CTEPlayerAnimEvent temp entities for every attack, reload and " +
            "flinch, and they are the only place a player's animation layers exist: " +
            "tf_player.cpp:774 excludes overlay_vars from the player's send table");
    }

    /// <remarks>
    /// **The control, and it is what stops the test above from passing on a fabrication.** If the
    /// feed matched the wrong class — every demo carries thousands of `CTEFireBullets` and
    /// `CTEEffectDispatch` effects — or ignored the event id, every player would carry a gesture at
    /// every tick. A real recording has players standing still, and the reload slot is not
    /// permanently occupied.
    /// </remarks>
    [Test]
    public void PlayersAt_OnARealMatch_LeavesSomePlayersWithNoGesture()
    {
        if (Corpus.Demo("z1800") is not { } path)
        {
            Assert.Ignore("z1800.dem is not available");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        List<ScenePlayer> players = [];

        int withoutGestures = 0;

        for (int tick = timeline.FirstTick + 100; tick < timeline.LastTick; tick += 500)
        {
            timeline.PlayersAt(tick, players);

            withoutGestures += players.Count(one => one.Gestures is null or { Count: 0 });
        }

        withoutGestures.ShouldBeGreaterThan(
            0,
            "a feed that matched the wrong temp entity class, or ignored the event id, would give " +
            "every player a gesture at every tick");
    }

    /// <remarks>
    /// **The other source of layers, and the pair is the point** (B285). A player's
    /// <c>m_AnimOverlay</c> is excluded from the send table (<c>tf_player.cpp:774</c>) while every
    /// other animating entity sends one — so a reading that found layers on players, or none on
    /// buildings, would have the mechanism backwards. Asserting both directions in one test is what
    /// makes either meaningful.
    ///
    /// Measured on `z1800.dem`: sentries carry two, three and four layers, and teleporters,
    /// dispensers, sappers and taunt props carry them too.
    /// </remarks>
    [Test]
    public void PropsAt_OnARealMatch_CarriesWireLayersOnBuildingsAndNoneOnPlayers()
    {
        if (Corpus.Demo("z1800") is not { } path)
        {
            Assert.Ignore("z1800.dem is not available");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        List<SceneProp> props = [];
        List<ScenePlayer> players = [];

        int layered = 0;
        int playersSeen = 0;

        for (int tick = timeline.FirstTick + 100; tick < timeline.LastTick; tick += 500)
        {
            timeline.PropsAt(tick, props);
            timeline.PlayersAt(tick, players);

            layered += props.Count(one => one.Pose.Layers.Count > 0);
            playersSeen += players.Count;
        }

        playersSeen.ShouldBeGreaterThan(0, "the control: the sweep must have seen players at all");

        layered.ShouldBeGreaterThan(
            0,
            "sentries, dispensers, teleporters and sappers all send m_AnimOverlay, and the array " +
            "is keyed by path because fifteen elements share one flat name");
    }

    /// <remarks>
    /// **The reload specifically, because it is what the owner reported missing.** A gesture that
    /// resolves to the reload activity has to appear somewhere in a match with 762 plain reload
    /// events in it; naming the activity rather than counting keeps this a claim about the mapping
    /// rather than about this recording.
    /// </remarks>
    [Test]
    public void PlayersAt_OnARealMatch_ProducesAReloadGesture()
    {
        if (Corpus.Demo("z1800") is not { } path)
        {
            Assert.Ignore("z1800.dem is not available");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        List<ScenePlayer> players = [];

        bool sawReload = false;

        for (int tick = timeline.FirstTick + 100;
            tick < timeline.LastTick && !sawReload;
            tick += 200)
        {
            timeline.PlayersAt(tick, players);

            sawReload = players.Any(one =>
                one.Gestures is { } gestures &&
                gestures.Any(gesture =>
                    gesture.ActivityName?.StartsWith("ACT_MP_RELOAD", System.StringComparison.Ordinal)
                        == true));
        }

        sawReload.ShouldBeTrue(
            "z1800.dem carries 762 PLAYERANIMEVENT_RELOAD events plus 925 loops and 287 ends, so " +
            "a reload gesture must reach a sampled player somewhere in the recording");
    }

    /// <remarks>
    /// **The air-walking reload, counted in the OUTPUT and checked against a second reading of the
    /// same bytes** (B112). `CTFPlayerAnimState::DoAnimationEvent` picks `ACT_MP_RELOAD_AIRWALK`,
    /// `_LOOP` or `_END` whenever `m_bInAirWalk` holds (`tf_playeranimstate.cpp:1141`, `:1154`,
    /// `:1167`), and nothing fed that latch to the gesture choice, so every reload begun
    /// mid-rocket-jump took the standing form.
    ///
    /// **The count is what the timeline hands the renderer** — every reload gesture that reached a
    /// sampled player, once each — and the instrument is proved by a control that shares nothing
    /// with the production path but the decoder: <see cref="IndependentAirWalkAtReloads"/> walks
    /// the demo itself, reads the raw wire fields, and applies the latch as the SDK writes it. Each
    /// reload in the output must agree with it, one by one, and the air-walking count must be the
    /// same number from both sides. Asserting the count alone would pass a production path that
    /// picked the right number of wrong reloads.
    ///
    /// **Greater than zero, because an absence needs a control too**: a latch that never set would
    /// agree with a control that never set, and z1800 is a match full of rocket and sticky jumps.
    /// </remarks>
    [Test]
    public void ReloadGestures_OnARealMatch_AirWalkExactlyWhereAnIndependentLatchIsSet()
    {
        if (Corpus.Demo("z1800") is not { } path)
        {
            Assert.Ignore("z1800.dem is not available");
            return;
        }

        DemoTimeline timeline = TimelineCache.For(path);
        Dictionary<(int Player, int Tick), bool> independent = IndependentAirWalkAtReloads(path);

        // Once per gesture: a slot's gesture is carried on every frame until something replaces it. Keyed on the
        // gesture alone, with the class it was first seen under beside it — a key that included the class counted a
        // gesture twice when it outlived a respawn into another class.
        Dictionary<(int Player, double Started, string Activity), int?> reloads = [];

        foreach (TimelineFrame frame in timeline.Frames)
        {
            foreach (ScenePlayer player in frame.Players)
            {
                foreach (SceneGesture gesture in player.Gestures ?? [])
                {
                    if (gesture.Slot == GestureSlot.AttackAndReload &&
                        gesture.ActivityName is { } name &&
                        name.StartsWith(ReloadActivityPrefix, StringComparison.Ordinal))
                    {
                        reloads.TryAdd((player.EntityIndex, gesture.StartedSeconds, name), player.PlayerClass);
                    }
                }
            }
        }

        int airWalking = 0;
        int independentlyAirWalking = 0;
        List<string> disagreements = [];
        Dictionary<int, int> airWalkingByClass = [];

        foreach (((int who, double started, string activity), int? playerClass) in reloads)
        {
            // A gesture's start is its event's arrival, `tick * interval`, so the tick comes back exactly.
            int tick = (int)Math.Round(started / timeline.IntervalPerTick);
            bool output = activity.StartsWith(AirwalkReloadActivityPrefix, StringComparison.Ordinal);

            if (!independent.TryGetValue((who, tick), out bool reading))
            {
                disagreements.Add($"{who}@{tick} {activity}: the independent walk decoded no reload there");
                continue;
            }

            if (output)
            {
                airWalking++;
                airWalkingByClass[playerClass ?? 0] = airWalkingByClass.GetValueOrDefault(playerClass ?? 0) + 1;
            }

            if (reading)
            {
                independentlyAirWalking++;
            }

            if (output != reading)
            {
                disagreements.Add($"{who}@{tick} {activity}: the independent latch reads {reading}");
            }
        }

        TestContext.Out.WriteLine(
            $"z1800: {reloads.Count} reload gestures reached a sampled player; {airWalking} took an " +
            $"air-walk activity, and the independent latch was set at {independentlyAirWalking} of them " +
            $"({independent.Count(pair => pair.Value)} of {independent.Count} reload events overall). " +
            $"By class: {string.Join(", ", airWalkingByClass.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}"))}");

        airWalking.ShouldBe(
            independentlyAirWalking,
            "every reload the output air-walks must be one the independent reading of the ground state " +
            "air-walks, and the other way round: " + string.Join("; ", disagreements.Take(12)));

        disagreements.ShouldBeEmpty();

        airWalking.ShouldBeGreaterThan(
            0,
            "z1800 is a match of rocket and sticky jumps, so some reload must begin mid-air-walk");
    }

    /// <summary>What every reload activity's name begins with.</summary>
    private const string ReloadActivityPrefix = "ACT_MP_RELOAD";

    /// <summary>What the three air-walking reload activities' names begin with.</summary>
    private const string AirwalkReloadActivityPrefix = "ACT_MP_RELOAD_AIRWALK";

    /// <summary>`m_bInAirWalk` as each reload event found it, read from the demo without the timeline.</summary>
    /// <param name="path">The demo.</param>
    /// <returns>For every player and tick carrying a reload event, whether the latch was set.</returns>
    /// <remarks>
    /// **A second reading, independent of the production path on purpose.** It shares only the
    /// decoder with <c>DemoTimeline</c>: it keeps its own latch, reads the raw wire keys rather than
    /// <c>EntityState</c>'s accessors, and applies the SDK's rule as written —
    ///
    /// <code>
    /// Update:            custom model w/o class animations, EF_NODRAW, dormant or dead → ClearAnimationState  (tf_playeranimstate.cpp:340-374, multiplayer_animstate.cpp:1381-1395)
    /// ClearAnimationState: m_bInAirWalk = false                                                             (tf_playeranimstate.cpp:114)
    /// HandleJumping:     a heavy with TF_COND_AIMING returns first                                           (:1439-1440)
    ///                    if ( ( vz &gt; 300 || m_bInAirWalk || grapple ) &amp;&amp; !bInDuck )                          (:1446)
    ///                        on the ground and latched → false; waist deep → false; in the air → true    (:1449-1472)
    /// DOUBLEJUMP:        m_bInAirWalk = false                                                             (:1193)
    /// SPAWN:             ClearAnimationState                                                              (multiplayer_animstate.cpp:313)
    /// RELOAD, _LOOP, _END read m_bInAirWalk                                                               (tf_playeranimstate.cpp:1141, :1154, :1167)
    /// </code>
    ///
    /// **An event reads the latch the last snapshot left**, because `DoAnimationEvent` runs before
    /// that frame's `HandleJumping`; the step for a snapshot runs once its packet is read. The class
    /// script's <c>DontDoAirwalk</c> is not applied — that half belongs to the installed game, and
    /// the timeline leaves it to the scene the same way.
    /// </remarks>
    private static Dictionary<(int Player, int Tick), bool> IndependentAirWalkAtReloads(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        NetDecodeState state = new() { NetworkProtocol = Corpus.ProtocolOf(path) };
        DemoSchema schema = Corpus.Schema(path);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
        EntityStateTable entities = new(decoder);
        Dictionary<int, string> classNames = [];

        foreach (ServerClass serverClass in schema.ServerClasses)
        {
            entities.SetClassName(serverClass.Id, serverClass.ClassName);
            classNames[serverClass.Id] = serverClass.ClassName;
        }

        float interval = 0f;
        HashSet<int> latched = [];
        Dictionary<int, (int Tick, float Z)> lastSeen = [];
        Dictionary<(int Player, int Tick), bool> atReload = [];

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            bool snapshot = false;

            foreach (INetMessage message in NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                switch (message)
                {
                    case ServerInfoMessage info when info.IntervalPerTick > 0f:
                        interval = info.IntervalPerTick;
                        break;

                    case CreateStringTableMessage { Name: BaselineBuilder.TableName } created:
                        BaselineBuilder.Apply(created.Entries, decoder);
                        break;

                    case UpdateStringTableMessage updated
                        when state.StringTableName(updated.TableId) == BaselineBuilder.TableName:
                        BaselineBuilder.Apply(updated.Entries, decoder);
                        break;

                    case NetTickMessage netTick:
                        entities.PacketTick = netTick.Tick;
                        break;

                    case PacketEntitiesMessage { LengthBits: > 0 } entityMessage:
                        foreach (DecodedEntity entity in decoder.Decode(
                            entityMessage.Body.Span, entityMessage, entityMessage.LengthBits))
                        {
                            entities.Apply(entity);
                        }

                        snapshot = true;
                        break;

                    case TempEntitiesMessage { BodyBits: > 0 } effects:
                        foreach (DecodedTempEntity effect in decoder.DecodeTempEntities(
                            effects.Body.Span, effects.Count, effects.BodyBits))
                        {
                            if (classNames.GetValueOrDefault(effect.ClassId) == "CTEPlayerAnimEvent" &&
                                AnimEvent(effect) is ({ } who, { } which))
                            {
                                switch ((PlayerAnimEvent)which)
                                {
                                    case PlayerAnimEvent.Reload or PlayerAnimEvent.ReloadLoop or PlayerAnimEvent.ReloadEnd:
                                        atReload[(who, command.Tick)] = latched.Contains(who);
                                        break;

                                    case PlayerAnimEvent.DoubleJump or PlayerAnimEvent.Spawn:
                                        latched.Remove(who);
                                        break;

                                    default:
                                        break;
                                }
                            }
                        }

                        break;

                    default:
                        break;
                }
            }

            if (!snapshot)
            {
                continue;
            }

            foreach (EntityState player in entities.OfClass("CTFPlayer"))
            {
                int who = player.EntityIndex;

                if (!player.IsVisible)
                {
                    latched.Remove(who);
                    continue;
                }

                if (player.Origin() is not { } origin)
                {
                    continue;
                }

                float? rising = lastSeen.TryGetValue(who, out (int Tick, float Z) before) &&
                    command.Tick > before.Tick &&
                    interval > 0f
                        ? (origin.Z - before.Z) / ((command.Tick - before.Tick) * interval)
                        : null;

                lastSeen[who] = (command.Tick, origin.Z);

                bool alive = player.Integer("DT_BasePlayer.m_lifeState") is null or 0;
                bool noDraw = ((player.Integer("DT_BaseEntity.m_fEffects") ?? 0) & 0x020) != 0;
                bool customModelWithoutClassAnimations =
                    player.Text("DT_TFPlayerClassShared.m_iszCustomModel") is { Length: > 0 } &&
                    player.Integer("DT_TFPlayerClassShared.m_bUseClassAnimations") is null or 0;

                if (!alive || noDraw || customModelWithoutClassAnimations)
                {
                    latched.Remove(who);
                    continue;
                }

                if (player.Integer("DT_BasePlayer.m_fFlags") is not { } flags ||
                    (player.Integer("DT_TFPlayerClassShared.m_iClass") == 6 &&
                        ((player.Integer("DT_TFPlayerShared.m_nPlayerCond") ?? 0) & 1) != 0))
                {
                    continue;
                }

                bool onGround = (flags & 1) != 0;
                bool ducking = (flags & 2) != 0;
                bool waistDeep = player.Integer("DT_TFPlayer.m_nWaterLevel") >= 2;
                // `Get()` compares the handle's serial with the slot's occupant, so a slot that changed hands is null.
                bool grappling = player.Integer("DT_TFPlayer.m_hGrapplingHookTarget") is { } hook &&
                    hook != (1 << 21) - 1 &&
                    entities.TryGet(hook & ((1 << 11) - 1), out EntityState? hooked) &&
                    hooked.SerialNumber == hook >> 11;
                bool was = latched.Contains(who);

                if ((rising > 300f || was || grappling) && !ducking)
                {
                    if (onGround && was)
                    {
                        latched.Remove(who);
                    }
                    else if (waistDeep)
                    {
                        latched.Remove(who);
                    }
                    else if (!onGround)
                    {
                        latched.Add(who);
                    }
                }
            }
        }

        return atReload;
    }

    /// <summary>The player and the event a `CTEPlayerAnimEvent` names, from the effect as the client holds it.</summary>
    private static (int? Player, int? Event) AnimEvent(DecodedTempEntity effect)
    {
        int? player = null;
        int? anEvent = null;

        foreach (DecodedProperty property in effect.State)
        {
            switch (property.Definition.Property.Name)
            {
                case "m_iPlayerIndex":
                    player = (int)property.Value.AsInt;
                    break;

                case "m_hPlayer":
                    player = (int)(property.Value.AsInt & ((1 << 11) - 1));
                    break;

                case "m_iEvent":
                    anEvent = (int)property.Value.AsInt;
                    break;

                default:
                    break;
            }
        }

        return (player, anEvent);
    }
}
