using System;
using System.Collections.Generic;
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

    private static BurstEffects Presenter(FakePosedEntities models) =>
        new(new ParticleEffects(), models, new BulletStrikes(models, new RecordingLogger()), new RecordingLogger());

    private static EconParticleItem Unusual(string attachment) =>
        new(Hat, Wearable: true, Received: null, Dormant: false, EconParticleState.Visible,
            [new EconParticle("superrare_burning1", 0, false, false, false, [attachment])], Worn);
}
