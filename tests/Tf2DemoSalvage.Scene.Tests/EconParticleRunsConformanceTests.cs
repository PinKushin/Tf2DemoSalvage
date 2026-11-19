using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CEconEntity::SetParticleSystemsVisible</c> stepped frame by frame (econ_entity.cpp:1510-1587, B69 g): an unchanged state
/// does nothing; a change stops every effect and creates afresh, which clears <c>EF_BONEMERGE_FASTCULL</c>. A wearable
/// re-decides every frame (<c>ClientThink</c>, econ_wearable.cpp:428-433), a weapon only on its own update
/// (tf_weaponbase.cpp:3525), and entering dormancy forces not-visible (:995-1001).
/// </summary>
public sealed class EconParticleRunsConformanceTests
{
    private const int Hat = 40;
    private const int Gun = 41;

    private static readonly EconParticleHolder Worn = new(
        HasLocalPlayer: true,
        HasOwner: true,
        IsWeapon: false,
        ExtraWearable: false,
        ExtraWearableViewModel: false,
        OwnerDrawnAsWorldModel: true,
        InLocalViewModel: false,
        HasViewModelOwner: false,
        ParticleSuffix: null);

    [Test]
    public void At_AWearableTurningVisible_CreatesFromThatTickAndClearsFastCull()
    {
        EconParticleRuns runs = new();
        runs.At(100, landed: true, [Wearable(EconParticleState.NotVisible)], Loaded, NoAttachment).ShouldBeEmpty();
        runs.ClearedAt(Hat).ShouldBeNull("the control: nothing created, nothing cleared");

        EconParticleRun run = runs.At(101, landed: false, [Wearable(EconParticleState.Visible)], Loaded, NoAttachment)
            .ShouldHaveSingleItem();

        (run.Entity, run.Effect.System, run.From).ShouldBe((Hat, "superrare_burning1", 101));
        runs.ClearedAt(Hat).ShouldBe(101);
    }

    [Test]
    public void At_AnUnchangedState_KeepsTheEffectsStart()
    {
        EconParticleRuns runs = new();
        runs.At(100, landed: true, [Wearable(EconParticleState.NotVisible)], Loaded, NoAttachment);
        runs.At(101, landed: false, [Wearable(EconParticleState.Visible)], Loaded, NoAttachment);

        runs.At(150, landed: false, [Wearable(EconParticleState.Visible)], Loaded, NoAttachment)
            .ShouldHaveSingleItem().From.ShouldBe(101);
    }

    /// <summary>A wearable re-decides every frame: hidden without an update of its own, its effect goes.</summary>
    [Test]
    public void At_AWearableHiddenWithoutAnUpdate_StopsItsEffect()
    {
        EconParticleRuns runs = new();
        runs.At(100, landed: true, [Wearable(EconParticleState.Visible)], Loaded, NoAttachment).ShouldHaveSingleItem();

        runs.At(101, landed: false, [Wearable(EconParticleState.NotVisible)], Loaded, NoAttachment).ShouldBeEmpty();
    }

    /// <summary>
    /// **A weapon keeps its decision until it is updated**: not visible this frame with no update of its own, the effect runs
    /// on; the update that follows ends it.
    /// </summary>
    [Test]
    public void At_AWeaponHiddenWithoutAnUpdate_KeepsItsEffectUntilItsNextUpdate()
    {
        EconParticleRuns runs = new();
        runs.At(100, landed: true, [Weapon(EconParticleState.Visible, received: 90)], Loaded, NoAttachment).ShouldHaveSingleItem();

        runs.At(101, landed: false, [Weapon(EconParticleState.NotVisible, received: 90)], Loaded, NoAttachment)
            .ShouldHaveSingleItem("no update of its own: not re-decided");
        runs.At(102, landed: false, [Weapon(EconParticleState.NotVisible, received: 102)], Loaded, NoAttachment).ShouldBeEmpty();
    }

