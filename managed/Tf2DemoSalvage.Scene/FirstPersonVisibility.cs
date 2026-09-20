using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// What is drawn when the camera is inside somebody's head.
/// </summary>
/// <remarks>
/// **The engine does not draw the player whose eyes you are using.**
/// <c>C_BasePlayer::ShouldDrawThisPlayer</c> (<c>c_baseplayer.cpp:1992</c>):
///
/// <code>
/// if ( !InFirstPersonView() )                                  { return true; }
/// if ( !UseVR() &amp;&amp; cl_first_person_uses_world_model.GetBool() ) { return true; }
/// return false;
/// </code>
///
/// The cvar that overrides it is the "see your own body" option and is off by default, so the rule
/// in practice is simply: in first person, the viewed player is hidden.
///
/// **Found by looking rather than by testing.** The first capture of this viewer's first-person
/// view drew the recorder's own model from the inside, and the owner described it as "a bunch of
/// purple and black checkerboard textures or some sort of blobs" and then "a player hat or
/// something that doesnt have textures". Every automated check was green at the time; the camera
/// was in the right place pointing the right way, which is all any of them measured.
/// </remarks>
public static class FirstPersonVisibility
{
    /// <summary>Filters a scene for the camera being inside a player.</summary>
    /// <param name="scene">Everything the timeline says exists at this tick.</param>
    /// <param name="viewed">
    /// The entity whose eyes the camera is in, or <c>null</c> for every other camera mode.
    /// </param>
    /// <returns>What to draw.</returns>
    /// <remarks>
    /// **Cosmetics are the half that is easy to miss.** A hat is its own entity with no origin of
    /// its own — <c>EF_BONEMERGE</c> takes the wearer's bones by name — so hiding the player and
    /// keeping the hat leaves it hanging in the middle of the picture, which is precisely what the
    /// first capture showed. Anything attached to the hidden player goes with them.
    ///
    /// Returns the original list unchanged when nothing is being looked through, so the map and
    /// free views pay nothing for a feature they do not use.
    /// </remarks>
    public static IReadOnlyList<SceneProp> Visible(IReadOnlyList<SceneProp> scene, int? viewed)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (viewed is not { } hidden)
        {
            return scene;
        }

        List<SceneProp> drawn = new(scene.Count);

        foreach (SceneProp prop in scene)
        {
            // **Owned counts as well as attached, and the engine keys on OWNED.**
            // `C_BaseCombatWeapon::ShouldDraw`:
            //
            //     C_BaseCombatCharacter *pOwner = GetOwner();
            //     if ( !pOwner ) return true;             // unowned, always drawn
            //     if ( pOwner == pLocalPlayer ) {
            //         ...
            //         return false;                        // first person: the viewmodel draws it
            //     }
            //
            // Attachment could not answer this. It reports where a prop is DRAWN — a parent, or an
            // owner when the prop also asked to be bone-merged — so a carried weapon that sends its
            // own origin is owned by its carrier and parented to nobody, and reads as attached to
            // nothing. It was therefore drawn in the first-person view alongside the viewmodel:
            // two sticky launchers overlapping, and a soldier's rocket launcher across the lens.
            // **And the owner rule reaches WEAPONS only** (B414). `ShouldDraw` above is
            // `C_BaseCombatWeapon`'s, and nothing else overrides it — `CTFBaseRocket : CBaseProjectile`
            // (`tf_weaponbase_rocket.h:36`) declares none, nor does any `c_tf_projectile_*`. A fired
            // rocket carries `m_hOwnerEntity` naming the soldier who fired it, so applying the weapon
            // rule to every prop hid every rocket from the one player with the best view of it. The
            // owner, on seeing it: *"for some reason the first person rockets still dont draw"*.
            //
            // `WeaponState` is the discriminator the wire already gives us: it is null for anything
            // that did not declare `DT_BaseCombatWeapon`.
            if (prop.EntityIndex == hidden ||
                prop.AttachedTo == hidden ||
                (prop.OwnedBy == hidden && prop.WeaponState is not null))
            {
                continue;
            }

            drawn.Add(prop);
        }

        return drawn;
    }
}
