using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Whether a carried weapon in a real demo says who is carrying it.
/// </summary>
/// <remarks>
/// **Written because a correct rule did not work, and there are only two reasons for that.** The
/// engine hides a carried weapon in first person by OWNERSHIP —
/// <c>C_BaseCombatWeapon::ShouldDraw</c> returns false when <c>GetOwner()</c> is the player whose
/// eyes the camera is in, because the viewmodel draws it instead. That rule was implemented in
/// <c>FirstPersonVisibility</c> against <c>SceneProp.OwnedBy</c>, matches the SDK, and did not fix
/// the duplicate weapon (B160).
///
/// So either the rule is wrong, or <b>the data never arrived</b> — and those look identical from a
/// screenshot. This project has a memory about exactly that confusion: a message that never reached
/// the decoder imitates a decode fault perfectly. Checking the input is cheaper than any further
/// theory about the output, and it is the check that was skipped.
///
/// This asks only the first question: does <c>m_hOwnerEntity</c> reach us at all, for the entities
/// that need it? It deliberately does not assert anything about what is drawn — that is a different
/// layer, and conflating them is what produced four wrong fixes in a day.
/// </remarks>
public sealed class CorpusWeaponOwnershipTests
{
    /// <summary>A point-of-view recording, which is where the duplicate was first reported.</summary>
    private const string PovDemo = "movement-test-pov-cp_process";

    [Test]
    public void OwnedBy_TheWeaponsInAPovDemo_NameTheCarrier()
    {
        string path = Corpus.Demo(PovDemo);

        DemoTimeline timeline = TimelineCache.For(path);

        // **Weapons identified by their MODEL PATH, not by a class name.** A prop track carries the
        // model it draws; `w_` and `c_` are Valve's own prefixes for a weapon's world and
        // first-person models, and every weapon in the game lives under models/weapons/.
        List<ScenePropTrack> weapons =
        [
            .. timeline.Props.Where(track =>
                track.ModelPath.Replace('\\', '/')
                    .Contains("models/weapons/", StringComparison.OrdinalIgnoreCase))
        ];

        TestContext.Out.WriteLine(
            $"{Path.GetFileName(path)}: {timeline.Props.Count} prop tracks, " +
            $"{weapons.Count} of them weapons");

        weapons.ShouldNotBeEmpty(
            "a POV recording of a player fighting contains weapon entities; none at all means " +
            "this test is measuring the wrong thing rather than that the demo is unarmed");

        List<ScenePropTrack> owned = [.. weapons.Where(track => track.OwnedBy is not null)];

        TestContext.Out.WriteLine(
            $"  {owned.Count} of {weapons.Count} weapon tracks carry an owner");

        foreach (ScenePropTrack track in weapons.Take(12))
        {
            TestContext.Out.WriteLine(
                $"  entity {track.EntityIndex} '{Path.GetFileName(track.ModelPath)}' " +
                $"owner {track.OwnedBy?.ToString(CultureInfo.InvariantCulture) ?? "NONE"} " +
                $"attached {track.AttachedTo?.ToString(CultureInfo.InvariantCulture) ?? "none"}");
        }

        // **The prediction, and it is the whole experiment.** If weapons carry no owner, the
        // first-person rule cannot fire however correctly it is written, and every further change
        // aimed at the rule is aimed at the wrong layer. If they DO carry one, the rule is reached
        // and the fault is downstream of it. Either answer redirects the search; that is what makes
        // this worth running rather than reasoning about.
        owned.ShouldNotBeEmpty(
            $"not one of {weapons.Count} weapon entities carries m_hOwnerEntity, so the " +
            "ownership rule in FirstPersonVisibility can never fire and the duplicate weapon " +
            "is upstream of it");
    }

    /// <summary>A viewmodel entity must not reach the world as an ordinary prop.</summary>
    /// <remarks>
    /// **The engine draws a viewmodel entity in exactly one circumstance, and a demo is the case it
    /// spells out.** <c>C_BaseViewModel::ShouldDraw</c> (<c>c_baseviewmodel.cpp:277</c>):
    ///
    /// <code>
    /// if ( engine->IsHLTV() )
    /// {
    ///     return ( HLTVCamera()->GetMode() == OBS_MODE_IN_EYE &amp;&amp;
    ///              HLTVCamera()->GetPrimaryTarget() == GetOwner() );
    /// }
    /// </code>
    ///
    /// In eye, and owned by the player being watched — otherwise not drawn at all. Nothing about
    /// that is a world prop, and there is no camera from which one is a piece of scenery.
    ///
    /// **Arms are the instrument because they are unambiguous.** TF2 ships no world entity that
    /// draws disembodied hands and sleeves; <c>c_*_arms.mdl</c> exists only to be held in front of
    /// a first-person camera. A prop track carrying one is therefore a viewmodel entity that
    /// reached the scene as furniture, which is what puts a second weapon on top of the one the
    /// viewmodel pass draws.
    ///
    /// **This is why the ownership fix did not work.** A viewmodel sends <c>m_hOwner</c> on
    /// <c>DT_BaseViewModel</c> (<c>baseviewmodel_shared.cpp:568</c>), a different property in a
    /// different table from the <c>m_hOwnerEntity</c> that <c>FirstPersonVisibility</c> tests. So
    /// the hiding rule was correct, applied to the right idea, and reading a property these
    /// entities never send.
    /// </remarks>
    [Test]
    public void Props_AViewmodelEntity_AreNotTrackedAsWorldProps()
    {
        string path = Corpus.Demo(PovDemo);

        DemoTimeline timeline = TimelineCache.For(path);

        List<ScenePropTrack> arms =
        [
            .. timeline.Props.Where(track =>
                Path.GetFileName(track.ModelPath.Replace('\\', '/'))
                    .EndsWith("_arms.mdl", StringComparison.OrdinalIgnoreCase))
        ];

        foreach (ScenePropTrack track in arms)
        {
            TestContext.Out.WriteLine(
                $"  world prop {track.EntityIndex} '{Path.GetFileName(track.ModelPath)}' " +
                $"owner {track.OwnedBy?.ToString(CultureInfo.InvariantCulture) ?? "NONE"}");
        }

        arms.ShouldBeEmpty(
            "an arms model is first-person-only content, so a world prop drawing one is a " +
            "viewmodel entity that escaped into the scene and is drawn a second time");
    }
}
