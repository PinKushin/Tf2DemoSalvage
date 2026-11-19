using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene;

/// <summary>How a created effect follows its owner — <c>ParticleAttachment_t</c>.</summary>
public enum EconParticleAttach
{
    /// <summary><c>PATTACH_ABSORIGIN_FOLLOW</c>.</summary>
    AbsOriginFollow,

    /// <summary><c>PATTACH_ROOTBONE_FOLLOW</c>.</summary>
    RootBoneFollow,

    /// <summary><c>PATTACH_POINT_FOLLOW</c>.</summary>
    PointFollow,
}

/// <summary>What <c>UpdateSingleParticleSystem</c> reads of the item and the people around it (B69 g).</summary>
/// <param name="HasLocalPlayer"><c>C_BasePlayer::GetLocalPlayer()</c> is non-null.</param>
/// <param name="HasOwner">A player owner, <c>ToBasePlayer( GetOwnerEntity() )</c>.</param>
/// <param name="IsWeapon">A <c>CTFWeaponBase</c>.</param>
/// <param name="ExtraWearable">The weapon's <c>m_hExtraWearable</c> is live.</param>
/// <param name="ExtraWearableViewModel">The weapon's <c>m_hExtraWearableViewModel</c> is live.</param>
/// <param name="OwnerDrawnAsWorldModel">The owner's <c>ShouldDrawThisPlayer()</c>.</param>
/// <param name="InLocalViewModel">The local view model's <c>GetOwningWeapon() == this</c>.</param>
/// <param name="HasViewModelOwner">
/// <c>pEffectOwnerVM</c> is non-null: the extra view-model wearable, or the owner's view model.
/// </param>
/// <param name="ParticleSuffix">The item definition's <c>GetParticleSuffix()</c>, or null.</param>
public readonly record struct EconParticleHolder(
    bool HasLocalPlayer,
    bool HasOwner,
    bool IsWeapon,
    bool ExtraWearable,
    bool ExtraWearableViewModel,
    bool OwnerDrawnAsWorldModel,
    bool InLocalViewModel,
    bool HasViewModelOwner,
    string? ParticleSuffix);

/// <summary>An effect <c>UpdateSingleParticleSystem</c> creates, which also cleared the item's fast-cull bit (B69 g).</summary>
/// <param name="System">The system created.</param>
/// <param name="Attach">How it follows its owner.</param>
/// <param name="Attachment">The attachment it follows for <see cref="EconParticleAttach.PointFollow"/>, else null.</param>
/// <param name="OnViewModel">Its owner is the view model (or its extra wearable) rather than the world item.</param>
/// <param name="ControlPoints">Control points 1..6 following this item's named attachments.</param>
/// <param name="StoppedAsMade">
/// One of those attachments is missing on the item, so the effect is stopped before it simulates (B69): created — and the
/// fast-cull cleared — but never emitting.
/// </param>
public sealed record EconParticleCreate(
    string System,
    EconParticleAttach Attach,
    string? Attachment,
    bool OnViewModel,
    IReadOnlyList<(int Point, string Attachment)> ControlPoints,
    bool StoppedAsMade = false);

/// <summary>
/// The stops <c>UpdateSingleParticleSystem</c> makes before it creates (<c>econ_entity.cpp:1695-1720</c>, B69): on the world
/// owner and, when there is one, the view-model owner, every effect of <see cref="Name"/> — and on the view-model owner
/// <see cref="UniqueViewModelName"/> too — whichever item made it.
/// </summary>
/// <param name="Name">The resolved system, <c>pszSystemName</c>.</param>
/// <param name="UniqueViewModelName"><c>pszTempNameVM</c> when it is a <c>_vm</c> system of its own, else null.</param>
/// <param name="Attachment">
/// <c>pszAttachmentName</c> when it was found on the effect owner: only effects with a control point on it stop
/// (<c>StopParticlesWithNameAndAttachment</c>, particle_property.cpp:389-422); null stops every one of the name
/// (<c>StopParticlesNamed</c>, :359-386).
/// </param>
public sealed record EconParticleStop(string Name, string? UniqueViewModelName, string? Attachment);

/// <summary><c>UpdateSingleParticleSystem</c>'s decision (<c>econ_entity.cpp:1592-1778</c>).</summary>
public static partial class EconParticleSystems
{
    /// <summary><c>attach_fob</c>, the extra wearable's attachment in the world.</summary>
    private const string Fob = "attach_fob";

    /// <summary><c>attach_fob_v</c>, its view-model twin.</summary>
    private const string FobViewModel = "attach_fob_v";

