namespace Tf2DemoSalvage.Scene;

/// <summary><c>ParticleSystemState_t</c>: whether an econ item's attached effects show, and where.</summary>
public enum EconParticleState
{
    /// <summary><c>PARTICLE_SYSTEM_STATE_NOT_VISIBLE</c>.</summary>
    NotVisible,

    /// <summary><c>PARTICLE_SYSTEM_STATE_VISIBLE</c>.</summary>
    Visible,

    /// <summary><c>PARTICLE_SYSTEM_STATE_VISIBLE_VM</c>: on the local player's view model.</summary>
    VisibleViewModel,
}

/// <summary>Which <c>ShouldDrawParticleSystems</c> override an econ entity runs.</summary>
public enum EconParticleOwnerKind
{
    /// <summary><c>CEconEntity</c>'s own (econ_entity.cpp:1266-1305): weapons, and any econ entity that is not a wearable.</summary>
    Entity,

    /// <summary><c>CTFWearable</c>'s (tf_item_wearable.cpp:417-450) over <c>CEconWearable</c>'s (econ_wearable.cpp:438-452).</summary>
    Wearable,
}

/// <summary>What <c>UpdateParticleSystems</c> and <c>ShouldDrawParticleSystems</c> read of an econ entity at one moment.</summary>
/// <param name="Kind">Which override runs.</param>
/// <param name="NoDraw"><c>IsEffectActive( EF_NODRAW )</c>.</param>
/// <param name="ShouldDraw">The entity's own <c>ShouldDraw()</c>.</param>
/// <param name="HasOwner"><c>GetOwnerEntity()</c> is non-null.</param>
/// <param name="Dormant">The entity's <c>IsDormant()</c>.</param>
/// <param name="OwnerIsPlayer">The owner is a player (<c>IsPlayer()</c>, <c>ToBasePlayer</c>, <c>ToTFPlayer</c> alike in TF).</param>
/// <param name="OwnerDormant">The owner's <c>IsDormant()</c>.</param>
/// <param name="OwnerAlive">The owner's <c>IsAlive()</c>.</param>
/// <param name="HeldInLocalViewModel">
/// The <c>VISIBLE_VM</c> test (econ_entity.cpp:1244-1253): a weapon whose owner is the local player, held in his view model,
/// with <c>!ShouldDrawLocalPlayer()</c> and him alive.
/// </param>
/// <param name="OwnerStealthed">The owner's <c>m_Shared.IsStealthed()</c>.</param>
/// <param name="OwnerDisguised">The owner's <c>InCond( TF_COND_DISGUISED )</c>.</param>
/// <param name="DisguiseWeapon">A weapon's <c>m_bDisguiseWeapon</c>.</param>
/// <param name="HasLocalPlayer"><c>C_BasePlayer::GetLocalPlayer()</c> is non-null.</param>
/// <param name="EffectOwnerDraws">
/// <c>pEffectOwner-&gt;ShouldDraw()</c> (econ_entity.cpp:1288-1301): the local view model's when the local player owns it in
/// first person, else the entity's own.
/// </param>
/// <param name="OwnerDrawnAsWorldModel">The owner's <c>ShouldDrawThisPlayer()</c> (c_baseplayer.cpp:1992-2011).</param>
/// <param name="HiddenWorldTraveler">
/// The item is the "World Traveler" and <c>MapInfo_DidPlayerDonate</c> is false — which a demo client always answers, as it
/// holds no shared-object cache for the item's account (tf_mapinfo.cpp:751-765).
/// </param>
public readonly record struct EconParticleView(
    EconParticleOwnerKind Kind,
    bool NoDraw,
    bool ShouldDraw,
    bool HasOwner,
    bool Dormant,
    bool OwnerIsPlayer,
    bool OwnerDormant,
    bool OwnerAlive,
    bool HeldInLocalViewModel,
    bool OwnerStealthed,
    bool OwnerDisguised,
    bool DisguiseWeapon,
    bool HasLocalPlayer,
    bool EffectOwnerDraws,
    bool OwnerDrawnAsWorldModel,
    bool HiddenWorldTraveler);

/// <summary><c>CEconEntity::UpdateParticleSystems</c> (econ_entity.cpp:1224-1262) and its <c>ShouldDrawParticleSystems</c> (B69 f).</summary>
public static class EconParticleVisibility
{
    /// <summary>The item definition the World Traveler rule names (<c>CSchemaItemDefHandle pItemDef_MapLoverHat( "World Traveler" )</c>).</summary>
    public const string WorldTraveler = "World Traveler";

    /// <summary>The state <c>UpdateParticleSystems</c> hands <c>SetParticleSystemsVisible</c>.</summary>
    /// <param name="view">The entity at the moment.</param>
    /// <returns>The state.</returns>
    public static EconParticleState State(in EconParticleView view)
    {
        EconParticleState state = EconParticleState.NotVisible;

        if (view.NoDraw || !view.ShouldDraw)
        {
            state = EconParticleState.NotVisible;
        }
        else if (!view.HasOwner && !view.Dormant)
        {
            state = EconParticleState.Visible;
        }
        else if (view.HasOwner && !view.OwnerDormant && view.OwnerIsPlayer && view.OwnerAlive)
        {
            state = EconParticleState.Visible;
        }

        // "Make sure the entity we're attaching to is being drawn" (:1242-1254).
        if (state == EconParticleState.NotVisible && view.HeldInLocalViewModel)
        {
            state = EconParticleState.VisibleViewModel;
        }

        return state != EconParticleState.NotVisible && !ShouldDrawParticleSystems(view) ? EconParticleState.NotVisible : state;
    }

    /// <summary>The override <see cref="EconParticleView.Kind"/> names.</summary>
    /// <param name="view">The entity at the moment.</param>
    /// <returns>Whether its systems may draw.</returns>
    public static bool ShouldDrawParticleSystems(in EconParticleView view) =>
        view.Kind == EconParticleOwnerKind.Wearable ? WearableDraws(view) : EntityDraws(view);

    /// <summary><c>CEconEntity::ShouldDrawParticleSystems</c> (econ_entity.cpp:1266-1305).</summary>
    private static bool EntityDraws(in EconParticleView view)
    {
        // `ToTFPlayer( GetOwnerEntity() )` (:1269-1286).
        if (view.HasOwner && view.OwnerIsPlayer &&
            (view.OwnerStealthed || (view.OwnerDisguised && !view.DisguiseWeapon)))
        {
            return false;
        }

        return !view.HasLocalPlayer || view.EffectOwnerDraws;
    }

    /// <summary>
    /// <c>CTFWearable::ShouldDrawParticleSystems</c> (tf_item_wearable.cpp:417-450): <c>CEconWearable</c>'s — a player owner
    /// whose <c>ShouldDrawThisPlayer()</c> holds — then not stealthed, then the World Traveler's donation.
    /// </summary>
    private static bool WearableDraws(in EconParticleView view) =>
        view.HasOwner && view.OwnerIsPlayer && view.OwnerDrawnAsWorldModel && !view.OwnerStealthed && !view.HiddenWorldTraveler;
}
