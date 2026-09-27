using System;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// The local player's killstreak eye state as `C_TFPlayer` keeps it — `m_pszEyeGlowEffectName`, `m_vEyeGlowColor1/2` —
/// set by `UpdateKillStreakEffects` (c_tf_player.cpp:10414-10610) when `ClientKillStreakBuffThink` (tf_player_shared.cpp:6520)
/// sees the streak or the weapon's slot change, or the player dead, and when stealth is added or removed (:7036, :7124).
/// </summary>
/// <remarks>
/// Only what `GetEyeGlowEffect`/`GetEyeGlowColor` read (c_tf_player.h:501-502). The player's own world eye effects are the
/// world renderer's. The wearable fallback (:10457-10473) matches `GetEquippedPositionForClass`, which reads SOC data
/// (econ_item_view.h:321) a demo does not have, so it never matches here.
/// </remarks>
public sealed class TfKillStreakEyes
{
    private const int TeamBlue = 3;
    private const int ClassDemoman = 4;
    private const int HighGlow = 20000;

    /// <summary>`tf_killstreakeyes_minkills` (c_tf_player.cpp:203), `FCVAR_DEVELOPMENTONLY`.</summary>
    private const int MinKills = 5;

    /// <summary>`tf_killstreakeyes_maxkills` (:204).</summary>
    private const int MaxKills = 10;

    /// <summary>`g_KillStreakEffectsBase` (c_tf_player.cpp:336-346): whether it has a blue table row, and colors 1 and 2 in 0..255.</summary>
    private static readonly (bool TeamColor, (int R, int G, int B) Color1, (int R, int G, int B) Color2)[] EffectsBase =
    [
        (false, (0, 0, 0), (0, 0, 0)),
        (true, (255, 118, 118), (255, 35, 28)),
        (false, (255, 237, 138), (255, 213, 65)),
        (false, (255, 111, 5), (255, 137, 31)),
        (false, (230, 255, 60), (193, 255, 61)),
        (false, (103, 255, 121), (165, 255, 193)),
        (false, (105, 20, 255), (185, 145, 255)),
        (false, (255, 120, 255), (255, 176, 217)),
    ];

    /// <summary>`g_KillStreakEffectsBlue` (:349-353).</summary>
    private static readonly ((int R, int G, int B) Color1, (int R, int G, int B) Color2)[] EffectsBlue =
    [
        ((0, 0, 0), (0, 0, 0)),
        ((0, 92, 255), (134, 203, 243)),
    ];

    private int _oldKillStreak;
    private int _oldKillStreakSlot;
    private bool _wasStealthed;

    /// <summary>`GetEyeGlowEffect()`: the system's name, or null.</summary>
    public string? EffectName { get; private set; }

    /// <summary>`m_vEyeGlowColor1` — `GetEyeGlowColor( true )`.</summary>
    public Vector3 Color1 { get; private set; }

    /// <summary>`m_vEyeGlowColor2` — `GetEyeGlowColor( false )`.</summary>
    public Vector3 Color2 { get; private set; }

    /// <summary>`ClientKillStreakBuffThink` plus the stealth add/remove calls, for the local player.</summary>
    /// <param name="player">The local player.</param>
    /// <param name="state">The game state, for a disguise target's streak.</param>
    /// <param name="schema">The item schema.</param>
    /// <param name="hook">`CALL_ATTRIB_HOOK_*_ON_OTHER( weapon, ... )`.</param>
    public void Think(ScenePlayer player, HudState state, ItemSchema schema, Func<ScenePlayer, SceneItem, string, float, float> hook)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(hook);

        int killStreak = player.KillStreak ?? 0;
        bool stealthed = player.Conditions.Has(PlayerConditions.Stealthed);

        if (stealthed != _wasStealthed)
        {
            // `OnAddStealthed` / `OnRemoveStealthed` (tf_player_shared.cpp:7036, :7124).
            _wasStealthed = stealthed;
            UpdateKillStreakEffects(killStreak, player, state, schema, hook);
        }

        int slot = ActiveWeapon(player) is { DefinitionIndex: { } definition }
            ? schema.LoadoutSlot(definition, player.PlayerClass ?? 0)
            : ItemSchema.LoadoutSlotPrimary;

