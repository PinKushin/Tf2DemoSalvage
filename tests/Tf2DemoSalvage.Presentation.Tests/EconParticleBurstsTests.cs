using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// An econ item's attached effects offered to the particle system (B69 g): <c>ParticleProp()-&gt;Create</c> on the item with
/// <c>PATTACH_POINT_FOLLOW</c> on a found attachment, else <c>PATTACH_ABSORIGIN_FOLLOW</c>, or on the local view model
/// (econ_entity.cpp:1742-1759); control point 0 read from its owner every frame.
/// </summary>
public sealed class EconParticleBurstsTests
{
    private const int Hat = 40;
    private const long EconKeys = 12L << 32;

    private static readonly ParticleSystem Burning = new(
        "superrare_burning1", [], [], [], [], [], new Dictionary<string, DmxValue>(StringComparer.Ordinal));

    private static readonly Dictionary<string, ParticleSystem> Systems = new(StringComparer.Ordinal) { [Burning.Name] = Burning };

    private static readonly ParticleControlPoint Head = ParticleControlPoint.Unoriented(new Vector3(10f, 20f, 70f));
    private static readonly ParticleControlPoint Origin = ParticleControlPoint.Unoriented(new Vector3(10f, 20f, 41f));

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

    /// <summary>
    /// A visible unusual on its hat's `bip_head`: offered from two seconds before the landing, at the attachment, and kept
    /// from that start by the next played frame.
    /// </summary>
    [Test]
    public void AddEconParticles_AVisibleUnusualOnItsAttachment_FollowsItFromItsStart()
    {
        FakePosedEntities models = new() { Named = { [(Hat, "bip_head")] = Head }, Origins = { [Hat] = Origin } };
        BurstEffects bursts = Presenter(models);

        bursts.AddEconParticles([Unusual("bip_head")], 1000, seeked: true, Systems);

        ParticleBurst burst = bursts.Bursts.ShouldHaveSingleItem();
        (burst.Definition, burst.At, burst.Tick, burst.Undrawn).ShouldBe((Burning, Head, 1000 - EconParticleRuns.ReplayTicks, false));
        (burst.Key & ~0xFFFFFFFFL).ShouldBe(EconKeys);
        bursts.FastCullClearedAt(Hat).ShouldBe(1000);

        bursts.AddEconParticles([Unusual("bip_head")], 1001, seeked: false, Systems);

        bursts.Bursts[^1].Key.ShouldBe(burst.Key, "the same effect, not a new one");
    }

    /// <summary>An item without the attachment follows its abs origin — `PATTACH_ABSORIGIN_FOLLOW`.</summary>
    [Test]
    public void AddEconParticles_AnItemWithoutTheAttachment_FollowsItsAbsOrigin()
    {
        FakePosedEntities models = new() { Origins = { [Hat] = Origin } };
        BurstEffects bursts = Presenter(models);

        bursts.AddEconParticles([Unusual("bip_head")], 1000, seeked: true, Systems);

        bursts.Bursts.ShouldHaveSingleItem().At.ShouldBe(Origin);
    }

    /// <summary>
    /// A root-bone entry without its attachment follows the root bone, not the abs origin — <c>PATTACH_ROOTBONE_FOLLOW</c>,
    /// <c>GetRootBone</c> (particle_property.cpp:642-656); and with no root bone it is offered undrawn.
    /// </summary>
    [TestCase(true, false)]
    [TestCase(false, true)]
    public void AddEconParticles_ARootBoneEntryWithoutTheAttachment_FollowsTheRootBone(bool rooted, bool undrawn)
    {
        ParticleControlPoint root = ParticleControlPoint.Unoriented(new Vector3(10f, 20f, 50f));
        FakePosedEntities models = new() { Origins = { [Hat] = Origin } };

        if (rooted)
        {
            models.Roots[Hat] = root;
        }

        BurstEffects bursts = Presenter(models);
        EconParticleItem hat = Unusual("bip_head") with
        {
            Systems = [new EconParticle("superrare_burning1", 0, FollowRootBone: true, false, false, ["bip_head"])],
        };

        bursts.AddEconParticles([hat], 1000, seeked: true, Systems);

        ParticleBurst burst = bursts.Bursts.ShouldHaveSingleItem();
        burst.Undrawn.ShouldBe(undrawn);

        if (rooted)
        {
            burst.At.ShouldBe(root);
        }
    }

