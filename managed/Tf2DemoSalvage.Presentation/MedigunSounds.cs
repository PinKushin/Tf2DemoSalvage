using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>A medigun's heal loop and detach, from its beams — sound no demo carries (B415).</summary>
/// <remarks>
/// <code>
/// ClientThink:   m_hHealingTarget and !m_bPlayingSound → SoundCreate( entindex(), GetHealSound() ), Play
/// OnDataChanged: !m_bHealing → StopHealSound( true, … ): destroy the loop, SoundCreate( GetDetachSound() ), Play
/// </code>
/// (`tf_weapon_medigun.cpp:2036-2329`). A beam that ends as the next begins is a new target with healing still on, so the
/// loop runs through it. `GetHealSound` / `GetDetachSound` take the Healer and Target variants only when the local player
/// is the medic or his target; a SourceTV demo's local player is neither. The loop is chosen by the item's
/// `set_weapon_mode`, so the Quick-Fix and the Vaccinator play their own. ponytail: the loop sits where the medic
/// stood when it started; follow him when a moving source is carried.
/// </remarks>
public static class MedigunSounds
{
    private const string Detach = "WeaponMedigun.HealingDetachWorld";

    /// <summary>`g_pszMedigunHealSounds` (`tf_weapon_medigun.cpp:208`), a charge-type table `GetHealSound` indexes with the weapon mode.</summary>
    private static readonly string[] HealSounds =
    [
        "WeaponMedigun.HealingWorld",
        "WeaponMedigun.HealingWorld",
        "Weapon_Quick_Fix.Healing",
        "WeaponMedigun_Vaccinator.Healing",
        "WeaponMedigun_Vaccinator.Healing",
        "WeaponMedigun_Vaccinator.Healing",
    ];

    /// <summary>Every heal loop, its stop and its detach, in tick order.</summary>
    /// <param name="beams">The demo's medigun beams.</param>
    /// <param name="at">Where an entity stood at a tick.</param>
    /// <param name="scripts">The sound scripts.</param>
    /// <returns>The sounds.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <param name="healingStops">When each medigun's `m_bHealing` fell; a beam's detach waits for the first at or after its end.</param>
    /// <param name="weaponModeOf">An item's `set_weapon_mode` hook, which picks the loop; null for the stock loop.</param>
    public static IReadOnlyList<SceneSound> For(
        IReadOnlyList<SceneHealBeam> beams,
        Func<int, int, (float X, float Y, float Z)> at,
        IReadOnlyDictionary<string, SoundScriptEntry> scripts,
        IReadOnlyList<(int Medigun, int Tick)>? healingStops = null,
        Func<int, int>? weaponModeOf = null)
    {
        ArgumentNullException.ThrowIfNull(beams);
        ArgumentNullException.ThrowIfNull(at);
        ArgumentNullException.ThrowIfNull(scripts);

        List<SceneSound> sounds = [];

        foreach (IGrouping<int, SceneHealBeam> medigun in beams.GroupBy(static beam => beam.Medigun))
        {
            SceneSound? loop = null;
            List<SceneHealBeam> ordered = [.. medigun.OrderBy(static beam => beam.Start)];

            for (int index = 0; index < ordered.Count; index++)
            {
                SceneHealBeam beam = ordered[index];
                int source = beam.Owner ?? beam.Medigun;

                int mode = beam.Item is { } item && weaponModeOf is not null ? weaponModeOf(item) : 0;
                string heal = HealSounds[mode >= 0 && mode < HealSounds.Length ? mode : 0];

                loop ??= EntitySounds.Emit(beam.Start, beam.Medigun, heal, at(source, beam.Start), scripts);

                // The next target arriving as this one goes: healing never stopped.
                if (index + 1 < ordered.Count && ordered[index + 1].Start == beam.End)
                {
                    continue;
                }

                if (loop is not { } playing)
                {
                    continue;
                }

                sounds.Add(playing);
                loop = null;

                if (beam.End is not { } ended)
                {
                    continue;
                }

                // `OnDataChanged` stops the sound when `m_bHealing` falls, which can trail the target clearing.
                int end = ended;

                foreach ((int stopped, int tick) in healingStops ?? [])
                {
                    if (stopped == beam.Medigun && tick >= ended)
                    {
                        end = tick;
                        break;
                    }
                }

                sounds.Add(playing with { Tick = end, IsStop = true });

                if (EntitySounds.Emit(end, beam.Medigun, Detach, at(source, end), scripts) is { } detach)
                {
                    sounds.Add(detach);
                }
            }
        }

        return [.. sounds.OrderBy(static sound => sound.Tick)];
    }
}
