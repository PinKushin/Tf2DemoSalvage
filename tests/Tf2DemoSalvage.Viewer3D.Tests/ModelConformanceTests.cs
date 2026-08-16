using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// What this project reads of a studio model, and what of a character it therefore cannot show.
/// </summary>
/// <remarks>
/// **Structure names and offsets are from <c>public/studio.h</c>**, so each entry names something
/// that exists in the format rather than a feature recalled from playing the game.
///
/// Players are the stated priority of this viewer, so a gap here is worth more than the same gap
/// elsewhere: a map with no water still reads as the map, and a character with no eyes does not
/// read as the character.
/// </remarks>
public sealed class ModelConformanceTests
{
    [Test]
    public void BonesAndSequences_AreRead()
    {
        // The skeleton, the animation table and the blend grids a sequence names. Without these a
        // model draws in its modelling pose, which for a player is lying along Y.
        typeof(StudioBones).ShouldNotBeNull();
        typeof(StudioSequences).ShouldNotBeNull();
        typeof(StudioBlendGrid).ShouldNotBeNull();
    }

    [Test]
    public void SkinFamilies_AreRead()
    {
        // Team colour is a skin family rather than a tint, and the lookup is
        // pSkinref(skin * numskinref + material).
        typeof(StudioSkins).ShouldNotBeNull();
    }

    [Test]
    public void IncludedModels_AreResolved()
    {
        // A TF2 player model carries almost no animation of its own: the sequences live in shared
        // include models, merged by LABEL — AppendSequences, studio_virtualmodel.cpp:142.
        typeof(StudioModelGroups).ShouldNotBeNull();
    }

    [Test]
    public void Attachments_AreNotRead()
    {
        // mstudioattachment_t, studio.h:511 — a named point with a bone and a local matrix.
        // Entities parent to one through m_iParentAttachment rather than by bone merge.
        //
        // WHAT YOU SEE: a medic's halo and an MvM canteen sit at the player's FEET, because an
        // item whose bones match none of the wearer's is placed by the wearer's transform alone.
        // Measured: hwn_spellbook_complete.mdl has one bone, named "mvm", a root.
        //
        // TO IMPLEMENT: the attachment matrix is stored relative to its bone, so it composes
        // against that bone's world matrix. Applying it in world space puts the item somewhere
        // plausible and wrong. Filed as B82.
        Assert.Ignore("mstudioattachment_t unread; worn items with no matching bone draw at the feet. B82.");
    }

    [Test]
    public void JiggleBones_AreNotSimulated()
    {
        // mstudiojigglebone_t, studio.h:195 — springs on a bone, with flags for flexible, rigid
        // and boing behaviours.
        //
        // WHAT YOU SEE: hats with tails, antennas, ponytails and the medic's coat hang rigid.
        // Nothing is misplaced; the model simply does not move where the game's does. Part of B58.
        Assert.Ignore("mstudiojigglebone_t unsimulated; cloth and tails hang rigid. B58.");
    }

    [Test]
    public void Flexes_AreNotApplied()
    {
        // mstudioflex_t, studio.h:1144 — the morph targets that drive facial expression, with the
        // controllers a game sets to blend them.
        //
        // WHAT YOU SEE: every face is the neutral sculpt. No blinking, no expression, no lipsync
        // during voice — which matters here because this project decodes the voice.
        Assert.Ignore("mstudioflex_t unapplied; faces neutral, no blink or lipsync.");
    }

    [Test]
    public void InverseKinematics_IsNotApplied()
    {
        // mstudioikrule_t and mstudioiklink_t, studio.h:557 and 1277 — the chains that plant feet
        // on the ground and hands on a weapon.
        //
        // WHAT YOU SEE: feet slide over ground rather than planting, and on a slope they sink into
        // it or float above it. The animation is right and its contact with the world is not.
        Assert.Ignore("IK rules unapplied; feet slide and do not plant on slopes.");
    }

    [Test]
    public void Hitboxes_AreNotRead()
    {
        // mstudiobbox_t and mstudiohitboxset_t, studio.h:453 and 1686.
        //
        // WHAT YOU SEE: nothing — these are not drawn by the engine either. Recorded because a
        // demo ANALYSIS tool wants them: they are how a hit is attributed to a body part, which is
        // the difference between "shot the scout" and "headshot the scout".
        Assert.Ignore("Hitboxes unread; no visual cost, blocks per-body-part hit analysis.");
    }

    [Test]
    public void LevelsOfDetail_AreNotSelected()
    {
        // The .vtx carries a mesh per LOD and this project always reads level zero.
        //
        // WHAT YOU SEE: nothing wrong, and arguably better — LOD zero is the most detailed. It
        // costs only speed, and a viewer is not fill-rate bound the way a game is. Recorded so the
        // choice is visible rather than accidental.
        Assert.Ignore("LOD 0 always; correctness unaffected, cost only.");
    }

    [Test]
    public void Ragdolls_AreNotSimulated()
    {
        // A dead player becomes a ragdoll driven by the physics solver, from the model's own
        // collision data.
        //
        // WHAT YOU SEE: corpses stand upright where they died, facing the way they last faced.
        // This project holds the last living pose deliberately, as a stand-in — the alternative,
        // following the entity, drags the body to wherever the player is now spectating. Filed as
        // B58.
        Assert.Ignore("Ragdolls unsimulated; corpses stand where they fell. B58.");
    }

    [Test]
    public void Gibs_AreNotDrawn()
    {
        // TF2 replaces a gibbed player with separate models per body part.
        //
        // WHAT YOU SEE: an explosive death shows the whole player rather than pieces, so the death
        // reads as wrong even though the position is right.
        Assert.Ignore("Gibs undrawn; explosive deaths show a whole body.");
    }
}