    /// <summary>
    /// Control points 1..6 follow the ITEM's named attachments every frame — <c>AddControlPoint( pEffect, i, this,
    /// PATTACH_POINT_FOLLOW, name )</c> (econ_entity.cpp:1762-1770); one the item lacks stops the effect as it is made, so
    /// nothing is offered, while the create still cleared the fast-cull (:1733-1740).
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void AddEconParticles_ControlPointsOneToSix_FollowTheItemsAttachments(bool itemHasEyes)
    {
        ParticleControlPoint eyes = ParticleControlPoint.Unoriented(new Vector3(12f, 20f, 72f));
        FakePosedEntities models = new() { Named = { [(Hat, "bip_head")] = Head }, Origins = { [Hat] = Origin } };

        if (itemHasEyes)
        {
            models.Named[(Hat, "eyes")] = eyes;
        }

        BurstEffects bursts = Presenter(models);
        EconParticleItem hat = Unusual("bip_head") with
        {
            Systems = [new EconParticle("superrare_burning1", 0, false, false, false, ["bip_head", null, "eyes"])],
        };

        bursts.AddEconParticles([hat], 1000, seeked: true, Systems);

        bursts.FastCullClearedAt(Hat).ShouldBe(1000);

        if (!itemHasEyes)
        {
            bursts.Bursts.ShouldBeEmpty();
            return;
        }

        bursts.Bursts.ShouldHaveSingleItem().Points.ShouldBe([(2, eyes)]);
    }

    /// <summary>
    /// **Its stop is instant** (econ_entity.cpp:946-1001, :1695-1720: <c>bForceRemoveInstantly</c>), so every offer says so; and
    /// an owner the frame does not hold is offered undrawn rather than left out, which would remove it.
    /// </summary>
    [Test]
    public void AddEconParticles_AnOwnerTheFrameDoesNotHold_IsOfferedUndrawnAndRemovedWhenUnoffered()
    {
        BurstEffects bursts = Presenter(new FakePosedEntities());

        bursts.AddEconParticles([Unusual("bip_head")], 1000, seeked: true, Systems);

        ParticleBurst burst = bursts.Bursts.ShouldHaveSingleItem();
        (burst.Undrawn, burst.RemovedWhenUnoffered).ShouldBe((true, true));
    }

    /// <summary>
    /// **A weapon's effect hangs from its extra wearable's <c>attach_fob</c>** (econ_entity.cpp:1611-1645, B69 part 4): placed
    /// there and posed with that wearable, while the fast-cull clear is the weapon's.
    /// </summary>
    [Test]
    public void AddEconParticles_AWeaponWithAnExtraWearable_FollowsTheWearablesFob()
    {
        const int Gun = 41;
        const int Fob = 50;
        ParticleControlPoint fob = ParticleControlPoint.Unoriented(new Vector3(5f, 6f, 7f));
        FakePosedEntities models = new() { Named = { [(Fob, "attach_fob")] = fob, [(Gun, "bip_head")] = Head } };
        BurstEffects bursts = Presenter(models);
        EconParticleItem banner = Unusual("bip_head") with
        {
            Entity = Gun,
            Wearable = false,
            Received = 90,
            Holder = Worn with { IsWeapon = true },
            ExtraWearable = new EconExtraWearable(Fob, false),
        };

        bursts.AddEconParticles([banner], 1000, seeked: true, Systems);

        bursts.Bursts.ShouldHaveSingleItem().At.ShouldBe(fob);
        bursts.FastCullClearedAt(Gun).ShouldBe(1000);
    }

