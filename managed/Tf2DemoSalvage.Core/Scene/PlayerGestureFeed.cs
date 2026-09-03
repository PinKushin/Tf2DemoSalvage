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
public readonly record struct SceneGesture(
    GestureSlot Slot,
    string? ActivityName,
    int? ActivityNumber,
    bool AutoKill,
    double StartedSeconds);

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
    /// <param name="context">What the player was doing, which decides which activity is chosen.</param>
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

        foreach (DecodedProperty property in effect.Properties)
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

        // **The mapping is asked here, at the moment the event arrives, because it depends on what
        // the player was doing THEN.** A reload started while crouched is a different activity from
        // one started standing, and the engine picks at `DoAnimationEvent` time
        // (`tf_playeranimstate.cpp:969`) rather than at draw time. Deferring it would resolve a
        // crouching reload against whatever posture the player is in when the frame is drawn.
        if (PlayerGestureEvent.Map((PlayerAnimEvent)which, context with { NData = data })
            is not { } trigger)
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

        slots[slot] = new SceneGesture(
            trigger.Slot, trigger.ActivityName, trigger.ActivityNumber, trigger.AutoKill, seconds);

        return true;
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
        }
    }
}
