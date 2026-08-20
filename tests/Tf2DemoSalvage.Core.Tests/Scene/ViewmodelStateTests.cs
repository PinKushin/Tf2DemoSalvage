using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Reading a viewmodel, whose properties are in a table of its own.
/// </summary>
/// <remarks>
/// **Every existing lookup here is hardcoded to <c>DT_BaseEntity</c>, and a viewmodel has none.**
/// Its table is declared <c>BEGIN_NETWORK_TABLE_NOBASE</c>, so it inherits nothing: no origin, no
/// angles, no <c>m_fEffects</c>, and its owner handle is <c>DT_BaseViewModel.m_hOwner</c> rather
/// than <c>DT_BaseEntity.m_hOwnerEntity</c>.
///
/// So <c>ModelIndex()</c> and <c>Attachment()</c> both answer null for a viewmodel that is
/// perfectly well described on the wire — which is the exact shape of
/// <c>docs/memory/a-property-name-needs-its-declaring-table.md</c>: <c>m_fFlags</c> and
/// <c>m_flCycle</c> were both looked up in the wrong table and both failed silently.
///
/// The reason it matters is that silence is indistinguishable from absence. A viewmodel read this
/// way looks like an entity with no model and no owner, which reads as "the demo does not carry
/// one" — and the corpus says otherwise, loudly: 95,480 viewmodel property updates in z1800 alone.
/// </remarks>
public sealed class ViewmodelStateTests
{
    /// <summary>The owning player's entity slot in these fixtures.</summary>
    private const int Owner = 2;

    [Test]
    public void ViewmodelModelIndex_IsFoundInTheViewmodelsOwnTable()
    {
        // ModelIndex() looks in DT_BaseEntity and finds nothing, which is not the same as the
        // entity having no model.
        EntityState state = Viewmodel(modelIndex: 535, owner: Owner);

        state.ModelIndex().ShouldBeNull("a viewmodel has no DT_BaseEntity to read");
        state.ViewmodelModelIndex().ShouldBe(535);
    }

    [Test]
    public void ViewmodelOwner_IsFoundInTheViewmodelsOwnTable()
    {
        // **m_hOwner, not m_hOwnerEntity.** Two different properties in two different tables, and
        // the existing one is additionally gated on EF_BONEMERGE which a viewmodel never sets.
        EntityState state = Viewmodel(modelIndex: 535, owner: Owner);

        state.Attachment().ShouldBeNull("a viewmodel is not bone-merged onto anything");
        state.ViewmodelOwner().ShouldBe(Owner);
    }

    [Test]
    public void ViewmodelOwner_MasksTheHandleToASlot_LikeEveryOtherHandle()
    {
        // A handle is a slot in the low bits and a serial above it. Reading it whole would give an
        // enormous entity index that matches nobody, so the viewmodel would silently never be
        // matched to its owner.
        const int Serial = 41;
        int handle = Owner | (Serial << 11);

        Viewmodel(modelIndex: 535, ownerHandle: handle).ViewmodelOwner().ShouldBe(Owner);
    }

    [Test]
    public void ViewmodelOwner_AnUnsetHandle_IsNobodyRatherThanSlot2047()
    {
        // **INVALID_NETWORKED_EHANDLE_VALUE is 21 bits of ones, and its low 11 bits mask to 2047**
        // — a perfectly ordinary-looking slot. Masking before testing turns "no owner" into "owned
        // by entity 2047", which is the trap the existing handle reader documents.
        Viewmodel(modelIndex: 535, ownerHandle: (1 << 21) - 1).ViewmodelOwner().ShouldBeNull();
    }

    [Test]
    public void ViewmodelSequence_AndPlaybackRate_ComeFromTheSameTable()
    {
        // What the weapon is doing, and how fast. Both are on the viewmodel's own table and both
        // are needed to pick a frame — the playback rate especially, since it is the factor that
        // was once decoded, retained and read by nothing.
        EntityState state = Viewmodel(modelIndex: 535, owner: Owner, sequence: 7, playbackRate: 2f);

        state.ViewmodelSequence().ShouldBe(7);
        state.ViewmodelPlaybackRate().ShouldNotBeNull().ShouldBe(2f, 0.001f);
    }

    [Test]
    public void ViewmodelLookups_OnAnOrdinaryEntity_AnswerNothing()
    {
        // **The control.** Every assertion above is that a viewmodel reads where an ordinary
        // entity does not; without this, a lookup that answered for everything would satisfy them
        // all and put a weapon on every prop in the map.
        EntityState player = new(entityIndex: Owner, classId: 0, serialNumber: 1, className: "CTFPlayer");

        player.ViewmodelModelIndex().ShouldBeNull();
        player.ViewmodelOwner().ShouldBeNull();
        player.ViewmodelSequence().ShouldBeNull();
    }

    /// <summary>An entity state carrying only what a viewmodel carries.</summary>
    /// <remarks>
    /// Set directly rather than decoded, because what is under test is WHERE the reader looks
    /// rather than how the bits arrive — and a fixture that went through the decoder would need a
    /// schema declaring the table, which is the thing being read.
    /// </remarks>
    private static EntityState Viewmodel(
        int modelIndex,
        int? owner = null,
        int? ownerHandle = null,
        int sequence = 0,
        float playbackRate = 1f)
    {
        EntityState state = new(entityIndex: 9, classId: 1, serialNumber: 1, className: "CTFViewModel");

        state.Set("DT_BaseViewModel.m_nModelIndex", PropertyValue.FromInt(modelIndex));
        state.Set("DT_BaseViewModel.m_nSequence", PropertyValue.FromInt(sequence));
        state.Set("DT_BaseViewModel.m_flPlaybackRate", PropertyValue.FromFloat(playbackRate));
        state.Set("DT_BaseViewModel.m_hOwner", PropertyValue.FromInt(ownerHandle ?? owner ?? 0));

        return state;
    }
}