    /// <summary>
    /// **An effect follows its OWNER's dormancy** (<c>C_BaseEntity::SetDormant</c> → <c>ParticleProp()-&gt;OwnerSetDormantTo</c>,
    /// particle_property.cpp:465-475): on an extra wearable that has gone dormant it stops emitting and starts again at the
    /// wake, whatever the weapon's own dormancy. The burst asks the wearable's, not the weapon's. **Only from a change**: the
    /// create does not ask (particle_property.cpp:116-163), so an effect made while its owner is already dormant emits until
    /// the owner next goes dormant — here made by the weapon's own update at 1000 inside a dormant spell that ends at 1005.
    /// (One a seek meets running was made before its window: <see cref="AddEconParticles_ASeekIntoADormantSpell_IsDormantAsPlayingThroughIt"/>.)
    /// </summary>
    [Test]
    public void AddEconParticles_AnEffectOnAnExtraWearable_IsDormantWithTheWearable()
    {
        const int Gun = 41;
        const int Fob = 50;
        FakePosedEntities models = new() { Named = { [(Fob, "attach_fob")] = Head } };
        BurstEffects bursts = Presenter(models);
        EconParticleItem banner = Unusual("bip_head") with
        {
            Entity = Gun,
            Wearable = false,
            Received = 1000,
            Holder = Worn with { IsWeapon = true },
            ExtraWearable = new EconExtraWearable(Fob, false),
        };
        static bool Dormant(int entity, int at) => entity == Fob && (at < 1005 || at is >= 1010 and < 1020);

        bursts.AddEconParticles([banner with { Received = 90, State = EconParticleState.NotVisible }], 990, seeked: true, Systems, Dormant);
        bursts.AddEconParticles([banner], 1000, seeked: false, Systems, Dormant);
        bursts.AddEconParticles([banner], 1030, seeked: false, Systems, Dormant);

        ParticleBurst burst = bursts.Bursts[^1];
        burst.Tick.ShouldBe(1000, "made by the update, not met by a seek");
        System.Func<int, bool> dormant = burst.DormantAt.ShouldNotBeNull();
        (dormant(burst.Tick), dormant(1004), dormant(1009), dormant(1010), dormant(1019), dormant(1020))
            .ShouldBe((false, false, false, true, true, false));
    }

    /// <summary>
    /// **A seek answers what playing through shows** (D221, B69 review): an effect met running by a seek was made before its
    /// replay window, so a dormant spell inside the window pauses it — <c>OwnerSetDormantTo</c> on the transition, held until
    /// the wake (particle_property.cpp:465-475) — exactly as it did the effect played into it. The window's backdated start is
    /// not a create, so the "made while dormant emits" rule does not reach it. The comparison: the same effect played from its
    /// real create at 900 through a dormant spell [1000, 1100), and met by a seek to 1132, answer the same dormancy.
    /// </summary>
    [Test]
    public void AddEconParticles_ASeekIntoADormantSpell_IsDormantAsPlayingThroughIt()
    {
        const int Gun = 41;
        const int Fob = 50;
        EconParticleItem banner = Unusual("bip_head") with
        {
            Entity = Gun,
            Wearable = false,
            Received = 90,
            Holder = Worn with { IsWeapon = true },
            ExtraWearable = new EconExtraWearable(Fob, false),
        };
        static bool Dormant(int entity, int at) => entity == Fob && at is >= 1000 and < 1100;

        BurstEffects played = Presenter(new FakePosedEntities { Named = { [(Fob, "attach_fob")] = Head } });
        played.AddEconParticles([banner], 900, seeked: true, Systems, Dormant);
        played.AddEconParticles([banner], 1132, seeked: false, Systems, Dormant);

        BurstEffects seeked = Presenter(new FakePosedEntities { Named = { [(Fob, "attach_fob")] = Head } });
        seeked.AddEconParticles([banner], 1132, seeked: true, Systems, Dormant);

        // Every tick the replay steps to — the first is the window's start plus one; the start itself is asked only by the
        // build, where an effect met running answers awake (AddEconParticles_ASeekWhoseWindowStartsInADormantSpell_IsBuiltAsPlayingThroughIt).
        int[] ticks = [1001, 1050, 1099, 1100, 1132];
        Func<int, bool> playedDormant = played.Bursts[^1].DormantAt.ShouldNotBeNull();
        Func<int, bool> seekedDormant = seeked.Bursts.ShouldHaveSingleItem().DormantAt.ShouldNotBeNull();

        ticks.Select(seekedDormant).ShouldBe(ticks.Select(playedDormant));
        ticks.Select(playedDormant).ShouldBe([true, true, true, false, false], "the control: playing through the spell pauses it");
    }

