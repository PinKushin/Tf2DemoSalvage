using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Which items are visible to the person watching — TF2's vision filters (B354).
/// </summary>
/// <remarks>
/// **`CEconEntity::ShouldDraw` is two lines and this is the first one** (<c>econ_entity.cpp:1800</c>):
///
/// <code>
///   bool CEconEntity::ShouldDraw()
///   {
///       if ( ShouldHideForVisionFilterFlags() )
///           return false;
///       return BaseClass::ShouldDraw();
///   }
/// </code>
///
/// An item declaring <c>vision_filter_flags</c> is drawn only to a viewer who has that vision — and
/// **the viewer, not the wearer**, which is what makes a Balloonicorn invisible to the eight players
/// who are not in Pyroland while its owner sees it perfectly well.
///
/// **23 shipped items declare a filter**, measured: the Pet Balloonicorn (738), the Infernal
/// Orchestrina (745), the Burning Bongos (746) and the Pet Reindoonicorn (995) at
/// <c>TF_VISION_FILTER_PYRO</c>, and the nineteen MvM robot skins 30143–30161 at
/// <c>TF_VISION_FILTER_ROME</c> (<c>shareddefs.h:977</c>).
///
/// **Two arms of the engine's own flag computation are deliberately not reproduced, and neither is
/// a gap.**
/// <list type="bullet">
/// <item>The Halloween arm sets <c>TF_VISION_FILTER_HALLOWEEN</c> for everybody on a holiday map
/// (<c>c_tf_player.cpp:8092</c>). **No shipped item requires that bit** — the 23 ask for 1 or 4 —
/// so it cannot change any answer here. It matters for particles and world tinting, which are not
/// this rule.</item>
/// <item>The Rome arm needs `IsMannVsMachineMode()`, `IsSharedVisionAvailable` and the client's own
/// <c>tf_romevision_opt_in</c>. A demo carries none of the three, so the nineteen MvM skins are
/// hidden — which is what TF2 draws for a viewer who has not opted in, its default.</item>
/// </list>
///
/// **What a viewer may not yet do is opt IN.** `tf_spectate_pyrovision` (`c_tf_player.cpp:218`,
/// default 0) lets a live spectator turn Pyroland on, and this viewer has no store for cvar values
/// — only binds and aliases — so there is nowhere to keep the setting. Its default is off, so the
/// behaviour here is TF2's out of the box; the knob is what is missing, not the rule.
/// </remarks>
public static class VisionVisibility
{
    /// <summary>The attribute an item grants its wearer, as the schema names it.</summary>
    /// <remarks>
    /// `CALL_ATTRIB_HOOK_INT( nVisionOptInFlags, vision_opt_in_flags )` (<c>c_tf_player.cpp:8043</c>).
    /// The attribute's NAME is what <c>ItemSchema.AttributeDefinitionIndex</c> takes; its class is
    /// the same words with underscores, and they are not interchangeable.
    /// </remarks>
    public const string OptInAttribute = "vision opt in flags";

    /// <summary>Drops the items the person watching has no vision for.</summary>
    /// <param name="scene">Everything the timeline says exists at this tick.</param>
    /// <param name="viewer">The watcher's vision, from <see cref="ViewerFlags"/>.</param>
    /// <param name="required">What an item requires, by definition index; 0 for most.</param>
    /// <returns>What to draw.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Every requested bit, not any** — `( nLocalPlayerFlags &amp; nFlags ) == nFlags`
    /// (<c>cdll_util.cpp:125</c>). No shipped item asks for two visions at once, so an overlap test
    /// would agree on all 23 and be wrong anyway.
    ///
    /// Returns the original list when nothing was dropped, like the other filters here: a scene with
    /// no Pyroland item in it — which is nearly every scene — pays a walk and no allocation.
    /// </remarks>
    public static IReadOnlyList<SceneProp> Visible(
        IReadOnlyList<SceneProp> scene, int viewer, Func<int, int> required)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(required);

        int hidden = 0;

        foreach (SceneProp prop in scene)
        {
            if (Hidden(prop, viewer, required))
            {
                hidden++;
            }
        }

        if (hidden == 0)
        {
            return scene;
        }

        List<SceneProp> kept = new(scene.Count - hidden);

        foreach (SceneProp prop in scene)
        {
            if (!Hidden(prop, viewer, required))
            {
                kept.Add(prop);
            }
        }

        return kept;
    }

    /// <summary>The vision the person watching the demo has.</summary>
    /// <param name="scene">This tick's props, whose items grant the vision.</param>
    /// <param name="recorder">The recorder's entity index, or null for a SourceTV recording.</param>
    /// <param name="grants">What an item grants its wearer, by definition index.</param>
    /// <returns>The OR of every grant the recorder carries.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **The viewer of a demo is its recorder**, so the flags are theirs and nobody else's. The
    /// items that grant Pyrovision are largely the Pyroland items themselves — the Rainblower, the
    /// Lollichop, the Balloonicorn, the Reindoonicorn — which is why a player carrying one sees
    /// their own.
    ///
    /// **OR rather than sum, and it is read from the engine rather than assumed.** The attribute's
    /// description format is `value_is_or`, and `ApplyAttribute` handles that case as
    /// `iTmp |= (int)flValueModifier` (<c>attribute_manager.cpp:613</c>). Two items each granting 1
    /// is the ordinary case — a pyro with the Rainblower and a Balloonicorn — and summing gives 2,
    /// which is the HALLOWEEN bit and grants Pyrovision to nobody.
    ///
    /// **A SourceTV recording has no recorder**, so the answer is none, and every filtered item is
    /// hidden. That is what a live spectator sees: `tf_spectate_pyrovision` defaults to 0.
    /// </remarks>
    public static int ViewerFlags(
        IReadOnlyList<SceneProp> scene, int? recorder, Func<SceneProp, int> grants)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(grants);

        if (recorder is not { } watching)
        {
            return 0;
        }

        int flags = 0;

        foreach (SceneProp prop in scene)
        {
            // **Asked of the PROP rather than of its definition index**, because an attribute can
            // arrive on the wire as well as from the schema — `IterateAttributes` has four branches
            // and B234 decodes the networked ones. Keying this on the item would silently consult
            // only the schema half.
            if (prop.ItemDefinitionIndex is not null
                && (prop.OwnedBy ?? prop.AttachedTo) == watching)
            {
                flags |= grants(prop);
            }
        }

        return flags;
    }

    /// <summary>Whether this prop is one the watcher cannot see.</summary>
    private static bool Hidden(SceneProp prop, int viewer, Func<int, int> required)
    {
        if (prop.ItemDefinitionIndex is not { } item)
        {
            return false;
        }

        int wanted = required(item);

        // `if ( nVisionFilterFlags != 0 )` guards the whole test (`econ_entity.cpp:1821`), so an
        // item asking for nothing is never hidden — which is all but 23 of them.
        return wanted != 0 && (viewer & wanted) != wanted;
    }
}
