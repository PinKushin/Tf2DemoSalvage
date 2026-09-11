using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// An old demo's sequence numbers, translated into today's models by name (B380, D160).
/// </summary>
/// <remarks>
/// **A networked <c>m_nSequence</c> indexes the model the recording client loaded**, merged with its
/// includes by label (<c>virtualmodel_t::AppendSequences</c>, `studio_virtualmodel.cpp:142`), and the
/// client resolves it against whatever model IT has. Valve later reordered some models: the 2008 sticky
/// launcher reads <c>idle, fire, draw, …</c> and today's <c>ref, idle, fire, draw, …</c>, so the demo's
/// <c>draw</c> (2) plays as today's <c>fire</c>. The translation takes the old index to its old LABEL
/// and finds that label in today's model.
///
/// **Where the label is gone, the ACTIVITY.** Six old labels have no sequence of that name today — the
/// melee <c>swing</c> became <c>swing_a</c> to <c>swing_c</c> — and the activity is the key the engine
/// asks a weapon animation by: <c>SendWeaponAnim( ACT_VM_HITCENTER )</c>. The engine picks among several
/// sequences of one activity by weighted random (<c>SelectWeightedSequence</c>); this takes the first,
/// which is a named divergence rather than a guess at the dice.
/// </remarks>
public sealed class SequenceErasConformanceTests
{
    private static readonly IReadOnlyList<EraSequence> StickyLauncher2008 =
    [
        new("idle", "ACT_VM_IDLE"),
        new("fire", "ACT_VM_PRIMARYATTACK"),
        new("draw", "ACT_VM_DRAW"),
        new("autofire", "ACT_VM_PULLBACK"),
        new("reload_start", "ACT_RELOAD_START"),
        new("reload_loop", "ACT_VM_RELOAD"),
        new("reload_end", "ACT_RELOAD_FINISH"),
    ];

    /// <summary>Today's sticky launcher, as the live install ships it.</summary>
    private static readonly IReadOnlyList<EraSequence> StickyLauncherToday =
    [
        new("ref", string.Empty),
        new("idle", "ACT_VM_IDLE"),
        new("fire", "ACT_VM_PRIMARYATTACK"),
        new("draw", "ACT_VM_DRAW"),
        new("autofire", "ACT_VM_PULLBACK"),
        new("reload_start", "ACT_RELOAD_START"),
        new("reload_loop", "ACT_VM_RELOAD"),
        new("reload_end", "ACT_RELOAD_FINISH"),
    ];

    /// <remarks>
    /// **The owner's defect, exactly.** The 2008 demo deploys the launcher on sequence 2, which was
    /// <c>draw</c>; today's <c>draw</c> is 3. Reading 2 against today's file plays <c>fire</c> and holds its
    /// last frame — the launcher thrust forward with no hand.
    /// </remarks>
    [Test]
    public void Translate_The2008StickyLaunchersSequence2_IsTodaysDraw()
    {
        Translate(StickyLauncher2008, 2, StickyLauncherToday).ShouldBe(3);
    }

    /// <remarks>
    /// **The control: a label at the same index in both eras translates to itself** — the 2008
    /// <c>fire</c> is 1 and today's <c>fire</c> is 2, so even an unshifted-looking name moves.
    /// </remarks>
    [Test]
    public void Translate_AnyOldSequence_IsFoundByItsLabel()
    {
        Translate(StickyLauncher2008, 1, StickyLauncherToday).ShouldBe(2);
    }

    /// <remarks>
    /// **The label decides where it can, not the activity.** The 2008 knife's three stabs all answer to
    /// <c>ACT_VM_HITCENTER</c>, so an activity lookup lands on <c>stab_a</c> for every one of them; only the
    /// label keeps <c>stab_b</c> as <c>stab_b</c>. The two tests above cannot tell label-first from
    /// activity-first — their activities are unique — and this one can.
    /// </remarks>
    [Test]
    public void Translate_ASequenceWhoseActivityIsShared_IsFoundByLabelNotActivity()
    {
        IReadOnlyList<EraSequence> knife2008 =
        [
            new("draw", "ACT_VM_DRAW"),
            new("idle", "ACT_VM_IDLE"),
            new("stab_a", "ACT_VM_HITCENTER"),
            new("stab_b", "ACT_VM_HITCENTER"),
            new("stab_c", "ACT_VM_HITCENTER"),
        ];

        IReadOnlyList<EraSequence> knifeToday =
        [
            new("ref", string.Empty),
            new("draw", "ACT_VM_DRAW"),
            new("idle", "ACT_VM_IDLE"),
            new("stab_a", "ACT_VM_HITCENTER"),
            new("stab_b", "ACT_VM_HITCENTER"),
            new("stab_c", "ACT_VM_HITCENTER"),
        ];

        Translate(knife2008, 3, knifeToday).ShouldBe(4);
    }

