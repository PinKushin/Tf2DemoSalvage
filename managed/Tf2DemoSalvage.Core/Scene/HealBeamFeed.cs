using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One medigun beam — one effect `UpdateEffects` created, from its creation to its stop (B415).</summary>
/// <param name="Medigun">The medigun's entity index, whose `muzzle` is control point 0.</param>
/// <param name="Target">`m_hHealingTarget`'s entity, whose origin plus 50 up is control point 1.</param>
/// <param name="ChargeRelease">`m_bChargeRelease`, which picks the `_invun` beam.</param>
/// <param name="Team">The medigun's team, which picks red or blue.</param>
/// <param name="Start">The tick the beam was created.</param>
/// <param name="End">The tick its emission stopped, or null while it still runs at the demo's end.</param>
public readonly record struct SceneHealBeam(int Medigun, int Target, bool ChargeRelease, int Team, int Start, int? End)
{
    /// <summary>The medigun's item definition, whose `custom_particlesystem` adds to the beam; null for none.</summary>
    public int? Item { get; init; }

    /// <summary>The medic — `m_hOwnerEntity` — who is `pFiringPlayer`, compared with the local player; null for none.</summary>
    public int? Owner { get; init; }
}

/// <summary>Every medigun beam a demo carries (B415).</summary>
/// <remarks>
/// <code>
/// // tf_weapon_medigun.cpp — RecvProxy_HealingTarget and a change of m_bChargeRelease both ForceHealingTargetUpdate,
/// // and OnDataChanged then runs UpdateEffects:
/// stop the current beam's emission
/// if m_hHealingTarget: Create( beam, PATTACH_POINT_FOLLOW, "muzzle" ); control point 1 on the target,
///                      PATTACH_ABSORIGIN_FOLLOW, offset ( 0, 0, 50 )
/// </code>
/// **A beam is decided by the target and the charge**, so a change of either ends one and starts the next. A medigun
/// leaving view ends its beam: its effects go dormant with it. *Not built:* a revive marker's `healbeam` attachment
/// (MvM only). `medicgun_beam_machinery` is unreachable: `IsAllowedToTargetBuildings` returns false (`:920`).
/// </remarks>
public sealed class HealBeamFeed
{
    private readonly Dictionary<int, (int? Target, bool ChargeRelease, int Beam)> _state = [];
    private readonly List<SceneHealBeam> _beams = [];

    /// <summary>The property the target travels in.</summary>
    public const string TargetKey = "DT_WeaponMedigun.m_hHealingTarget";

    /// <summary>The property the charge travels in.</summary>
    public const string ChargeReleaseKey = "DT_WeaponMedigun.m_bChargeRelease";

    /// <summary>`m_bHealing`, whose fall is when `OnDataChanged` stops the heal sound (`tf_weapon_medigun.cpp:2229`).</summary>
    public const string HealingKey = "DT_WeaponMedigun.m_bHealing";

    private readonly Dictionary<int, bool> _healing = [];
    private readonly List<(int Medigun, int Tick)> _healingStops = [];

    /// <summary>Every tick a medigun's `m_bHealing` fell from true to false, in order.</summary>
    public IReadOnlyList<(int Medigun, int Tick)> HealingStops => _healingStops;

    /// <summary>The system `UpdateEffects` creates for a player target (`tf_weapon_medigun.cpp:2419-2454`).</summary>
    /// <param name="team">The medic's team; red is 2, anything else takes the blue branch.</param>
    /// <param name="chargeRelease">`m_bChargeRelease`, which wins over the marker.</param>
    /// <param name="targeted">`hud_medichealtargetmarker` is on and this medic is the local player.</param>
    /// <returns>The system's name.</returns>
    public static string EffectName(int team, bool chargeRelease, bool targeted)
    {
        string side = team == 2 ? "medicgun_beam_red" : "medicgun_beam_blue";

        if (chargeRelease)
        {
            return side + "_invun";
        }

        return targeted ? side + "_targeted" : side;
    }

    /// <summary>Every beam, in the order they started.</summary>
    public IReadOnlyList<SceneHealBeam> All => _beams;

    /// <summary>Takes one applied medigun update.</summary>
    /// <param name="medigun">Its entity index.</param>
    /// <param name="entering">Whether it entered view on this update.</param>
    /// <param name="target">Its target's entity index, or null for none.</param>
    /// <param name="chargeRelease">Whether its charge is released.</param>
    /// <param name="team">Its team.</param>
    /// <param name="tick">The tick.</param>
    /// <param name="item">Its item definition, or null.</param>
    /// <param name="owner">Its medic's entity, or null.</param>
    /// <param name="isHealing">`m_bHealing`, or null when not sent.</param>
    public void Observe(
        int medigun, bool entering, int? target, bool chargeRelease, int team, int tick, int? item = null, int? owner = null, bool? isHealing = null)
    {
        if (isHealing is { } now)
        {
            if (!now && _healing.TryGetValue(medigun, out bool before) && before)
            {
                _healingStops.Add((medigun, tick));
            }

            _healing[medigun] = now;
        }

        if (!entering &&
            _state.TryGetValue(medigun, out (int? Target, bool ChargeRelease, int Beam) was) &&
            was.Target == target && was.ChargeRelease == chargeRelease)
        {
            return;
        }

        Leave(medigun, tick);

        int beam = -1;

        if (target is { } healing)
        {
            beam = _beams.Count;
            _beams.Add(new SceneHealBeam(medigun, healing, chargeRelease, team, tick, null) { Item = item, Owner = owner });
        }

        _state[medigun] = (target, chargeRelease, beam);
    }

    /// <summary>Ends a medigun's beam, for one that left view or was deleted.</summary>
    /// <param name="medigun">Its entity index.</param>
    /// <param name="tick">The tick.</param>
    public void Leave(int medigun, int tick)
    {
        if (_state.Remove(medigun, out (int? Target, bool ChargeRelease, int Beam) was) && was.Beam >= 0)
        {
            _beams[was.Beam] = _beams[was.Beam] with { End = tick };
        }
    }
}
