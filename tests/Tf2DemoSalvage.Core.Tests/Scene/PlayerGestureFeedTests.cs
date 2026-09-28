using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The gesture slots a demo's <c>CTEPlayerAnimEvent</c> temp entities describe.
/// </summary>
/// <remarks>
/// **A player's animation layers are not on the wire and never were.**
/// <c>SendPropExclude( "DT_BaseAnimatingOverlay", "overlay_vars" )</c> (<c>tf_player.cpp:774</c>)
/// removes the whole <c>m_AnimOverlay</c> array from the player's send table, so the reload the
/// owner could not see is not a decode gap in the entity stream — it is not there. What is there is
/// the trigger, as a temp entity, 40,288 times in <c>z1800.dem</c>.
///
/// These are synthetic rather than corpus tests, per D38: the decode has ground truth because the
/// test puts the value in. What only real bytes can answer — that the class and its properties are
/// spelled this way in a real demo — is asked once by a corpus test beside these.
/// </remarks>
public sealed class PlayerGestureFeedTests
{
    [Test]
    public void Record_AReloadEvent_FillsTheAttackAndReloadSlot()
    {
        PlayerGestureFeed feed = new();

        feed.Record(
            PlayerGestureFeed.EventClassName,
            Event(player: 4, anEvent: (int)PlayerAnimEvent.Reload),
            seconds: 13.6d,
            default)
            .ShouldBeTrue("the gesture class must be recognised");

        List<SceneGesture> gestures = [];
        feed.For(4, gestures);

        gestures.ShouldHaveSingleItem();
        gestures[0].Slot.ShouldBe(GestureSlot.AttackAndReload);
        gestures[0].ActivityName.ShouldBe("ACT_MP_RELOAD_STAND");
        gestures[0].StartedSeconds.ShouldBe(13.6d);
    }

    /// <remarks>
    /// **The posture is read when the event ARRIVES, not when the frame is drawn.** The engine picks
    /// the activity inside <c>DoAnimationEvent</c> (<c>tf_playeranimstate.cpp:969</c>), so a reload
    /// begun while crouched stays the crouching reload even if the player stands up during it.
    /// Resolving it later would swap the animation mid-play.
    /// </remarks>
    [Test]
    public void Record_AReloadWhileCrouched_ChoosesTheCrouchingActivity()
    {
        PlayerGestureFeed feed = new();

        feed.Record(
            PlayerGestureFeed.EventClassName,
            Event(player: 4, anEvent: (int)PlayerAnimEvent.Reload),
            seconds: 13.6d,
            new GestureContext(InDuck: true));

        List<SceneGesture> gestures = [];
        feed.For(4, gestures);

        gestures.ShouldHaveSingleItem();
        gestures[0].ActivityName.ShouldBe("ACT_MP_RELOAD_CROUCH");
    }

    /// <remarks>
    /// **One gesture per slot, replaced.** <c>AddToGestureSlot</c> overwrites every field of the
    /// slot it is handed (<c>multiplayer_animstate.cpp:640-651</c>), so a second reload before the
    /// first finished restarts it. A feed that appended would play both at once.
    /// </remarks>
    [Test]
    public void Record_ASecondEventInOneSlot_ReplacesTheFirst()
    {
        PlayerGestureFeed feed = new();

        feed.Record(
            PlayerGestureFeed.EventClassName,
            Event(player: 4, anEvent: (int)PlayerAnimEvent.Reload),
            seconds: 13.6d,
            default);

        feed.Record(
            PlayerGestureFeed.EventClassName,
            Event(player: 4, anEvent: (int)PlayerAnimEvent.Reload),
            seconds: 14.2d,
            default);

        List<SceneGesture> gestures = [];
        feed.For(4, gestures);

        gestures.ShouldHaveSingleItem();
        gestures[0].StartedSeconds.ShouldBe(14.2d, "the newer event restarts the slot's gesture");
    }

    /// <remarks>
    /// **The control for the slot map: two different slots must both survive.** A flinch and a
    /// reload are separate slots in the engine, so a player hit while reloading plays both — and an
    /// implementation that kept one gesture per PLAYER rather than per slot would pass every test
    /// above while silently dropping one of these.
    /// </remarks>
    [Test]
    public void Record_AFlinchDuringAReload_KeepsBothSlots()
    {
        PlayerGestureFeed feed = new();

        feed.Record(
            PlayerGestureFeed.EventClassName,
            Event(player: 4, anEvent: (int)PlayerAnimEvent.Reload),
            seconds: 13.6d,
            default);

        feed.Record(
            PlayerGestureFeed.EventClassName,
            Event(player: 4, anEvent: (int)PlayerAnimEvent.FlinchChest),
            seconds: 13.7d,
            default);

        List<SceneGesture> gestures = [];
        feed.For(4, gestures);

        gestures.Count.ShouldBe(2, "a flinch and a reload occupy different gesture slots");
        gestures.ShouldContain(one => one.Slot == GestureSlot.AttackAndReload);
        gestures.ShouldContain(one => one.Slot == GestureSlot.Flinch);
    }