    [Test]
    public void At_AWeaponGoingDormant_StopsWithoutAnUpdate()
    {
        EconParticleRuns runs = new();
        runs.At(100, landed: true, [Weapon(EconParticleState.Visible, received: 90)], Loaded, NoAttachment).ShouldHaveSingleItem();

        runs.At(101, landed: false, [Weapon(EconParticleState.Visible, received: 90) with { Dormant = true }], Loaded, NoAttachment)
            .ShouldBeEmpty();
    }

    [Test]
    public void At_AnItemLeavingTheFrame_TakesItsEffectsAndItsClear()
    {
        EconParticleRuns runs = new();
        runs.At(100, landed: true, [Wearable(EconParticleState.Visible), Weapon(EconParticleState.Visible, 90)], Loaded, NoAttachment)
            .Count.ShouldBe(2);

        runs.At(101, landed: false, [Weapon(EconParticleState.Visible, 90)], Loaded, NoAttachment)
            .ShouldHaveSingleItem().Entity.ShouldBe(Gun);
        runs.ClearedAt(Hat).ShouldBeNull();
    }

    /// <summary>Our seek (D221): an effect met running starts two seconds back, its clear dated at the landing.</summary>
    [Test]
    public void At_ASeekLandingOnAVisibleItem_StartsItTwoSecondsBack()
    {
        EconParticleRuns runs = new();
        runs.At(100, landed: false, [Wearable(EconParticleState.NotVisible)], Loaded, NoAttachment);

        runs.At(500, landed: true, [Wearable(EconParticleState.Visible)], Loaded, NoAttachment)
            .ShouldHaveSingleItem().From.ShouldBe(500 - EconParticleRuns.ReplayTicks);
        runs.ClearedAt(Hat).ShouldBe(500);
    }

    /// <summary>A clock gone back is a landing, as it is for every burst.</summary>
    [Test]
    public void At_AClockGoneBack_DecidesAfresh()
    {
        EconParticleRuns runs = new();
        runs.At(300, landed: true, [Wearable(EconParticleState.Visible)], Loaded, NoAttachment);

        runs.At(200, landed: false, [Wearable(EconParticleState.Visible)], Loaded, NoAttachment)
            .ShouldHaveSingleItem().From.ShouldBe(200 - EconParticleRuns.ReplayTicks);
    }

    /// <summary>A custom entry is the weapon's own (:1546-1548); the style rule hides only `drg_pipe_smoke` (:1556-1576).</summary>
    [Test]
    public void At_ACustomEntryAndAStyleWithoutSmoke_CreateOnlyTheRest()
    {
        EconParticleItem pipe = Wearable(EconParticleState.Visible) with
        {
            Systems = [Entry("drg_pipe_smoke"), Entry("superrare_burning1"), Entry("weapon_own") with { CustomType = 1 }],
            SmokeStyleOff = true,
        };

        new EconParticleRuns().At(100, landed: true, [pipe], Loaded, NoAttachment)
            .Select(run => run.Effect.System).ShouldBe(["superrare_burning1"]);
    }

    /// <summary>The attachment question is asked of the item's own entity.</summary>
    [Test]
    public void At_AnAttachmentTheItemHas_IsAskedOfThatItem()
    {
        EconParticleItem notes = Wearable(EconParticleState.Visible) with { Systems = [Entry("notes", "ear_R")] };

        new EconParticleRuns().At(100, landed: true, [notes], Loaded, (entity, _, name) => entity == Hat && name == "ear_R")
            .ShouldHaveSingleItem().Effect.Attach.ShouldBe(EconParticleAttach.PointFollow);
    }

    private static EconParticleItem Wearable(EconParticleState state) =>
        new(Hat, Wearable: true, Received: null, Dormant: false, state, [Entry("superrare_burning1")], Worn);

    private static EconParticleItem Weapon(EconParticleState state, int received) =>
        new(Gun, Wearable: false, received, Dormant: false, state, [Entry("superrare_burning1")], Worn with { IsWeapon = true });

    private static EconParticle Entry(string name, string? attachment = null) =>
        new(name, 0, FollowRootBone: false, DrawInViewModel: false, UseSuffixName: false, [attachment]);

    private static bool Loaded(string name) => true;

    private static bool NoAttachment(int entity, bool viewModel, string name) => false;
}