    /// <summary>
    /// **An effect a seek meets running is BUILT when its window starts inside a dormant spell** (B69 review A): it was made
    /// before the window, so the particle system's "none is made on a dormant owner" (c_particle_system.cpp:189-190) does not
    /// reach the window's backdated start; the spell only pauses it (<c>OwnerSetDormantTo</c>, particle_property.cpp:465-475).
    /// Played from its real create at 700 through the spell [990, 1100), it is built and running at 1132; met by a seek to
    /// 1132, whose window starts at 1000 inside the spell, it must be too, and stay built on the next played frame.
    /// </summary>
    [Test]
    public void AddEconParticles_ASeekWhoseWindowStartsInADormantSpell_IsBuiltAsPlayingThroughIt()
    {
        static bool Dormant(int entity, int at) => entity == Hat && at is >= 990 and < 1100;
        EconParticleItem hat = Unusual("bip_head");

        ParticleEffects playedEffects = new();
        BurstEffects played = new(
            playedEffects, new FakePosedEntities { Named = { [(Hat, "bip_head")] = Head } }, new BulletStrikes(new FakePosedEntities(), new RecordingLogger()), new RecordingLogger());
        played.AddEconParticles([hat with { State = EconParticleState.NotVisible }], 500, seeked: true, Systems, Dormant);
        played.AddEconParticles([hat], 700, seeked: false, Systems, Dormant);
        playedEffects.Bursts(played.Bursts, 1f / 66f, Systems, 700, seeked: true);
        played.AddEconParticles([hat], 1132, seeked: false, Systems, Dormant);
        playedEffects.Bursts(played.Bursts, 1f / 66f, Systems, 1132);
        playedEffects.BurstSteps(played.Bursts[^1].Key).ShouldBeGreaterThanOrEqualTo(0, "the control: played through, it runs");

        ParticleEffects seekedEffects = new();
        BurstEffects seeked = new(
            seekedEffects, new FakePosedEntities { Named = { [(Hat, "bip_head")] = Head } }, new BulletStrikes(new FakePosedEntities(), new RecordingLogger()), new RecordingLogger());
        seeked.AddEconParticles([hat], 1132, seeked: true, Systems, Dormant);
        seekedEffects.Bursts(seeked.Bursts, 1f / 66f, Systems, 1132, seeked: true);
        long key = seeked.Bursts[^1].Key;
        seekedEffects.BurstSteps(key).ShouldBeGreaterThanOrEqualTo(0, "met running at the seek");

        seeked.AddEconParticles([hat], 1133, seeked: false, Systems, Dormant);
        seekedEffects.Bursts(seeked.Bursts, 1f / 66f, Systems, 1133);
        seekedEffects.BurstSteps(key).ShouldBeGreaterThanOrEqualTo(0, "and still the next frame");
    }

    /// <summary>
    /// **A gone extra wearable's effect is released, not removed** (particle_property.cpp:69-74, :251-281): offered once
    /// more under the same key, stopped at that tick and marked released, so the particle system fades it — or removes it
    /// when asleep — rather than the instant stop of every other econ end.
    /// </summary>
    [Test]
    public void AddEconParticles_AnExtraWearableGone_ReleasesItsEffectUnderTheSameKey()
    {
        const int Gun = 41;
        const int Fob = 50;
        FakePosedEntities models = new() { Named = { [(Fob, "attach_fob")] = Head } };
        BurstEffects bursts = Presenter(models);
        EconParticleItem banner = Unusual("bip_head") with
        {
            Entity = Gun,
            Wearable = false,
            Received = 90,
            Holder = Worn with { IsWeapon = true },
            ExtraWearable = new EconExtraWearable(Fob, false),
        };

        bursts.AddEconParticles([banner], 1000, seeked: true, Systems);
        ParticleBurst running = bursts.Bursts.ShouldHaveSingleItem();
        running.RemovedWhenUnoffered.ShouldBeTrue("the control: an econ effect is otherwise stopped instantly");

        models.Named.Remove((Fob, "attach_fob"));
        bursts.AddEconParticles([banner with { ExtraWearable = null }], 1001, seeked: false, Systems);

        ParticleBurst released = bursts.Bursts[^1];
        (released.Key, released.Released, released.StopTick, released.RemovedWhenUnoffered, released.Undrawn)
            .ShouldBe((running.Key, true, 1001, false, true));
    }

