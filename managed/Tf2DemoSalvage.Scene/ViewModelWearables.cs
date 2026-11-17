using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>A viewmodel wearable (`CTFWearableVM`): drawn on its owner's viewmodel, and only there (B771).</summary>
/// <remarks>
/// **`CEconWearable::ShouldDraw`'s mode test** (`econ_wearable.cpp:375-393`): `bUseViewModel = !pPlayerOwner->
/// ShouldDrawThisPlayer()`, and a viewmodel wearable draws only in viewmodel mode, any other only out of it. It follows
/// `pOwner->GetViewModel()` (`CEconWearable::Equip`, `:185-192`) and is drawn in `RENDER_GROUP_VIEW_MODEL_TRANSLUCENT`
/// (`CEconWearable::GetRenderGroup`, `:454-460`).
///
/// **So no world list ever holds one.** Every player in a world list is drawn, a monitor's included —
/// `ForceTempForceDraw` makes the local player draw there (`viewrender.cpp:3266-3287`) — and the one player who is not,
/// the one whose eyes the camera uses, has his viewmodel wearables drawn by the first-person pass instead.
/// </remarks>
public static class ViewModelWearables
{
    /// <summary>`CTFWearableVM`, the one class whose `IsViewModelWearable` is true (`tf_item_wearable.h:106-112`).</summary>
    public const string ClassName = "CTFWearableVM";

    /// <summary>Whether a prop is a viewmodel wearable.</summary>
    /// <param name="prop">The prop.</param>
    /// <returns>Whether its class is <see cref="ClassName"/>.</returns>
    public static bool Is(SceneProp prop)
    {
        ArgumentNullException.ThrowIfNull(prop);

        return string.Equals(prop.ClassName, ClassName, StringComparison.Ordinal);
    }

    /// <summary>Takes a world list's viewmodel wearables out of it, in place, keeping the rest in order.</summary>
    /// <param name="drawn">A world pass's list.</param>
    /// <param name="into">Given what was taken, in the list's order, for the first-person pass; null to drop them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="drawn"/> is null.</exception>
    public static void MoveOut(IList<SceneProp> drawn, ICollection<SceneProp>? into)
    {
        ArgumentNullException.ThrowIfNull(drawn);

        int kept = 0;

        for (int index = 0; index < drawn.Count; index++)
        {
            SceneProp prop = drawn[index];

            if (Is(prop))
            {
                into?.Add(prop);
            }
            else
            {
                drawn[kept++] = prop;
            }
        }

        while (drawn.Count > kept)
        {
            drawn.RemoveAt(drawn.Count - 1);
        }
    }
}