    /// <remarks>
    /// **Slot order, because the slot IS the layer order** —
    /// <c>m_pAnimLayer-&gt;m_nOrder = iGestureSlot</c> (<c>multiplayer_animstate.cpp:645</c>) — and
    /// <c>AccumulateLayers</c> walks the layers in order, so the sequence in which they are handed
    /// over decides which one wins on a shared bone.
    /// </remarks>
    [Test]
    public void For_SeveralSlots_ReportsThemInSlotOrder()
    {
        PlayerGestureFeed feed = new();

        feed.Record(
            PlayerGestureFeed.EventClassName,
            Event(player: 4, anEvent: (int)PlayerAnimEvent.FlinchChest),
            seconds: 13.7d,
            default);

        feed.Record(
            PlayerGestureFeed.EventClassName,
            Event(player: 4, anEvent: (int)PlayerAnimEvent.Reload),
            seconds: 13.6d,
            default);

        List<SceneGesture> gestures = [];
        feed.For(4, gestures);

        gestures[0].Slot.ShouldBe(GestureSlot.AttackAndReload);
        gestures[1].Slot.ShouldBe(GestureSlot.Flinch);
    }

    /// <remarks>
    /// **The other spelling of the same field.** The published SDK declares <c>m_hPlayer</c> as an
    /// <c>EHANDLE</c> (<c>tf_player.cpp:335</c>) while modern TF2 sends <c>m_iPlayerIndex</c>; the
    /// SDK is one build's snapshot, so both are read and a handle is masked down to its entity
    /// index.
    /// </remarks>
    [Test]
    public void Record_AnEventNamingAHandle_ReadsTheEntityIndexFromIt()
    {
        PlayerGestureFeed feed = new();

        // Serial 3 in the high bits above the eleven index bits, entity 4 below them.
        feed.Record(
            PlayerGestureFeed.EventClassName,
            Event(
                player: 4 | (3 << 11),
                anEvent: (int)PlayerAnimEvent.Reload,
                playerProperty: PlayerGestureFeed.PlayerHandleProperty),
            seconds: 13.6d,
            default);

        List<SceneGesture> gestures = [];
        feed.For(4, gestures);

        gestures.ShouldHaveSingleItem(
            "a handle carries the entity index in its low eleven bits and a serial above them");
    }

    /// <remarks>
    /// **The control for the class match.** Every other temp entity in a demo goes through this same
    /// call — 3,601 <c>CTEFireBullets</c> and 1,946 <c>CTEEffectDispatch</c> in <c>z1800.dem</c>
    /// alone — and matching one of those would fabricate gestures from gunfire.
    /// </remarks>
    [Test]
    public void Record_AnUnrelatedTempEntity_IsIgnored()
    {
        PlayerGestureFeed feed = new();

        feed.Record(
            "CTEFireBullets",
            Event(player: 4, anEvent: (int)PlayerAnimEvent.Reload),
            seconds: 13.6d,
            default)
            .ShouldBeFalse("only the gesture class carries a gesture");

        feed.AnyRecorded.ShouldBeFalse();
    }

    /// <remarks>
    /// **Not every event is a gesture, and the ones that are not must leave no slot behind.**
    /// <c>PLAYERANIMEVENT_JUMP</c> drives the MAIN sequence rather than a gesture layer, and it is
    /// the second most common event in the corpus — 2,298 of them in <c>z1800.dem</c>. Mapping it to
    /// a layer would hang a jump animation on every player's arms.
    /// </remarks>
    [Test]
    public void Record_AJumpEvent_LeavesNoGestureSlot()
    {
        PlayerGestureFeed feed = new();

        feed.Record(
            PlayerGestureFeed.EventClassName,
            Event(player: 4, anEvent: (int)PlayerAnimEvent.Jump),
            seconds: 13.6d,
            default)
            .ShouldBeTrue("the class is still the gesture class");

        List<SceneGesture> gestures = [];
        feed.For(4, gestures);

        gestures.ShouldBeEmpty("a jump drives the main sequence, not a gesture layer");
    }

    /// <remarks>
    /// **The gestures belong to the player the event named, and to nobody else.** With one player in
    /// the fixture, a feed that ignored the index entirely would pass every test above.
    /// </remarks>
    [Test]
    public void For_APlayerWhoRaisedNothing_ReportsNoGestures()
    {
        PlayerGestureFeed feed = new();

        feed.Record(
            PlayerGestureFeed.EventClassName,
            Event(player: 4, anEvent: (int)PlayerAnimEvent.Reload),
            seconds: 13.6d,
            default);

        List<SceneGesture> gestures = [];
        feed.For(9, gestures);

        gestures.ShouldBeEmpty("player 9 raised no event and must have no gesture");
    }

