using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One gesture a player is playing, as the demo's own events describe it.</summary>
/// <param name="Slot">Which gesture slot it occupies. The slot is also the layer's order.</param>
/// <param name="ActivityName">
/// The activity to look up on the player's model, or null when the event named a number instead.
/// </param>
/// <param name="ActivityNumber">
/// The activity index carried on the wire, for the two events that send one, or null.
/// </param>
/// <param name="AutoKill">
/// Whether the gesture disappears when its cycle passes one, rather than holding its last frame.
/// </param>
/// <param name="StartedSeconds">
/// Demo time when the event arrived, in seconds. Seconds rather than ticks because the layer's
/// cycle is elapsed time times the sequence's rate, and only the timeline knows the tick interval.
/// </param>
/// <remarks>
/// **What CORE can say, and no more.** The engine resolves a gesture to a sequence immediately —
/// <c>AddToGestureSlot</c> calls <c>SelectWeightedSequence( iGestureActivity )</c>
/// (<c>multiplayer_animstate.cpp:633</c>) and abandons the gesture when that returns nothing — and
/// the sequence is what gives the layer its length. Core has no models, so it carries the activity
/// and the start, and the scene resolves both.
///
/// **The slot is the order**, which is Valve's own assignment:
/// <c>m_pAnimLayer-&gt;m_nOrder = iGestureSlot</c> (<c>multiplayer_animstate.cpp:645</c>), alongside
/// <c>m_flWeight = 1.0f</c> and <c>m_flCycle = 0.0f</c>. So a gesture needs no weight of its own
/// here: every one of them starts at full weight and at cycle zero.
/// </remarks>
/// <param name="SceneName">
/// The compiled scene this gesture came out of, for a taunt, or null for an ordinary gesture (B351).
/// </param>
/// <param name="Taunt">
/// What that scene stages, once something with the game's own files has resolved
/// <paramref name="SceneName"/>. Null until then, and null for ever on a machine with no TF2.
/// </param>
/// <param name="StoppedSeconds">
/// Demo time when the scene stopped playing back, or null while it still is. **Whether that ends the
/// gesture depends on the scene**: `C_TFPlayer::StopGestureSceneEvent` resets the VCD slot only for a
/// scene containing a `LOOP` (<c>c_tf_player.cpp:9505</c>), deliberately, so that a running taunt
/// plays out — so the decision needs the resolved plan and cannot be made here.
/// </param>
/// <param name="OnlyIfSlotIdle">
/// A voice command queued behind the gesture holding its slot: it plays only if that gesture is no longer active when
/// it arrives — `if ( !IsGestureSlotActive( GESTURE_SLOT_ATTACK_AND_RELOAD ) )` (`tf_playeranimstate.cpp:1053-1058`).
/// Whether it is depends on that gesture's cycle, which needs the model, so the scene decides (B437).
/// </param>
public readonly record struct SceneGesture(
    GestureSlot Slot,
    string? ActivityName,
    int? ActivityNumber,
    bool AutoKill,
    double StartedSeconds,
    string? SceneName = null,
    SceneTaunt? Taunt = null,
    double? StoppedSeconds = null,
    bool OnlyIfSlotIdle = false);

/// <summary>One animation layer an entity sends on the wire.</summary>
/// <param name="Order">
/// Its position, <c>m_nOrder</c> — which is also the order layers accumulate in, and which the
/// engine sets to <c>MAX_OVERLAYS</c> to mark a slot unused.
/// </param>
/// <param name="Sequence">The sequence it plays, in the model's own numbering.</param>
/// <param name="Cycle">How far through, zero to one.</param>
/// <param name="Weight">How strongly it is applied, <c>m_flWeight</c>.</param>
/// <remarks>
/// **A player never has one of these** — <c>tf_player.cpp:774</c> excludes the whole array from the
/// player's send table, and their layers arrive as temp entities instead (B282). Everything else
/// that animates does send them: sentries, dispensers, teleporters, sappers and taunt props, with
/// two to four layers each measured on <c>z1800.dem</c>.
///
/// **The sequence is the ENTITY's own numbering**, not an activity, so unlike a gesture it needs no
/// resolution — the server has already chosen it.
/// </remarks>
public readonly record struct SceneAnimationLayer(
    int Order,
    int Sequence,
    float Cycle,
    float Weight);