    /// <remarks>
    /// **A label gone today falls back to its activity.** The 2008 shovel's <c>swing</c> (3) has no
    /// namesake now. The fixture puts an unrelated sequence at index 3 today, so a translation that
    /// kept the index — or matched anything but the activity — lands on the wrong one.
    /// </remarks>
    [Test]
    public void Translate_ALabelGoneToday_TakesTheFirstSequenceWithItsActivity()
    {
        IReadOnlyList<EraSequence> shovel2008 =
        [
            new("ref", string.Empty),
            new("draw", "ACT_VM_DRAW"),
            new("idle", "ACT_VM_IDLE"),
            new("swing", "ACT_VM_HITCENTER"),
        ];

        IReadOnlyList<EraSequence> shovelToday =
        [
            new("ref", string.Empty),
            new("draw", "ACT_VM_DRAW"),
            new("idle", "ACT_VM_IDLE"),
            new("inspect_start", "ACT_PRIMARY_VM_INSPECT_START"),
            new("swing_a", "ACT_VM_HITCENTER"),
            new("swing_b", "ACT_VM_HITCENTER"),
        ];

        Translate(shovel2008, 3, shovelToday).ShouldBe(4);
    }

    /// <remarks>
    /// **Neither the name nor the activity exists today, so there is no such sequence** — −1, which is
    /// what <c>StudioSequenceTable.At</c> answers null for. Keeping the old index would play some other
    /// animation that nothing in the demo asked for.
    /// </remarks>
    [Test]
    public void Translate_WhenNothingTodayAnswers_IsNoSequence()
    {
        IReadOnlyList<EraSequence> old = [new("idle", "ACT_VM_IDLE"), new("gone", "ACT_GONE")];

        Translate(old, 1, [new("idle", "ACT_VM_IDLE")]).ShouldBe(-1);
    }

    /// <remarks>
    /// **An index past the old list named nothing even then**, so it names nothing now.
    /// </remarks>
    [Test]
    public void Translate_AnIndexPastTheOldList_IsNoSequence()
    {
        Translate(StickyLauncher2008, StickyLauncher2008.Count, StickyLauncherToday).ShouldBe(-1);
    }

    [Test]
    public void Parse_AProtocolLine_NamesItsEra()
    {
        SequenceEras eras = SequenceEras.Parse("protocol 14 2008\nera 2008\n");

        eras.EraFor(14).ShouldBe("2008");
        eras.EraFor(24).ShouldBeNull("a protocol with no line has no table");
    }

    /// <remarks>
    /// **Looked up ignoring case and slash direction**, because a model path from the wire and one in the
    /// table were written by different tools on different machines.
    /// </remarks>
    [Test]
    public void Parse_AModelLine_IsFoundIgnoringCaseAndSlashes()
    {
        SequenceEras eras = SequenceEras.Parse(
            "era 2008\nmodels/weapons/v_models/v_x.mdl\tidle:ACT_VM_IDLE\tref:\n");

        IReadOnlyList<EraSequence>? found = eras.For("2008", "MODELS\\Weapons\\v_models\\V_X.mdl");

        found.ShouldNotBeNull();
        found.Count.ShouldBe(2);
        found[0].ShouldBe(new EraSequence("idle", "ACT_VM_IDLE"));
        found[1].ShouldBe(new EraSequence("ref", string.Empty));
    }

    [Test]
    public void Parse_AModelTheEraDoesNotList_HasNoTable()
    {
        SequenceEras.Parse("era 2008\n").For("2008", "models/props/anything.mdl").ShouldBeNull();
    }

    /// <remarks>
    /// **The file that ships, read as the program reads it** — the output-level check that the table
    /// is embedded, parsed and carries the owner's case: the 2008 sticky launcher's index 2 is
    /// <c>draw</c>, under protocol 14.
    /// </remarks>
    [Test]
    public void Shipped_For2008_CarriesTheStickyLaunchersDraw()
    {
        SequenceEras shipped = SequenceEras.Shipped;

        shipped.EraFor(14).ShouldBe("2008");
        shipped.For("2008", "models/weapons/v_models/v_stickybomb_launcher_demo.mdl")![2].Label
            .ShouldBe("draw");
    }

    private static int Translate(
        IReadOnlyList<EraSequence> old, int sequence, IReadOnlyList<EraSequence> today) =>
        EraSequenceTranslation.Translate(
            old,
            sequence,
            label => IndexWhere(today, entry => string.Equals(entry.Label, label, StringComparison.OrdinalIgnoreCase)),
            activity => IndexWhere(today, entry => string.Equals(entry.Activity, activity, StringComparison.OrdinalIgnoreCase)));

    private static int IndexWhere(IReadOnlyList<EraSequence> list, Func<EraSequence, bool> match)
    {
        for (int index = 0; index < list.Count; index++)
        {
            if (match(list[index]))
            {
                return index;
            }
        }

        return -1;
    }
}
