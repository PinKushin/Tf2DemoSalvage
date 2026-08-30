using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A weapon whose model the wire never carried is named from its item.
/// </summary>
/// <remarks>
/// **<c>CEconEntity::SetModel</c> resolves through <c>items_game.txt</c>, not through the wire** —
/// <c>pItem-&gt;GetPlayerDisplayModel( iClass, team )</c>, <c>econ_entity.cpp:1167</c>. Measured on
/// `cp_fulgur`, every <c>CWeaponMedigun</c> networks neither <c>m_nModelIndex</c> nor
/// <c>m_iWorldModelIndex</c> and every one states item 211, the stock Medi Gun; a minigun does the
/// same. Owner's report: *"mediguns still are not drawing on other players too"*.
///
/// The resolver itself is <see cref="WeaponModels"/>, which needs an installed game. What is tested
/// here is the STEP: which props it is asked about, what it is asked, and what is done with the
/// answer — so the resolution is passed in and the test needs no `items_game.txt`.
/// </remarks>
public sealed class WeaponPropModelsTests
{
    /// <summary>The stock Medi Gun.</summary>
    private const int MediGun = 211;

    private const string MedigunModel = "models/weapons/c_models/c_medigun/c_medigun.mdl";

    [Test]
    public void Resolve_APropWithNoModelButAnItem_TakesTheItemsModel()
    {
        // The reported case.
        List<SceneProp> drawn = [Weapon(model: "", item: MediGun, owner: 3)];

        new WeaponPropModels().Resolve(drawn, [], (_, _, _) => MedigunModel);

        drawn[0].ModelPath.ShouldBe(MedigunModel);
    }

    [Test]
    public void Resolve_APropWhoseItemNamesADifferentModel_TakesTheItems()
    {
        // **Valve's rule, and this shipped the narrower one first.**
        // `CEconEntity::UpdateModelToClass` (`econ_entity.cpp:411`) replaces the model whenever the
        // ITEM names something different — it does not merely fill in a blank:
        //
        //     if ( pszModel && pszModel[0] )
        //         if ( V_stricmp( STRING( GetModelName() ), pszModel ) != 0 )
        //             SetModel( pszModel );
        //
        // The first version resolved only when the wire carried nothing, which agreed with Valve on
        // every weapon measured and diverges when an owner's CLASS changes — the engine re-resolves
        // per class and the narrow rule keeps the stale value.
        List<SceneProp> drawn = [Weapon(model: "models/weapons/w_rocket.mdl", item: 513, owner: 3)];

        new WeaponPropModels().Resolve(drawn, [], (_, _, _) => MedigunModel);

        drawn[0].ModelPath.ShouldBe(MedigunModel, "the item is what the engine draws from");
    }

    [Test]
    public void Resolve_AnItemThatNamesNothing_KeepsTheWiresModel()
    {
        // **The other half of Valve's own condition**, and it is what makes the rule above safe:
        // `if ( pszModel && pszModel[0] )` — an item that names nothing leaves the entity drawing
        // what it already had. The lookup answers null on a machine with no game installed, which
        // is every CI run, so without this the wider rule would blank every weapon there.
        List<SceneProp> drawn = [Weapon(model: "models/weapons/w_rocket.mdl", item: 513, owner: 3)];

        new WeaponPropModels().Resolve(drawn, [], (_, _, _) => null);

        drawn[0].ModelPath.ShouldBe("models/weapons/w_rocket.mdl", "nothing named it, so nothing changed");
    }

    [Test]
    public void Resolve_APropWithNoModelAndNoItem_IsLeftAlone()
    {
        // A player's track has a pose and no model on purpose, and nothing could name it. Without
        // this, "resolves weapons" and "assigns a model to anything blank" are the same observation.
        List<SceneProp> drawn = [Weapon(model: "", item: null, owner: 3)];

        new WeaponPropModels().Resolve(drawn, [], (_, _, _) => MedigunModel);

        drawn[0].ModelPath.ShouldBe(string.Empty);
    }

    [Test]
    public void Resolve_AnItemThatNamesNothing_LeavesThePropBlank()
    {
        // A resolver that cannot answer must not be allowed to blank or corrupt the prop — the
        // viewer runs without an installed game in CI, where every lookup returns null.
        List<SceneProp> drawn = [Weapon(model: "", item: MediGun, owner: 3)];

        new WeaponPropModels().Resolve(drawn, [], (_, _, _) => null);

        drawn[0].ModelPath.ShouldBe(string.Empty);
    }