/// <summary>
/// Turns the <c>CTEPlayerAnimEvent</c> temp entities a demo carries into per-player gesture slots.
/// </summary>
/// <remarks>
/// **This exists because TF2 puts a player's animation layers nowhere else.**
/// <c>tf_player.cpp:774</c> excludes the whole array from the player's send table:
///
/// <code>
///   SendPropExclude( "DT_BaseAnimatingOverlay", "overlay_vars" ),
/// </code>
///
/// so <c>m_AnimOverlay</c> is never networked for a player, in any TF2 demo. Sentries, dispensers,
/// teleporters, sappers and taunt props do send it; players do not. The same block excludes
/// <c>m_nSequence</c>, <c>m_flPlaybackRate</c>, <c>m_flPoseParameter</c>,
/// <c>DT_ServerAnimationData.m_flCycle</c> and <c>m_flAnimTime</c> — everything
/// <c>CTFPlayerAnimState</c> rebuilds on the client.
///
/// **What IS sent is the trigger.** <c>CTEPlayerAnimEvent</c> (<c>tf_player.cpp:324</c>) carries
/// the player, a <c>PlayerAnimEvent_t</c> and a data word, and <c>TE_PlayerAnimEvent</c> broadcasts
/// it to everyone who can see that player. Measured in <c>z1800.dem</c>: 40,288 of them, the most
/// common temp entity in the file by an order of magnitude, of which 762 are plain reloads.
///
/// **One gesture per slot, replaced rather than queued**, which is why this keeps a slot map and
/// not a list. <c>AddToGestureSlot</c> overwrites every field of the slot it is given
/// (<c>multiplayer_animstate.cpp:640-651</c>), so a second reload before the first finished
/// restarts it rather than stacking.
///
/// **A POV demo cannot show the recorder's own gestures.** <c>TE_PlayerAnimEvent</c> calls
/// <c>filter.RemoveRecipient( pPlayer )</c> for every event except the custom gestures and
/// <c>SNAP_YAW</c>, because a player predicts their own. So a POV recording carries every other
/// player's gestures and none of its own, and a SourceTV recording carries all of them. That is a
/// fact about the format, not a gap here.
/// </remarks>
public sealed class PlayerGestureFeed
{
    /// <summary>The temp entity class that carries a player animation event.</summary>
    public const string EventClassName = "CTEPlayerAnimEvent";

    /// <summary>The property naming the player, by index rather than by handle.</summary>
    /// <remarks>
    /// **The SDK declares <c>m_hPlayer</c> as an <c>EHANDLE</c>** (<c>tf_player.cpp:335</c>) and
    /// modern TF2 sends <c>m_iPlayerIndex</c> instead — measured on the wire, where every one of
    /// the 40,288 events in <c>z1800.dem</c> names the field that way. Both spellings are accepted
    /// because the published SDK is one build's snapshot and an era demo may well use the other;
    /// a handle is decoded to its entity index by the same mask the rest of the project uses.
    /// </remarks>
    public const string PlayerIndexProperty = "m_iPlayerIndex";

    /// <summary>The handle spelling of the same field, as the published SDK declares it.</summary>
    public const string PlayerHandleProperty = "m_hPlayer";

    /// <summary>The event id, a <c>PlayerAnimEvent_t</c>.</summary>
    public const string EventProperty = "m_iEvent";

    /// <summary>The event's data word: an activity index for the two events that carry one.</summary>
    public const string DataProperty = "m_nData";

    /// <summary>Entity index bits in a networked handle, <c>const.h</c>.</summary>
    private const int EntityIndexBits = 11;

    /// <summary>The number of gesture slots, <c>GESTURE_SLOT_COUNT</c>.</summary>
    private const int SlotCount = 7;

    private readonly Dictionary<int, SceneGesture?[]> _byPlayer = [];