    /// <summary>
    /// **A release in the frame its owner's dormancy changed removes at once** (B69 d): <c>OwnerSetDormantTo</c> stamps
    /// <c>m_iDormancyChangedAtFrame</c> (particle_property.cpp:465-475), and <c>StopEmission</c>'s <c>bRemoveInstantly</c> is
    /// that stamp equal to <c>gpGlobals-&gt;framecount</c> (:251-281). So the effect is not offered to fade — unoffered, its
    /// instant stop removes it. The control: a wearable whose dormancy did not change that frame is released to fade.
    /// </summary>
    [TestCase(true, false)]
    [TestCase(false, true)]
    public void AddEconParticles_AnExtraWearableGoneTheFrameItsDormancyChanged_IsRemovedAtOnce(bool changed, bool fades)
    {
        const int Gun = 41;
        const int Fob = 50;
        FakePosedEntities models = new() { Named = { [(Fob, "attach_fob")] = Head } };
        BurstEffects bursts = Presenter(models);
        EconParticleItem banner = Unusual("bip_head") with
        {
            Entity = Gun,
            Wearable = false,
            Received = 90,
            Holder = Worn with { IsWeapon = true },
            ExtraWearable = new EconExtraWearable(Fob, false),
        };

        bool Dormant(int entity, int at) => changed && entity == Fob && at >= 1001;

        bursts.AddEconParticles([banner], 1000, seeked: true, Systems, Dormant);
        models.Named.Remove((Fob, "attach_fob"));
        bursts.AddEconParticles([banner with { ExtraWearable = null }], 1001, seeked: false, Systems, Dormant);

        bursts.Bursts.Any(burst => burst.Released).ShouldBe(fades);
    }

    /// <summary>The control: a hidden hat offers nothing and clears nothing.</summary>
    [Test]
    public void AddEconParticles_AHiddenItem_OffersNothing()
    {
        FakePosedEntities models = new() { Origins = { [Hat] = Origin } };
        BurstEffects bursts = Presenter(models);

        bursts.AddEconParticles([Unusual("bip_head") with { State = EconParticleState.NotVisible }], 1000, seeked: true, Systems);

        bursts.Bursts.ShouldBeEmpty();
        bursts.FastCullClearedAt(Hat).ShouldBeNull();
    }

    /// <summary>An effect on the local view model follows the view model's attachment, not the item's.</summary>
    [Test]
    public void AddEconParticles_AViewModelEffect_FollowsTheViewModel()
    {
        ParticleControlPoint barrel = ParticleControlPoint.Unoriented(new Vector3(1f, 2f, 3f));
        FakePosedEntities models = new()
        {
            Named = { [(Hat, "barrel")] = Head, [(ViewmodelScene.ArmsEntityIndex, "barrel")] = barrel },
        };
        BurstEffects bursts = Presenter(models);

        EconParticleItem gun = Unusual("barrel") with
        {
            Wearable = false,
            State = EconParticleState.VisibleViewModel,
            Systems = [new EconParticle("superrare_burning1", 0, false, DrawInViewModel: true, false, ["barrel"])],
            Holder = Worn with { IsWeapon = true, OwnerDrawnAsWorldModel = false, InLocalViewModel = true, HasViewModelOwner = true },
        };

        bursts.AddEconParticles([gun], 1000, seeked: true, Systems);

        bursts.Bursts.ShouldHaveSingleItem().At.ShouldBe(barrel);
    }

