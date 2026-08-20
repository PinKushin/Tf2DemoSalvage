using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Schema;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where the name `Corpus` binds to the
// namespace rather than to the helper class.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// What a demo carries about the weapon in the player's hands.
/// </summary>
/// <remarks>
/// **For a point-of-view demo the viewmodel is a large part of what the player actually saw**, and
/// this project draws none. The first-person camera landed without one, which the owner noticed
/// immediately: "theres no weapon or anything showing which should show in pov".
///
/// **The send table is the surprising part, and it decides the whole implementation.**
/// <c>baseviewmodel_shared.cpp:557</c> opens with <c>BEGIN_NETWORK_TABLE_NOBASE</c> — no base
/// table, so no <c>DT_BaseEntity</c>, so **no origin and no angles on the wire**:
///
/// <code>
/// BEGIN_NETWORK_TABLE_NOBASE(CBaseViewModel, DT_BaseViewModel)
///     SendPropModelIndex(SENDINFO(m_nModelIndex)),
///     SendPropInt   (SENDINFO(m_nBody), 8),
///     SendPropInt   (SENDINFO(m_nSkin), 10),
///     SendPropInt   (SENDINFO(m_nSequence), 8, SPROP_UNSIGNED),
///     SendPropFloat (SENDINFO(m_flPlaybackRate), 8, SPROP_ROUNDUP, -4.0, 12.0f),
///     SendPropEHandle (SENDINFO(m_hWeapon)),
///     SendPropEHandle (SENDINFO(m_hOwner)),
///     ...
/// </code>
///
/// That is the same shape as a bone-merged cosmetic: the demo says WHICH model and WHAT it is
/// doing, and the client works out where it goes. <c>CBaseViewModel::CalcViewModelView</c> starts
/// it at the eye —
///
/// <code>
/// QAngle vmangles = eyeAngles;
/// Vector vmorigin = eyePosition;
/// ... AddViewmodelBob, CalcViewModelLag, ApplyShake ...
/// SetLocalOrigin( vmorigin );
/// SetLocalAngles( vmangles );
/// </code>
///
/// — and then adds bob, lag and shake, all of which are functions of movement and elapsed time
/// rather than anything the demo records. The eye placement is what a recording can support; the
/// embellishments would be this viewer inventing motion, which is the one thing it is for not
/// doing.
///
/// **And it is drawn with the cull mode flipped** (<c>c_baseviewmodel.cpp:373</c>), because the
/// model is mirrored for the left-handed view. That is the detail that makes a naive
/// implementation draw the weapon inside out.
/// </remarks>
public sealed class ViewmodelConformanceTests
{
    /// <summary>The table a viewmodel's properties arrive under.</summary>
    private const string Table = "DT_BaseViewModel";

    /// <summary>Which of a player's two viewmodels this one is.</summary>
    private const string SlotPropertyName = "m_nViewModelIndex";

    /// <summary>Source's <c>SPROP_UNSIGNED</c>, the low bit of the flags word.</summary>
    private const int UnsignedFlag = 1 << 0;

    [Test]
    public void Viewmodel_ThePropertiesTheImplementationNeeds_AreAllOnTheWire()
    {
        // Model, pose and playback rate are what a viewer needs to draw one, and the owner handle
        // is what ties it to the player being followed. Asserted against the real schema rather
        // than against the SDK header, because what matters is that THESE demos carry them.
        IReadOnlyList<string> needed =
        [
            "m_nModelIndex", "m_nSequence", "m_flPlaybackRate", "m_hOwner", SlotPropertyName,
        ];

        List<string> checkedDemos = [];

        foreach (string path in Corpus.FilesWithSchema())
        {
            IReadOnlyList<string> names = ViewmodelProperties(path);

            if (names.Count == 0)
            {
                continue;
            }

            foreach (string property in needed)
            {
                names.ShouldContain(
                    property, $"{Path.GetFileName(path)} has no {Table}.{property}");
            }

            checkedDemos.Add(Path.GetFileName(path));
        }

        // A positive control: an empty sweep passes every loop above vacuously, and this project
        // has already been caught by five absence claims that were facts about the search.
        checkedDemos.ShouldNotBeEmpty("no demo in the corpus declares a viewmodel at all");
    }

    [Test]
    public void Viewmodel_CarriesNoOriginAndNoAngles_SoItsPlacementIsComputed()
    {
        // **The finding the implementation turns on.** BEGIN_NETWORK_TABLE_NOBASE means the table
        // inherits nothing, so a viewmodel never says where it is — exactly like a bone-merged
        // hat. A reader expecting an origin would find none and could easily conclude the entity
        // was broken rather than that it is positioned by the client.
        foreach (string path in Corpus.FilesWithSchema())
        {
            IReadOnlyList<string> names = ViewmodelProperties(path);

            if (names.Count == 0)
            {
                continue;
            }

            names.ShouldNotContain(
                "m_vecOrigin", $"{Path.GetFileName(path)}: a viewmodel now carries an origin");

            names.ShouldNotContain(
                "m_angRotation", $"{Path.GetFileName(path)}: a viewmodel now carries angles");
        }
    }