    /// <summary>Whether any gesture has ever been recorded.</summary>
    /// <remarks>
    /// **For telling "this demo has none" from "we read none".** A POV recording of a quiet moment
    /// and a decoder that never matched the class look identical from any one player's slots, and
    /// the difference is the whole question when a gesture fails to appear.
    /// </remarks>
    public bool AnyRecorded { get; private set; }

    /// <summary>Records one decoded temp entity, ignoring every class but the gesture one.</summary>
    /// <param name="className">The temp entity's class name, from the schema.</param>
    /// <param name="effect">The decoded effect.</param>
    /// <param name="seconds">Demo time when it arrived, in seconds.</param>
    /// <param name="context">
    /// What the player was doing, which decides which activity is chosen. Its <c>InAirWalk</c> and <c>NData</c> are
    /// not read: the first is this feed's own <see cref="InAirWalk"/>, which is anim-state memory rather than
    /// anything on the player, and the second is the event's own <c>m_nData</c>.
    /// </param>
    /// <returns>Whether this effect was a gesture event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> is null.</exception>
    public bool Record(
        string className, DecodedTempEntity effect, double seconds, GestureContext context)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (!string.Equals(className, EventClassName, StringComparison.Ordinal))
        {
            return false;
        }

        int? player = null;
        int? anEvent = null;
        int data = 0;

        foreach (DecodedProperty property in effect.State)
        {
            string name = property.Definition.Property.Name;

            if (string.Equals(name, PlayerIndexProperty, StringComparison.Ordinal))
            {
                player = (int)property.Value.AsInt;
            }
            else if (string.Equals(name, PlayerHandleProperty, StringComparison.Ordinal))
            {
                // A handle packs the entity index in its low bits and a serial above them.
                player = (int)(property.Value.AsInt & ((1 << EntityIndexBits) - 1));
            }
            else if (string.Equals(name, EventProperty, StringComparison.Ordinal))
            {
                anEvent = (int)property.Value.AsInt;
            }
            else if (string.Equals(name, DataProperty, StringComparison.Ordinal))
            {
                data = (int)property.Value.AsInt;
            }
        }

        if (player is not { } who || anEvent is not { } which || who <= 0)
        {
            return false;
        }

        AnyRecorded = true;

        // **The latch is read before the event writes anything**, as the reload cases read it
        // (`tf_playeranimstate.cpp:1141`, `:1154`, `:1167`) — and it is the value the last step left, because
        // `DoAnimationEvent` runs before that frame's `HandleJumping` (B112).
        GestureContext posture = context with { NData = data, InAirWalk = InAirWalk(who) };

        switch ((PlayerAnimEvent)which)
        {
            // **The jump drives the main sequence, not a layer**: `DoAnimationEvent( PLAYERANIMEVENT_JUMP )` sets
            // `m_bJumping` and `m_flJumpStartTime` (`multiplayer_animstate.cpp:288`), which HandleJumping then reads.
            case PlayerAnimEvent.Jump:
                _jumpStarted[who] = seconds;
                break;

            // **The air dash is a jump when none is in force, and it forces the air walk off**
            // (`tf_playeranimstate.cpp:1184-1193`): `if ( !m_bJumping )` it starts the jump clock, and then
            // `m_bInAirWalk = false` unconditionally — so a scout who walks off a ledge and dashes plays the jump
            // phases, and a reload after the dash stands even while he is still in the air.
            case PlayerAnimEvent.DoubleJump:
                _jumpStarted.TryAdd(who, seconds);
                _inAirWalk.Remove(who);
                break;

            // **A respawn clears the animation state** (`multiplayer_animstate.cpp:310-313`).
            case PlayerAnimEvent.Spawn:
                ClearAnimationState(who);
                break;

            default:
                break;
        }

        // **The mapping is asked here, at the moment the event arrives, because it depends on what
        // the player was doing THEN.** A reload started while crouched is a different activity from
        // one started standing, and the engine picks at `DoAnimationEvent` time
        // (`tf_playeranimstate.cpp:969`) rather than at draw time. Deferring it would resolve a
        // crouching reload against whatever posture the player is in when the frame is drawn.
        if (PlayerGestureEvent.Map((PlayerAnimEvent)which, posture) is not { } trigger)
        {
            return true;
        }