        if (killStreak != _oldKillStreak || _oldKillStreakSlot != slot)
        {
            UpdateKillStreakEffects(killStreak, player, state, schema, hook);
            _oldKillStreak = killStreak;
            _oldKillStreakSlot = slot;
        }
        else if (!player.IsAlive)
        {
            UpdateKillStreakEffects(0, player, state, schema, hook);
        }
    }

    /// <summary>`C_TFPlayer::UpdateKillStreakEffects( iCount )` (c_tf_player.cpp:10414-10610), its eye-state half.</summary>
    private void UpdateKillStreakEffects(
        int count, ScenePlayer player, HudState state, ItemSchema schema, Func<ScenePlayer, SceneItem, string, float, float> hook)
    {
        if (ActiveWeapon(player) is not { } weapon)
        {
            return;
        }

        // "Disguised Spies, Use targets kill streak. Otherwise nothing" (:10434).
        if (player.Conditions.Has(PlayerConditions.Disguised))
        {
            count = player.DisguiseTarget is { } target && state.Player(target) is { } disguiseTarget ? disguiseTarget.KillStreak ?? 0 : 0;
        }

        int effectIndex = AttributeHooks.RoundFloatToInt(hook(player, weapon, "killstreak_effect", 0f));
        int colorIndex = AttributeHooks.RoundFloatToInt(hook(player, weapon, "killstreak_idleeffect", 0f));

        if (colorIndex == 0 || effectIndex == 0)
        {
            EffectName = null;
            Color1 = Vector3.Zero;
            return;
        }

        bool blue = player.Team == TeamBlue;

        Color1 = ColorFor(colorIndex, blue, useColor2: false, Color1);
        Color2 = ColorFor(colorIndex, blue, useColor2: true, Color2);

        if (count < MinKills || player.Conditions.Has(PlayerConditions.Stealthed))
        {
            EffectName = null;
            return;
        }

        if (effectIndex <= 0 || count <= 0)
        {
            EffectName = null;
            return;
        }

        // "This is the wrong type, value is too large" (:10532) and "no eyeglows for tier0" (:10536).
        if (effectIndex > HighGlow || effectIndex == 2001)
        {
            return;
        }

        if (count >= MaxKills)
        {
            effectIndex += HighGlow;
        }

        AttributeParticleSystem? system = schema.AttributeControlledParticleSystem(effectIndex);

        if (system?.SystemName is { } name)
        {
            if (blue && name.Contains("_teamcolor_red", StringComparison.OrdinalIgnoreCase))
            {
                system = schema.FindAttributeControlledParticleSystem(Substitute(name, "_teamcolor_red", "_teamcolor_blue"));
            }
            else if (player.Team == 2 && name.Contains("_teamcolor_blue", StringComparison.OrdinalIgnoreCase))
            {
                system = schema.FindAttributeControlledParticleSystem(Substitute(name, "_teamcolor_blue", "_teamcolor_red"));
            }
        }

        if (system?.SystemName is { } effect)
        {
            EffectName = effect;
        }
    }

    /// <summary>`C_TFPlayer::GetDemomanEyeEffectName( iDecapitations )` (c_tf_player.cpp:10370).</summary>
    /// <param name="decapitations">The heads taken.</param>
    /// <returns>The system, or null below one.</returns>
    public static string? DemomanEyeEffectName(int decapitations) => decapitations switch
    {
        < 1 => null,
        1 => "eye_powerup_green_lvl_1",
        2 => "eye_powerup_green_lvl_2",
        3 => "eye_powerup_green_lvl_3",
        _ => "eye_powerup_green_lvl_4",
    };

    /// <summary>`GetVectorColorForParticleSystem` (c_tf_player.cpp:10389): out of range leaves the vector as it was.</summary>
    private static Vector3 ColorFor(int system, bool blue, bool useColor2, Vector3 current)
    {
        if (system < 0 || system >= EffectsBase.Length)
        {
            return current;
        }

        ((int R, int G, int B) color1, (int R, int G, int B) color2) = (EffectsBase[system].Color1, EffectsBase[system].Color2);

        if (blue && EffectsBase[system].TeamColor)
        {
            (color1, color2) = EffectsBlue[system];
        }

        (int r, int g, int b) = useColor2 ? color2 : color1;

        return new Vector3(r / 255f, g / 255f, b / 255f);
    }

    /// <summary>`V_StrSubst`: every occurrence, case sensitive as `V_strstr` is.</summary>
    private static string Substitute(string text, string find, string replace) => text.Replace(find, replace, StringComparison.Ordinal);

    /// <summary>`GetActiveTFWeapon()`'s item.</summary>
    private static SceneItem? ActiveWeapon(ScenePlayer player)
    {
        foreach (SceneItem item in player.Items ?? [])
        {
            if (item.IsWeapon && item.EntityIndex == player.ActiveWeapon)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>`TF_CLASS_DEMOMAN`, whose left eye is the Eyelander's (tf_playermodelpanel.cpp:1742).</summary>
    internal const int Demoman = ClassDemoman;
}
