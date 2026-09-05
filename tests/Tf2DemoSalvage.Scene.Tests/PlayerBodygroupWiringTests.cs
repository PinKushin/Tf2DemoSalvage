using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A player's equipped items hide the parts of them they replace (B352).
/// </summary>
/// <remarks>
/// **This is how a hat removes the head it sits on**, and until B352 nothing did it: the only body
/// number a player prop ever carried was the spy's mask, so every hat sat on top of the hair it is
/// modelled to replace and every headset drew through the one on the cosmetic.
///
/// `CTFPlayerShared::RecalculatePlayerBodygroups` (`tf_player_shared.cpp:13693`) clears the number
/// and rebuilds it from the equipped set in three passes:
///
/// <code>
///   m_pOuter-&gt;m_nBody = 0;
///   CTFWeaponBase::UpdateWeaponBodyGroups( m_pOuter, false );
///   CEconWearable::UpdateWearableBodyGroups( m_pOuter );
///   CTFWeaponBase::UpdateWeaponBodyGroups( m_pOuter, true );
/// </code>
///
/// **The three passes collapse into one here, and the reason is arithmetic rather than
/// convenience.** Both callers pass a state of 1 — `pWpn-&gt;UpdateBodygroups( pPlayer, 1 )`
/// (`tf_weaponbase.cpp:6229`) and `nVisibleState = 1` (`econ_wearable.cpp:317`, dropping to 0 only
/// for a pyro-vision-filtered item, a condition no demo carries) — and
/// `CEconEntity::UpdateBodygroups` applies an entry only when its value equals that state
/// (`econ_entity.cpp:2046`). So every entry that can apply sets its group to 1, no two applied
/// entries can disagree, and the order between the passes cannot change the result.
///
/// **What the passes DO decide is the deployed-only weapon**, which is why that survives as its own
/// condition: an item declaring `hide_bodygroups_deployed_only` is handled in the third pass, which
/// skips it unless `pPlayer-&gt;GetActiveWeapon() == pWpn`. Eight shipped items declare it and all
/// eight are weapons — the Fists of Steel, the KGB, the Holiday Punch and the Short Circuit among
/// them — which is what makes the Fists of Steel's enormous hands appear only while they are out.
/// </remarks>
public sealed class PlayerBodygroupWiringTests
{
    [Test]
    public void Add_APlayerWearingAHat_HidesTheHatBodygroup()
    {
        List<SceneProp> drawn = [Worn(Hat)];

        PlayerProps.Add([Scout()], drawn, new Wardrobe(), Bodygroup.Instance);

        // `hat` is part 0 on the fixture model, so its place is 1 and state 1 reads as 1.
        Player(drawn).Pose.Body.ShouldBe(1);
    }

    /// <remarks>
    /// The control. Without it "always sets 1" passes the test above, and the fixture's
    /// <see cref="Scout"/> is not a spy, so nothing else can write a body number.
    /// </remarks>
    [Test]
    public void Add_APlayerWearingNothing_HasBodyZero()
    {
        List<SceneProp> drawn = [];

        PlayerProps.Add([Scout()], drawn, new Wardrobe(), Bodygroup.Instance);

        Player(drawn).Pose.Body.ShouldBe(0);
    }

    /// <remarks>
    /// **The bystander.** One player wearing a hat and one not is the only condition under which
    /// "applied it to the wearer" and "applied it to everybody" predict different observations —
    /// with a single player in the list they are the same reading.
    /// </remarks>
    [Test]
    public void Add_AHatOnOnePlayer_LeavesTheOtherBare()
    {
        List<SceneProp> drawn = [Worn(Hat, wearer: 3)];

        PlayerProps.Add([Scout(), Scout() with { EntityIndex = 4 }], drawn, new Wardrobe(), Bodygroup.Instance);

        Player(drawn, entity: 3).Pose.Body.ShouldBe(1);
        Player(drawn, entity: 4).Pose.Body.ShouldBe(0);
    }

    /// <remarks>
    /// **Two items naming the same part is the ordinary case, not an edge**: 457 shipped items hide
    /// `hat` and a player wears several cosmetics at once, so a hat and a misc that both hide it
    /// meet on nearly every modern player.
    ///
    /// **Summing the contributions gives 2 and is wrong.** The parts share one integer as digits of
    /// a mixed-radix number — `body - iCurrent * base + iValue * base`, `shared/animation.cpp:863`
    /// — so setting `hat` twice must land on 1. At 2 the digit runs past the part's alternative
    /// count and, on a model with more parts, carries into the NEXT part's digit, which draws as a
    /// different piece quietly missing.
    /// </remarks>
    [Test]
    public void Add_TwoItemsHidingOnePart_SetItOnceRatherThanTwice()
    {
        List<SceneProp> drawn = [Worn(Hat), Worn(HatAgain, entity: 21)];

        PlayerProps.Add([Scout()], drawn, new Wardrobe(), Bodygroup.Instance);

        Player(drawn).Pose.Body.ShouldBe(1);
    }

