using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CEconEntity::UpdateSingleParticleSystem</c>'s create decision (econ_entity.cpp:1592-1778, B69 g): whether a visible
/// entry makes an effect, on which entity, how it is attached, and that making one clears <c>EF_BONEMERGE_FASTCULL</c>.
/// </summary>
/// <remarks>
/// No local player: nothing. A player owner not drawn as a world model (<c>!ShouldDrawThisPlayer</c>) draws only
/// <c>bDrawInViewModel</c> entries, on the view model when the local view model's weapon is this one. The attachment is the
/// entry's control point 0, or <c>attach_fob</c>/<c>attach_fob_v</c> on an extra wearable; found on the effect owner it is
/// <c>PATTACH_POINT_FOLLOW</c>, else <c>PATTACH_ROOTBONE_FOLLOW</c> for <c>bFollowRootBone</c>, else
/// <c>PATTACH_ABSORIGIN_FOLLOW</c>. Control points 1..6 follow THIS entity's named attachments. Every create runs
/// <c>RemoveEffects( EF_BONEMERGE_FASTCULL )</c> first (:1733-1740).
/// </remarks>
public sealed class EconParticleCreateConformanceTests
{
    /// <summary>A hat on a player drawn as a world model, the local player watching.</summary>
    private static readonly EconParticleHolder Hat = new(
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
    public void Create_AVisibleEntryWithNoAttachment_FollowsTheAbsOrigin()
    {
        EconParticleCreate made = Make(Entry("superrare_burning1"), Hat).ShouldNotBeNull();

        (made.System, made.Attach, made.Attachment, made.OnViewModel).ShouldBe(
            ("superrare_burning1", EconParticleAttach.AbsOriginFollow, (string?)null, false));
    }

    [Test]
    public void Create_AnAttachmentTheItemHas_FollowsThePoint()
    {
        EconParticleCreate made = Make(Entry("notes", attachment: "ear_R"), Hat, has: (_, name) => name == "ear_R").ShouldNotBeNull();

        (made.Attach, made.Attachment).ShouldBe((EconParticleAttach.PointFollow, "ear_R"));
    }

    [Test]
    public void Create_AnAttachmentTheItemLacksWithFollowRootBone_FollowsTheRootBone() =>
        Make(Entry("notes", attachment: "ear_R", rootBone: true), Hat).ShouldNotBeNull().Attach.ShouldBe(EconParticleAttach.RootBoneFollow);

    [Test]
    public void Create_NotVisible_MakesNothing() =>
        EconParticleSystems.Create(Entry("x"), visible: false, Hat, Loaded, (_, _) => false).ShouldBeNull();

    [Test]
    public void Create_NoLocalPlayer_MakesNothing() =>
        Make(Entry("x"), Hat with { HasLocalPlayer = false }).ShouldBeNull();

    [Test]
    public void Create_ASystemTheLevelLacks_MakesNothing() =>
        EconParticleSystems.Create(Entry("x"), visible: true, Hat, _ => false, (_, _) => false).ShouldBeNull();

    [Test]
    public void Create_AWorldOnlyEntryOfAPlayerInFirstPerson_MakesNothing() =>
        Make(Entry("x"), Hat with { OwnerDrawnAsWorldModel = false }).ShouldBeNull();

    [Test]
    public void Create_AViewModelEntryOfAPlayerInFirstPerson_StaysOnTheItemWhenNotHeld() =>
        Make(Entry("x", viewModel: true), Hat with { OwnerDrawnAsWorldModel = false }).ShouldNotBeNull().OnViewModel.ShouldBeFalse();

    [Test]
    public void Create_AViewModelEntryOfTheHeldWeaponInFirstPerson_GoesOnTheViewModel()
    {
        EconParticleHolder held = Hat with { IsWeapon = true, OwnerDrawnAsWorldModel = false, InLocalViewModel = true, HasViewModelOwner = true };

        Make(Entry("x", viewModel: true), held).ShouldNotBeNull().OnViewModel.ShouldBeTrue();
    }

    [Test]
    public void Create_TheViewModelWithNoViewModelOwner_MakesNothing() =>
        Make(Entry("x", viewModel: true), Hat with { IsWeapon = true, OwnerDrawnAsWorldModel = false, InLocalViewModel = true })
            .ShouldBeNull();

    [Test]
    public void Create_AWeaponWithAnExtraWearable_HangsFromAttachFob()
    {
        List<string> asked = [];
        EconParticleCreate made = EconParticleSystems.Create(
            Entry("x", attachment: "muzzle"), visible: true, Hat with { IsWeapon = true, ExtraWearable = true }, Loaded,
            (_, name) => { asked.Add(name); return true; }).ShouldNotBeNull();

        (made.Attachment, asked.ShouldHaveSingleItem()).ShouldBe(("attach_fob", "attach_fob"));
    }

    [Test]
    public void Create_ControlPointsOneToSix_FollowTheItemsNamedAttachments()
    {
        EconParticle entry = new("x", 0, false, false, false, [null, null, "eyes", string.Empty, null, null, "hand"]);

        Make(entry, Hat).ShouldNotBeNull().ControlPoints.ShouldBe([(2, "eyes"), (6, "hand")]);
    }

    /// <summary>
    /// **A control point the ITEM lacks stops the effect as it is made.** <c>AddControlPoint( pEffect, i, this,
    /// PATTACH_POINT_FOLLOW, name )</c> looks the name up on the item itself (<c>econ_entity.cpp:1762-1770</c>); a miss is
    /// <c>INVALID_PARTICLE_ATTACHMENT</c>, 0 on the client (<c>particle_property.h:26</c>), which
    /// <c>GetAttachment( 0, … )</c> refuses (<c>c_baseanimating.cpp</c>, <c>number &lt; 1</c>), so the first
    /// <c>UpdateControlPoint</c> warns and <c>StopParticlesNamed</c> (<c>particle_property.cpp:608-617</c>) before it ever
    /// simulates. Asked on the world item even for a view-model effect, whose owner is the view model.
    /// </summary>
    [TestCase(true, false)]
    [TestCase(false, true)]
    public void Create_AControlPointTheItemLacks_StopsTheEffectAsItIsMade(bool itemHasEyes, bool stopped)
    {
        EconParticle entry = new("x", 0, false, DrawInViewModel: true, false, ["muzzle", null, "eyes"]);
        EconParticleHolder gun = Hat with
        {
            IsWeapon = true, OwnerDrawnAsWorldModel = false, InLocalViewModel = true, HasViewModelOwner = true,
        };

        // The view model has every attachment; the world item has `eyes` only when the case says so.
        EconParticleCreate made = Make(entry, gun, (viewModel, name) => viewModel || (itemHasEyes && name == "eyes"))
            .ShouldNotBeNull();

        (made.OnViewModel, made.StoppedAsMade).ShouldBe((true, stopped));
    }

    private static Func<string, bool> Loaded => _ => true;

    private static EconParticleCreate? Make(EconParticle entry, EconParticleHolder holder, Func<bool, string, bool>? has = null) =>
        EconParticleSystems.Create(entry, visible: true, holder, Loaded, has ?? ((_, _) => false));

    private static EconParticle Entry(string system, string? attachment = null, bool rootBone = false, bool viewModel = false) =>
        new(system, 0, rootBone, viewModel, false, [attachment]);
}
