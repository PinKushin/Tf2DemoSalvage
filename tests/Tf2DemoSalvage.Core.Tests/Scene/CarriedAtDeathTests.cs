using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// What a corpse's bodygroups are built from — the player's whole equipment at death (B395).
/// </summary>
/// <remarks>
/// **A second, WIDER walk than the one that dresses the corpse, and the difference is the point.**
/// `CreateBoneAttachmentsFromWearables` hangs the econ wearable list off the body
/// (`c_tf_player.cpp:10178`), so weapons are dropped there. `m_nBody` is `pPlayer->GetBody()`
/// copied whole (`:790-793`), and `RecalculatePlayerBodygroups` builds that from weapons AND
/// wearables (`tf_player_shared.cpp:13693-13709`). One list filtered to serve both is wrong in one
/// direction or the other.
///
/// **Stated as inputs rather than measured on a demo.** The surrounding `Build` needs a whole
/// recording, and every case here — a weapon beside a wearable, a disguise mismatch, a track with
/// no item index — is exact when the test puts the value there (D38).
/// </remarks>
public sealed class CarriedAtDeathTests
{
    private const int Player = 4;
    private const int Hat = 30700;
    private const int Launcher = 30701;

    [Test]
    public void CarriedAtDeath_ForAWearableAndAWeapon_KeepsBothAndTellsThemApart()
    {
        List<SceneCarriedItem> carried = DemoTimeline.CarriedAtDeath(
            Tracks(
                Worn(10, Hat, "CTFWearable"),
                Worn(11, Launcher, "CTFRocketLauncher")),
            Player,
            disguised: false,
            activeWeapon: null)!;

        carried.Count.ShouldBe(2, "the body is built from cosmetics AND weapons");
        carried.ShouldContain(new SceneCarriedItem(Hat, Weapon: false, Deployed: false));
        carried.ShouldContain(new SceneCarriedItem(Launcher, Weapon: true, Deployed: false));
    }

    /// <remarks>
    /// **A powerup bottle is a wearable despite its class name**, which `WornAtDeath` also has to
    /// know. Asserted here so the two walks cannot drift on the same question.
    /// </remarks>
    [Test]
    public void CarriedAtDeath_ForAPowerupBottle_CountsItAsAWearable()
    {
        List<SceneCarriedItem> carried = DemoTimeline.CarriedAtDeath(
            Tracks(Worn(10, Hat, "CTFPowerupBottle")), Player, false, null)!;

        carried.ShouldHaveSingleItem().Weapon.ShouldBeFalse();
    }

    [Test]
    public void CarriedAtDeath_ForTheWeaponBeingHeld_MarksItDeployed()
    {
        List<SceneCarriedItem> carried = DemoTimeline.CarriedAtDeath(
            Tracks(Worn(11, Launcher, "CTFRocketLauncher")), Player, false, activeWeapon: 11)!;

        carried.ShouldHaveSingleItem().Deployed.ShouldBeTrue();
    }

    /// <remarks>
    /// **The control on the case above.** Naming a DIFFERENT entity must leave it holstered, or
    /// the flag would be true whenever any weapon was held and the deployed-only pass would apply
    /// items the player was not carrying.
    /// </remarks>
    [Test]
    public void CarriedAtDeath_WhenAnotherWeaponIsHeld_LeavesThisOneHolstered()
    {
        List<SceneCarriedItem> carried = DemoTimeline.CarriedAtDeath(
            Tracks(Worn(11, Launcher, "CTFRocketLauncher")), Player, false, activeWeapon: 99)!;

        carried.ShouldHaveSingleItem().Deployed.ShouldBeFalse();
    }

    [Test]
    public void CarriedAtDeath_ForATrackWithNoItemIndex_SkipsIt()
    {
        DemoTimeline.CarriedAtDeath(
            Tracks(Worn(10, item: null, "CTFWearable")), Player, false, null)
            .ShouldBeNull("the schema is asked by index, so a track without one says nothing");
    }

    [Test]
    public void CarriedAtDeath_ForSomebodyElsesEquipment_SkipsIt()
    {
        ScenePropTrack other = Worn(10, Hat, "CTFWearable");
        other.AttachedTo = Player + 1;

        DemoTimeline.CarriedAtDeath(Tracks(other), Player, false, null).ShouldBeNull();
    }

    /// <remarks>
    /// **The disguise pairing, which is a filter in both directions.** A spy who died disguised
    /// wears the disguise's gear and none of his own; one who did not wears his own and none of
    /// the disguise's.
    /// </remarks>
    [Test]
    public void CarriedAtDeath_ForTheWrongSideOfTheDisguisePairing_SkipsIt()
    {
        ScenePropTrack pretend = Worn(10, Hat, "CTFWearable");
        pretend.OfDisguise = true;

        DemoTimeline.CarriedAtDeath(Tracks(pretend), Player, disguised: false, null).ShouldBeNull();
        DemoTimeline.CarriedAtDeath(Tracks(pretend), Player, disguised: true, null)
            .ShouldNotBeNull();
    }

    [Test]
    public void CarriedAtDeath_ForAnItemThatIsNotBoneMerged_SkipsIt()
    {
        ScenePropTrack loose = Worn(10, Hat, "CTFWearable");
        loose.BoneMerged = false;

        DemoTimeline.CarriedAtDeath(Tracks(loose), Player, false, null).ShouldBeNull();
    }

    /// <remarks>
    /// **A corpse whose player was never identified has no equipment to find**, which is the
    /// nine-of-159 case the wearable walk also reports: players the recorder could not see.
    /// </remarks>
    [Test]
    public void CarriedAtDeath_WithNoPlayer_IsNull()
    {
        DemoTimeline.CarriedAtDeath(
            Tracks(Worn(10, Hat, "CTFWearable")), player: null, false, null).ShouldBeNull();
    }

    private static Dictionary<int, ScenePropTrack> Tracks(params ScenePropTrack[] tracks)
    {
        Dictionary<int, ScenePropTrack> by = [];

        foreach (ScenePropTrack track in tracks)
        {
            by[track.EntityIndex] = track;
        }

        return by;
    }

    private static ScenePropTrack Worn(int entity, int? item, string className)
    {
        ScenePropTrack track = new(entity, "models/anything.mdl")
        {
            AttachedTo = Player,
            BoneMerged = true,
            OfDisguise = false,
            ItemDefinitionIndex = item,
            ClassName = className,
        };

        return track;
    }
}
