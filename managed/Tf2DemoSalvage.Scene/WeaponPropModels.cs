using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Names the weapons whose model the wire never carried.
/// </summary>
/// <remarks>
/// **A weapon's model comes from its ITEM, and the engine never reads the networked index at all.**
/// <c>CEconEntity::SetModel</c> resolves <c>pItem-&gt;GetPlayerDisplayModel( iClass, team )</c> —
/// <c>model_player</c> from <c>items_game.txt</c>, <c>econ_entity.cpp:1167</c>. This project already
/// wrote that down, and already implements it in <see cref="WeaponModels"/>, for the viewmodel and
/// for the followed player. The weapon entities OTHER players carry took
/// <c>WorldModelIndex() ?? ModelIndex()</c> and nothing else.
///
/// **Which is fine until a weapon networks neither.** Measured on `cp_fulgur`, every weapon with an
/// owner:
///
/// <code>
///   CTFRocketLauncher model  996 worldmodel  426 item   513
///   CTFFlameThrower   model none worldmodel  225 item    40
///   CTFMinigun        model none worldmodel  393 item   424
///   CWeaponMedigun    model none worldmodel none item   211
///   CWeaponMedigun    model none worldmodel none item   211
///   CTFMinigun        model none worldmodel none item 15123
/// </code>
///
/// **211 is the stock Medi Gun**, so nothing was missing from the recording — the number that names
/// the model was there the whole time and no one asked it. Owner's report: *"mediguns still are not
/// drawing on other players too, but the flamethrower, and it looks like everything else, draws"*,
/// and it is not medigun-specific: a minigun does it too.
///
/// **Only a prop with NO model is touched**, which is the majority-case control. Rocket launchers,
/// flamethrowers and most miniguns network a world model and keep it; re-resolving them would
/// replace a measured index with a lookup and is a chance to be wrong about weapons that work.
/// </remarks>
internal static class WeaponPropModels
{
    /// <summary>Fills in the model of any prop whose item names one.</summary>
    /// <param name="drawn">The props for this moment, edited in place.</param>
    /// <param name="players">Players, for the owner's class — models differ per class.</param>
    /// <param name="model">
    /// Resolves item, entity class and player class to a model path.
    /// <see cref="WeaponModels.For(int?, string?, int?)"/> in production.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void Resolve(
        IList<SceneProp> drawn,
        IReadOnlyList<ScenePlayer> players,
        Func<int?, string?, int?, string?> model)
    {
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(model);

        for (int index = 0; index < drawn.Count; index++)
        {
            SceneProp prop = drawn[index];

            if (prop.ModelPath.Length != 0 || prop.ItemDefinitionIndex is null)
            {
                continue;
            }

            // **The owner's class, because `GetPlayerDisplayModel` takes one.** A multi-class item
            // has a different `model_player` per class, and the shotgun is the obvious case: soldier,
            // pyro, heavy and engineer share an item and not a model. Null when the owner is not a
            // player this moment knows about, which `WeaponModels.For` reads as class zero.
            int? playerClass = ClassOf(players, prop.AttachedTo ?? prop.OwnedBy);

            if (model(prop.ItemDefinitionIndex, prop.ClassName, playerClass)
                is { Length: > 0 } named)
            {
                drawn[index] = prop with { ModelPath = named };
            }
        }
    }

    /// <summary>Which class the entity holding this weapon plays, when it is a known player.</summary>
    private static int? ClassOf(IReadOnlyList<ScenePlayer> players, int? entityIndex)
    {
        if (entityIndex is not { } owner)
        {
            return null;
        }

        // A linear walk over at most a couple of dozen players, called only for the props that have
        // no model — which in a real match is a handful. A dictionary here would allocate every
        // frame to answer a question asked three times.
        foreach (ScenePlayer player in players)
        {
            if (player.EntityIndex == owner)
            {
                return player.PlayerClass;
            }
        }

        return null;
    }
}
