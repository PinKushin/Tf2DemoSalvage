using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// The local player's screen overlay — the one slot <c>view-&gt;SetScreenOverlayMaterial</c> writes, which
/// <c>CViewRender::PerformScreenOverlay</c> (viewrender.cpp:1215) draws over the frame after the viewmodels.
/// </summary>
/// <remarks>
/// **History, not a function of the conditions.** Every <c>CTFPlayerShared::OnAdd*</c> that has an overlay overwrites
/// the slot, and every <c>OnRemove*</c> clears it only when the slot holds its OWN material (tf_player_shared.cpp,
/// "only remove the overlay if it is urine"). So a bleed added over jarate replaces it, and removing the bleed leaves
/// nothing even though the jarate still holds. Only the local player's hooks write it (<c>IsLocalPlayer()</c> on
/// every one), which in playback is the recorder.
///
/// **Not ported, named:** <c>IsErrorMaterial</c> — a missing material leaves the slot alone; all nine ship
/// (probe <c>vmt</c>, 2026-10-06). <c>m_nForceConditions</c> (a forced re-add) is not on the wire this reads.
/// <c>OnAddStealthed</c>'s <c>bFirstPrediction</c> is taken as true: a demo's local player is not predicted.
/// </remarks>
public static class ScreenOverlay
{
    /// <summary><c>TF_SCREEN_OVERLAY_MATERIAL_BURNING</c> (tf_player_shared.cpp:223).</summary>
    public const string Burning = "effects/imcookin";

    /// <summary><c>TF_SCREEN_OVERLAY_MATERIAL_INVULN_RED</c> (:224).</summary>
    public const string InvulnRed = "effects/invuln_overlay_red";

    /// <summary><c>TF_SCREEN_OVERLAY_MATERIAL_INVULN_BLUE</c> (:225).</summary>
    public const string InvulnBlue = "effects/invuln_overlay_blue";

    /// <summary><c>TF_SCREEN_OVERLAY_MATERIAL_URINE</c> and <c>_SWIMMING_CURSE</c> — the same material (:228, :231).</summary>
    public const string Urine = "effects/jarate_overlay";

    /// <summary><c>TF_SCREEN_OVERLAY_MATERIAL_BLEED</c> (:229).</summary>
    public const string Bleed = "effects/bleed_overlay";

    /// <summary><c>TF_SCREEN_OVERLAY_MATERIAL_STEALTH</c> (:230).</summary>
    public const string Stealth = "effects/stealth_overlay";

    /// <summary><c>TF_SCREEN_OVERLAY_MATERIAL_GAS</c> (:232).</summary>
    public const string Gas = "effects/gas_overlay";

    /// <summary><c>TF_SCREEN_OVERLAY_MATERIAL_PHASE</c> (:234).</summary>
    public const string Phase = "effects/dodge_overlay";

    /// <summary>Every material the slot can hold, for loading them with the map.</summary>
    public static IReadOnlyList<string> All { get; } = [Burning, InvulnRed, InvulnBlue, Urine, Bleed, Stealth, Gas, Phase];

    private const int TeamRed = 2;
    private const int Conditions = 160;

    /// <summary>`SyncConditions` over the five words (tf_player_shared.cpp:1504, called word by word ascending).</summary>
    /// <param name="current">The slot before this update, or null for none.</param>
    /// <param name="before">The conditions the client last had.</param>
    /// <param name="after">The conditions this update brings — what <c>InCond</c> reads while the hooks run.</param>
    /// <param name="team">The local player's team, which picks the invulnerability colour.</param>
    /// <returns>The slot after this update.</returns>
    public static string? Step(string? current, PlayerConditions before, PlayerConditions after, int? team)
    {
        if (before == after)
        {
            return current;
        }

        for (int condition = 0; condition < Conditions; condition++)
        {
            bool was = before.Has(condition);
            bool now = after.Has(condition);

            if (now && !was)
            {
                current = Added(current, condition, after, team);
            }
            else if (was && !now)
            {
                current = Removed(current, condition, after);
            }
        }

        return current;
    }

    /// <summary>The overlay half of <c>OnConditionAdded</c>'s switch (:1571).</summary>
    private static string? Added(string? current, int condition, PlayerConditions now, int? team) => condition switch
    {
        // OnAddInvulnerable (:3438): BLUE and default → blue.
        5 or 52 or 57 => team == TeamRed ? InvulnRed : InvulnBlue,

        // AddUberScreenEffect (:4684) — feign death, the medigun uber resists, the immunities — and OnAddMegaHeal.
        13 or 28 or 58 or 59 or 60 or 67 or 68 or 69 => team == TeamRed ? InvulnRed : InvulnBlue,

        // OnAddStealthed (:6983): only under TF_COND_STEALTHED_USER_BUFF. A spy's own cloak has no overlay.
        4 or 64 => now.Has(64) ? Stealth : current,

        // OnAddStealthedUserBuffFade.
        66 => Stealth,
        22 => Burning,
        24 or 86 => Urine,
        25 => Bleed,
        106 => Phase,
        123 => Gas,
        _ => current,
    };

    /// <summary>The overlay half of <c>OnConditionRemoved</c>'s switch (:1892): clear only one's own.</summary>
    private static string? Removed(string? current, int condition, PlayerConditions now)
    {
        string?[] own = condition switch
        {
            5 or 52 or 57 or 13 or 28 or 58 or 59 or 60 or 67 or 68 or 69 => [InvulnRed, InvulnBlue],

            // OnRemoveStealthed (:7076): not while TF_COND_STEALTHED_USER_BUFF_FADING holds.
            4 or 64 => now.Has(66) ? [] : [Stealth],
            66 => [Stealth],
            22 => [Burning],
            24 or 86 => [Urine],

            // OnRemoveBleeding, and OnRemovePlague (:5846), which clears the bleed's.
            25 or 112 => [Bleed],
            106 => [Phase],
            123 => [Gas],
            _ => [],
        };

        return Array.IndexOf(own, current) >= 0 ? null : current;
    }
}
