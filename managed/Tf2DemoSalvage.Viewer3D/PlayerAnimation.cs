using System;

using Tf2DemoSalvage.Core.Scene;

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
/// The choice itself is <see cref="PlayerActivityState"/>, which is
/// <c>CMultiPlayerAnimState::CalcMainActivity</c> — jumping, then ducking, then swimming, then
/// dying, and moving only if none of those claimed it. Aiming, taunting, the loser state and the
/// gesture layers are still missing.
///
/// **By ACTIVITY, not by label, and that correction is the point of this file's second version.**
/// The first asked the model for a sequence called <c>run_PRIMARY</c>, which TF2's models do happen
/// to be named — but it is not how the engine finds an animation. <c>mstudioseqdesc_t</c> carries an
/// activity name beside the label and <c>SelectWeightedSequence</c> works from the activity; the
/// label is a human name for one sequence. Selecting by label meant relying on a naming convention
/// instead of on the field that exists for the purpose.
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
    public static int For(PropModels.SkinnedModel model, float speed) =>
        For(model, speed, flags: null, alive: true);

    /// <summary>Which sequence a player should be playing.</summary>
    /// <param name="model">The player's model, for resolving activities to numbers.</param>
    /// <param name="speed">Horizontal speed in units a second.</param>
    /// <param name="flags">The player's <c>m_fFlags</c>, or null when the recording did not say.</param>
    /// <param name="alive">Whether the player is alive.</param>
    /// <returns>A merged sequence number, or −1 when the model offers nothing suitable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <remarks>
    /// **Null flags are a real case rather than an error.** <c>m_fFlags</c> is declared in
    /// <c>DT_LocalPlayerExclusive</c>, so a POV demo carries it for the recorder alone while a
    /// SourceTV recording carries it for everybody. Absent, the state machine sees a player standing
    /// on the ground — which is what they usually are, and is the same answer this file gave before
    /// the flags existed.
    ///
    /// **Falls back rather than returning nothing.** A model that claims no sequence for the chosen
    /// activity — a crouch-walk it does not have, say — takes the standing form instead. A player
    /// frozen in the reference pose lies on their back, which reads as a broken model rather than as
    /// a missing animation.
    /// </remarks>
    public static int For(PropModels.SkinnedModel model, float speed, int? flags, bool alive)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Absent flags read as standing on the ground: FL_ONGROUND set, nothing else. Passing zero
        // instead would say AIRBORNE, and every player in a POV demo would be drawn falling.
        int state = flags ?? PlayerActivityState.OnGround;

        PlayerActivity activity = PlayerActivityState.For(state, speed, waistDeep: false, alive);

        int wanted = model.ForActivity(PlayerActivityState.NameOf(activity));

        if (wanted >= 0)
        {
            return wanted;
        }

        // The two the engine starts from, in order: whatever the player is doing, standing or
        // running is closer to it than the reference pose.
        int fallback = speed > MovingMinimumSpeed
            ? model.ForActivity(PlayerActivityState.NameOf(PlayerActivity.Run))
            : model.ForActivity(PlayerActivityState.NameOf(PlayerActivity.StandIdle));

        return fallback >= 0 ? fallback : model.Find("Stand_PRIMARY");
    }
}
