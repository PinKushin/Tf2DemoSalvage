using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// An item nobody has the vision for is not drawn — <c>vision_filter_flags</c> (B354).
/// </summary>
/// <remarks>
/// **`CEconEntity::ShouldDraw` is two lines and this is the first one** (`econ_entity.cpp:1800`):
///
/// <code>
///   bool CEconEntity::ShouldDraw()
///   {
///       if ( ShouldHideForVisionFilterFlags() )
///           return false;
///       return BaseClass::ShouldDraw();
///   }
/// </code>
///
/// and the test it runs is the VIEWER's vision, not the wearer's (`econ_entity.cpp:1812`):
///
/// <code>
///   int nVisionFilterFlags = pData->GetVisionFilterFlags();
///   if ( nVisionFilterFlags != 0 )
///       if ( !IsLocalPlayerUsingVisionFilterFlags( nVisionFilterFlags, true ) )
///           return true;   // hide it
/// </code>
///
/// with the final comparison `( nLocalPlayerFlags &amp; nFlags ) == nFlags` (`cdll_util.cpp:125`) —
/// **every** requested bit, not any.
///
/// **23 shipped items declare a filter**: four Pyroland at `TF_VISION_FILTER_PYRO` (the Pet
/// Balloonicorn, the Pet Reindoonicorn, the Infernal Orchestrina, the Burning Bongos) and nineteen
/// MvM robot skins at `TF_VISION_FILTER_ROME` (`shareddefs.h:977`).
///
/// **The viewer of a demo is its recorder**, and their flags come from the items they carry —
/// `CALL_ATTRIB_HOOK_INT( nVisionOptInFlags, vision_opt_in_flags )` (`c_tf_player.cpp:8043`). The
/// items that grant Pyrovision are largely the Pyroland items themselves: the Rainblower, the
/// Lollichop, the Balloonicorn and the Reindoonicorn all both grant it and require it.
/// </remarks>
public sealed class VisionVisibilityConformanceTests
{
    [Test]
    public void Visible_AnItemRequiringVisionTheViewerLacks_IsDropped()
    {
        IReadOnlyList<SceneProp> kept = VisionVisibility.Visible(
            [Worn(Balloonicorn)], viewer: NoVision, Flags);

        kept.ShouldBeEmpty();
    }

    /// <remarks>
    /// The control for the pair, and the branch that makes the rule worth having: with Pyrovision
    /// the same item draws. Without this, "drop every econ item" passes the test above.
    /// </remarks>
    [Test]
    public void Visible_AnItemRequiringVisionTheViewerHas_IsKept()
    {
        IReadOnlyList<SceneProp> kept = VisionVisibility.Visible(
            [Worn(Balloonicorn)], viewer: Pyro, Flags);

        kept.Count.ShouldBe(1);
    }

    /// <remarks>
    /// **The bystander.** Almost nothing in a scene declares a filter — 23 items out of the
    /// thousands — so a rule that dropped too much would be invisible in a test carrying only the
    /// filtered item.
    /// </remarks>
    [Test]
    public void Visible_AnOrdinaryItemBesideAFilteredOne_Survives()
    {
        IReadOnlyList<SceneProp> kept = VisionVisibility.Visible(
            [Worn(Balloonicorn, entity: 20), Worn(Hat, entity: 21)], viewer: NoVision, Flags);

        kept.Count.ShouldBe(1);
        kept[0].EntityIndex.ShouldBe(21);
    }

    /// <remarks>
    /// A prop with no item at all — a map decoration, a projectile, a player — has no filter to
    /// consult and is never the subject of this rule.
    /// </remarks>
    [Test]
    public void Visible_APropWithNoItem_IsNeverDropped()
    {
        IReadOnlyList<SceneProp> kept = VisionVisibility.Visible(
            [Worn(Balloonicorn) with { ItemDefinitionIndex = null }], viewer: NoVision, Flags);

        kept.Count.ShouldBe(1);
    }

    /// <remarks>
    /// **`( nLocalPlayerFlags &amp; nFlags ) == nFlags` — every bit, not any** (`cdll_util.cpp:125`).
    /// No shipped item asks for two at once, so this is the case that pins the comparison rather
    /// than describing the data: an item wanting PYRO and ROME is not satisfied by PYRO alone.
    /// Reading it as an overlap test is the plausible mistake and agrees on all 23 shipped items.
    /// </remarks>
    [Test]
    public void Visible_AnItemWantingTwoVisionsFromAViewerWithOne_IsDropped()
    {
        IReadOnlyList<SceneProp> kept = VisionVisibility.Visible(
            [Worn(WantsBoth)], viewer: Pyro, Flags);

        kept.ShouldBeEmpty();
    }

