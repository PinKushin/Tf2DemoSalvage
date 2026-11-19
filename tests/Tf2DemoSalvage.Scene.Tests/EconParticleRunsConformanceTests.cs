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

    /// <summary>
    /// **The clear holds until the next `m_fEffects` receive** (B69 h): a create at 120 after the receive at 100 leaves the
    /// prop without fast-cull; a receive at 150 brings it back; the same tick is the clear's, its `OnDataChanged` running after
    /// the receive.
    /// </summary>
    [Test]
    public void FastCull_ClearedAfterTheLastEffectsReceive_IsOffUntilTheNext()
    {
        Core.Scene.SceneProp hat = new(Hat, "models/hat.mdl", Core.Scene.SceneModelKind.Studio, new Core.Scene.ScenePose(),
            BoneMerged: true, BoneMergeFastCull: true, EffectsReceived: 100);

        EconParticleRuns.FastCull(hat, clearedAt: 120, tick: 130).BoneMergeFastCull.ShouldBeFalse();
        EconParticleRuns.FastCull(hat, clearedAt: 100, tick: 130).BoneMergeFastCull.ShouldBeFalse("the same tick is the clear's");
        EconParticleRuns.FastCull(hat with { EffectsReceived = 150 }, clearedAt: 120, tick: 160).BoneMergeFastCull
            .ShouldBeTrue("a later receive restores it");
        EconParticleRuns.FastCull(hat, clearedAt: null, tick: 130).BoneMergeFastCull.ShouldBeTrue("the control: never cleared");
        EconParticleRuns.FastCull(hat, clearedAt: 200, tick: 130).BoneMergeFastCull
            .ShouldBeTrue("a clear the timeline has not reached, as after a seek back");
    }

    /// <summary>
    /// **A weapon's world effects hang from its extra wearable** (econ_entity.cpp:1611-1624, :1641-1645): owned by
    /// <c>m_hExtraWearable</c>'s particle property, on <c>attach_fob</c> asked of that wearable; the fast-cull clear is still the
    /// weapon's own (:1733-1740).
    /// </summary>
    [Test]
    public void At_AWeaponWithAnExtraWearable_HangsItsEffectFromTheWearable()
    {
        EconParticleItem gun = Weapon(EconParticleState.Visible, received: 90) with { ExtraWearable = new(Fob, false) };

        EconParticleRun run = new EconParticleRuns()
            .At(100, landed: true, [gun], Loaded, (entity, _, name) => entity == Fob && name == "attach_fob")
            .ShouldHaveSingleItem();

        (run.Entity, run.Owner, run.Effect.Attach, run.Effect.Attachment).ShouldBe((Gun, Fob, EconParticleAttach.PointFollow, "attach_fob"));
    }

    /// <summary>
    /// **An unchanged state still re-creates when the wearable's own state differs** (:1512-1537): the weapon writes its state
    /// into the wearable and re-creates. A wearable newly made holds <c>NOT_VISIBLE</c> (:1022-1027), so the weapon's next
    /// decision re-creates; the one after, the two agree and nothing moves. **The stop reaches only the owners it names**
    /// (:1695-1720, on <c>pEffectOwnerWM</c> and <c>pEffectOwnerVM</c>): the effect the weapon made on itself before the
    /// wearable existed runs on.
    /// </summary>
    [Test]
    public void At_AnExtraWearableBehindItsWeaponsState_RecreatesOnTheWeaponsNextDecision()
    {
        EconParticleRuns runs = new();
        runs.At(100, landed: true, [Weapon(EconParticleState.Visible, 90)], Loaded, NoAttachment);

        EconParticleItem withFob = Weapon(EconParticleState.Visible, 101) with { ExtraWearable = new(Fob, false) };
        List<EconParticleRun> made = [.. runs.At(101, landed: false, [withFob], Loaded, NoAttachment)];

        made.Select(run => (run.Owner, run.From)).ShouldBe([(Gun, 100 - EconParticleRuns.ReplayTicks), (Fob, 101)], ignoreOrder: true);

        runs.At(102, landed: false, [withFob with { Received = 102 }], Loaded, NoAttachment)
            .Single(run => run.Owner == Fob).From.ShouldBe(101, "the control: the wearable now agrees");
    }

    /// <summary>
    /// **A state CHANGE does not write the wearable** — only the unchanged branch does (:1514-1532) — so the decision after a
    /// change finds the wearable stale and creates a second time.
    /// </summary>
    [Test]
    public void At_AStateChangeWithAnExtraWearable_CreatesAgainOnTheNextDecision()
    {
        EconParticleRuns runs = new();
        EconParticleItem gun = Weapon(EconParticleState.NotVisible, 90) with { ExtraWearable = new(Fob, false) };

        runs.At(100, landed: true, [gun], Loaded, NoAttachment).ShouldBeEmpty();
        runs.At(101, landed: false, [gun with { Received = 101, State = EconParticleState.Visible }], Loaded, NoAttachment)
            .ShouldHaveSingleItem().From.ShouldBe(101);
        runs.At(102, landed: false, [gun with { Received = 102, State = EconParticleState.Visible }], Loaded, NoAttachment)
            .ShouldHaveSingleItem().From.ShouldBe(102);
        runs.At(103, landed: false, [gun with { Received = 103, State = EconParticleState.Visible }], Loaded, NoAttachment)
            .ShouldHaveSingleItem().From.ShouldBe(102, "the control: settled");
    }

    /// <summary>
    /// **The view-model wearable's comparison overwrites the world one's** (:1520-1530): <c>bDirty</c> is assigned, not or-ed,
    /// so a stale world wearable re-creates nothing while the view-model one agrees.
    /// </summary>
    [Test]
    public void At_AStaleWorldWearableBesideAnAgreeingViewModelOne_CreatesNothing()
    {
        EconParticleRuns runs = new();
        EconParticleItem gun = Weapon(EconParticleState.Visible, 90) with { ExtraWearableViewModel = new(FobViewModel, false) };

        runs.At(100, landed: true, [gun], Loaded, NoAttachment);
        runs.At(101, landed: false, [gun with { Received = 101 }], Loaded, NoAttachment).ShouldHaveSingleItem().From.ShouldBe(101);

        runs.At(102, landed: false, [gun with { Received = 102, ExtraWearable = new(Fob, false) }], Loaded, NoAttachment)
            .ShouldHaveSingleItem().From.ShouldBe(101);
    }

    /// <summary>
    /// **A wearable going dormant writes its own state** (<c>SetDormant</c> → <c>SetParticleSystemsVisible( NOT_VISIBLE )</c>,
    /// :994-1001, :1585), so the weapon's next decision finds it stale and re-creates.
    /// </summary>
    [Test]
    public void At_AnExtraWearableThatWentDormant_IsStaleAtTheWeaponsNextDecision()
    {
        EconParticleRuns runs = new();
        EconParticleItem gun = Weapon(EconParticleState.Visible, 90) with { ExtraWearable = new(Fob, false) };

        runs.At(100, landed: true, [gun], Loaded, NoAttachment);
        runs.At(101, landed: false, [gun with { Received = 101 }], Loaded, NoAttachment).ShouldHaveSingleItem().From.ShouldBe(101);
        runs.At(102, landed: false, [gun with { Received = 101, ExtraWearable = new(Fob, true) }], Loaded, NoAttachment);

        runs.At(103, landed: false, [gun with { Received = 103 }], Loaded, NoAttachment).ShouldHaveSingleItem().From.ShouldBe(103);
    }

    /// <summary>
    /// **An extra wearable that is gone takes the effects it owned** (<c>CEconEntity::Release</c>, :946-989; the particle
    /// property's destructor, particle_property.cpp:69-74), and the weapon, its state unchanged, makes no other.
    /// </summary>
    [Test]
    public void At_AWeaponsExtraWearableGone_EndsTheEffectsHungOnIt()
    {
        EconParticleRuns runs = new();
        EconParticleItem gun = Weapon(EconParticleState.Visible, 90) with { ExtraWearable = new(Fob, false) };

        runs.At(100, landed: true, [gun], Loaded, NoAttachment).ShouldHaveSingleItem().Owner.ShouldBe(Fob);

        runs.At(101, landed: false, [gun with { ExtraWearable = null }], Loaded, NoAttachment).ShouldBeEmpty();
    }

    /// <summary>
    /// **A by-name stop takes an earlier entry's effect of the same pass** (<c>StopParticlesNamed</c>, econ_entity.cpp:1708-1720,
    /// particle_property.cpp:359-386): the second entry finds no attachment, so its stop is by name alone and ends the first
    /// entry's effect, made a moment before on <c>ear_R</c>; one effect is left, the second's. The control: a second entry of
    /// another system stops nothing of the first.
    /// </summary>
    [TestCase("superrare_burning1", 1)]
    [TestCase("superrare_flies", 2)]
    public void At_ASecondEntryOfTheSameSystemWithoutItsAttachment_StopsTheFirstsEffect(string second, int left)
    {
        EconParticleItem hat = Wearable(EconParticleState.Visible) with
        {
            Systems = [Entry("superrare_burning1", "ear_R"), Entry(second, "ear_L")],
        };

        List<EconParticleRun> made =
            [.. new EconParticleRuns().At(100, landed: true, [hat], Loaded, (entity, _, name) => entity == Hat && name == "ear_R")];

        made.Count.ShouldBe(left);
        made[^1].Effect.Attach.ShouldBe(EconParticleAttach.AbsOriginFollow, "the second entry's own");
    }

    /// <summary>
    /// The other half: a stop whose attachment was found takes only an effect with a control point on THAT attachment
    /// (<c>StopParticlesWithNameAndAttachment</c>, particle_property.cpp:389-422), so the same system on two attachments
    /// leaves both.
    /// </summary>
    [Test]
    public void At_TheSameSystemOnTwoFoundAttachments_KeepsBoth()
    {
        EconParticleItem hat = Wearable(EconParticleState.Visible) with
        {
            Systems = [Entry("superrare_burning1", "ear_R"), Entry("superrare_burning1", "ear_L")],
        };

        new EconParticleRuns().At(100, landed: true, [hat], Loaded, (_, _, name) => name is "ear_R" or "ear_L")
            .Select(run => run.Effect.Attachment).ShouldBe(["ear_R", "ear_L"]);
    }

    /// <summary>
    /// **The stop on the view model reaches another weapon's effect there** (<c>pEffectOwnerVM-&gt;ParticleProp()</c>,
    /// econ_entity.cpp:1609, :1712-1719): the local player's active weapon made its effect on his view model; a holstered
    /// weapon of the same system, updated into a new state, stops every effect of that name on the same view model before it
    /// makes its own on itself. The control: a holstered weapon of another system leaves it running.
    /// </summary>
    [TestCase("superrare_burning1", false)]
    [TestCase("superrare_flies", true)]
    public void At_AnotherWeaponsStateChange_StopsTheSameSystemOnTheSharedViewModel(string holsteredSystem, bool kept)
    {
        const int Holstered = 42;
        EconParticleHolder local = Worn with { IsWeapon = true, OwnerDrawnAsWorldModel = false, HasViewModelOwner = true };
        EconParticleItem active = Weapon(EconParticleState.VisibleViewModel, 90) with
        {
            Systems = [Entry("superrare_burning1") with { DrawInViewModel = true }],
            Holder = local with { InLocalViewModel = true },
        };
        EconParticleItem holstered = new(
            Holstered, Wearable: false, Received: 90, Dormant: false, EconParticleState.NotVisible,
            [Entry(holsteredSystem) with { DrawInViewModel = true }], local);

        EconParticleRuns runs = new();
        runs.At(100, landed: true, [active, holstered], Loaded, NoAttachment)
            .ShouldHaveSingleItem().Owner.ShouldBe(ViewmodelScene.ArmsEntityIndex);

        List<EconParticleRun> after =
            [.. runs.At(101, landed: false, [active, holstered with { Received = 101, State = EconParticleState.Visible }], Loaded, NoAttachment)];

        after.Any(run => run.Entity == Gun).ShouldBe(kept);
        after.ShouldContain(run => run.Entity == Holstered && run.Owner == Holstered);
    }

    private const int Fob = 50;
    private const int FobViewModel = 51;

    private static EconParticleItem Wearable(EconParticleState state) =>
        new(Hat, Wearable: true, Received: null, Dormant: false, state, [Entry("superrare_burning1")], Worn);

    private static EconParticleItem Weapon(EconParticleState state, int received) =>
        new(Gun, Wearable: false, received, Dormant: false, state, [Entry("superrare_burning1")], Worn with { IsWeapon = true });

    private static EconParticle Entry(string name, string? attachment = null) =>
        new(name, 0, FollowRootBone: false, DrawInViewModel: false, UseSuffixName: false, [attachment]);

    private static bool Loaded(string name) => true;

    private static bool NoAttachment(int entity, bool viewModel, string name) => false;
}
