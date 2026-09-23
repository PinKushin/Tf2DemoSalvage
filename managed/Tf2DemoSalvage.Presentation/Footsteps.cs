using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What a footstep needs of a surface: `surfacegameprops_t.material` and its two step sounds.</summary>
/// <param name="Material">The `gamematerial` character, such as <c>'C'</c>.</param>
/// <param name="Left">`stepleft`.</param>
/// <param name="Right">`stepright`.</param>
public readonly record struct StepSurface(int Material, string? Left, string? Right);

/// <summary>
/// TF2's footsteps: animation event 7001 → `UpdateStepSound` → `PlayStepSound` — sound no demo carries (B172).
/// </summary>
/// <remarks>
/// **`C_TFPlayer::FireEvent`** (`c_tf_player.cpp:9066`) zeroes `m_flStepSoundTime` and calls `UpdateStepSound`
/// with the ground surface, the abs origin and the estimated velocity, so every 7001 the walk crosses is a step and
/// the step timer never gates one. `C_TFPlayer::UpdateStepSound` (`:9206`) first refuses a taunting player and one in
/// a Halloween kart.
/// </remarks>
public sealed class Footsteps
{
    /// <summary>`CHAN_BODY`, the channel `PlayStepSound` plays on.</summary>
    private const int BodyChannel = 4;

    private const int OnGround = 1 << 0;
    private const int Ducking = 1 << 1;

    /// <summary>`FL_FROZEN | FL_ATCONTROLS` in the TF branch of `const.h` (`1 &lt;&lt; 6`, `1 &lt;&lt; 7`).</summary>
    private const int Frozen = (1 << 6) | (1 << 7);

    private const int Taunting = 7;
    private const int HalloweenKart = 82;

    /// <summary>`WL_Feet` and `WL_Waist`.</summary>
    private const int FeetInWater = 1;
    private const int WaistInWater = 2;

    private const int Dirt = 'D';
    private const int Vent = 'V';

    /// <summary>`m_Local.m_nStepside`, per player: 0 plays `stepright`, 1 `stepleft`.</summary>
    private readonly Dictionary<int, int> _side = [];

    /// <summary>`UpdateStepSound`'s `static int iSkipStep` — one counter shared by every player, as in the engine.</summary>
    private int _skipStep;

    /// <summary>Forgets every foot, for a seek.</summary>
    public void Reset()
    {
        _side.Clear();
        _skipStep = 0;
    }

    /// <summary>One animation event 7001 on a player: the footstep it makes, or null where the engine makes none.</summary>
    /// <param name="tick">When.</param>
    /// <param name="player">The player, with `m_fFlags`, `m_nWaterLevel`, `m_flMaxspeed` and speed.</param>
    /// <param name="ground">`GetGroundSurface()`: what a hull trace 64 units down found, or null for nothing.</param>
    /// <param name="named">A surface by name, for `wade` and `water`.</param>
    /// <param name="scripts">The sound scripts.</param>
    /// <returns>The step, or null.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public SceneSound? Step(
        int tick,
        ScenePlayer player,
        StepSurface? ground,
        Func<string, StepSurface?> named,
        IReadOnlyDictionary<string, SoundScriptEntry> scripts)
    {
        ArgumentNullException.ThrowIfNull(named);
        ArgumentNullException.ThrowIfNull(scripts);

        int flags = player.Flags ?? 0;

        if (player.Conditions.Has(Taunting) || player.Conditions.Has(HalloweenKart) || (flags & Frozen) != 0)
        {
            return null;
        }

        // `GetStepSoundVelocities`. ponytail: the humiliated loser's zero walk speed is not carried; add it with
        // `IsLoser` when a round-end demo needs it. Speed stands for both `speed` and `groundspeed`: on the ground they
        // differ only by the vertical part, which is what makes one zero.
        float maximum = player.MaxSpeed ?? 0f;
        bool ducked = (flags & Ducking) != 0;
        float walk = maximum * (ducked ? 0.25f : 0.3f);
        float run = maximum * (ducked ? 0.3f : 0.8f);
        float speed = player.Speed;

        if (speed < walk || (flags & OnGround) == 0 || speed <= 0.0001f)
        {
            return null;
        }

        bool walking = speed < run;
        float volume;
        StepSurface? surface;

        switch (player.WaterLevel ?? 0)
        {
            case WaistInWater:
                // Every fourth step is skipped, starting with the first: 0 skips, then 1, 2, 3 play and 3 resets.
                if (_skipStep == 0)
                {
                    _skipStep++;
                    return null;
                }

                if (_skipStep++ == 3)
                {
                    _skipStep = 0;
                }

                surface = named("wade");
                volume = 0.65f;
                break;

            case FeetInWater:
                surface = named("water");
                volume = walking ? 0.2f : 0.5f;
                break;

            default:
                surface = ground;
                volume = surface?.Material switch
                {
                    Dirt => walking ? 0.25f : 0.55f,
                    Vent => walking ? 0.4f : 0.7f,
                    _ => walking ? 0.2f : 0.5f,
                };
                break;
        }

        if (surface is not { } found)
        {
            return null;
        }

        if (ducked)
        {
            volume *= 0.65f;
        }

        return Play(tick, player, found, volume, scripts);
    }

    /// <summary>`PlayStepSound`: this foot's sound, then the other foot next time.</summary>
    private SceneSound? Play(int tick, ScenePlayer player, StepSurface surface, float volume, IReadOnlyDictionary<string, SoundScriptEntry> scripts)
    {
        _side.TryGetValue(player.EntityIndex, out int side);

        if ((side != 0 ? surface.Left : surface.Right) is not { } name)
        {
            return null;
        }

        _side[player.EntityIndex] = side ^ 1;

        // `ep.m_flVolume = fvol` outside Mann vs. Machine: the script's own volume is drawn and thrown away.
        return EntitySounds.Emit(tick, player.EntityIndex, name, (player.X, player.Y, player.Z), scripts) is { } drawn
            ? drawn with { Channel = BodyChannel, Volume = volume }
            : null;
    }
}
