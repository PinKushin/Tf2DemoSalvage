using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

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
/// **There is no per-class playback rate, and an earlier version of this comment said there was.**
/// It claimed <c>m_flMaxGroundSpeed</c> drives the playback RATE so a heavy's run cycles slower
/// than a scout's, and that this was unimplemented. That was read from
/// <c>CBasePlayerAnimState::ComputePlaybackRate</c> — a class TF2 does not inherit from.
/// <c>CTFPlayerAnimState</c> derives from <c>CMultiPlayerAnimState</c>, which is standalone, and the
/// only <c>SetPlaybackRate</c> in it is <c>SetPlaybackRate( 1.0f )</c> for the local player. Its
/// <c>m_flMaxGroundSpeed</c> is maintained by <c>UpdateInterpolators</c> and returned by a getter
/// nothing in the TF2 hierarchy reads for the main sequence.
///
/// What TF2 actually does with speed is the pose-parameter scaling in
/// <c>ComputePoseParam_MoveYaw</c> — <c>x *= flSpeed / flMaxSpeed</c> against the sequence's own
/// authored ground speed — which is implemented (B101). A heavy at his 230 against a run authored
/// at 230 scales by one; a heavy at 150 is pulled toward the centre of the blend grid. The rate is
/// left at the authored value because that is what the engine does.
/// </remarks>
internal static class PlayerAnimation
{
    /// <summary>Which sequence to play at a given speed.</summary>
    /// <param name="model">The player's model, for resolving names to numbers.</param>
    /// <param name="speed">Horizontal speed in units a second.</param>
    /// <returns>A merged sequence number; 0 when the model has none for it.</returns>
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
    /// The table the held weapon drives — <c>PRIMARY</c>, <c>SECONDARY</c>, <c>MELEEALLCLASS</c> and
    /// so on, as <c>WeaponRoles</c> reads it from the weapon's script and its item. Defaulted rather
    /// than required, because the engine defaults it the same way.
    /// </param>
    /// <param name="jumping">HandleJumping's answer, or null when it returned false (B437).</param>
    /// <param name="waterLevel">How deep in water they are; 2 or more is waist deep.</param>
    /// <param name="table">The player's own activity table, walked before the weapon's (B437).</param>
    /// <param name="competitiveWinnerClass">The class of a competitive winner, else null (B437).</param>
    /// <param name="item">The held item's `animation_replacement` rows, or null (B437).</param>
    /// <param name="posture">TF's HandleDucking and HandleMoving inputs from the decode (B437).</param>
    /// <returns>A merged sequence number; 0 when the model has none for the activity, as the engine draws.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <remarks>
    /// **Null flags are a real case rather than an error**, though a rarer one than this comment
    /// used to claim. It said <c>m_fFlags</c> was declared in <c>DT_LocalPlayerExclusive</c> and so
    /// reached only a POV recorder; it is on <c>DT_BasePlayer</c> and reaches every player in the
    /// PVS (B103). Absent, the state machine sees a player standing on the ground — which is what
    /// they usually are.
    ///
    /// **Sequence 0, not a ladder of our own** (B437). This used to try the primary form of the activity, then
    /// running or standing, then the label <c>Stand_PRIMARY</c>; the engine has none of that. What it does have is
    /// `HandleDucking`'s crouch-walk check, which is what the ladder was mostly standing in for — ported above.
    /// </remarks>
    public static int For(
        PropModels.SkinnedModel model,
        float speed,
        int? flags,
        bool alive,
        string slot = "PRIMARY",
        PlayerActivity? jumping = null,
        int? waterLevel = null,
        PlayerActivityOverride table = PlayerActivityOverride.None,
        int? competitiveWinnerClass = null,
        IReadOnlyDictionary<string, string>? item = null,
        TfPosture posture = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Absent flags read as standing on the ground: FL_ONGROUND set, nothing else. Passing zero
        // instead would say AIRBORNE, and every player in a POV demo would be drawn falling.
        int state = flags ?? PlayerActivityState.OnGround;

        // **WL_Waist, which is 2** — Valve documents the four levels at player.cpp:1961 and both
        // HandleJumping and HandleSwimming test `>= WL_Waist`. This was hardcoded false until the
        // water level was decoded, so nobody in any recording ever swam.
        bool waistDeep = waterLevel >= PlayerActivityState.WaistDeepWaterLevel;

        Func<string, bool> declared = name => model.ForActivity(name) >= 0;

        // **TF's HandleDucking asks THIS model for the translated crouch walk** (`tf_playeranimstate.cpp:1343-1347`); the
        // rest of the posture — `IsLoser()`, `IsAiming()`, the air dash, the deployed hold — the decode carried.
        TfPosture asked = posture with
        {
            LacksCrouchWalk =
                model.ForActivity(Translate(PlayerActivity.CrouchWalk, slot, table, competitiveWinnerClass, item, declared)) < 0,
        };

        PlayerActivity activity = PlayerActivityState.For(state, speed, waistDeep, alive, jumping, asked);

        // **`if ( animDesired < 0 ) animDesired = 0;`** (`multiplayer_animstate.cpp:1174-1177`) — the engine's one
        // fallback. Sequence 0 is the merged model's first sequence, whatever that is.
        int wanted = model.ForActivity(Translate(activity, slot, table, competitiveWinnerClass, item, declared));

        return wanted >= 0 ? wanted : 0;
    }

    /// <summary>The activity a model is asked for: <c>CalcMainActivity</c>'s answer through `TranslateActivity`.</summary>
    /// <param name="activity">What the player is doing.</param>
    /// <param name="role">The held weapon's table, as <c>WeaponRoles</c> names it.</param>
    /// <param name="table">The player's own table, walked first (B437).</param>
    /// <param name="competitiveWinnerClass">The class of a competitive winner, else null.</param>
    /// <param name="item">The held item's `animation_replacement` rows, or null.</param>
    /// <param name="declared">Whether the model declares an activity, for a replacement outside the shared list.</param>
    /// <returns>The name the model is asked for.</returns>
    /// <remarks>
    /// **`CTFPlayerAnimState::TranslateActivity`** (`tf_playeranimstate.cpp:124-153`), by
    /// <see cref="PlayerActivityTable.Translate"/> — the same route every gesture takes (`EntityModels.LayersFor`), so
    /// the body and its layers cannot disagree.
    /// </remarks>
    internal static string Translate(
        PlayerActivity activity,
        string role,
        PlayerActivityOverride table = PlayerActivityOverride.None,
        int? competitiveWinnerClass = null,
        IReadOnlyDictionary<string, string>? item = null,
        Func<string, bool>? declared = null) =>
        PlayerActivityTable.Translate(
            PlayerActivityState.IdealName(activity), role, table, competitiveWinnerClass, item, declared);
}
