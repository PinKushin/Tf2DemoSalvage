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
/// **The item WINS whenever it names something different**, which is Valve's rule and not the
/// narrower one this shipped first. `CEconEntity::UpdateModelToClass` (`econ_entity.cpp:411`)
/// replaces the model on a difference rather than filling in a blank, and the networked index is
/// only the fallback for an item that names nothing.
///
/// The first version resolved only when the wire carried nothing. It agreed with Valve on every
/// weapon in the corpus — the server sets `m_iWorldModelIndex` from the same item — and diverged
/// when an owner's CLASS changes, since the engine re-resolves per class and the narrow rule keeps
/// the stale value. It was recorded as a known divergence and then closed, because a divergence
/// left open on the grounds that it has not bitten yet is the shape of every bug in this file's
/// history.
/// </remarks>
// Public so a probe can measure the REAL resolution rather than a second copy of the rule written
// in the probe. A probe that reimplemented "the item wins, the wire is the fallback" would agree
// with whoever wrote it, not with what the viewer does — which is the whole failure
// `measure-the-output-not-the-capability` names.
public sealed class WeaponPropModels
{
    /// <summary>What each (item, class, player class) resolved to last time it was asked.</summary>
    /// <remarks>
    /// **This runs every frame, and without the cache it cost 44 ms of one** — measured on the
    /// owner's machine, drawlist mean 2.9 ms before and 46.6 ms after, 1,201 slow moments against
    /// one. 122 of `cp_fulgur`'s 1,158 prop tracks await an item lookup, and every one was
    /// re-resolved on every frame it was alive.
    ///
    /// **The key is Valve's own invalidation rule, not a guess at one.** `UpdateModelToClass` is
    /// called from `OnOwnerClassChange` and `ReapplyProvision` (`econ_entity.cpp:288`, `:358`) — the
    /// model is re-derived when the ITEM or the owner's CLASS changes and at no other time, which is
    /// exactly this tuple. A null answer is cached too: a game that is not installed answers null
    /// for every item, for ever, and re-asking is the same wasted work.
    /// </remarks>
    private readonly Dictionary<(int Item, string Class, int PlayerClass), string?> _resolved = [];

    /// <summary>Fills in the model of any prop whose item names one.</summary>
    /// <param name="drawn">The props for this moment, edited in place.</param>
    /// <param name="players">Players, for the owner's class — models differ per class.</param>
    /// <param name="model">
    /// Resolves item, entity class and player class to a model path.
    /// <see cref="WeaponModels.For(int?, string?, int?)"/> in production.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void Resolve(
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

            if (prop.ItemDefinitionIndex is null)
            {
                continue;
            }

            // **The owner's class, because `GetPlayerDisplayModel` takes one.** A multi-class item
            // has a different `model_player` per class, and the shotgun is the obvious case: soldier,
            // pyro, heavy and engineer share an item and not a model. Null when the owner is not a
            // player this moment knows about, which `WeaponModels.For` reads as class zero.
            int? playerClass = ClassOf(players, prop.AttachedTo ?? prop.OwnedBy);

            (int, string, int) key =
                (prop.ItemDefinitionIndex.Value, prop.ClassName, playerClass ?? 0);

            if (!_resolved.TryGetValue(key, out string? named))
            {
                named = model(prop.ItemDefinitionIndex, prop.ClassName, playerClass);
                _resolved[key] = named;
            }

            // **The item WINS whenever it names something different**, which is Valve's rule
            // rather than the narrower "fill in when the wire said nothing" this shipped first.
            // `CEconEntity::UpdateModelToClass`, `econ_entity.cpp:411`:
            //
            //     pszModel = pItem->GetPlayerDisplayModel( m_iOldOwnerClass, nTeam );
            //     if ( pszModel && pszModel[0] )
            //         if ( V_stricmp( STRING( GetModelName() ), pszModel ) != 0 )
            //             SetModel( pszModel );
            //
            // The networked index stays as the FALLBACK, which is the other half of the same
            // condition: `if ( pszModel && pszModel[0] )` means an item that names nothing leaves
            // the entity drawing what it already had. That matters here beyond parity — the lookup
            // answers null on a machine with no game installed, which is every CI run.
            if (named is { Length: > 0 } && !string.Equals(named, prop.ModelPath, StringComparison.Ordinal))
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