    /// <remarks>
    /// Two different parts DO compose, and the arithmetic is what says which: `headphones` is part
    /// 1 with a place of 2, so a hat that hides both reads as 3 rather than as either alone.
    /// </remarks>
    [Test]
    public void Add_AnItemHidingTwoParts_SetsBoth()
    {
        List<SceneProp> drawn = [Worn(HatAndHeadphones)];

        PlayerProps.Add([Scout()], drawn, new Wardrobe(), Bodygroup.Instance);

        Player(drawn).Pose.Body.ShouldBe(3);
    }

    /// <remarks>
    /// **`if ( iBody != iState ) continue;`** (`econ_entity.cpp:2046`). An entry valued 0 exists to
    /// put a part BACK when the item is hidden by a vision filter, a state a demo never reaches, so
    /// reading the pair as "set this group to this number" applies it and removes a part the engine
    /// leaves alone. Eight shipped entries are valued 0 and 1,044 are valued 1.
    /// </remarks>
    [Test]
    public void Add_AnEntryValuedZero_IsNotApplied()
    {
        List<SceneProp> drawn = [Worn(PutsTheHatBack)];

        PlayerProps.Add([Scout()], drawn, new Wardrobe(), Bodygroup.Instance);

        Player(drawn).Pose.Body.ShouldBe(0);
    }

    /// <remarks>
    /// `if ( bHideBodygroupsDeployedOnly &amp;&amp; pPlayer-&gt;GetActiveWeapon() != pWpn ) continue;`
    /// (`tf_weaponbase.cpp:6226`). A holstered Fists of Steel leaves the hands alone.
    /// </remarks>
    [Test]
    public void Add_ADeployedOnlyWeaponThatIsStowed_HidesNothing()
    {
        List<SceneProp> drawn = [Worn(FistsOfSteel, entity: 30)];

        PlayerProps.Add([Scout() with { ActiveWeapon = 31 }], drawn, new Wardrobe(), Bodygroup.Instance);

        Player(drawn).Pose.Body.ShouldBe(0);
    }

    /// <remarks>
    /// The other side of the same branch, and the one that makes the test above falsifiable:
    /// without it, "never apply a deployed-only item" passes.
    /// </remarks>
    [Test]
    public void Add_ADeployedOnlyWeaponThatIsActive_HidesItsParts()
    {
        List<SceneProp> drawn = [Worn(FistsOfSteel, entity: 30)];

        PlayerProps.Add([Scout() with { ActiveWeapon = 30 }], drawn, new Wardrobe(), Bodygroup.Instance);

        Player(drawn).Pose.Body.ShouldBe(1);
    }

    /// <remarks>
    /// **The spy's mask is written by a different function and must survive.**
    /// `C_TFPlayer::ValidateModelIndex` (`c_tf_player.cpp:9024`) sets it, and it runs AFTER the
    /// rebuild within one frame: `C_TFPlayer::DrawModel` calls `RecalcBodygroupsIfDirty()` at
    /// `c_tf_player.cpp:6935` and then falls through to `C_BaseAnimating::DrawModel`, which calls
    /// `ValidateModelIndex()` at `c_baseanimating.cpp:3195` under `TF_CLIENT_DLL`. So the mask is
    /// applied over the items rather than instead of them — 8 for the mask plus 1 for the hat.
    /// </remarks>
    [Test]
    public void Add_ADisguisedSpyWearingAHat_KeepsTheMaskAndTheHat()
    {
        List<SceneProp> drawn = [Worn(Hat)];

        PlayerProps.Add([DisguisedSpy()], drawn, new Wardrobe(), Bodygroup.Instance);

        Player(drawn).Pose.Body.ShouldBe(9);
    }

    /// <remarks>
    /// The control for the pair above: the mask alone is 8, so a hat that failed to apply would
    /// still read as "the spy wears a mask" without this.
    /// </remarks>
    [Test]
    public void Add_ADisguisedSpyWearingNothing_IsTheMaskAlone()
    {
        List<SceneProp> drawn = [];

        PlayerProps.Add([DisguisedSpy()], drawn, new Wardrobe(), Bodygroup.Instance);

        Player(drawn).Pose.Body.ShouldBe(8);
    }

