using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A corpse's bodygroups are the player's, copied at death — <c>m_nBody</c> (B395).
/// </summary>
/// <remarks>
/// **`CreateTFRagdoll` copies the whole body off the living player, and the ORDER against the
/// wearable skip is the mechanism** (`client/tf/c_tf_player.cpp:790-793`):
///
/// <code>
/// if ( !m_bFeignDeath || m_bWasDisguised )
/// {
///     pPlayer-&gt;RecalcBodygroupsIfDirty();
///     m_nBody = pPlayer-&gt;GetBody();
/// }
/// </code>
///
/// Only afterwards, at `:10206-10214`, are HEAD and MISC wearables refused on a decapitating death.
/// So a decapitated TF2 corpse keeps the hidden-head bodygroup its hat imposed and loses the hat —
/// a soldier corpse with no head is something the engine draws by construction, and it is the
/// symptom the owner reported.
///
/// **This project set no body on a corpse at all**, so `Body` kept `ScenePose`'s default of 0 and
/// every part showed its first alternative: our corpses drew with MORE geometry than TF2's, a stock
/// helmet under a cosmetic rather than a cosmetic replacing it. The divergence was filed in
/// `RagdollProps` and `RagdollAppearance`, both citing these lines, and filed is not fixed
/// (`docs/memory/filing-a-divergence-is-not-fixing-it.md`).
///
/// **The stubs here answer for one item and one part, which is what makes the assertions exact.**
/// A real `items_game.txt` would make the expected number a fact about TF2's schema rather than
/// about this code — see `docs/DECISIONS.md` D38 on why a synthetic fixture is the stronger test.
/// </remarks>
public sealed class RagdollBodygroupConformanceTests
{
    /// <summary>The item the fixture's player wears — a hat that hides the base head.</summary>
    private const int Hat = 30700;

    /// <summary>The part that hat hides, and the value the stub returns for it.</summary>
    private const int HeadGroup = 2;

    /// <summary>A weapon claiming the SAME part as the hat, so pass order decides the answer.</summary>
    private const int RivalWeapon = 30701;

    /// <summary>Its value for that part — different from the hat's, or order is unobservable.</summary>
    private const int RivalGroup = 5;

    /// <summary>A weapon that hides parts only while it is being held.</summary>
    private const int DeployedOnlyWeapon = 30702;

    [Test]
    public void Fill_ForACorpseWearingAnItemThatHidesAPart_CopiesThePlayersBody()
    {
        List<SceneProp> scene = [];

        Fill(Corpse(), scene);

        // **The corpse's own prop, not one of its wearables.** `Fill` appends the body and then the
        // items that hang off it, so an assertion on the first is an assertion on the corpse.
        scene[0].Pose.Body.ShouldBe(
            HeadGroup,
            "the engine copies m_nBody off the player it was made from (c_tf_player.cpp:790-793)");
    }

    /// <remarks>
    /// **The control that makes the case above mean something.** A corpse wearing nothing must draw
    /// at body 0, so the assertion is reading a computed value rather than any non-zero constant.
    /// </remarks>
    [Test]
    public void Fill_ForACorpseWearingNothing_LeavesTheBodyAtZero()
    {
        List<SceneProp> scene = [];

        // **An EMPTY list, not null.** `Corpse`'s default fills in the hat when none is given, so
        // passing null asked for the opposite of this case and the test reddened against correct
        // code — the fixture's fault, caught by the control failing rather than the subject.
        Fill(Corpse(carried: []), scene);

        scene[0].Pose.Body.ShouldBe(0);
    }

    /// <remarks>
    /// **The engine's own guard, and both arms.** A feign death leaves the player alive, so there
    /// is no new body to copy; a spy who feigned while disguised has one taken anyway. A guard
    /// asserted on one side is a constant, so both are here.
    /// </remarks>
    [Test]
    public void Fill_ForAFeignedDeathThatWasNotDisguised_TakesNoBody()
    {
        List<SceneProp> scene = [];

        Fill(Corpse(feign: true), scene);

        scene[0].Pose.Body.ShouldBe(0, "!m_bFeignDeath || m_bWasDisguised is false here");
    }

    [Test]
    public void Fill_ForAFeignedDeathWhileDisguised_TakesTheBodyAnyway()
    {
        List<SceneProp> scene = [];

        Fill(Corpse(feign: true, disguised: true), scene);

        scene[0].Pose.Body.ShouldBe(HeadGroup);
    }

    /// <remarks>
    /// **Without an install there is no schema, and 0 is the engine's own starting value** — the
    /// same thing a viewer with no TF2 drew before any of this existed.
    /// </remarks>
    [Test]
    public void Fill_WithNoItemSchema_LeavesTheBodyAtZero()
    {
        List<SceneProp> scene = [];

        RagdollProps.Fill([Corpse()], tick: 150d, Classes, scene).ShouldBeGreaterThan(0);

        scene[0].Pose.Body.ShouldBe(0);
    }

    /// <remarks>
    /// **The pass ORDER, which is the only thing this case can be about.** Both items name the same
    /// body part with different values, so a single loop answers whichever came last in the list and
    /// the engine answers whichever came last in ITS order — weapons without the flag, then
    /// wearables, then the deployed weapon (`tf_player_shared.cpp:13693-13709`). The list here is
    /// deliberately in the WRONG order, so a one-pass implementation returns the weapon's value and
    /// only the three-pass one returns the wearable's.
    ///
    /// `SetBodygroup` is last-writer-wins on a part, which is what makes the order observable at all.
    /// </remarks>
    [Test]
    public void Fill_WhenAWeaponAndAWearableClaimTheSamePart_TakesTheWearablesValue()
    {
        List<SceneProp> scene = [];

        Fill(
            Corpse(carried:
            [
                // Listed weapon-first so that list order and engine order disagree.
                new SceneCarriedItem(RivalWeapon, Weapon: true, Deployed: false),
                new SceneCarriedItem(Hat, Weapon: false, Deployed: false),
            ]),
            scene);

        scene[0].Pose.Body.ShouldBe(
            HeadGroup,
            "the wearable pass runs after the undeployed-weapon pass, so its value stands");
    }