        if (!_byPlayer.TryGetValue(who, out SceneGesture?[]? slots))
        {
            slots = new SceneGesture?[SlotCount];
            _byPlayer[who] = slots;
        }

        int slot = (int)trigger.Slot;

        if (slot < 0 || slot >= SlotCount)
        {
            return true;
        }

        SceneGesture started = new(trigger.Slot, trigger.ActivityName, trigger.ActivityNumber, trigger.AutoKill, seconds);

        // **A voice command over an occupied slot waits behind it** (B437) — see SceneGesture.OnlyIfSlotIdle. Every
        // other event in the slot is a RestartGesture, which replaces whatever held it and whatever waited.
        if ((PlayerAnimEvent)which == PlayerAnimEvent.VoiceCommandGesture && slots[slot] is not null)
        {
            if (!_waiting.TryGetValue(who, out List<SceneGesture>? queue))
            {
                queue = [];
                _waiting[who] = queue;
            }

            // ponytail: a bounded queue; a player who spams voice commands over one held gesture keeps the latest 8.
            if (queue.Count == MaxWaiting)
            {
                queue.RemoveAt(0);
            }

            queue.Add(started with { OnlyIfSlotIdle = true });
            return true;
        }

        if (slot == (int)GestureSlot.AttackAndReload)
        {
            _waiting.Remove(who);
        }

        slots[slot] = started;

