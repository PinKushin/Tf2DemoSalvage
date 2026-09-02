using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// What an entity's animation-event walk remembers between frames.
/// </summary>
/// <param name="Sequence">
/// The sequence the walk was last run for — <c>m_nEventSequence</c>. Not the same as the entity's
/// current sequence: the difference between them is what tells the walk to restart.
/// </param>
/// <param name="PreviousCycle">
/// Where the walk got to — <c>m_flPrevEventCycle</c>. Negative after a restart, deliberately.
/// </param>
/// <remarks>
/// **State the engine keeps on the entity, returned instead** so the traversal is a function of its
/// inputs. <c>C_BaseAnimating</c> holds both as members and mutates them inside
/// <c>DoAnimationEvents</c>; a caller here holds this and hands it back next frame.
/// </remarks>
public readonly record struct AnimationEventState(int Sequence, float PreviousCycle)
{
    /// <summary>The state an entity starts in, before any sequence has been walked.</summary>
    /// <remarks>
    /// Sequence −1 because that is what <c>C_BaseAnimating</c> initialises <c>m_nEventSequence</c>
    /// to, and it is not a legal sequence — so the first walk always sees a change and restarts,
    /// which is what fires an event authored at cycle zero on the very first frame.
    /// </remarks>
    public static AnimationEventState Fresh => new(-1, 0f);
}

/// <summary>
/// Which animation events a cycle crossed since the last frame.
/// </summary>
/// <remarks>
/// **<c>C_BaseAnimating::DoAnimationEvents</c>, <c>game/client/c_baseanimating.cpp:3550</c>** —
/// transcribed rather than approximated, because every one of its branches is load-bearing and
/// three of them are counter-intuitive (B275).
///
/// **What is deliberately NOT here.** The engine's function opens with a visibility test, a muzzle
/// flash, and guards for a missing header or an out-of-range sequence; those belong to the caller,
/// which knows whether the entity is drawn and which model it has. What is left is the walk, and
/// the walk is the part with the rules.
///
/// **The events were already read and nothing fired them.** `StudioSequence.FiredEvents` has
/// carried them since the sequence reader learned about `mstudioevent_t`, and the only consumer was
/// a probe printing them — so a taunt played silently and no foot ever landed. Same shape as B268
/// and B269 earlier the same day: the hard half was done and the wiring was missing.
/// </remarks>
public static class AnimationEventFiring
{
    /// <summary>How far a cycle must fall to count as having looped rather than slipped.</summary>
    /// <remarks>
    /// Valve's <c>0.5</c>. Below it the engine's comment is explicit that the animation has "backed
    /// up, which is bad", and it refuses to replay the slice rather than firing everything twice.
    /// </remarks>
    public const float LoopThreshold = 0.5f;

    /// <summary>Where the walk restarts from, so an event at cycle zero is still ahead of it.</summary>
    /// <remarks>Valve's literal <c>-0.01</c>, commented "back up to get 0'th frame animations".</remarks>
    public const float RestartCycle = -0.01f;

    /// <summary>How far back the loop pass leaves the cycle for the pass that follows it.</summary>
    /// <remarks>Valve's <c>flEventCycle - 0.001f</c>, "necessary to get the next loop working".</remarks>
    public const float LoopBacktrack = 0.001f;

    /// <summary>Collects the client events this frame crossed, and returns the new state.</summary>
    /// <param name="events">The sequence's events, in the order the model declares them.</param>
    /// <param name="sequence">The sequence the entity is playing now.</param>
    /// <param name="state">What the last walk left behind.</param>
    /// <param name="cycle">The entity's cycle now.</param>
    /// <param name="resetEvents">Whether <c>m_nResetEventsParity</c> changed this frame.</param>
    /// <param name="into">Collects what fired, in the order the engine fires it.</param>
    /// <returns>The state to keep for next frame.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static AnimationEventState Fired(
        IReadOnlyList<StudioEvent> events,
        int sequence,
        AnimationEventState state,
        float cycle,
        bool resetEvents,
        ICollection<StudioEvent> into)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(into);

        // "Adrian: eh? This should never happen." — and it is still checked, because a sequence of
        // −1 indexes nothing.
        if (sequence < 0 || events.Count == 0)
        {
            return state;
        }

        float eventCycle = cycle;
        float previousCycle = state.PreviousCycle;

        // **A sequence change restarts the walk at the very beginning**, and the previous cycle goes
        // NEGATIVE rather than to zero — Valve's comment is "back up to get 0'th frame animations".
        // Without that an event authored at cycle 0, which is where a taunt's first sound sits,
        // never satisfies `cycle > previous` and is skipped for the life of the demo.
        //
        // `resetEvents` is `m_nResetEventsParity` changing, which is how a repeated taunt replays
        // its sounds without the sequence number ever moving.
        if (state.Sequence != sequence || resetEvents)
        {
            eventCycle = 0f;
            previousCycle = RestartCycle;
        }

        // **Stalled: the cycle has crossed nothing**, however many events sit before it. Compared
        // BIT for bit, which is the engine's `flEventCycle == m_flPrevEventCycle` and is the right
        // question — not "are these close" but "did this frame advance the animation at all". A
        // tolerance here would swallow the smallest real steps, which at 500 fps is most of them.
        if (BitConverter.SingleToInt32Bits(eventCycle)
            == BitConverter.SingleToInt32Bits(previousCycle))
        {
            return new AnimationEventState(sequence, previousCycle);
        }

        bool looped = false;

        if (eventCycle <= previousCycle)
        {
            if (previousCycle - eventCycle > LoopThreshold)
            {
                looped = true;
            }
            else
            {
                // **Not a loop — the animation was nudged backwards.** The engine's own comment
                // calls this "bad" and expects a hitch, and it returns rather than replaying the
                // slice, which would fire the same events a second time.
                return new AnimationEventState(sequence, previousCycle);
            }
        }

        if (looped)
        {
            // **The tail before the head**, which is Valve's own reason for the separate pass:
            // "This makes sure events that occur at the end of a sequence occur are sent before
            // events that occur at the beginning of a sequence." One pass in cycle order would put
            // a footstep at 0.1 ahead of one at 0.9 that happened first.
            foreach (StudioEvent fired in events)
            {
                if (fired.FiresOnTheClient() && fired.Cycle > previousCycle)
                {
                    into.Add(fired);
                }
            }

            previousCycle = eventCycle - LoopBacktrack;
        }

        foreach (StudioEvent fired in events)
        {
            if (fired.FiresOnTheClient() &&
                fired.Cycle > previousCycle &&
                fired.Cycle <= eventCycle)
            {
                into.Add(fired);
            }
        }

        return new AnimationEventState(sequence, eventCycle);
    }
}
