using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>Who a sniper's zoom hides: the local player, in every view, monitors included (B598).</summary>
/// <remarks>
/// <c>C_TFPlayer::ShouldDraw</c> (<c>c_tf_player.cpp:8014-8020</c>) returns false for
/// <c>this == GetLocalTFPlayer()</c> in <c>TF_COND_ZOOMED</c> before reaching <c>BaseClass::ShouldDraw</c>, so the
/// monitor's <c>ForceTempForceDraw</c> (<c>viewrender.cpp:3266-3287</c>) cannot bring him back.
/// <c>C_TFWearable::ShouldDraw</c> hides his cosmetics by the same test (<c>tf_item_wearable.cpp:338-340</c>) and
/// <c>C_BaseCombatWeapon::ShouldDraw</c> his weapon, because its owner does not draw
/// (<c>c_basecombatweapon.cpp:427-431</c>) — the set <see cref="FirstPersonVisibility.Visible"/> already removes.
///
/// The demo carries the zoom as bit 1 of <c>m_Shared.m_nPlayerCond</c> (<c>TF_COND_ZOOMED</c>,
/// <see cref="PlayerConditions.Zoomed"/>). The local player during playback is the recorder; a SourceTV recording's
/// is the spectator, who is never zoomed, so <c>Recorder</c> null hides nobody.
/// </remarks>
public static class ZoomVisibility
{
    /// <summary>The entity the zoom hides this moment, or null.</summary>
    /// <param name="players">The players, for their conditions.</param>
    /// <param name="recorder">The local player, <see cref="MomentInfo.Recorder"/>.</param>
    /// <returns>The recorder when he is zoomed, else null.</returns>
    public static int? Hidden(IReadOnlyList<ScenePlayer> players, int? recorder)
    {
        System.ArgumentNullException.ThrowIfNull(players);

        if (recorder is not { } local)
        {
            return null;
        }

        foreach (ScenePlayer player in players)
        {
            if (player.EntityIndex == local)
            {
                return player.Conditions.Has(PlayerConditions.Zoomed) ? local : null;
            }
        }

        return null;
    }
}
