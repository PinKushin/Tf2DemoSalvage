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
    /// The primary-weapon variants, which is the engine's default as well as this overload's:
    /// <c>ActivityList</c> gives <c>TF_WPN_TYPE_PRIMARY</c> the same body as <c>default:</c>. Every
    /// class has the primary forms, so they resolve for all nine.
    /// </remarks>
    public static int For(PropModels.SkinnedModel model, float speed) =>
        For(model, speed, flags: null, alive: true);

    /// <summary>Which sequence a player should be playing.</summary>
    /// <param name="model">The player's model, for resolving activities to numbers.</param>
    /// <param name="speed">Horizontal speed in units a second.</param>
    /// <param name="flags">The player's <c>m_fFlags</c>, or null when the recording did not say.</param>
    /// <param name="alive">Whether the player is alive.</param>
    /// <param name="slot">
    /// The activity suffix the held weapon drives — <c>PRIMARY</c>, <c>SECONDARY</c>, <c>MELEE</c>
    /// and so on, as <c>WeaponRoles</c> reads it from the weapon's own script. Defaulted rather than
    /// required, because the engine defaults it the same way.
    /// </param>
    /// <param name="airborneSeconds">
    /// How long since the player left the ground, or null when it cannot be told — which splits a
    /// jump into <c>ACT_MP_JUMP_START</c> and <c>ACT_MP_JUMP_FLOAT</c>.
    /// </param>
    /// <param name="waterLevel">How deep in water they are; 2 or more is waist deep.</param>
    /// <param name="airwalking">
    /// Whether they are rising fast enough to air-walk and their class allows it, which supersedes
    /// the jump entirely.
    /// </param>
    /// <returns>A merged sequence number, or −1 when the model offers nothing suitable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <remarks>
    /// **Null flags are a real case rather than an error**, though a rarer one than this comment
    /// used to claim. It said <c>m_fFlags</c> was declared in <c>DT_LocalPlayerExclusive</c> and so
    /// reached only a POV recorder; it is on <c>DT_BasePlayer</c> and reaches every player in the
    /// PVS (B103). Absent, the state machine sees a player standing on the ground — which is what
    /// they usually are.
    ///
    /// **Falls back rather than returning nothing.** A model that claims no sequence for the chosen
    /// activity — a crouch-walk it does not have, say — takes the standing form instead. A player
    /// frozen in the reference pose lies on their back, which reads as a broken model rather than as
    /// a missing animation.
    /// </remarks>
    public static int For(
        PropModels.SkinnedModel model,
        float speed,
        int? flags,
        bool alive,
        string slot = "PRIMARY",
        float? airborneSeconds = null,
        bool airwalking = false,
        int? waterLevel = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Absent flags read as standing on the ground: FL_ONGROUND set, nothing else. Passing zero
        // instead would say AIRBORNE, and every player in a POV demo would be drawn falling.
        int state = flags ?? PlayerActivityState.OnGround;

        // **WL_Waist, which is 2** — Valve documents the four levels at player.cpp:1961 and both
        // HandleJumping and HandleSwimming test `>= WL_Waist`. This was hardcoded false until the
        // water level was decoded, so nobody in any recording ever swam.
        bool waistDeep = waterLevel >= PlayerActivityState.WaistDeepWaterLevel;

        PlayerActivity activity = PlayerActivityState.For(
            state, speed, waistDeep, alive, airborneSeconds, airwalking);

        int wanted = model.ForActivity(PlayerActivityState.NameOf(activity, slot));

        if (wanted >= 0)
        {
            return wanted;
        }

        // **The weapon's own suffix first, then the primary form of the same activity.** A class
        // that has no crouch-walk for the slot it is holding still has the primary one, and that is
        // nearer to what the player is doing than a different activity would be. Only after both
        // fail does the activity itself change, below.
        if (!string.Equals(slot, "PRIMARY", StringComparison.Ordinal) &&
            model.ForActivity(PlayerActivityState.NameOf(activity)) is var primary and >= 0)
        {
            return primary;
        }

        // The two the engine starts from, in order: whatever the player is doing, standing or
        // running is closer to it than the reference pose.
        int fallback = speed > MovingMinimumSpeed
            ? model.ForActivity(PlayerActivityState.NameOf(PlayerActivity.Run, slot))
            : model.ForActivity(PlayerActivityState.NameOf(PlayerActivity.StandIdle, slot));

        return fallback >= 0 ? fallback : model.Find("Stand_PRIMARY");
    }
}