    /// <summary>
    /// **A view-model effect's point-attached control points are taken back into the view model's projection** —
    /// <c>FormatViewModelAttachment( vecOrigin, true )</c> on every <c>PATTACH_POINT_FOLLOW</c> point once the effect is
    /// <c>GetIsViewModelEffect()</c> (particle_property.cpp:598-628), control point 0 on the view model and 1..6 on the item
    /// alike — and the effect is marked one (<c>SetIsViewModelEffect( true )</c>, econ_entity.cpp:1771-1775). The control: the
    /// same effect on the world item is neither formatted nor marked.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void AddEconParticles_AViewModelEffectsPoints_AreFormattedBackAndMarked(bool viewModel)
    {
        ParticleControlPoint barrel = ParticleControlPoint.Unoriented(new Vector3(1f, 2f, 3f));
        ParticleControlPoint eyes = ParticleControlPoint.Unoriented(new Vector3(12f, 20f, 72f));
        Vector3 moved = new(0f, 0f, 1000f);
        FakePosedEntities models = new()
        {
            Named = { [(Hat, "barrel")] = Head, [(Hat, "eyes")] = eyes, [(ViewmodelScene.ArmsEntityIndex, "barrel")] = barrel },
            ViewModelFormat = point => point with { At = point.At + moved },
        };
        BurstEffects bursts = Presenter(models);

        EconParticleItem gun = Unusual("barrel") with
        {
            Wearable = false,
            State = viewModel ? EconParticleState.VisibleViewModel : EconParticleState.Visible,
            Systems = [new EconParticle("superrare_burning1", 0, false, DrawInViewModel: true, false, ["barrel", "eyes"])],
            Holder = Worn with
            {
                IsWeapon = true, OwnerDrawnAsWorldModel = !viewModel, InLocalViewModel = viewModel, HasViewModelOwner = true,
            },
        };

        bursts.AddEconParticles([gun], 1000, seeked: true, Systems);

        ParticleBurst burst = bursts.Bursts.ShouldHaveSingleItem();

        if (viewModel)
        {
            (burst.At.At, burst.ViewModel).ShouldBe((barrel.At + moved, true));
            burst.Points.ShouldBe([(1, eyes with { At = eyes.At + moved })]);
        }
        else
        {
            (burst.At, burst.ViewModel).ShouldBe((Head, false));
            burst.Points.ShouldBe([(1, eyes)]);
        }
    }

    /// <summary>
    /// **A definition flagged <c>view model effect</c> is one wherever it is made** (B69 a): <c>m_bViewModelEffect</c> starts
    /// as the definition's (particles_new.cpp:68), so <c>UpdateControlPoint</c> formats its point-attached points back into
    /// the view model's projection (particle_property.cpp:625-628) even on a world item, which never calls
    /// <c>SetIsViewModelEffect</c>. The control: the same effect without the flag is left in the world.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void AddEconParticles_ADefinitionFlaggedViewModelOnAWorldItem_HasItsPointsFormatted(bool flagged)
    {
        Vector3 moved = new(0f, 0f, 1000f);
        FakePosedEntities models = new()
        {
            Named = { [(Hat, "bip_head")] = Head },
            ViewModelFormat = point => point with { At = point.At + moved },
        };
        BurstEffects bursts = Presenter(models);
        ParticleSystem definition = Burning with
        {
            Parameters = new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["view model effect"] = new(DmxAttributeType.Boolean, flagged ? 1d : 0d),
            },
        };

        bursts.AddEconParticles([Unusual("bip_head")], 1000, seeked: true, new Dictionary<string, ParticleSystem>(StringComparer.Ordinal) { [Burning.Name] = definition });

