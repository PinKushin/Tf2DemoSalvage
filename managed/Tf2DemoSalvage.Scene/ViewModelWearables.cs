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

    /// <summary>A world list without its viewmodel wearables.</summary>
    /// <param name="drawn">A world pass's list.</param>
    /// <returns>The props that survive, in the shape <c>DrawList.KeepOnly</c> takes; <paramref name="drawn"/> itself when
    /// it holds none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="drawn"/> is null.</exception>
    public static IReadOnlyList<SceneProp> OutOfTheWorld(IReadOnlyList<SceneProp> drawn)
    {
        ArgumentNullException.ThrowIfNull(drawn);

        List<SceneProp>? kept = null;

        for (int index = 0; index < drawn.Count; index++)
        {
            bool viewModel = Is(drawn[index]);

            if (viewModel && kept is null)
            {
                kept = new List<SceneProp>(drawn.Count);

                for (int before = 0; before < index; before++)
                {
                    kept.Add(drawn[before]);
                }
            }
            else if (!viewModel)
            {
                kept?.Add(drawn[index]);
            }
        }

        return kept ?? drawn;
    }

    /// <summary>The viewmodel wearables of a moment, for the first-person pass to place on their viewmodel.</summary>
    /// <param name="moment">Every prop of the moment.</param>
    /// <param name="into">Cleared, then given those whose class is <see cref="ClassName"/>, in the moment's order.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void Of(IReadOnlyList<SceneProp> moment, ICollection<SceneProp> into)
    {
        ArgumentNullException.ThrowIfNull(moment);
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        foreach (SceneProp prop in moment)
        {
            if (Is(prop))
            {
                into.Add(prop);
            }
        }
    }
}
