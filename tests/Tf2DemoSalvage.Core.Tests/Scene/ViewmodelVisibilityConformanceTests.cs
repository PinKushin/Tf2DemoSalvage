using System;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// When the engine draws a viewmodel, and which of the two it is drawing.
/// </summary>
/// <remarks>
/// **Written before the off hand was drawn, because the demo does not obviously say when to.**
/// A player carries two viewmodel entities at all times, not only while holding something in each
/// hand — z1800 sends 24 with slot 0 and 23 with slot 1 across its first 400 snapshots, in a match
/// with one spy. Drawing every slot-1 entity would put a watch in everybody's hand for the whole
/// match.
///
/// The engine's answer is <c>EF_NODRAW</c>, and this suite is here because THIS PROJECT ASSERTED THE
/// OPPOSITE. <c>EntityState.ViewModelTable</c> carried the comment "a viewmodel inherits no
/// <c>DT_BaseEntity</c> — no origin, no angles, no <c>m_fEffects</c>", and it is wrong about the last
/// one: <c>DT_BaseViewModel</c> declares <c>m_fEffects</c> itself, ten bits unsigned. The claim was
/// plausible — everything else in that list is true — and it survived because
/// <c>EntityState.IsDrawn</c> reads <c>DT_BaseEntity.m_fEffects</c>, so a viewmodel answered null,
/// which reads as "no flags set" and therefore "draw it".
///
/// That is <c>docs/memory/a-property-name-needs-its-declaring-table.md</c> for the third time in
/// this repository: the property exists, the name is right, the table is wrong, and nothing fails.
/// </remarks>
public sealed class ViewmodelVisibilityConformanceTests
{
    /// <summary>Where the viewmodel's network table is declared.</summary>
    private const string ViewModelShared = "src/game/shared/baseviewmodel_shared.cpp";

    /// <summary>Where the spy's watch is implemented.</summary>
    private const string Invis = "src/game/shared/tf/tf_weapon_invis.cpp";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void BaseViewModelTable_DespiteInheritingNothing_SendsEffects()
    {
        // **The claim under test, and the one this project had backwards.** NOBASE means no
        // inherited DT_BaseEntity; it does not mean the table cannot declare a property of the same
        // name itself, and this one does.
        string table = SendTable();

        table.ShouldContain(
            "SENDINFO(m_fEffects)",
            Case.Sensitive,
            "DT_BaseViewModel sends m_fEffects, so a viewmodel can be told not to draw");

        // The control, and it is what makes the assertion above mean anything. `m_vecOrigin` is the
        // canonical DT_BaseEntity property and is genuinely absent here — a viewmodel really does
        // have no origin. Without this, a block extraction that silently matched the whole file
        // would pass the first assertion while proving nothing about where m_fEffects lives.
        table.ShouldNotContain(
            "SENDINFO(m_vecOrigin)",
            Case.Sensitive,
            "a viewmodel genuinely has no origin, which is what NOBASE does mean");
    }

    [Test]
    public void WeaponInvis_OnSpawn_TakesTheOffHandSlot()
    {
        // `CTFWeaponInvis::Spawn`, under Valve's own comment "Use the offhand view model".
        Normalised(Invis).ShouldContain("SetViewModelIndex( 1 );", Case.Sensitive);
    }

    [Test]
    public void WeaponInvis_WhenHidden_AddsNoDrawToTheViewmodel()
    {
        // **The mechanism, and it is applied to the VIEWMODEL rather than to the weapon.**
        // `SetWeaponVisible` resolves `pOwner->GetViewModel( m_nViewModelIndex )` and flags that
        // entity, which is why the flag is readable from a demo at all: the weapon's own effects
        // would say nothing about whether the watch is on screen.
        string source = Normalised(Invis);

        source.ShouldContain("vm->AddEffects( EF_NODRAW );", Case.Sensitive);
        source.ShouldContain("vm->RemoveEffects( EF_NODRAW );", Case.Sensitive);
    }

    [Test]
    public void WeaponInvis_ForItsViewmodel_UsesThePlayerDisplayModel()
    {
        // **The watch is not a `v_` model, and this is why the off hand needs no attached weapon
        // the way the main hand does.** Valve's comment states it outright: "Watch uses the player
        // model as its viewmodel, because it's never seen being carried by the player". So the
        // model index on the wire is the whole thing to draw, where a modern main-hand viewmodel is
        // arms that need a separate client-built weapon merged onto them.
        string source = Normalised(Invis);

        source.ShouldContain("return pItem->GetPlayerDisplayModel( iClass, iTeam );", Case.Sensitive);
    }

    /// <summary>The body of <c>BEGIN_NETWORK_TABLE_NOBASE(CBaseViewModel, DT_BaseViewModel)</c>.</summary>
    /// <remarks>
    /// Cut to the block rather than searched whole, so "the file mentions m_fEffects somewhere"
    /// cannot pass for "the viewmodel's table sends it". The file also declares
    /// <c>DT_BaseViewModel</c>'s receive table, which is fine to include — both halves list the same
    /// property — and the block ends at the first <c>END_NETWORK_TABLE</c>.
    /// </remarks>
    private static string SendTable()
    {
        string source = Normalised(ViewModelShared);

        const string Opens = "BEGIN_NETWORK_TABLE_NOBASE(CBaseViewModel, DT_BaseViewModel)";

        int start = source.IndexOf(Opens, StringComparison.Ordinal);

        start.ShouldBeGreaterThanOrEqualTo(
            0,
            $"{ViewModelShared} no longer declares {Opens} — this suite is reading the wrong thing");

        int end = source.IndexOf("END_NETWORK_TABLE", start, StringComparison.Ordinal);

        end.ShouldBeGreaterThan(start, "the table block is unterminated");

        return source[start..end];
    }

    /// <summary>An SDK file with runs of whitespace collapsed to single spaces.</summary>
    /// <remarks>
    /// Valve's sources are tab-aligned into columns, so <c>SendPropInt (SENDINFO(m_fEffects), 10,
    /// ...)</c> carries several tabs whose exact count is a formatting detail. Matching on it would
    /// make this suite fail on a reformat rather than on a change of behaviour.
    /// </remarks>
    private static string Normalised(string relativePath)
    {
        string? text = SourceSdk.Text(relativePath);

        text.ShouldNotBeNull($"{relativePath} is missing from the SDK checkout");

        return Regex.Replace(text, @"[ \t]+", " ");
    }
}
