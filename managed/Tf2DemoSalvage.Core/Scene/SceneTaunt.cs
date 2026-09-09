using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One gesture a compiled scene stages, and when the scene stages it (B351).</summary>
/// <param name="At">Seconds into the scene when the event begins, <c>CChoreoEvent::GetStartTime</c>.</param>
/// <param name="Sequence">
/// The sequence name the model must look up — the event's first parameter, which is what
/// <c>LookupSequence( event-&gt;GetParameters() )</c> is handed (<c>c_tf_player.cpp:9456</c>).
/// </param>
public readonly record struct SceneTauntGesture(float At, string Sequence);

/// <summary>
/// What a compiled scene does to the player it animates, resolved once (B351).
/// </summary>
/// <param name="Gestures">
/// Every <c>GESTURE</c> and <c>SEQUENCE</c> event, in the order the compiler wrote them.
/// </param>
/// <param name="LoopsFrom">
/// Where the scene's clock is folded back to, or a negative number when it never loops. This is the
/// `LOOP` event's own parameter, read as a float: <c>float backtime = (float)atof(
/// event-&gt;GetParameters() );</c> then <c>SetCurrentTime( backtime, true )</c>
/// (<c>c_sceneentity.cpp:574</c>, <c>:594</c>). A real one measured on
/// <c>taunt_jackhammer_rodeo</c> is the string <c>"2.500000"</c>.
/// </param>
/// <param name="LoopsAt">
/// When the scene's clock reaches the `LOOP` event, or a negative number when it never loops. The
/// scene therefore cycles over <c>[LoopsFrom, LoopsAt]</c> for as long as the server keeps it
/// playing.
/// </param>
/// <remarks>
/// **Resolved once per scene rather than per player per tick.** Turning a filename into this costs a
/// CRC search, an LZMA decompression and an event walk, and a taunt lasting five seconds is three
/// hundred sampled ticks — so the same arrangement the weapon roles use applies: read what the
/// recording actually mentions, up front, and hand out an immutable answer
/// (`docs/memory/a-lazy-cache-makes-reading-a-write.md`).
///
/// **A scene with no gesture still gets one of these**, with an empty list, because "this scene names
/// no animation" is an answer — most of the archive is speech — and it must be told apart from "the
/// archive could not be read".
/// </remarks>
public sealed record SceneTaunt(
    IReadOnlyList<SceneTauntGesture> Gestures,
    float LoopsFrom,
    float LoopsAt)
{
    /// <summary>Whether the scene folds its own clock back, and so repeats.</summary>
    /// <remarks>
    /// **This is also the engine's test for whether stopping the scene clears the gesture slot.**
    /// `C_TFPlayer::StopGestureSceneEvent` walks the scene's events looking for a `LOOP` and calls
    /// `ResetGestureSlot( GESTURE_SLOT_VCD )` only if it finds one (<c>c_tf_player.cpp:9505</c>),
    /// under the comment *"The ResetGestureSlot call will prevent people from doing running taunts
    /// (which they like to do), so let's only reset the gesture slot if the scene contains a loop"*.
    /// </remarks>
    public bool Loops => LoopsAt > LoopsFrom && LoopsFrom >= 0f;

    /// <summary>Where the scene's clock stands after however many loops have elapsed.</summary>
    /// <param name="elapsedSeconds">Seconds since playback began.</param>
    /// <returns>The scene's own time.</returns>
    /// <remarks>
    /// **Closed form rather than stepping, because a viewer seeks.** The engine reaches the same
    /// place by advancing `m_flCurrentTime` a frame at a time and setting it back to `LoopsFrom`
    /// whenever the `LOOP` event is crossed, which is exactly folding the excess into
    /// <c>[LoopsFrom, LoopsAt)</c>.
    /// </remarks>
    public float TimeAt(float elapsedSeconds)
    {
        if (!Loops || elapsedSeconds < LoopsAt)
        {
            return elapsedSeconds;
        }

        float period = LoopsAt - LoopsFrom;

        return LoopsFrom + ((elapsedSeconds - LoopsFrom) % period);
    }

    /// <summary>The gesture in force at a moment in the scene, and how far into it that is.</summary>
    /// <param name="sceneSeconds">The scene's own time, from <see cref="TimeAt"/>.</param>
    /// <returns>The gesture and its elapsed time, or null before the first one begins.</returns>
    /// <remarks>
    /// **The LAST one that has begun wins, because each start RESETS the slot.**
    /// `StartGestureSceneEvent` calls `ResetGestureSlot( GESTURE_SLOT_VCD )` and then
    /// `AddVCDSequenceToGestureSlot` on the next line (<c>c_tf_player.cpp:9477</c>), so a scene
    /// staging two gestures never layers them — the second replaces the first.
    /// </remarks>
    public (SceneTauntGesture Gesture, float Elapsed)? At(float sceneSeconds)
    {
        SceneTauntGesture? found = null;

        for (int index = 0; index < Gestures.Count; index++)
        {
            if (Gestures[index].At <= sceneSeconds &&
                (found is not { } held || Gestures[index].At >= held.At))
            {
                found = Gestures[index];
            }
        }

        return found is { } playing ? (playing, sceneSeconds - playing.At) : null;
    }
}