    [Test]
    public void Resolve_AWeaponHeldByAPlayer_AsksWithThatPlayersClass()
    {
        // **`GetPlayerDisplayModel( iClass, team )` takes a class, and it changes the answer.** The
        // shotgun is the case: soldier, pyro, heavy and engineer share one item and not one model.
        // Passing the owner's class is what makes the lookup the engine's rather than a guess.
        List<SceneProp> drawn = [Weapon(model: "", item: MediGun, owner: 7)];

        int? asked = null;

        new WeaponPropModels().Resolve(
            drawn,
            [Player(entityIndex: 7, playerClass: 5)],
            (_, _, forClass) =>
            {
                asked = forClass;
                return MedigunModel;
            });

        asked.ShouldBe(5, "the medic holding it decides which model_player is right");
    }

    [Test]
    public void Resolve_AWeaponWhoseOwnerIsNotAPlayerHere_AsksWithNoClass()
    {
        // The control on the same lookup: an owner this moment does not know about must produce a
        // null class rather than somebody else's. Reading a missing player as class zero HERE would
        // be indistinguishable from a scout holding it.
        List<SceneProp> drawn = [Weapon(model: "", item: MediGun, owner: 7)];

        int? asked = 99;

        new WeaponPropModels().Resolve(
            drawn,
            [Player(entityIndex: 8, playerClass: 5)],
            (_, _, forClass) =>
            {
                asked = forClass;
                return MedigunModel;
            });

        asked.ShouldBeNull();
    }

    [Test]
    public void Resolve_TheSameWeaponOnASecondFrame_IsNotLookedUpAgain()
    {
        // **A performance regression this shipped, measured on the owner's machine.** The drawlist
        // phase went from a 2.9 ms mean to 46.6 ms and slow moments from one to 1,201, because 122
        // of `cp_fulgur`'s 1,158 prop tracks await an item lookup and every one was re-resolved on
        // every frame it was alive.
        //
        // **The key is Valve's own invalidation rule, not a guess.** `UpdateModelToClass` is called
        // from `OnOwnerClassChange` and `ReapplyProvision` — the model is re-derived when the item
        // or the owner's class changes, and at no other time.
        WeaponPropModels models = new();

        int lookups = 0;

        for (int frame = 0; frame < 5; frame++)
        {
            List<SceneProp> drawn = [Weapon(model: "", item: MediGun, owner: 7)];

            models.Resolve(
                drawn,
                [Player(entityIndex: 7, playerClass: 5)],
                (_, _, _) =>
                {
                    lookups++;
                    return MedigunModel;
                });

            drawn[0].ModelPath.ShouldBe(MedigunModel, "every frame still gets its answer");
        }

        lookups.ShouldBe(1, "the answer cannot change while item and owner class do not");
    }

    [Test]
    public void Resolve_ADifferentPlayerClassHoldingTheSameItem_IsLookedUpSeparately()
    {
        // **The control, and it is what makes the cache key correct rather than merely small.** A
        // shotgun is one item and four models — soldier, pyro, heavy and engineer — so a cache keyed
        // on the item alone would hand the engineer the soldier's model. Caching by the whole tuple
        // is the difference between an optimisation and a bug.
        WeaponPropModels models = new();

        List<int?> asked = [];

        foreach (int playerClass in (int[])[3, 9])
        {
            List<SceneProp> drawn = [Weapon(model: "", item: MediGun, owner: 7)];

            models.Resolve(
                drawn,
                [Player(entityIndex: 7, playerClass: playerClass)],
                (_, _, forClass) =>
                {
                    asked.Add(forClass);
                    return MedigunModel;
                });
        }

        asked.ShouldBe([3, 9], "a different class is a different question");
    }

    private static SceneProp Weapon(string model, int? item, int owner) =>
        new(
            EntityIndex: 40,
            ModelPath: model,
            Kind: SceneModelKind.Studio,
            Pose: default,
            AttachedTo: owner,
            AttachmentPoint: null,
            OwnedBy: owner,
            WeaponState: null,
            BoneMerged: true,
            ItemDefinitionIndex: item,
            ClassName: "CWeaponMedigun");

    private static ScenePlayer Player(int entityIndex, int playerClass) =>
        new(entityIndex, 0f, 0f, 0f, Team: 3, Health: 150, PlayerClass: playerClass);
}
