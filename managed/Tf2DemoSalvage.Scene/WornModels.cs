using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Which models are placed by somebody else's skeleton, and so may never be baked.
/// </summary>
/// <remarks>
/// **A bone-merged model must be skinned however cheap it is, and this is not an optimisation
/// choice.** Baking pre-transforms the vertices by one pose and discards the bone indices, which is
/// fine for a model drawn at its own transform and useless for one placed entirely by its wearer's
/// bones: there is nothing left to attach it by. `PropModels` then keeps the model's own matrices and
/// the caller has already applied the WEARER's transform, so it draws at the wearer's origin.
///
/// **That origin is a different disaster depending on the wearer, which is why this has bitten
/// twice.** On a player it is their feet — every cosmetic on cp_process baked, and the hats sat at
/// ankle height. On a viewmodel it is the CAMERA, so a fourteen-unit knife against a near plane of
/// one clipped away entirely and the spy's hand was empty (B119).
///
/// **Both failures are silent.** The model packs, uploads, instances and draws; the merge returns at
/// its first guard without logging; the poser reports the full count. Only looking at the picture
/// shows it, which is why the rule lives here as one testable function rather than inline in a form.
/// </remarks>
public static class WornModels
{
    /// <summary>Every model path that must be loaded skinned rather than baked.</summary>
    /// <param name="props">The scene's prop tracks, of which the attached ones are worn.</param>
    /// <param name="heldWeapons">
    /// The first-person weapon models, which are worn but are NOT prop tracks.
    /// </param>
    /// <returns>Paths, compared without case like every other model path here.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <remarks>
    /// **The second argument exists because the first cannot see it.** The weapon drawn in first
    /// person is created by the CLIENT (<c>econ_entity.cpp:1153</c>,
    /// <c>InitializeAsClientEntity</c>), so no demo carries it and it is not in the timeline at all —
    /// the viewer builds it from the item definition and attaches it to the viewmodel. A rule that
    /// walked only the prop tracks was therefore correct about every model the demo describes and
    /// blind to the one place the consequence is most visible.
    /// </remarks>
    public static HashSet<string> From(
        IEnumerable<ScenePropTrack> props,
        IEnumerable<string> heldWeapons)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(heldWeapons);

        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

        foreach (ScenePropTrack track in props)
        {
            // Studio only: a brush entity has no skeleton to merge onto and asking for one would
            // force a pointless skinned load of map geometry.
            if (track.AttachedTo is not null && track.Kind == SceneModelKind.Studio)
            {
                paths.Add(track.ModelPath);
            }
        }

        foreach (string weapon in heldWeapons)
        {
            if (weapon.Length > 0)
            {
                paths.Add(weapon);
            }
        }

        return paths;
    }
}
