using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Resolving an item definition index to the model in a player's hands.
/// </summary>
/// <remarks>
/// The order is <c>CEconItemView::GetPlayerDisplayModel</c>'s: a per-class model if the definition
/// has one for that class, otherwise the base model, and either may be inherited from a prefab.
/// See <see cref="ItemSchemaConformanceTests"/> for the citations and for what the shipped file
/// really contains.
/// </remarks>
public sealed class ItemSchemaTests
{
    /// <summary>A schema shaped like the real one, small enough to reason about.</summary>
    private const string Schema = """
        "items_game"
        {
            "prefabs"
            {
                "weapon_scattergun"
                {
                    "attach_to_hands" "1"
                    "model_player" "models/weapons/c_models/c_scattergun.mdl"
                }
                "base_melee"
                {
                    "attach_to_hands" "1"
                }
                "weapon_shovel"
                {
                    "prefab" "base_melee"
                    "model_player" "models/weapons/c_models/c_shovel.mdl"
                }
                "hat"
                {
                    "model_player" "models/player/items/all/hat.mdl"
                    "model_player_per_class"
                    {
                        "scout" "models/player/items/scout/hat_scout.mdl"
                        "spy"   "models/player/items/spy/hat_spy.mdl"
                    }
                }
                "cap"
                {
                    "model_player_per_class"
                    {
                        "basename" "models/player/items/%s/%s_cap.mdl"
                    }
                }
                "badge"
                {
                    "model_player_per_class"
                    {
                        "basename" "models/player/items/%s/badge_%s.mdl"
                        "spy" "models/player/items/spy/special_badge.mdl"
                    }
                }
            }
            "items"
            {
                "13"
                {
                    "name" "TF_WEAPON_SCATTERGUN"
                    "prefab" "weapon_scattergun"
                }
                "6"
                {
                    "name" "TF_WEAPON_SHOVEL"
                    "prefab" "weapon_shovel"
                }
                "99"
                {
                    "name" "A_HAT"
                    "prefab" "hat"
                }
                "500"
                {
                    "name" "SOMETHING_ITS_OWN"
                    "model_player" "models/weapons/c_models/c_special.mdl"
                }
                "261"
                {
                    "name" "MANN_CO_CAP"
                    "prefab" "cap"
                }
                "262"
                {
                    "name" "A_BADGE"
                    "prefab" "badge"
                }
            }
        }
        """;

    [Test]
    public void ModelFor_AStockWeapon_ComesFromItsPrefab()
    {
        // The case that is most of a demo: a stock weapon's definition is a name and a prefab, and
        // the model is one level up the chain.
        Read().ModelFor(13, playerClass: 1)
            .ShouldBe("models/weapons/c_models/c_scattergun.mdl");
    }

    [Test]
    public void ModelFor_APrefabThatInheritsFromAnother_FollowsTheChain()
    {
        // Prefabs nest — a weapon prefab commonly sits on a `base_melee` or `base_weapon`. A
        // resolver that looked one level up would answer for the scattergun and miss the shovel.
        Read().ModelFor(6, playerClass: 3).ShouldBe("models/weapons/c_models/c_shovel.mdl");
    }

    [Test]
    public void ModelFor_AnItemWithItsOwnModel_DoesNotNeedAPrefab()
    {
        Read().ModelFor(500, playerClass: 1).ShouldBe("models/weapons/c_models/c_special.mdl");
    }

    [Test]
    public void ModelFor_AClassWithItsOwnModel_TakesThatOverTheBase()
    {
        // `model_player_per_class` wins over `model_player` — tf_item_schema.cpp:1058. A cosmetic
        // is the usual case and gets this wrong invisibly: the base model is a real model, so the
        // scout wears the spy's hat and nothing reports anything.
        ItemSchema schema = Read();

        schema.ModelFor(99, playerClass: 1).ShouldBe("models/player/items/scout/hat_scout.mdl");
        schema.ModelFor(99, playerClass: 8).ShouldBe("models/player/items/spy/hat_spy.mdl");
    }

    [Test]
    public void ModelFor_AClassWithNoEntryOfItsOwn_FallsBackToTheBase()
    {
        // **The control for the test above.** Without it, a resolver that ignored the per-class
        // block entirely would still pass one of the two assertions there by luck.
        Read().ModelFor(99, playerClass: 4).ShouldBe("models/player/items/all/hat.mdl");
    }

