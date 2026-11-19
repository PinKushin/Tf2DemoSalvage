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
public sealed record EconParticleCreate(
    string System, EconParticleAttach Attach, string? Attachment, bool OnViewModel, IReadOnlyList<(int Point, string Attachment)> ControlPoints);

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
    /// <returns>The effect, or null.</returns>
    /// <exception cref="ArgumentNullException">A delegate is null.</exception>
    /// <remarks>
    /// **A non-null answer is also the fast-cull clear**: <c>RemoveEffects( EF_BONEMERGE_FASTCULL )</c> on THIS entity runs
    /// exactly when an effect is about to be created (:1733-1740), whether or not the create then succeeds.
    /// *Not carried:* the stops before the create, which end what a previous state made; the caller replaces its own run
    /// instead. An item with no player owner is not answered: the engine reads <c>pOwner-&gt;ShouldDrawThisPlayer()</c>
    /// unguarded (:1625), so no such item reaches here alive.
    /// </remarks>
    public static EconParticleCreate? Create(
        EconParticle system, bool visible, in EconParticleHolder holder, Func<string, bool> found, Func<bool, string, bool> hasAttachment)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(hasAttachment);

        if (!holder.HasLocalPlayer || !holder.HasOwner)
        {
            return null;
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

        if (Resolve(system, holder.ParticleSuffix, viewModel, found) is not { } name ||
            !draw ||
            (viewModel && !holder.IsWeapon) ||
            !visible ||
            !ownerExists)
        {
            return null;
        }

        // "Attachments can fall back to following root bones if the attachment point wasn't found" (:1748-1758).
        EconParticleAttach attach = system.FollowRootBone ? EconParticleAttach.RootBoneFollow : EconParticleAttach.AbsOriginFollow;

        if (attached)
        {
            attach = EconParticleAttach.PointFollow;
        }

        List<(int, string)> points = [];

        for (int point = 1; point < system.ControlPoints.Count; point++)
        {
            if (system.ControlPoints[point] is { Length: > 0 } named)
            {
                points.Add((point, named));
            }
        }

        return new EconParticleCreate(name.System, attach, attached ? attachment : null, viewModel, points);
    }
}