    /// <remarks>
    /// **A prop that belongs to nobody must not dress anybody.** Map decorations and projectiles
    /// share the draw list with worn items, and the engine reads a maintained per-player list
    /// rather than the scene, so the owner test is what stands in for that list.
    /// </remarks>
    [Test]
    public void Add_AnUnownedPropCarryingAnItemIndex_DressesNobody()
    {
        List<SceneProp> drawn = [Worn(Hat) with { AttachedTo = null, OwnedBy = null }];

        PlayerProps.Add([Scout()], drawn, new Wardrobe(), Bodygroup.Instance);

        Player(drawn).Pose.Body.ShouldBe(0);
    }

    /// <remarks>
    /// **A weapon is owned rather than attached**, so both halves of the chain
    /// `prop.OwnedBy ?? prop.AttachedTo` have to reach the wearer — the same chain the paint and
    /// the burn level already resolve through (`MomentScene.cs:305`).
    /// </remarks>
    [Test]
    public void Add_AnItemOwnedRatherThanAttached_StillDressesItsOwner()
    {
        List<SceneProp> drawn = [Worn(Hat) with { AttachedTo = null, OwnedBy = 3 }];

        PlayerProps.Add([Scout()], drawn, new Wardrobe(), Bodygroup.Instance);

        Player(drawn).Pose.Body.ShouldBe(1);
    }

    /// <remarks>
    /// **`wm_bodygroup_override`, the one arm that addresses a part by NUMBER** (B353,
    /// `econ_entity.cpp:2083`). Two shipped items declare it — the Purity Fist and the Short
    /// Circuit — and both replace a hand with a robot arm, so what is switched is the wearer's own
    /// limb rather than a cosmetic slot.
    ///
    /// Part 2 here is `shoes_socks` at place 4, and state 1 therefore reads as 4.
    /// </remarks>
    [Test]
    public void Add_AnItemWithAWorldModelOverride_SetsThatPartByNumber()
    {
        List<SceneProp> drawn = [Worn(RobotArm)];

        PlayerProps.Add([Scout()], drawn, new Wardrobe(), Bodygroup.Instance);

        Player(drawn).Pose.Body.ShouldBe(4);
    }

    /// <remarks>
    /// **`if ( iBodyOverride &gt; -1 &amp;&amp; iBodyStateOverride &gt; -1 )`** — half a declaration does nothing.
    ///
    /// **The item ALSO hides `hat`, and that is what makes this test able to fail.** The first
    /// version wore an item with the half-declaration and nothing else, and asserted a body of 0 —
    /// which it could not help but observe. `SetBodygroup` returns the body unchanged for a negative
    /// value in our code and in Valve's (`shared/animation.cpp:863` returns early for an
    /// out-of-range value), so removing the outer guard entirely changes NOTHING observable; a
    /// sabotage confirmed the test stayed green with the clause deleted.
    ///
    /// **The mistake the assertion actually has to catch is the state defaulting to 0**, and setting
    /// a part to 0 is only visible from a body that is not already 0. So the item hides `hat` — part
    /// 0 here, so a correct read leaves 1 — and a reader that treated the missing state as 0 would
    /// put the part straight back and read 0. Same item, one extra field, and now the two readings
    /// disagree.
    /// </remarks>
    [Test]
    public void Add_AnItemWhoseOverrideNamesNoState_KeepsWhatItsNamesDid()
    {
        List<SceneProp> drawn = [Worn(HalfAnOverride)];

        PlayerProps.Add([Scout()], drawn, new Wardrobe(), Bodygroup.Instance);

        Player(drawn).Pose.Body.ShouldBe(1);
    }

    /// <remarks>
    /// **The override runs after the named entries on the same item**, which is the engine's order
    /// within `UpdateBodygroups` — names first (`:2044`), then the override (`:2083`). They address
    /// different parts here, so both survive: `hat` at place 1 and `shoes_socks` at place 4.
    /// </remarks>
    [Test]
    public void Add_AnItemWithBothNamesAndAnOverride_AppliesBoth()
    {
        List<SceneProp> drawn = [Worn(RobotArmWithAHat)];

        PlayerProps.Add([Scout()], drawn, new Wardrobe(), Bodygroup.Instance);

        Player(drawn).Pose.Body.ShouldBe(5);
    }

    private const int ScoutClass = 1;
    private const int SpyClass = 8;

    private const int Hat = 100;
    private const int HatAgain = 101;
    private const int HatAndHeadphones = 102;
    private const int PutsTheHatBack = 103;
    private const int FistsOfSteel = 104;
    private const int RobotArm = 105;
    private const int HalfAnOverride = 106;
    private const int RobotArmWithAHat = 107;