    [Test]
    public void SlotProperty_OnEveryDemoThatCarriesAViewmodel_Is1BitUnsigned()
    {
        // **A player has TWO viewmodels, and the demo says which is which.** `shareddefs.h:325`
        // sets `MAX_VIEWMODELS 2` and `CTFPlayer::GetOffHandViewModel` is the naming:
        //
        //     // off hand model is slot 1
        //     return GetViewModel( 1 );
        //
        // Slot 0 is the weapon in the player's hands; slot 1 is the off hand, which in TF2 is set
        // by exactly two things — `CTFWeaponInvis::Spawn` (the spy's watch) and
        // `tf_weaponbase_grenade`. So a demo can legitimately describe two viewmodels at once, and
        // a reader that keeps whichever arrived last shows the wrong one.
        //
        // **One bit, not eight**, which is the arithmetic that makes the pair exhaustive:
        // `VIEWMODEL_INDEX_BITS 1` (`baseviewmodel_shared.h:29`) with `SPROP_UNSIGNED`, so the
        // only values on the wire are 0 and 1 and there is no third case to handle.
        //
        // Asserted against each demo's own schema rather than against the header, because the
        // question is whether the OLD eras carry it — the disagreement that prompted this is on a
        // 2009 recording, and a property added later would not be there to read.
        List<string> checkedDemos = [];

        foreach (string path in Corpus.FilesWithSchema())
        {
            IReadOnlyList<SendProperty> properties = ViewmodelTable(path);

            if (properties.Count == 0)
            {
                continue;
            }

            SendProperty slot = properties
                .First(property => string.Equals(
                    property.Name, SlotPropertyName, StringComparison.Ordinal));

            slot.BitCount.ShouldBe(
                1, $"{Path.GetFileName(path)}: VIEWMODEL_INDEX_BITS is 1");

            (slot.Flags & UnsignedFlag).ShouldNotBe(
                0, $"{Path.GetFileName(path)}: the slot is SPROP_UNSIGNED");

            checkedDemos.Add(Path.GetFileName(path));
        }

        checkedDemos.ShouldNotBeEmpty("no demo in the corpus declares a viewmodel at all");
    }

    [Test]
    public void Viewmodel_IsCarriedBySourceTvRecordingsToo_ExceptTheEarliest()
    {
        // **Measured 2026-08-20, and it widens the feature.** A viewmodel is the local player's
        // own weapon, so the obvious guess is that only a point-of-view recording carries one.
        // Most SourceTV demos in the corpus carry them as well:
        //
        //     2007 granary STV        0
        //     2008 granary STV      889
        //     2011 viaduct STV      667
        //     2013 foundry STV     1773
        //     z1800 (SourceTV)    95480
        //
        // So a first-person view of a spectated player can show their weapon on every era but the
        // earliest, which is a fact about what can be built rather than about the format.
        //
        // Stated as "at least one SourceTV demo carries them" rather than as a per-era table: the
        // counts are properties of these particular recordings, and pinning them would fail
        // whenever the corpus grows.
        List<string> sourceTvWithViewmodels =
        [
            .. Corpus.FilesWithSchema()
                .Where(path => !IsPointOfView(path))
                .Where(path => ViewmodelProperties(path).Count > 0)
                .Select(path => Path.GetFileName(path) ?? path),
        ];

        sourceTvWithViewmodels.ShouldNotBeEmpty(
            "no SourceTV demo declares a viewmodel, so spectated players can never show a weapon");
    }

    /// <summary>The property names a demo's schema declares under the viewmodel's table.</summary>
    private static IReadOnlyList<string> ViewmodelProperties(string path) =>
        [.. ViewmodelTable(path).Select(property => property.Name)];

    /// <summary>The viewmodel table's properties as the demo declares them, widths included.</summary>
    private static IReadOnlyList<SendProperty> ViewmodelTable(string path)
    {
        DemoSchema schema = Corpus.Schema(path);

        return
        [
            .. schema.Tables
                .Where(table => string.Equals(table.Name, Table, StringComparison.Ordinal))
                .SelectMany(table => table.Properties),
        ];
    }

    private static bool IsPointOfView(string path) =>
        !string.Equals(
            Corpus.Header(path).ClientName, "SourceTV Demo", StringComparison.Ordinal);
}