    /// <remarks>
    /// **And the deployed weapon runs LAST, so it beats the wearable.** The mirror of the case
    /// above: same two items, same part, and the only change is that the weapon is the one being
    /// held — which moves it from the first pass to the third.
    /// </remarks>
    [Test]
    public void Fill_WhenTheHeldWeaponClaimsThePartAWearableDid_TakesTheWeaponsValue()
    {
        List<SceneProp> scene = [];

        Fill(
            Corpse(carried:
            [
                new SceneCarriedItem(Hat, Weapon: false, Deployed: false),
                new SceneCarriedItem(RivalWeapon, Weapon: true, Deployed: true),
            ]),
            scene);

        scene[0].Pose.Body.ShouldBe(RivalGroup, "the deployed-weapon pass is the last one to run");
    }

    /// <remarks>
    /// **A weapon that declares the deployed-only flag and is NOT held contributes nothing**, which
    /// is the guard at `tf_weaponbase.cpp:6226`. Without it a holstered weapon would hide parts its
    /// owner is not carrying.
    /// </remarks>
    [Test]
    public void Fill_ForAHolsteredDeployedOnlyWeapon_IgnoresIt()
    {
        List<SceneProp> scene = [];

        Fill(
            Corpse(carried: [new SceneCarriedItem(DeployedOnlyWeapon, Weapon: true, Deployed: false)]),
            scene);

        scene[0].Pose.Body.ShouldBe(0);
    }

    [Test]
    public void Fill_ForAHeldDeployedOnlyWeapon_AppliesIt()
    {
        List<SceneProp> scene = [];

        Fill(
            Corpse(carried: [new SceneCarriedItem(DeployedOnlyWeapon, Weapon: true, Deployed: true)]),
            scene);

        scene[0].Pose.Body.ShouldBe(RivalGroup);
    }

    private static void Fill(SceneRagdoll corpse, List<SceneProp> into) =>
        RagdollProps.Fill(
            [corpse],
            tick: 150d,
            Classes,
            into,
            appearance: new Appearance(),
            bodygroups: new Bodygroups()).ShouldBeGreaterThan(0);

    private static SceneRagdoll Corpse(
        bool feign = false,
        bool disguised = false,
        IReadOnlyList<SceneCarriedItem>? carried = null) =>
        new(EntityIndex: 40,
            Serial: 1,
            PlayerClass: 5,
            Team: SceneTeams.Blu,
            X: -5446f,
            Y: 4055f,
            Z: 21f,
            Gib: false,
            Burning: false,
            FeignDeath: feign,
            WasDisguised: disguised,
            FirstTick: 100,
            LastTick: 200,
            Yaw: 137f,
            Carried: carried ?? [new SceneCarriedItem(Hat, Weapon: false, Deployed: false)]);

    private static string? Classes(int playerClass) =>
        playerClass == 5 ? "models/player/medic.mdl" : null;

    /// <summary>Answers for one item: the hat declares the head part by NUMBER.</summary>
    private sealed class Appearance : IPlayerAppearance
    {
        /// <inheritdoc/>
        /// <remarks>
        /// **By number rather than by name**, so the assertion does not also depend on
        /// `FindBodygroup` resolving a string — one mechanism per test.
        /// </remarks>
        public ItemBodygroups BodygroupsOf(int itemDefinitionIndex) => itemDefinitionIndex switch
        {
            Hat => new ItemBodygroups(new Dictionary<string, int>(), false, HeadGroup, 1),

            // **Same part, different value** — the only shape in which pass order is observable,
            // because `SetBodygroup` is last-writer-wins.
            RivalWeapon => new ItemBodygroups(new Dictionary<string, int>(), false, RivalGroup, 1),

            DeployedOnlyWeapon => new ItemBodygroups(
                new Dictionary<string, int>(), true, RivalGroup, 1),

            _ => ItemBodygroups.None,
        };

        /// <inheritdoc/>
        public string? ModelOf(int playerClass) => null;

        /// <inheritdoc/>
        public string? WeaponSuffix(string? weaponClass, int? playerClass) => null;

        /// <inheritdoc/>
        public bool Airwalks(int playerClass) => true;

        /// <inheritdoc/>
        public bool Lands(int playerClass) => true;

        /// <inheritdoc/>
        public string? Hands(int playerClass) => null;

        /// <inheritdoc/>
        public SceneTaunt? TauntForScene(string scene) => null;
    }

    /// <summary>A model with one part, so a set of it is exactly its group number.</summary>
    private sealed class Bodygroups : IModelBodygroups
    {
        public int FindBodygroup(string modelPath, string group) => -1;

        /// <inheritdoc/>
        /// <remarks>
        /// **Last-writer-wins, which is the property the order tests depend on.** A real
        /// `SetBodygroup` replaces one part's selection inside the packed body, so two items
        /// naming the same part resolve to whichever ran last. An ADDING stub would make both
        /// orders produce the same sum and the order cases could not fail — the first version of
        /// this fixture did exactly that.
        /// </remarks>
        public int SetBodygroup(string modelPath, int group, int value, int body) =>
            group < 0 ? body : group;
    }
}
