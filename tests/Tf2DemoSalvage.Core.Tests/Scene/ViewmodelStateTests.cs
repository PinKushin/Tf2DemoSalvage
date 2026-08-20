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
    public void ViewmodelSlot_OnAMainHandAndAnOffHand_Are0And1()
    {
        // **A player has two, and without this they are indistinguishable.** `MAX_VIEWMODELS` is
        // 2 (`shareddefs.h:325`); slot 0 is the weapon in hand and slot 1 is the off hand —
        // `CTFPlayer::GetOffHandViewModel` is literally `return GetViewModel( 1 )`.
        Viewmodel(modelIndex: 535, owner: Owner, slot: 0).ViewmodelSlot().ShouldBe(0);
        Viewmodel(modelIndex: 535, owner: Owner, slot: 1).ViewmodelSlot().ShouldBe(1);
    }

    [Test]
    public void ViewmodelSlot_WhenTheDemoDoesNotSay_IsNull()
    {
        // Null rather than defaulting to 0, because "did not say" and "said main hand" are
        // different claims and a caller that prefers slot 0 would otherwise treat every silent
        // entity as the weapon in hand.
        EntityState state = new(entityIndex: 9, classId: 1, serialNumber: 1, className: "CTFViewModel");

        state.Set("DT_BaseViewModel.m_nModelIndex", PropertyValue.FromInt(535));

        state.ViewmodelSlot().ShouldBeNull();
    }

    [Test]
    public void IsDrawn_AViewmodelFlaggedNoDraw_IsFalse()
    {
        // **The flag is how the engine hides the off hand, and it is on the viewmodel's own table.**
        // `CTFWeaponInvis::SetWeaponVisible` resolves `pOwner->GetViewModel( m_nViewModelIndex )`
        // and calls `vm->AddEffects( EF_NODRAW )`, so the watch's ENTITY stays and stops drawing.
        //
        // Measured on z1800: `DT_BaseViewModel.m_fEffects 32` appears in the first 400 snapshots,
        // and 32 is EF_NODRAW. A reader that misses it draws a watch in every player's left hand
        // for the whole match, because every player carries a slot-1 viewmodel at all times —
        // 23 of them in that same sample, in a game with one spy.
        Viewmodel(modelIndex: 535, owner: Owner, slot: 1, effects: 0x020).IsDrawn.ShouldBeFalse();
    }

    [Test]
    public void IsDrawn_AViewmodelWithOtherFlagsSet_IsTrue()
    {
        // **The control, and it is the one that fails against `effects != 0`.** EF_NODRAW is one bit
        // of a field carrying a dozen unrelated flags; EF_NOINTERP (0x008) and EF_NOSHADOW (0x010)
        // together are 24, which is non-zero and says nothing about visibility.
        Viewmodel(modelIndex: 535, owner: Owner, slot: 1, effects: 0x008 | 0x010)
            .IsDrawn.ShouldBeTrue();
    }

    [Test]
    public void IsDrawn_AViewmodelThatSentNoEffects_IsTrue()
    {
        // Silence is not concealment. An early-protocol demo that never sends the property must
        // still draw, or the fix for the off hand would blank the main hand everywhere.
        Viewmodel(modelIndex: 535, owner: Owner).IsDrawn.ShouldBeTrue();
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
        player.ViewmodelSlot().ShouldBeNull();
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
        float playbackRate = 1f,
        int slot = 0,
        int? effects = null)
    {
        EntityState state = new(entityIndex: 9, classId: 1, serialNumber: 1, className: "CTFViewModel");

        if (effects is { } flags)
        {
            state.Set("DT_BaseViewModel.m_fEffects", PropertyValue.FromInt(flags));
        }

        state.Set("DT_BaseViewModel.m_nModelIndex", PropertyValue.FromInt(modelIndex));
        state.Set("DT_BaseViewModel.m_nSequence", PropertyValue.FromInt(sequence));
        state.Set("DT_BaseViewModel.m_flPlaybackRate", PropertyValue.FromFloat(playbackRate));
        state.Set("DT_BaseViewModel.m_hOwner", PropertyValue.FromInt(ownerHandle ?? owner ?? 0));
        state.Set("DT_BaseViewModel.m_nViewModelIndex", PropertyValue.FromInt(slot));

        return state;
    }
}