    /// <remarks>
    /// The other half of that comparison: a viewer with MORE vision than the item asks for keeps
    /// it, so the test is a subset check rather than an equality.
    /// </remarks>
    [Test]
    public void Visible_AViewerWithMoreVisionThanTheItemAsks_KeepsIt()
    {
        IReadOnlyList<SceneProp> kept = VisionVisibility.Visible(
            [Worn(Balloonicorn)], viewer: Pyro | Rome, Flags);

        kept.Count.ShouldBe(1);
    }

    /// <remarks>
    /// **Nothing is dropped when the install cannot say**, which is the same degradation every
    /// other schema question makes: with no TF2 the flags read 0 for every item, and drawing an
    /// item that should have been hidden is a far better failure than hiding the scene.
    /// </remarks>
    [Test]
    public void Visible_WithNoSchemaToAsk_KeepsEverything()
    {
        IReadOnlyList<SceneProp> kept = VisionVisibility.Visible(
            [Worn(Balloonicorn), Worn(WantsBoth, entity: 21)], viewer: NoVision, _ => 0);

        kept.Count.ShouldBe(2);
    }

    /// <remarks>
    /// **The viewer's flags are an OR, not a sum**, and that is read from the engine rather than
    /// assumed: `ATTDESCFORM_VALUE_IS_OR` applies as `iTmp |= (int)flValueModifier`
    /// (`attribute_manager.cpp:613`). Two Pyroland items each granting 1 are the ordinary case — a
    /// player with the Rainblower and a Balloonicorn — and adding gives 2, which is the HALLOWEEN
    /// bit and grants Pyrovision to nobody.
    /// </remarks>
    [Test]
    public void ViewerFlags_ForTwoItemsGrantingTheSameVision_AreOredNotSummed()
    {
        VisionVisibility.ViewerFlags(
            [Worn(Balloonicorn, entity: 20), Worn(Rainblower, entity: 21)],
            recorder: 3,
            OptIn).ShouldBe(Pyro);
    }

    /// <remarks>
    /// Two DIFFERENT visions do combine, which is what makes the test above a statement about the
    /// operation rather than about the number 1.
    /// </remarks>
    [Test]
    public void ViewerFlags_ForItemsGrantingDifferentVisions_CombineThem()
    {
        VisionVisibility.ViewerFlags(
            [Worn(Balloonicorn, entity: 20), Worn(RomeGranting, entity: 21)],
            recorder: 3,
            OptIn).ShouldBe(Pyro | Rome);
    }

    /// <remarks>
    /// **Only the RECORDER's items count.** The filter asks about the local player, so a Balloonicorn
    /// on somebody else grants the viewer nothing — which is exactly the case that makes Pyroland
    /// cosmetics invisible to the players who are not in Pyroland.
    /// </remarks>
    [Test]
    public void ViewerFlags_ForAnItemOnAnotherPlayer_GrantNothing()
    {
        VisionVisibility.ViewerFlags(
            [Worn(Balloonicorn, wearer: 4)], recorder: 3, OptIn).ShouldBe(NoVision);
    }

    /// <remarks>
    /// A SourceTV recording has no recorder entity, and a spectator carries nothing — so the flags
    /// are zero and every filtered item is hidden, which is what a live spectator sees with
    /// `tf_spectate_pyrovision` at its default of 0 (`c_tf_player.cpp:218`).
    /// </remarks>
    [Test]
    public void ViewerFlags_WithNoRecorder_AreNone()
    {
        VisionVisibility.ViewerFlags([Worn(Balloonicorn)], recorder: null, OptIn)
            .ShouldBe(NoVision);
    }

    private const int NoVision = 0;
    private const int Pyro = 1;
    private const int Rome = 4;

    private const int Balloonicorn = 738;
    private const int Rainblower = 741;
    private const int RomeGranting = 30143;
    private const int WantsBoth = 900;
    private const int Hat = 100;

    /// <summary>What each item REQUIRES to be seen.</summary>
    private static int Flags(int item) => item switch
    {
        Balloonicorn => Pyro,
        RomeGranting => Rome,
        WantsBoth => Pyro | Rome,
        _ => NoVision,
    };

    /// <summary>What each prop GRANTS its wearer, asked of the prop as production asks it.</summary>
    private static int OptIn(SceneProp prop) => prop.ItemDefinitionIndex switch
    {
        Balloonicorn or Rainblower => Pyro,
        RomeGranting => Rome,
        _ => NoVision,
    };

    private static SceneProp Worn(int item, int entity = 20, int wearer = 3) =>
        new(
            EntityIndex: entity,
            ModelPath: "models/player/items/pyro/balloonicorn.mdl",
            Kind: SceneModelKind.Studio,
            Pose: new ScenePose(),
            AttachedTo: wearer,
            BoneMerged: true,
            ItemDefinitionIndex: item);
}