        bursts.Bursts.ShouldHaveSingleItem().At.At.ShouldBe(flagged ? Head.At + moved : Head.At);
    }

    /// <summary>
    /// **The decision comes before the pose and the offer after it** (B69 g): deciding alone clears the fast-cull — what the
    /// pose reads (cdll_client_int.cpp:2193-2239) — and offers nothing; the draw then offers what was decided.
    /// </summary>
    [Test]
    public void DecideEconParticles_BeforeTheDraw_ClearsTheFastCullAndTheDrawOffersIt()
    {
        FakePosedEntities models = new() { Named = { [(Hat, "bip_head")] = Head }, Origins = { [Hat] = Origin } };
        BurstEffects bursts = Presenter(models);

        bursts.DecideEconParticles([Unusual("bip_head")], 1000, landed: true, Systems.ContainsKey);

        bursts.FastCullClearedAt(Hat).ShouldBe(1000);
        bursts.Bursts.ShouldBeEmpty("nothing is offered until the draw");

        bursts.DrawEconParticles(1000, Systems);

        bursts.Bursts.ShouldHaveSingleItem().At.ShouldBe(Head);
    }

    /// <summary>
    /// **The attachment is the owner's model's, not a posed one's** (B69 g): <c>LookupAttachment</c> reads the model
    /// (econ_entity.cpp:1649-1653), so an item whose bones this frame has not built yet still creates on its attachment —
    /// <c>PATTACH_POINT_FOLLOW</c> — and follows it once posed.
    /// </summary>
    [Test]
    public void DecideEconParticles_AnAttachmentOfTheModelNotYetPosed_CreatesOnIt()
    {
        FakePosedEntities models = new() { Indices = { [(Hat, "bip_head")] = 3 }, Origins = { [Hat] = Origin } };
        BurstEffects bursts = Presenter(models);

        bursts.DecideEconParticles([Unusual("bip_head")], 1000, landed: true, Systems.ContainsKey);
        models.Named[(Hat, "bip_head")] = Head;
        bursts.DrawEconParticles(1000, Systems);

        bursts.Bursts.ShouldHaveSingleItem().At.ShouldBe(Head);
    }

    /// <summary>
    /// **The decision looks an item's hands model up by its path** (B69 review D): the presenter hands the runs the models'
    /// own lookup by path, so a hands model no pass has posed still finds its <c>muzzle</c>. The control: the hands entity
    /// itself answers nothing.
    /// </summary>
    [Test]
    public void DecideEconParticles_AHandsModelNotYetPosed_IsLookedUpByItsPath()
    {
        FakePosedEntities models = new() { ModelIndices = { [("models/c_gun.mdl", "muzzle")] = 2 } };
        BurstEffects bursts = Presenter(models);
        EconParticleItem gun = Unusual("muzzle") with
        {
            Entity = 41,
            Wearable = false,
            Received = 90,
            Holder = Worn with { IsWeapon = true },
            AttachmentModel = true,
            HandsModel = "models/c_gun.mdl",
        };
        models.AttachmentIndex(ViewmodelScene.WeaponEntityIndex, "muzzle").ShouldBe(0, "the control");

        bursts.DecideEconParticles([gun], 1000, landed: true, Systems.ContainsKey);

        bursts.EconDecided.ShouldHaveSingleItem().Effect.Attachment.ShouldBe("muzzle");
    }

    /// <summary>
    /// **A map change forgets every econ entity's created state** (B69 review): the next level's entities reuse the indices,
    /// and a kept record would hand one a clear it never made (<see cref="EconParticleRuns.FastCull"/>) and its runs. The
    /// client destroys every entity with the level, and a new one is made <c>NOT_VISIBLE</c> (econ_entity.cpp:1022-1027).
    /// The control: before the change the clear and the run are there.
    /// </summary>
    [Test]
    public void ClearMap_AfterAnEffectWasMade_ForgetsItsClearAndItsRun()
    {
        FakePosedEntities models = new() { Named = { [(Hat, "bip_head")] = Head } };
        BurstEffects bursts = Presenter(models);

        bursts.DecideEconParticles([Unusual("bip_head")], 1000, landed: true, Systems.ContainsKey);
        (bursts.FastCullClearedAt(Hat), bursts.EconDecided.Count).ShouldBe((1000, 1), "the control");

        bursts.ClearMap();
        bursts.DrawEconParticles(1001, Systems);

        (bursts.FastCullClearedAt(Hat), bursts.EconDecided.Count, bursts.Bursts.Count).ShouldBe((null, 0, 0));
    }

    /// <remarks>
    /// **A running effect's frame allocates nothing** (B69 review; the target is 1000 fps, so a per-frame allocation in a hot
    /// path is a defect): the decision's attachment lookup, the control points' list, the owner's dormancy, its history and
    /// its model are reused, not made per run per frame. Three frames before the measured one: the list of offers is never
    /// cleared here (`Step` clears it), so its growth to 4 happens before the measurement.
    /// </remarks>
    [Test]
    public void DrawEconParticles_ARunningEffectsFrame_AllocatesNothing()
    {
        FakePosedEntities models = new()
        {
            Named = { [(Hat, "bip_head")] = Head, [(Hat, "eyes")] = Origin },
        };
        BurstEffects bursts = Presenter(models);
        EconParticleItem hat = Unusual("bip_head") with
        {
            Systems = [new EconParticle("superrare_burning1", 0, false, false, false, ["bip_head", "eyes"])],
        };
        EconParticleItem[] items = [hat];
        Func<string, bool> found = Systems.ContainsKey;
        Func<int, int, bool> dormantAt = static (_, at) => at < 0;

        for (int tick = 1000; tick < 1003; tick++)
        {
            bursts.DecideEconParticles(items, tick, landed: tick == 1000, found);
            bursts.DrawEconParticles(tick, Systems, dormantAt);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        bursts.DecideEconParticles(items, 1003, landed: false, found);
        bursts.DrawEconParticles(1003, Systems, dormantAt);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0);
        ParticleBurst burst = bursts.Bursts[^1];
        (burst.At, burst.Points!.Count, burst.DormantAt!(1003), burst.History!(1003).At).ShouldBe((Head, 1, false, Head), "the control: it was offered");
    }

    /// <summary>
    /// **An item with a hands model is placed by that model's attachments** — <c>CEconEntity::GetAttachment</c> answers from
    /// <c>m_hViewmodelAttachment</c> (econ_entity.cpp:1978-1984), which <c>UpdateControlPoint</c> asks for every point-attached
    /// point (particle_property.cpp:598-623): control point 0 and the item's control points 1..6 alike. The control: without
    /// one they are the item's own.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void DrawEconParticles_AnItemWithAHandsModel_IsPlacedByItsAttachments(bool handsModel)
    {
        const int Gun = 41;
        ParticleControlPoint handsMuzzle = ParticleControlPoint.Unoriented(new Vector3(1f, 2f, 3f));
        ParticleControlPoint handsEyes = ParticleControlPoint.Unoriented(new Vector3(4f, 5f, 6f));
        FakePosedEntities models = new()
        {
            Named =
            {
                [(Gun, "muzzle")] = Head,
                [(Gun, "eyes")] = Origin,
                [(ViewmodelScene.WeaponEntityIndex, "muzzle")] = handsMuzzle,
                [(ViewmodelScene.WeaponEntityIndex, "eyes")] = handsEyes,
            },
        };
        BurstEffects bursts = Presenter(models);
        EconParticleItem gun = Unusual("muzzle") with
        {
            Entity = Gun,
            Wearable = false,
            Received = 90,
            Systems = [new EconParticle("superrare_burning1", 0, false, false, false, ["muzzle", "eyes"])],
            Holder = Worn with { IsWeapon = true },
            AttachmentModel = handsModel,
        };

        bursts.AddEconParticles([gun], 1000, seeked: true, Systems);

        ParticleBurst burst = bursts.Bursts.ShouldHaveSingleItem();
        (burst.At, burst.Points!.Single().At).ShouldBe(handsModel ? (handsMuzzle, handsEyes) : (Head, Origin));
    }

    private static BurstEffects Presenter(FakePosedEntities models) =>
        new(new ParticleEffects(), models, new BulletStrikes(models, new RecordingLogger()), new RecordingLogger());

    private static EconParticleItem Unusual(string attachment) =>
        new(Hat, Wearable: true, Received: null, Dormant: false, EconParticleState.Visible,
            [new EconParticle("superrare_burning1", 0, false, false, false, [attachment])], Worn);
}