    /// <summary>Whether a visible entry creates an effect, and how; null when it creates none.</summary>
    /// <param name="system">The entry.</param>
    /// <param name="visible"><c>bVisible</c>, from <c>SetParticleSystemsVisible</c>.</param>
    /// <param name="holder">The item and its owners.</param>
    /// <param name="found"><c>FindParticleSystem( name ) != NULL</c>.</param>
    /// <param name="hasAttachment">
    /// The effect owner's <c>LookupAttachment( name )</c> is valid, given whether it is the view model.
    /// </param>
    /// <param name="itemHasAttachment">
    /// The item's own lookup, for control points 1..6; null when the world owner is the item, as it is without an extra wearable.
    /// </param>
    /// <returns>The effect, or null.</returns>
    /// <exception cref="ArgumentNullException">A delegate is null.</exception>
    /// <remarks>
    /// **A non-null answer is also the fast-cull clear**: <c>RemoveEffects( EF_BONEMERGE_FASTCULL )</c> on THIS entity runs
    /// exactly when an effect is about to be created (:1733-1740), whether or not the create then succeeds.
    /// The stops before the create are <see cref="Update"/>'s. An item with no player owner is not answered: the engine reads <c>pOwner-&gt;ShouldDrawThisPlayer()</c>
    /// unguarded (:1625), so no such item reaches here alive.
    /// </remarks>
    public static EconParticleCreate? Create(
        EconParticle system,
        bool visible,
        in EconParticleHolder holder,
        Func<string, bool> found,
        Func<bool, string, bool> hasAttachment,
        Func<string, bool>? itemHasAttachment = null) =>
        Update(system, visible, holder, found, hasAttachment, itemHasAttachment).Create;

    /// <summary>The whole of one entry's update: the stops it makes, then what it creates (B69).</summary>
    /// <param name="system">The entry.</param>
    /// <param name="visible"><c>bVisible</c>.</param>
    /// <param name="holder">The item and its owners.</param>
    /// <param name="found"><c>FindParticleSystem( name ) != NULL</c>.</param>
    /// <param name="hasAttachment">The effect owner's lookup, given whether it is the view model.</param>
    /// <param name="itemHasAttachment">The item's own lookup, for control points 1..6.</param>
    /// <returns>
    /// The stops, null when the function returns before them (no local player or owner, no such system: :1596-1598,
    /// :1691-1693); and the create, as <see cref="Create"/> answers it.
    /// </returns>
    /// <exception cref="ArgumentNullException">A delegate is null.</exception>
    public static (EconParticleStop? Stop, EconParticleCreate? Create) Update(
        EconParticle system,
        bool visible,
        in EconParticleHolder holder,
        Func<string, bool> found,
        Func<bool, string, bool> hasAttachment,
        Func<string, bool>? itemHasAttachment = null)
    {
        // Control points 1..6 are looked up on THIS item (:1762-1770), which is the world owner unless an extra wearable is.
        itemHasAttachment ??= name => hasAttachment(false, name);

        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(hasAttachment);

        if (!holder.HasLocalPlayer || !holder.HasOwner)
        {
            return (null, null);
        }

        bool draw = true;
        bool viewModel = false;

        // `if ( !pOwner->ShouldDrawThisPlayer() )` (:1625-1639).
        if (!holder.OwnerDrawnAsWorldModel)
        {
            draw = system.DrawInViewModel;
            viewModel = holder.InLocalViewModel;
        }

        bool ownerExists = !viewModel || holder.HasViewModelOwner;

        // `pszAttachmentName`, the fob for an extra wearable (:1641-1645).
        string? attachment = system.ControlPoints.Count > 0 ? system.ControlPoints[0] : null;

        if (viewModel && holder.ExtraWearableViewModel)
        {
            attachment = FobViewModel;
        }
        else if (!viewModel && holder.IsWeapon && holder.ExtraWearable)
        {
            attachment = Fob;
        }

        bool attached = ownerExists && !string.IsNullOrEmpty(attachment) && hasAttachment(viewModel, attachment);

        if (Resolve(system, holder.ParticleSuffix, viewModel, found) is not { } name)
        {
            return (null, null);
        }

        // The stops reach every effect of the name on the owners, whoever made it (:1695-1720).
        EconParticleStop stop = new(
            name.System, system.DrawInViewModel && name.UniqueViewModel ? name.ViewModel : null, attached ? attachment : null);

        if (!draw || (viewModel && !holder.IsWeapon) || !visible || !ownerExists)
        {
            return (stop, null);
        }

        // "Attachments can fall back to following root bones if the attachment point wasn't found" (:1748-1758).
        EconParticleAttach attach = system.FollowRootBone ? EconParticleAttach.RootBoneFollow : EconParticleAttach.AbsOriginFollow;

        if (attached)
        {
            attach = EconParticleAttach.PointFollow;
        }

        List<(int, string)> points = [];
        bool stopped = false;

        for (int point = 1; point < system.ControlPoints.Count; point++)
        {
            if (system.ControlPoints[point] is { Length: > 0 } named)
            {
                points.Add((point, named));

                // `AddControlPoint( pEffect, i, this, … )` looks it up on THIS item (:1762-1770): a miss is attachment 0, which
                // `GetAttachment` refuses, and `UpdateControlPoint` stops the effect by name (particle_property.cpp:608-617).
                stopped |= !itemHasAttachment(named);
            }
        }

        return (stop, new EconParticleCreate(name.System, attach, attached ? attachment : null, viewModel, points, stopped));
    }
}
