using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Whom `C_TFPlayer::ShouldDraw`'s local-player tests hide: the recorder, in every view, monitors included. Named
/// `ZoomVisibility` until B765 gave it its second test.
/// </summary>
/// <remarks>
/// **Two tests, one after the other, both before <c>BaseClass::ShouldDraw</c>** (<c>c_tf_player.cpp:8008-8020</c>), so
/// the monitor's <c>ForceTempForceDraw</c> (<c>viewrender.cpp:3266-3287</c>) cannot bring him back:
///
/// <code>
///   if ( IsLocalPlayer() &amp;&amp; HasCustomModel() &amp;&amp; !CustomModelIsVisibleToSelf() ) return false;   // B765
///   if ( this == GetLocalTFPlayer() &amp;&amp; InCond( TF_COND_ZOOMED ) )                         return false;   // B598
/// </code>
///
/// <c>C_BaseCombatWeapon::ShouldDraw</c> hides his weapon either way, because its owner does not draw
/// (<c>c_basecombatweapon.cpp:427-431</c>). **His cosmetics differ between the two**: <c>C_TFWearable::ShouldDraw</c>
/// repeats the zoom test (<c>tf_item_wearable.cpp:338-340</c>) but asks nothing about the custom model, so a recorder
/// hidden from himself keeps his cosmetics, bone-merged to a body that is posed and not drawn.
///
/// The local player during playback is the recorder; a SourceTV recording's is the spectator, who is never zoomed and
/// carries no custom model, so <c>Recorder</c> null hides nobody. The timeline sets the custom-model test only on the
/// recorder.
/// </remarks>
public static class LocalPlayerVisibility
{
    /// <summary>The entity these tests hide this moment, and whether his cosmetics stay drawn, or null.</summary>
    /// <param name="players">The players, for their conditions.</param>
    /// <param name="recorder">The local player, <see cref="MomentInfo.Recorder"/>.</param>
    /// <returns>The recorder when he is hidden, else null.</returns>
    public static (int Entity, bool KeepsCosmetics)? Hidden(IReadOnlyList<ScenePlayer> players, int? recorder)
    {
        System.ArgumentNullException.ThrowIfNull(players);

        if (recorder is not { } local)
        {
            return null;
        }

        foreach (ScenePlayer player in players)
        {
            if (player.EntityIndex != local)
            {
                continue;
            }

            if (player.Conditions.Has(PlayerConditions.Zoomed))
            {
                return (local, false);
            }

            return player.CustomModelHiddenFromSelf ? (local, true) : null;
        }

        return null;
    }
}
