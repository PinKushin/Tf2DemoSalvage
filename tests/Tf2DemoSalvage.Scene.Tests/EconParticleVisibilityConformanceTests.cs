namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CEconEntity::UpdateParticleSystems</c> (econ_entity.cpp:1224-1262) and the three <c>ShouldDrawParticleSystems</c>
/// (econ_entity.cpp:1266-1305, econ_wearable.cpp:438-452, tf_item_wearable.cpp:417-450): whether an item's attached effects
/// show (B69 f).
/// </summary>
/// <remarks>
/// <code>
/// if ( IsEffectActive( EF_NODRAW ) || !ShouldDraw() )                         NOT_VISIBLE
/// else if ( !GetOwnerEntity() &amp;&amp; !IsDormant() )                              VISIBLE
/// else if ( owner &amp;&amp; !owner->IsDormant() &amp;&amp; owner->IsPlayer() &amp;&amp; owner->IsAlive() ) VISIBLE
/// if NOT_VISIBLE and the local player holds this weapon in his view model     VISIBLE_VM
/// if not NOT_VISIBLE and !ShouldDrawParticleSystems()                         NOT_VISIBLE
/// </code>
/// A weapon's override: not while its owner is stealthed, nor disguised unless it is the disguise weapon, and only when
/// the effect's owner draws. A wearable's: a player owner drawn as a world model (<c>ShouldDrawThisPlayer</c>), not
/// stealthed, and not the World Traveler, whose <c>MapInfo_DidPlayerDonate</c> a demo client answers false.
/// </remarks>
public sealed class EconParticleVisibilityConformanceTests
{
    /// <summary>A hat on a live, drawn player, with a local player — VISIBLE.</summary>
    private static readonly EconParticleView Hat = new(
        EconParticleOwnerKind.Wearable,
        NoDraw: false,
        ShouldDraw: true,
        HasOwner: true,
        Dormant: false,
        OwnerIsPlayer: true,
        OwnerDormant: false,
        OwnerAlive: true,
        HeldInLocalViewModel: false,
        OwnerStealthed: false,
        OwnerDisguised: false,
        DisguiseWeapon: false,
        HasLocalPlayer: true,
        EffectOwnerDraws: true,
        OwnerDrawnAsWorldModel: true,
        HiddenWorldTraveler: false);

    /// <summary>The same as a weapon.</summary>
    private static readonly EconParticleView Gun = Hat with { Kind = EconParticleOwnerKind.Entity };

    [Test]
    public void State_AHatOnALiveDrawnPlayer_IsVisible() =>
        EconParticleVisibility.State(Hat).ShouldBe(EconParticleState.Visible);

    [Test]
    public void State_NoDraw_IsNotVisible() =>
        EconParticleVisibility.State(Hat with { NoDraw = true }).ShouldBe(EconParticleState.NotVisible);

    [Test]
    public void State_NotDrawn_IsNotVisible() =>
        EconParticleVisibility.State(Hat with { ShouldDraw = false }).ShouldBe(EconParticleState.NotVisible);

    [Test]
    public void State_AnOwnerlessWeaponNotDormant_IsVisible() =>
        EconParticleVisibility.State(Gun with { HasOwner = false, OwnerIsPlayer = false, OwnerAlive = false }).ShouldBe(EconParticleState.Visible);

    [Test]
    public void State_AnOwnerlessWeaponDormant_IsNotVisible() =>
        EconParticleVisibility.State(Gun with { HasOwner = false, Dormant = true }).ShouldBe(EconParticleState.NotVisible);

    [Test]
    public void State_TheOwnerDormant_IsNotVisible() =>
        EconParticleVisibility.State(Hat with { OwnerDormant = true }).ShouldBe(EconParticleState.NotVisible);

    [Test]
    public void State_TheOwnerDead_IsNotVisible() =>
        EconParticleVisibility.State(Hat with { OwnerAlive = false }).ShouldBe(EconParticleState.NotVisible);

    [Test]
    public void State_AWeaponHeldInTheLocalViewModelButNotDrawn_IsVisibleInTheViewModel() =>
        EconParticleVisibility.State(Gun with { ShouldDraw = false, HeldInLocalViewModel = true }).ShouldBe(EconParticleState.VisibleViewModel);

    [Test]
    public void State_AWearableWhoseOwnerIsInFirstPerson_IsNotVisible() =>
        EconParticleVisibility.State(Hat with { OwnerDrawnAsWorldModel = false }).ShouldBe(EconParticleState.NotVisible);

    [Test]
    public void State_AWearableWhoseOwnerIsStealthed_IsNotVisible() =>
        EconParticleVisibility.State(Hat with { OwnerStealthed = true }).ShouldBe(EconParticleState.NotVisible);

    [Test]
    public void State_TheWorldTravelerWithNoDonationData_IsNotVisible() =>
        EconParticleVisibility.State(Hat with { HiddenWorldTraveler = true }).ShouldBe(EconParticleState.NotVisible);

    /// <summary>A disguise is only an assert in the wearable's override (tf_item_wearable.cpp:425-427).</summary>
    [Test]
    public void State_AWearableWhoseOwnerIsDisguised_IsVisible() =>
        EconParticleVisibility.State(Hat with { OwnerDisguised = true }).ShouldBe(EconParticleState.Visible);

    [Test]
    public void State_AWeaponWhoseOwnerIsDisguised_IsNotVisible() =>
        EconParticleVisibility.State(Gun with { OwnerDisguised = true }).ShouldBe(EconParticleState.NotVisible);

    [Test]
    public void State_TheDisguiseWeaponOfADisguisedOwner_IsVisible() =>
        EconParticleVisibility.State(Gun with { OwnerDisguised = true, DisguiseWeapon = true }).ShouldBe(EconParticleState.Visible);

    [Test]
    public void State_AWeaponWhoseOwnerIsStealthed_IsNotVisible() =>
        EconParticleVisibility.State(Gun with { OwnerStealthed = true }).ShouldBe(EconParticleState.NotVisible);

    [Test]
    public void State_AWeaponWhoseEffectOwnerIsNotDrawn_IsNotVisible() =>
        EconParticleVisibility.State(Gun with { EffectOwnerDraws = false }).ShouldBe(EconParticleState.NotVisible);

    [Test]
    public void State_AWeaponWithNoLocalPlayer_IgnoresTheEffectOwner() =>
        EconParticleVisibility.State(Gun with { EffectOwnerDraws = false, HasLocalPlayer = false }).ShouldBe(EconParticleState.Visible);

    /// <summary>A weapon's override never asks <c>ShouldDrawThisPlayer</c>; only the wearable's does.</summary>
    [Test]
    public void State_AWeaponWhoseOwnerIsInFirstPerson_IsVisible() =>
        EconParticleVisibility.State(Gun with { OwnerDrawnAsWorldModel = false }).ShouldBe(EconParticleState.Visible);
}