    /// <summary>The fixture model's body parts, as a <c>.mdl</c> declares them.</summary>
    /// <remarks>
    /// **Measured on the shipped `models/player/scout.mdl`**, which carries `hat` at place 1,
    /// `headphones` at place 2 and `shoes_socks` at place 4, each with two alternatives and a mesh
    /// only at alternative 0. `spyMask` is the spy's own part 3 here so the two mechanisms can be
    /// seen composing in one number.
    /// </remarks>
    private static readonly Dictionary<string, (int Place, int Count)> Parts = new()
    {
        ["hat"] = (1, 2),
        ["headphones"] = (2, 2),
        ["shoes_socks"] = (4, 2),
        [Disguise.MaskBodygroup] = (8, 2),
    };

    /// <summary>The fixture model, answering the two questions the engine asks of a model.</summary>
    /// <remarks>
    /// **The arithmetic is the production one**, so this cannot disagree with the scene about how
    /// digits pack — and every assertion above predicts an exact number anyway, so a broken
    /// <see cref="StudioModelInfo.WithBodygroup(int, int, int, int)"/> still reddens them.
    /// </remarks>
    private sealed class Bodygroup : IModelBodygroups
    {
        public static Bodygroup Instance { get; } = new();

        public int FindBodygroup(string modelPath, string group) =>
            Order.IndexOf(group);

        public int SetBodygroup(string modelPath, int group, int value, int body)
        {
            if (group < 0 || group >= Order.Count)
            {
                return body;
            }

            (int place, int count) = Parts[Order[group]];

            return StudioModelInfo.WithBodygroup(body, place, count, value);
        }

        private static readonly List<string> Order = [.. Parts.Keys];
    }

    /// <summary>The player prop the run produced, found by the entity it was built for.</summary>
    private static SceneProp Player(IReadOnlyList<SceneProp> drawn, int entity = 3) =>
        drawn.Single(prop => prop.EntityIndex == entity);

    private static SceneProp Worn(int item, int entity = 20, int wearer = 3) =>
        new(
            EntityIndex: entity,
            ModelPath: "models/workshop/player/items/scout/a_hat.mdl",
            Kind: SceneModelKind.Studio,
            Pose: new ScenePose(),
            AttachedTo: wearer,
            BoneMerged: true,
            ItemDefinitionIndex: item);

    private static ScenePlayer Scout() =>
        new(
            EntityIndex: 3,
            X: 10f,
            Y: 20f,
            Z: 30f,
            Team: SceneTeams.Red,
            Health: 125,
            PlayerClass: ScoutClass);

    /// <summary>A spy disguised as a spy, which is the case that wears a visible mask.</summary>
    private static ScenePlayer DisguisedSpy() =>
        Scout() with
        {
            PlayerClass = SpyClass,
            Conditions = new PlayerConditions(1 << PlayerConditions.Disguised, 0, 0, 0, 0),
            DisguiseClass = SpyClass,
        };

    /// <summary>A stand-in for <c>items_game.txt</c>, so this needs no TF2 install.</summary>
    private sealed class Wardrobe : IPlayerAppearance
    {
        /// <inheritdoc/>
        public string? ModelOf(int playerClass) =>
            playerClass == SpyClass ? "models/player/spy.mdl" : "models/player/scout.mdl";

        /// <inheritdoc/>
        public string? WeaponSuffix(string? weaponClass, int? playerClass) => "PRIMARY";

        /// <inheritdoc/>
        public bool Airwalks(int playerClass) => true;

        /// <inheritdoc/>
        public bool Lands(int playerClass) => true;

        /// <inheritdoc/>
        public string? Hands(int playerClass) => null;

        /// <inheritdoc/>
        public ItemBodygroups BodygroupsOf(int itemDefinitionIndex) => itemDefinitionIndex switch
        {
            Hat or HatAgain => new ItemBodygroups(new Dictionary<string, int> { ["hat"] = 1 }, false),
            HatAndHeadphones => new ItemBodygroups(
                new Dictionary<string, int> { ["hat"] = 1, ["headphones"] = 1 }, false),
            PutsTheHatBack => new ItemBodygroups(new Dictionary<string, int> { ["hat"] = 0 }, false),
            FistsOfSteel => new ItemBodygroups(
                new Dictionary<string, int> { ["hat"] = 1 }, true),
            RobotArm => ItemBodygroups.None with { OverrideGroup = 2, OverrideState = 1 },
            // Names `hat` as well, so the half-declaration has something to undo if it is read
            // wrongly — see the test's remarks. The override names part 0, which IS `hat`.
            HalfAnOverride => new ItemBodygroups(
                new Dictionary<string, int> { ["hat"] = 1 }, false)
            {
                OverrideGroup = 0,
            },
            RobotArmWithAHat => new ItemBodygroups(
                new Dictionary<string, int> { ["hat"] = 1 }, false)
            {
                OverrideGroup = 2,
                OverrideState = 1,
            },
            _ => ItemBodygroups.None,
        };
    }
}
