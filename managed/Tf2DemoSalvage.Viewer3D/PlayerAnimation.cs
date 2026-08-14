using System;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// Which sequence a player should be playing, since no demo says.
/// </summary>
/// <remarks>
/// **Nothing on the wire answers this.** Measured across the whole committed corpus, 2007 to 2026:
/// every playing player reports <c>m_nSequence</c> absent and <c>m_flCycle</c> at zero, over
/// 244,951 samples on one demo alone. TF2 computes a player's animation on the client in
/// <c>CTFPlayerAnimState</c> and sends none of it, so a viewer has to compute it too.
///
/// This is the first slice of that, and only the first: standing against running, which is
/// <c>CBasePlayerAnimState::HandleMoving</c> comparing horizontal speed against
/// <c>MOVING_MINIMUM_SPEED</c>. Ducking, aiming, jumping, swimming, taunting, the loser state and
/// the weapon-specific variants are all still missing, and a player doing any of them will be
/// drawn standing or running instead.
///
/// **Named rather than numbered, deliberately.** Sequence numbers differ per class — the scout's
/// 212 is not the heavy's — so the choice is made by label and resolved per model.
///
/// **Speed is the engine's own input here, not a substitute for one.**
/// <c>CBasePlayerAnimState::GetOuterXYSpeed</c> is <c>vel.Length2D()</c> — the entity's absolute
/// velocity, horizontal — so differencing recorded positions measures the same quantity the client
/// does, rather than approximating an input the demo lacks.
///
/// **Per-class run speeds do matter, but not here.** Every class plays the same run sequence; what
/// differs is <c>m_flMaxGroundSpeed</c> from <c>GetCurrentMaxGroundSpeed</c>, which drives the
/// playback RATE and the move-yaw pose parameter. A heavy at 230 units and a scout at 400 both run;
/// the heavy's animation cycles slower. That is not implemented, so every class currently animates
/// at the authored rate and a heavy will look faster-footed than he should.
/// </remarks>
internal static class PlayerAnimation
{
    /// <summary>
    /// <c>MOVING_MINIMUM_SPEED</c> from <c>base_playeranimstate.h</c>: half a unit a second.
    /// </summary>
    /// <remarks>
    /// Small enough to mean "moving at all" rather than "moving quickly", which is what the engine
    /// means by it — a player easing off a ledge is still running, animation-wise.
    /// </remarks>
    private const float MovingMinimumSpeed = 0.5f;

    /// <summary>Which sequence to play at a given speed.</summary>
    /// <param name="model">The player's model, for resolving names to numbers.</param>
    /// <param name="speed">Horizontal speed in units a second.</param>
    /// <returns>A merged sequence number, or −1 when the model offers neither.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <remarks>
    /// The primary-weapon variants are used because a demo does not say which weapon is out either
    /// — <c>m_hActiveWeapon</c> is a separate decode this project has not done. Every class has the
    /// primary forms, so they resolve for all nine.
    /// </remarks>
    public static int For(PropModels.SkinnedModel model, float speed)
    {
        ArgumentNullException.ThrowIfNull(model);

        bool moving = speed > MovingMinimumSpeed;

        int wanted = moving ? model.Find("run_PRIMARY") : model.Find("Stand_PRIMARY");

        if (wanted >= 0)
        {
            return wanted;
        }

        // A model without the named sequence falls back to the other rather than to nothing: a
        // player frozen in the reference pose is worse than one running on the spot, and either is
        // honest about being an approximation.
        return moving ? model.Find("Stand_PRIMARY") : model.Find("run_PRIMARY");
    }
}