    [Test]
    public void Jumping_AfterAJumpEvent_RunsUntilTheGroundIsBelievedOrTheWaterIsWaistDeep()
    {
        // HandleJumping (tf_playeranimstate.cpp:1491): `m_bJumping` clears on the ground only 0.2 s after the jump, or
        // at once in waist-deep water.
        PlayerGestureFeed feed = new();
        feed.Jumping(4, 10d, onGround: false, waistDeep: false).ShouldBeNull("no jump yet");

        feed.Record(PlayerGestureFeed.EventClassName, Event(player: 4, anEvent: (int)PlayerAnimEvent.Jump), 10d, default);

        feed.Jumping(4, 10.1d, onGround: true, waistDeep: false).ShouldNotBeNull("the ground is not believed yet").ShouldBe(0.1d, 1e-9);
        feed.Jumping(4, 10.5d, onGround: false, waistDeep: false).ShouldNotBeNull().ShouldBe(0.5d, 1e-9);
        feed.Jumping(4, 10.6d, onGround: true, waistDeep: false).ShouldBeNull();
        feed.Jumping(4, 10.7d, onGround: false, waistDeep: false).ShouldBeNull("the clear is kept");

        feed.Record(PlayerGestureFeed.EventClassName, Event(player: 4, anEvent: (int)PlayerAnimEvent.Jump), 20d, default);
        feed.Jumping(4, 20.05d, onGround: false, waistDeep: true).ShouldBeNull();
    }

    [Test]
    public void Landed_AfterADoubleJump_RestartsTheJumpSlotAsTheLandingOnce()
    {
        // `RestartGesture( GESTURE_SLOT_JUMP, ACT_MP_JUMP_LAND )` (tf_playeranimstate.cpp:1507), past the same 0.2 s.
        PlayerGestureFeed feed = new();
        feed.Landed(4, 1d);
        feed.Record(PlayerGestureFeed.EventClassName, Event(player: 4, anEvent: (int)PlayerAnimEvent.DoubleJump), 10d, default);

        feed.Landed(4, 10.1d);
        Gestures(feed, 4).ShouldHaveSingleItem().ActivityName.ShouldBe("ACT_MP_DOUBLEJUMP", "too soon to believe the ground");

        feed.Landed(4, 10.5d);
        feed.Landed(4, 11d);
        SceneGesture landing = Gestures(feed, 4).ShouldHaveSingleItem();
        (landing.ActivityName, landing.StartedSeconds, landing.AutoKill).ShouldBe(("ACT_MP_JUMP_LAND", 10.5d, true), "not restarted by the second");
    }

    [Test]
    public void RecordAndStopScene_TheVcdSlot_HoldsTheSceneAndOnlyItsOwnStopMarksIt()
    {
        PlayerGestureFeed feed = new();

        feed.RecordScene(4, string.Empty, 1d);
        feed.AnyRecorded.ShouldBeFalse("a scene with no name records nothing");

        feed.RecordScene(4, "scenes/player/heavy/low/taunt01.vcd", 2d);
        feed.StopScene(4, "scenes/player/heavy/low/taunt02.vcd", 3d);
        feed.StopScene(9, "scenes/player/heavy/low/taunt01.vcd", 3d);

        SceneGesture taunt = Gestures(feed, 4).ShouldHaveSingleItem();
        (taunt.Slot, taunt.SceneName, taunt.StartedSeconds, taunt.StoppedSeconds).ShouldBe(
            (GestureSlot.Vcd, "scenes/player/heavy/low/taunt01.vcd", 2d, (double?)null));

        feed.StopScene(4, "scenes/player/heavy/low/taunt01.vcd", 4d);
        Gestures(feed, 4).ShouldHaveSingleItem().StoppedSeconds.ShouldBe(4d);
    }

    private static List<SceneGesture> Gestures(PlayerGestureFeed feed, int player)
    {
        List<SceneGesture> gestures = [];
        feed.For(player, gestures);

        return gestures;
    }

    /// <summary>A decoded <c>CTEPlayerAnimEvent</c> naming a player and an event.</summary>
    private static DecodedTempEntity Event(
        int player,
        int anEvent,
        int data = 0,
        string playerProperty = PlayerGestureFeed.PlayerIndexProperty) =>
        new(
            ClassId: 164,
            DelaySeconds: 0f,
            Properties:
            [
                Property(playerProperty, player),
                Property(PlayerGestureFeed.EventProperty, anEvent),
                Property(PlayerGestureFeed.DataProperty, data),
            ]);

    /// <summary>One decoded integer property under the gesture event's own table.</summary>
    private static DecodedProperty Property(string name, int value) =>
        new(
            Index: 0,
            Definition: new FlatProperty(
                new SendProperty(
                    SendPropType.Int, name, 0, string.Empty, 0f, 0f, 32, 0),
                OwnerTable: "DT_TEPlayerAnimEvent",
                ArrayElement: null),
            Value: PropertyValue.FromInt(value));
}