        return true;
    }

    /// <summary>Voice commands waiting behind each player's attack-and-reload gesture, oldest first.</summary>
    private readonly Dictionary<int, List<SceneGesture>> _waiting = [];

    /// <summary>How many voice commands one slot keeps waiting.</summary>
    private const int MaxWaiting = 8;

    /// <summary>Each player's `m_bInAirWalk` (`tf_playeranimstate.h`), the latch `HandleJumping` keeps (B112).</summary>
    private readonly HashSet<int> _inAirWalk = [];

    /// <summary>`m_bInAirWalk`: whether the player is air-walking, as the last step or event left it.</summary>
    /// <param name="entityIndex">The player.</param>
    /// <returns>The latch.</returns>
    /// <remarks>
    /// **One latch, read by the body and by the reload**, which is how the engine has it: `HandleJumping` sets it and
    /// returns the air-walking body activity from the same test, and `DoAnimationEvent` reads it for the three reload
    /// cases. A class whose script sets `DontDoAirwalk` never reaches the block, so its latch is never set.
    /// </remarks>
    public bool InAirWalk(int entityIndex) => _inAirWalk.Contains(entityIndex);

    /// <summary>`CTFPlayerAnimState::HandleJumping`, once a frame (`tf_playeranimstate.cpp:1427-1537`).</summary>
    /// <param name="entityIndex">The player.</param>
    /// <param name="risingSpeed">
    /// `vecVelocity.z` — on the client `GetOuterAbsVelocity` is `EstimateAbsVelocity`, from position history — or null
    /// when there is no history yet, which is no rise.
    /// </param>
    /// <param name="flags">`m_fFlags`: `FL_ONGROUND` and `FL_DUCKING`.</param>
    /// <param name="waistDeep">`GetWaterLevel() &gt;= WL_Waist`.</param>
    /// <param name="grappling">`GetGrapplingHookTarget() != NULL`.</param>
    /// <param name="firingHeavy">A heavy under `TF_COND_AIMING`, for whom the function returns first.</param>
    /// <param name="seconds">Demo time now, which a landing starts at.</param>
    /// <param name="script">The player's class script: `bValidAirWalkClass` and `bNewJump`.</param>
    /// <returns>
    /// The activity it leaves in `idealActivity` when it returns true, or null when it returns false and
    /// `CalcMainActivity` goes on to the duck, the swim and the run.
    /// </returns>
    /// <remarks>
    /// <code>
    /// if ( heavy &amp;&amp; InCond( TF_COND_AIMING ) ) return false;                                          // :1439-1440
    /// if ( bValidAirWalkClass &amp;&amp; ( vecVelocity.z &gt; 300.0f || m_bInAirWalk || grapple ) &amp;&amp; !bInDuck )      // :1446
    ///     if ( onGround &amp;&amp; m_bInAirWalk ) { m_bInAirWalk = false; RestartGesture( JUMP, ACT_MP_JUMP_LAND ); } // :1449-1453
    ///     else if ( waist deep )           m_bInAirWalk = false;                                        // :1455-1458
    ///     else if ( !onGround )          { idealActivity = ACT_MP_AIRWALK; m_bInAirWalk = true; }       // :1461-1472
    /// else if ( m_bJumping ) …                                                                          // :1476-1530
    /// return m_bJumping || m_bInAirWalk;                                                                // :1534
    /// </code>
    ///
    /// **The duck shuts the block out, so it neither sets nor clears.** A player who crouches through a rocket jump
    /// never latches, and one who latched and then crouches keeps it — and stands, because the return is true with
    /// `idealActivity` untouched. **The jump's bookkeeping is the block's `else`**, so while the block runs the jump
    /// is neither timed out nor landed, and the step the latch ends with a jump still in force stands too (B437).
    /// `bInDuck` is the raw flag; the engine also drops it when the model has no crouch-walk for the held weapon.
    ///
    /// **Not ported:** `ACT_MP_FALLING_STOMP` (`:1464-1467`), which needs the local-only `m_flFallVelocity`.
    /// </remarks>
    public PlayerActivity? HandleJumping(
        int entityIndex,
        float? risingSpeed,
        int flags,
        bool waistDeep,
        bool grappling,
        bool firingHeavy,
        double seconds,
        ClassAnimationScript script)
    {
        if (firingHeavy)
        {
            return null;
        }

        bool onGround = (flags & PlayerActivityState.OnGround) != 0;
        PlayerActivity ideal = PlayerActivity.StandIdle;

        if (!script.DontDoAirwalk &&
            (risingSpeed > PlayerActivityState.AirwalkRiseSpeed || InAirWalk(entityIndex) || grappling) &&
            (flags & PlayerActivityState.Ducking) == 0)
        {
            // The landing (:1449-1453) and the water (:1455-1458) both clear, and both are asked before the air.
            if (onGround && _inAirWalk.Remove(entityIndex))
            {
                Land(entityIndex, seconds);
            }
            else if (waistDeep)
            {
                _inAirWalk.Remove(entityIndex);
            }
            else if (!onGround)
            {
                ideal = PlayerActivity.Airwalk;
                _inAirWalk.Add(entityIndex);
            }
        }
        else if (Jumping(entityIndex, seconds, onGround, waistDeep, script) is { } jumping)
        {
            ideal = PhaseOf(jumping, script);
        }

        return _jumpStarted.ContainsKey(entityIndex) || InAirWalk(entityIndex) ? ideal : null;
    }

    /// <summary>The jump's activity while `m_bJumping` holds (`:1511-1528`).</summary>
    private static PlayerActivity PhaseOf(double jumping, ClassAnimationScript script)
    {
        if (script.DontDoNewJump)
        {
            return PlayerActivity.LegacyJump;
        }

        return jumping > PlayerActivityState.JumpStartSeconds ? PlayerActivity.Jump : PlayerActivity.JumpStart;
    }

    /// <summary>`RestartGesture( GESTURE_SLOT_JUMP, ACT_MP_JUMP_LAND )`, whatever the slot held.</summary>
    private void Land(int entityIndex, double seconds)
    {
        if (!_byPlayer.TryGetValue(entityIndex, out SceneGesture?[]? slots))
        {
            slots = new SceneGesture?[SlotCount];
            _byPlayer[entityIndex] = slots;
        }

        slots[(int)GestureSlot.Jump] = new SceneGesture(GestureSlot.Jump, LandActivity, null, AutoKill: true, seconds);
    }

    /// <summary>`ClearAnimationState`, for everything this feed holds of one player.</summary>
    /// <param name="entityIndex">The player.</param>
    /// <remarks>
    /// **The TF override clears `m_bInAirWalk` and chains to the base** (`tf_playeranimstate.cpp:112-117`), which
    /// clears `m_bJumping` and resets every gesture slot (`multiplayer_animstate.cpp:136-146`). It runs on a respawn
    /// (`:310-313`) and on every frame `Update` does not animate the player: a custom model without the class's
    /// animations (`tf_playeranimstate.cpp:340-366`), `EF_NODRAW`, a dormant player and a dead one
    /// (`multiplayer_animstate.cpp:1381-1395`).
    ///
    /// Not held here and so not cleared here: the specific main sequence, which the base also resets. The base's
    /// `m_bCurrentFeetYawInitialized = false` is vestigial — nothing reads it — so it moves no feet (B437).
    /// </remarks>
    public void ClearAnimationState(int entityIndex)
    {
        _inAirWalk.Remove(entityIndex);
        _jumpStarted.Remove(entityIndex);
        _byPlayer.Remove(entityIndex);
        _waiting.Remove(entityIndex);
    }

    /// <summary>Each player's `m_flJumpStartTime` while `m_bJumping` holds, in demo seconds.</summary>
    private readonly Dictionary<int, double> _jumpStarted = [];

    /// <summary>
    /// How long a player has been jumping, or null when `m_bJumping` is not set — HandleJumping's clear, applied here:
    /// back on the ground more than 0.2 s after the jump began, or waist-deep in water (`tf_playeranimstate.cpp:1491`).
    /// </summary>
    /// <param name="entityIndex">The player.</param>
    /// <param name="seconds">Demo time now.</param>
    /// <param name="onGround">`FL_ONGROUND`.</param>
    /// <param name="waistDeep">`GetWaterLevel() >= WL_Waist`.</param>
    /// <param name="script">The class script, whose `bNewJump` gates the landing gesture; the engine's default when omitted.</param>
    /// <returns>Seconds since the jump event, or null.</returns>
    public double? Jumping(
        int entityIndex, double seconds, bool onGround, bool waistDeep, ClassAnimationScript script = default)
    {
        if (!_jumpStarted.TryGetValue(entityIndex, out double started))
        {
            return null;
        }

        if (waistDeep)
        {
            _jumpStarted.Remove(entityIndex);
            return null;
        }

        // **The landing is made HERE, by the clear, whatever the slot held** (`:1501-1508`, B437). An ordinary jump
        // plays no gesture of its own, so a landing that replaced the slot's double jump never followed one. Without
        // it `ACT_MP_DOUBLEJUMP` — a full-body animation — also goes on playing after the landing (B284).
        if (onGround && seconds - started > GroundBelievedAfterSeconds)
        {
            _jumpStarted.Remove(entityIndex);

            if (!script.DontDoNewJump)
            {
                Land(entityIndex, seconds);
            }

            return null;
        }

        return Math.Max(0d, seconds - started);
    }

    /// <summary>The activity a landing replaces the jump gesture with.</summary>
    public const string LandActivity = "ACT_MP_JUMP_LAND";

    /// <summary>
    /// How long after a jump began the ground flag is believed, in seconds.
    /// </summary>
    /// <remarks>
    /// **Valve's own guard, and Valve's own reason** (<c>tf_playeranimstate.cpp:1498</c>):
    /// *"Don't check if he's on the ground for a sec.. sometimes the client still has the on-ground
    /// flag set right when the message comes in."* Without it a jump would be cancelled by the
    /// ground flag of the tick it started on.
    /// </remarks>
    private const double GroundBelievedAfterSeconds = 0.2d;

    /// <summary>Records a choreographed scene as a VCD gesture on the player it animates (B351).</summary>
    /// <param name="entityIndex">The actor, out of the scene entity's <c>m_hActorList</c>.</param>
    /// <param name="scene">The compiled scene's filename, out of the <c>Scenes</c> string table.</param>
    /// <param name="seconds">Demo time when playback began.</param>
    /// <remarks>
    /// **Writing over the slot IS what the engine does.** `C_TFPlayer::StartGestureSceneEvent` calls
    /// `ResetGestureSlot( GESTURE_SLOT_VCD )` and then `AddVCDSequenceToGestureSlot` on the very next
    /// line (<c>c_tf_player.cpp:9477</c>), so a second taunt replaces the first rather than layering
    /// over it.
    ///
    /// **Auto-kill is true, unconditionally**, because that is the argument the engine passes at both
    /// of its call sites (<c>c_tf_player.cpp:4357</c> and <c>:9478</c>). A taunt therefore ends when
    /// its sequence's cycle passes one, without needing the scene to be stopped — which is how a
    /// viewer that never sees `m_bIsPlayingBack` go false still stops drawing it.
    ///
    /// **The SEQUENCE is not resolved here** and cannot be: it is a string inside the compiled scene,
    /// which lives in the installed game's own archive. Core carries the scene's name and the layer
    /// with the game's files fills in <see cref="SceneGesture.Taunt"/>.
    /// </remarks>
    public void RecordScene(int entityIndex, string scene, double seconds)
    {
        if (string.IsNullOrEmpty(scene))
        {
            return;
        }

        if (!_byPlayer.TryGetValue(entityIndex, out SceneGesture?[]? slots))
        {
            slots = new SceneGesture?[SlotCount];
            _byPlayer[entityIndex] = slots;
        }

        slots[(int)GestureSlot.Vcd] = new SceneGesture(
            GestureSlot.Vcd, null, null, AutoKill: true, seconds, SceneName: scene);

        AnyRecorded = true;
    }

    /// <summary>Notes that a scene has stopped playing back (B351).</summary>
    /// <param name="entityIndex">The actor.</param>
    /// <param name="scene">Which scene stopped.</param>
    /// <param name="seconds">Demo time when <c>m_bIsPlayingBack</c> went false.</param>
    /// <remarks>
    /// **Recorded rather than acted on, because the rule needs the scene's own contents.**
    /// `C_TFPlayer::StopGestureSceneEvent` resets the VCD slot only when the scene contains a `LOOP`
    /// (<c>c_tf_player.cpp:9505</c>) — the comment there is explicit that this is to let a running
    /// taunt play out — and whether it does is inside the compiled scene, which Core cannot read.
    ///
    /// **Only the gesture naming THIS scene is marked.** A player who began a second taunt already
    /// holds a different scene in the slot, and the first one's stop must not end it.
    /// </remarks>
    public void StopScene(int entityIndex, string scene, double seconds)
    {
        // Stryker disable all : the guard condition spans three lines, so a mutant that empties the
        // guard body leaves 'playing' unassigned at its use below (CS0165), and Safe Mode then
        // drops every mutation in this method — B410.
        if (!_byPlayer.TryGetValue(entityIndex, out SceneGesture?[]? slots) ||
            slots[(int)GestureSlot.Vcd] is not { } playing ||
            !string.Equals(playing.SceneName, scene, StringComparison.Ordinal))
        {
            return;
        }

        // Stryker restore all

        slots[(int)GestureSlot.Vcd] = playing with { StoppedSeconds = seconds };
    }

    /// <summary>The gestures a player has going, newest per slot, in slot order.</summary>
    /// <param name="entityIndex">The player.</param>
    /// <param name="into">Cleared and filled.</param>
    /// <exception cref="ArgumentNullException"><paramref name="into"/> is null.</exception>
    /// <remarks>
    /// **Slot order, because the slot IS the layer order** — <c>m_nOrder = iGestureSlot</c>. Nothing
    /// here decides when a gesture ends: its length comes from the sequence its activity resolves
    /// to, and only the scene has the model.
    /// </remarks>
    public void For(int entityIndex, ICollection<SceneGesture> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        if (!_byPlayer.TryGetValue(entityIndex, out SceneGesture?[]? slots))
        {
            return;
        }

        for (int slot = 0; slot < slots.Length; slot++)
        {
            if (slots[slot] is { } gesture)
            {
                into.Add(gesture);
            }

            // The waiting voice commands follow the gesture they wait behind, in arrival order.
            if (slot == (int)GestureSlot.AttackAndReload && _waiting.TryGetValue(entityIndex, out List<SceneGesture>? queue))
            {
                foreach (SceneGesture waiting in queue)
                {
                    into.Add(waiting);
                }
            }
        }
    }
}