    [Test]
    public void ModelFor_AnItemTheSchemaDoesNotHave_IsNothing()
    {
        // Null rather than a placeholder path: a caller can then say "no model" rather than
        // reporting a missing asset for a model that was never named.
        Read().ModelFor(4242, playerClass: 1).ShouldBeNull();
    }

    [Test]
    public void AttachesToHands_IsInheritedFromThePrefabChain()
    {
        // ShouldAttachToHands, which decides whether the weapon is a SEPARATE model parented to the
        // arms or whether the viewmodel itself is the weapon. The shovel inherits it from
        // `base_melee` two levels up.
        ItemSchema schema = Read();

        schema.AttachesToHands(13).ShouldBeTrue();
        schema.AttachesToHands(6).ShouldBeTrue();
        schema.AttachesToHands(99).ShouldBeFalse("a hat declares no attach_to_hands");
    }

    [Test]
    public void ModelFor_APerClassBasename_SubstitutesTheClassName()
    {
        // **`model_player_per_class` has two forms and only one was read.** Besides a map of class
        // to path it may carry a single `basename` with `%s` placeholders, which
        // `InitPerClassStringArray` (`tf_item_schema.cpp:489`) expands per class:
        //
        //     fmtStr.sprintf( pszBaseName, ClassUsability[i], ClassUsability[i], ClassUsability[i] );
        //
        // Item 261 is the Mann Co. Cap, and it is the real shape: no `model_player` at all, only a
        // basename. Reading one form and not the other resolved 47 of a real match's cosmetics to
        // nothing at all.
        Read().ModelFor(261, playerClass: 1)
            .ShouldBe("models/player/items/scout/scout_cap.mdl");
    }

    [Test]
    public void ModelFor_APerClassBasenameForTheDemoman_SaysDemoNotDemoman()
    {
        // **Valve's own special case, and it is in the source with an apology attached** — the
        // usability string is "Demoman" and the model files say `demo`, so `InitPerClassStringArray`
        // forces it: *"If this class is the TF_CLASS_DEMOMAN, just force 'demo'"*
        // (`tf_item_schema.cpp:526`). Without it every demoman cosmetic with a basename names a
        // path that does not exist, which is indistinguishable on screen from naming none.
        Read().ModelFor(261, playerClass: 4)
            .ShouldBe("models/player/items/demo/demo_cap.mdl");
    }

    [Test]
    public void ModelFor_AClassNamedBesideABasename_PrefersItsOwnEntry()
    {
        // The order inside the block: *"If there's a class specific string defined, use that"*,
        // and only otherwise the basename. Item 262 names one for the spy and leaves the rest to
        // the pattern.
        Read().ModelFor(262, playerClass: 8)
            .ShouldBe("models/player/items/spy/special_badge.mdl");
    }

    [Test]
    public void ModelFor_AClassNotNamedBesideABasename_TakesThePattern()
    {
        // **The control for the test above**, and the pair is what makes either meaningful: a
        // reader that took the basename for everyone would pass the first assertion of this pair
        // and fail this one's partner, and a reader that took only explicit entries would do the
        // reverse.
        Read().ModelFor(262, playerClass: 1)
            .ShouldBe("models/player/items/scout/badge_scout.mdl");
    }

    [Test]
    public void ModelFor_AnUnknownClassOnAPerClassItem_TakesTheFirstClassesModel()
    {
        // **Slot zero is not empty, it is a copy** — `InitPerClassStringArray` ends each iteration
        // with `if ( outputArray[0] == NULL ) outputArray[0] = outputArray[i]`, so
        // `TF_CLASS_UNDEFINED` answers with whichever class resolved first. `CEconItemView::
        // GetPlayerDisplayModel` then returns it before ever reaching the base model
        // (`econ_item_view.cpp:962`).
        //
        // This is not academic: a prop whose owner is not a player the moment knows about resolves
        // with no class, and this project answered nothing for every one of them.
        Read().ModelFor(261, playerClass: 0)
            .ShouldBe("models/player/items/scout/scout_cap.mdl");
    }

    [Test]
    public void ModelFor_AnUnknownClassOnAnItemWithNoPerClassBlock_IsStillTheBase()
    {
        // The control on that: slot zero only carries a per-class answer when there IS one. An
        // ordinary weapon must keep resolving to `model_player` when the class is unknown, which
        // is the common case for a weapon whose owner has left.
        Read().ModelFor(500, playerClass: 0)
            .ShouldBe("models/weapons/c_models/c_special.mdl");
    }

    private static ItemSchema Read() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));
}
