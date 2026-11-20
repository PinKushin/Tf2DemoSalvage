using System;
using System.Globalization;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// `CTFTips` (game/client/tf/tf_tips.cpp), `g_TFTips`: the class tips a point-of-view recorder's spectator bar shows (B733).
/// </summary>
/// <param name="random">
/// vstdlib's global `RandomInt` stream. *Interpolated:* the engine draws from the client's one global stream, which every
/// other caller moves too, so which random tip TF2 showed cannot be recovered — only the rule that picks it.
/// </param>
/// <param name="hoursPlayed">
/// `CTFStatPanel::GetTotalHoursPlayed()` (tf_hud_statpanel.cpp:1014): the WATCHER's Steam stats, which a viewer has no way
/// to read. 0 is what the function sums with no stats loaded. *Interpolated:* a watcher with twenty hours or more sees
/// the abuse tip a tenth as often.
/// </param>
/// <remarks>
/// **One index for every class.** `m_iCurrentClassTip` is a single member, so a class change carries the walk over: after
/// a soldier's first tip, a scout gets his second. The arena walk uses it too. *Not ported:* `GetRandomMvMTip` and the
/// Captain Canteen parts, which the spectator bar never asks for.
/// </remarks>
public sealed class TfTips(UniformRandomStream random, float hoursPlayed = 0f)
{
    // `TF_FIRST_NORMAL_CLASS`, `TF_LAST_NORMAL_CLASS` (tf_shareddefs.h).
    private const int FirstClass = 1;
    private const int LastClass = 9;

    /// <summary>"The chance is very high for the first 20 hours of play since newbies get picked on a lot." (:102-108)</summary>
    private const float NewPlayerHours = 20f;

    private readonly int[] _tipCount = new int[LastClass + 1];
    private int _tipCountAll;
    private int _arenaTipCount;
    private int _currentClassTip;
    private bool _inited;

    /// <summary>`GetNextClassTip( iClass )` (:143).</summary>
    /// <param name="playerClass">`m_Shared.GetDesiredPlayerClassIndex()`; outside 1–9 a random tip of any class.</param>
    /// <param name="arena">`TFGameRules()->IsInArenaMode()`.</param>
    /// <param name="localize">`g_pVGuiLocalize->Find`, the `#` stripped.</param>
    /// <param name="lookupBinding">`engine->Key_LookupBinding`, or null for none bound.</param>
    /// <returns>The tip, its keys bound; empty where the strings have none.</returns>
    public string NextClassTip(int playerClass, bool arena, Func<string, string?>? localize, Func<string, string?>? lookupBinding)
    {
        // "OK to call this function with TF_CLASS_UNDEFINED or TF_CLASS_RANDOM, just return a random tip for any class".
        if (playerClass is < FirstClass or > LastClass)
        {
            return RandomTip(localize, lookupBinding);
        }

        Init(localize);

        if (arena)
        {
            return ArenaTip(localize, lookupBinding);
        }

        int count = _tipCount[playerClass];

        if (count == 0)
        {
            return string.Empty;
        }

        // "wrap the tip index to the valid range for this class".
        if (_currentClassTip >= count)
        {
            _currentClassTip %= count;
        }

        string tip = Tip(playerClass, _currentClassTip + 1, localize, lookupBinding);

        _currentClassTip++;
        return tip;
    }

    /// <summary>`Init` (:31), once: each class's `Tip_%d_Count` and `Tip_arena_Count`, read as `_wtoi`.</summary>
    private void Init(Func<string, string?>? localize)
    {
        if (_inited)
        {
            return;
        }

        _tipCountAll = 0;

        for (int playerClass = FirstClass; playerClass <= LastClass; playerClass++)
        {
            _tipCount[playerClass] = Count(localize, string.Create(CultureInfo.InvariantCulture, $"Tip_{playerClass}_Count"));
            _tipCountAll += _tipCount[playerClass];
        }

        _arenaTipCount = Count(localize, "Tip_arena_Count");
        _inited = true;
    }

    private static int Count(Func<string, string?>? localize, string token) =>
        localize?.Invoke(token) is { } count ? CStdlib.Atoi(count) : 0;

    /// <summary>`GetRandomTip` (:97): the abuse tip by chance, else one tip drawn over every class's.</summary>
    private string RandomTip(Func<string, string?>? localize, Func<string, string?>? lookupBinding)
    {
        Init(localize);

        int abuseHintChance = hoursPlayed < NewPlayerHours ? 33 : 3;

        if (random.RandomInt(1, 100) <= abuseHintChance)
        {
            // `iClassUsed = RandomInt( ... )`: drawn and thrown away by this caller, but it moves the stream.
            _ = random.RandomInt(FirstClass, LastClass);
            return Bound(localize?.Invoke("Tip_Abuse_Report"), localize, lookupBinding);
        }

        // `RandomInt( 0, -1 )` answers its low end without a draw, and the walk then finds no class: an empty tip.
        if (_tipCountAll == 0)
        {
            return string.Empty;
        }

        int tip = random.RandomInt(0, _tipCountAll - 1);

        // "walk through each class until we find the class this tip lands in".
        for (int playerClass = FirstClass; playerClass <= LastClass; playerClass++)
        {
            if (tip < _tipCount[playerClass])
            {
                return Tip(playerClass, tip + 1, localize, lookupBinding);
            }

            tip -= _tipCount[playerClass];
        }

        return string.Empty;
    }

    /// <summary>`GetArenaTip` (:173): the arena walk, on the same index.</summary>
    private string ArenaTip(Func<string, string?>? localize, Func<string, string?>? lookupBinding)
    {
        if (_arenaTipCount == 0)
        {
            return string.Empty;
        }

        if (_currentClassTip >= _arenaTipCount)
        {
            _currentClassTip %= _arenaTipCount;
        }

        string tip = Bound(
            localize?.Invoke(string.Create(CultureInfo.InvariantCulture, $"Tip_arena_{_currentClassTip + 1}")), localize, lookupBinding);

        _currentClassTip++;
        return tip;
    }

    /// <summary>`GetTip( iClass, iTip )` (:196).</summary>
    private static string Tip(int playerClass, int tip, Func<string, string?>? localize, Func<string, string?>? lookupBinding) =>
        Bound(localize?.Invoke(string.Create(CultureInfo.InvariantCulture, $"Tip_{playerClass}_{tip}")), localize, lookupBinding);

    /// <summary>`UTIL_ReplaceKeyBindings` (cdll_util.cpp:868), which writes nothing for a null or empty string (:874).</summary>
    private static string Bound(string? format, Func<string, string?>? localize, Func<string, string?>? lookupBinding) =>
        string.IsNullOrEmpty(format) ? string.Empty : TfKeyBindings.Replace(format, lookupBinding, localize);
}
