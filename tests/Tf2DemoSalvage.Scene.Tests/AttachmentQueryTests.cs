using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// What `EntityModelSet` answers when the viewer asks for a posed entity's attachment or hitbox — `GetAttachment`,
/// `LookupAttachment` and `TestHitboxes` — each pinned to an exact world value.
/// </summary>
/// <remarks>
/// **The subject stands at (100, 200, 300) turned yaw 90**, so its one bone's world matrix is that placement: forward
/// (column 0) is +Y, left (column 1) is −X, up (column 2) is +Z (`AngleMatrix`, `mathlib_base.cpp`). A turn is what makes
/// a mistaken column or a missing transpose visible — at yaw 0 every one of them reads the same.
///
/// The attachment hangs one unit along the bone's own X: `ConcatTransforms( GetBone( iBone ), pattachment.local, world )`
/// (`c_baseanimating.cpp:2055`), so it lands at (100, 201, 300).
/// </remarks>
public sealed class AttachmentQueryTests
{
    /// <summary>The entity under test.</summary>
    private const int Entity = 6;

    /// <summary><c>CONTENTS_HITBOX</c>, <c>bspflags.h</c>.</summary>
    private const int ContentsHitbox = 0x40000000;

    /// <summary>
    /// **An attachment asks for the attachment bones and nothing more** (finding 82): `GetAttachment` calls
    /// `CalcAttachments`, which is `SetupBones( NULL, -1, BONE_USED_BY_ATTACHMENT, gpGlobals->curtime )`
    /// (`c_baseanimating.cpp:2088-2094`). An entity in the scene that nothing drew this frame, and that was never built
    /// before, is built for exactly that mask; the widening by last frame's mask (`:2920`) has nothing to add.
    /// </summary>
    [Test]
    public void AttachmentPosition_OnAnEntityNothingDrew_BuildsOnlyTheAttachmentBones()
    {
        EntityModelSet models = new() { PosesUndrawn = false };
        SceneProp[] props = [Subject()];

        models.Add(props, _ => Model());
        models.Instances(props, [], seconds: 0d, pass: EntityModelSet.UndrawnPass);
        models.ReadableBonesOf(Entity).ShouldBe(0, "the control: the undrawn pass built nothing");

        models.AttachmentPosition(Entity, "muzzle").ShouldNotBeNull().Y.ShouldBe(201f, 1e-3f);

        models.ReadableBonesOf(Entity).ShouldBe(StudioBoneFlags.UsedByAttachment);
    }

    /// <remarks>
    /// **The instrument, not the behaviour** (finding 105 D): a skeleton really built twice after the world pass. The
    /// wearer is not drawn (`kRenderNone`), but its bone-merged item is, so the world pass builds the wearer through the
    /// merge with the merge's own mask, `BONE_USED_BY_BONE_MERGE` (`bone_merge_cache.cpp:130`), and its first frame has no
    /// previous mask to widen it. An attachment then asks for `BONE_USED_BY_ATTACHMENT`, which that build did not make
    /// readable, so the query builds the same skeleton a second time — one build, and it is a rebuild. The case beside it
    /// is the control the other way: a first build on an entity nothing built is a build and not a rebuild.
    /// </remarks>
    [Test]
    public void AttachmentRebuilds_AQueryOnAWearerBuiltNarrowByItsItemsMerge_CountsItsBuildAsARebuild()
    {
        EntityModelSet models = new();
        SceneProp wearer = Subject() with { Pose = Subject().Pose with { RenderMode = RenderModes.None } };
        SceneProp hat = new(
            Entity + 1, "models/player/items/hat.mdl", ScenePropTrack.Classify("models/player/items/hat.mdl"), new ScenePose(), Entity)
        {
            BoneMerged = true,
        };
        SceneProp[] props = [hat, wearer];

        models.Add(props, _ => Model());
        models.Instances(props, [], seconds: 0d);
        models.ReadableBonesOf(Entity).ShouldBe(StudioBoneFlags.UsedByBoneMerge, "the control: built through the merge, narrow");

        models.AttachmentPosition(Entity, "muzzle").ShouldNotBeNull();

        (models.AttachmentQueries, models.AttachmentBuilds, models.AttachmentRebuilds).ShouldBe((1, 1, 1));
    }

    /// <remarks>The other half of the counter's control: an entity nothing built this frame is built once, not rebuilt.</remarks>
    [Test]
    public void AttachmentRebuilds_AQueryOnAnEntityNothingBuilt_CountsABuildAndNoRebuild()
    {
        EntityModelSet models = new() { PosesUndrawn = false };
        SceneProp[] props = [Subject()];

        models.Add(props, _ => Model());
        models.Instances(props, [], seconds: 0d, pass: EntityModelSet.UndrawnPass);
        models.AttachmentPosition(Entity, "muzzle").ShouldNotBeNull();

        (models.AttachmentQueries, models.AttachmentBuilds, models.AttachmentRebuilds).ShouldBe((1, 1, 0));
    }

    /// <remarks>
    /// **One bone counter a frame, bumped before anything is drawn** — `C_BaseAnimating::InvalidateBoneCaches()`, called once
    /// from `OnRenderStart` (cdll_client_int.cpp:2163) and nowhere in the view's passes. So an effect asking a drawn entity
    /// for an attachment after the viewmodel and undrawn passes finds its bones readable (`c_baseanimating.cpp:2911`) and
    /// builds nothing. Advancing our clock on every pass made each such query rebuild the skeleton.
    /// </remarks>
    [Test]
    public void AttachmentPosition_AfterTheLaterPassesOnADrawnEntity_BuildsNothing()
    {
        EntityModelSet models = Posed();

        models.Instances([], [], seconds: 0d, pass: EntityModelSet.UndrawnPass);
        models.AttachmentPosition(Entity, "muzzle").ShouldNotBeNull().Y.ShouldBe(201f, 1e-3f);

        (models.AttachmentQueries, models.AttachmentBuilds).ShouldBe((1, 0));
    }

    /// <remarks>
    /// **And the second half of the same defect: the query rolled the mask.** A query that took the first touch of a later
    /// clock frame rolled `m_iPrevBoneMask` to the attachment bit alone (`c_baseanimating.cpp:2884`); next frame an item drawn
    /// before its wearer asks the wearer with its merge mask (`bone_merge_cache.cpp:130`), which widens by that narrow mask
    /// only (`:2920`), and the wearer's own full request then builds it again. One counter a frame: two skeletons, the
    /// wearer and the item, each built once.
    /// </remarks>
    [Test]
    public void Instances_AFrameAfterAnAttachmentQuery_BuildsTheWearerOnceWhenItsItemIsDrawnFirst()
    {
        EntityModelSet models = new();
        SceneProp hat = new(
            Entity + 1, "models/player/items/hat.mdl", ScenePropTrack.Classify("models/player/items/hat.mdl"), new ScenePose(), Entity)
        {
            BoneMerged = true,
        };
        SceneProp[] props = [hat, Subject()];

        models.Add(props, _ => Model());
        models.Instances(props, [], seconds: 0d);
        models.Instances([], [], seconds: 0d, pass: EntityModelSet.UndrawnPass);
        _ = models.AttachmentPosition(Entity, "muzzle").ShouldNotBeNull();

        int before = models.BuildsDrawing;

        models.Instances(props, [], seconds: 0.015d);

        (models.BuildsDrawing - before).ShouldBe(2);
    }

    [Test]
    public void AttachmentPosition_ByName_IsTheBoneTimesTheLocalOffset()
    {
        EntityModelSet models = Posed();

        (float X, float Y, float Z) at = models.AttachmentPosition(Entity, "muzzle").ShouldNotBeNull();

        at.X.ShouldBe(100f, 1e-3f);
        at.Y.ShouldBe(201f, 1e-3f);
        at.Z.ShouldBe(300f, 1e-3f);
    }

    /// <summary>`Studio_FindAttachment` compares with `stricmp`.</summary>
    [Test]
    public void AttachmentPosition_ByNameInAnotherCase_StillFindsIt()
    {
        Posed().AttachmentPosition(Entity, "MUZZLE").ShouldNotBeNull().Y.ShouldBe(201f, 1e-3f);
    }

    [Test]
    public void AttachmentPosition_ByANameTheModelLacks_IsNull()
    {
        Posed().AttachmentPosition(Entity, "nothing").ShouldBeNull();
    }

    /// <summary>`GetAttachment( iAttachment )` is one-based: 1 is the first, 0 is none.</summary>
    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(2, false)]
    [TestCase(3, false)]
    public void AttachmentPosition_ByNumber_IsOneBased(int number, bool found)
    {
        (float X, float Y, float Z)? at = Posed().AttachmentPosition(Entity, number);

        // Number 2 names an attachment on a bone the model does not have, which resolves to nothing.
        if (found)
        {
            at.ShouldNotBeNull().Y.ShouldBe(201f, 1e-3f);
        }
        else
        {
            at.ShouldBeNull();
        }
    }

    /// <summary>
    /// `MatrixVectors( attachmentToWorld, &amp;forward, &amp;right, &amp;up )`: forward is column 0, up column 2, and right the
    /// NEGATED column 1, because a Source matrix's second axis points left.
    /// </summary>
    [Test]
    public void AttachmentPoint_ByNumber_CarriesForwardRightAndUp()
    {
        ParticleControlPoint point = Posed().AttachmentPoint(Entity, 1).ShouldNotBeNull();

        Near(point.At, new Vector3(100f, 201f, 300f));
        Near(point.Forward, new Vector3(0f, 1f, 0f));
        Near(point.Right, new Vector3(1f, 0f, 0f));
        Near(point.Up, new Vector3(0f, 0f, 1f));
    }

    [Test]
    public void AttachmentPoint_ByName_IsTheSamePoint()
    {
        Near(Posed().AttachmentPoint(Entity, "muzzle").ShouldNotBeNull().At, new Vector3(100f, 201f, 300f));
    }

    [Test]
    public void AttachmentPoint_ForAnEntityNotPosed_IsNull()
    {
        Posed().AttachmentPoint(Entity + 1, 1).ShouldBeNull();
    }

    /// <summary>
    /// A ray along +X at the bone's height meets the box's face at world X = 98: the box's ±2 along its LOCAL Y is ±2
    /// along world X once yaw 90 has turned it, so the fraction is (98 − 90) / 20.
    /// </summary>
    [Test]
    public void TraceHitboxes_ARayThroughTheTurnedBox_StopsAtItsFace()
    {
        float? fraction = Posed().TraceHitboxes(
            Entity, new Vector3(90f, 200f, 300f), new Vector3(20f, 0f, 0f), ContentsHitbox);

        fraction.ShouldNotBeNull().ShouldBe(0.4f, 1e-4f);
    }

    /// <remarks>
    /// **A static prop no view reached this frame has no posed hitboxes** (finding 83 item 1). It is filed with the leaf
    /// system and simulated only when a view collates it, so a bullet strike against one that is off screen answers
    /// null, and `BulletStrikes` falls back to its unposed rule. The engine traces a static prop through its collision
    /// model by index, never its hitboxes — `IStaticPropMgr::TraceRayAgainstStaticProp` (`IStaticPropMgr.h:47`), and a
    /// decal goes on one through `AddDecalToStaticProp` (`:64`); here that is `StaticPropCollision` — so this is not a
    /// loss of parity. The control: the same prop in view stops the ray at its face.
    /// </remarks>
    [TestCase(true, 0.4f)]
    [TestCase(false, null)]
    public void TraceHitboxes_OnAStaticProp_AnswersOnlyWhenAViewReachedIt(bool viewed, float? expected)
    {
        SceneProp placed = PropModels.StaticModel(
            new Content.Bsp.BspStaticProp("models/props_gameplay/resupply_locker.mdl", 100f, 200f, 300f, 0f, 90f, 0f, 1f), 0);
        EntityModelSet models = new() { StaticProps = [placed] };

        models.Add([], _ => Model() with { HeaderBounds = new StudioBox(-4f, -4f, -4f, 4f, 4f, 4f) });
        models.Instances(
            [],
            [],
            seconds: 0d,
            frustum: viewed
                ? default
                : ViewFrustum.Perspective((0f, 0f, 0f), (-1f, 0f, 0f), (0f, 1f, 0f), (0f, 0f, 1f), 1f, 10000f, 90f, 60f));

        float? fraction = models.TraceHitboxes(
            placed.EntityIndex, new Vector3(90f, 200f, 300f), new Vector3(20f, 0f, 0f), ContentsHitbox);

        if (expected is { } stops)
        {
            fraction.ShouldNotBeNull().ShouldBe(stops, 1e-4f);
        }
        else
        {
            fraction.ShouldBeNull();
        }
    }

    /// <summary>
    /// A point five units along the bone's forward lies at local (5, 0, 0): the transpose of the rotation, not the
    /// rotation. The box reaches 1 along X, so it is 4 outside, and 5 from the centre.
    /// </summary>
    [Test]
    public void HitboxGap_APointAlongTheBonesForward_IsMeasuredInTheBonesFrame()
    {
        (float Outside, Vector3 FromCentre) gap =
            Posed().HitboxGap(Entity, 0, new Vector3(100f, 205f, 300f)).ShouldNotBeNull();

        gap.Outside.ShouldBe(4f, 1e-3f);
        Near(gap.FromCentre, new Vector3(5f, 0f, 0f));
    }

    [TestCase(-1)]
    [TestCase(1)]
    public void HitboxGap_ForABoxTheSetLacks_IsNull(int box)
    {
        Posed().HitboxGap(Entity, box, Vector3.Zero).ShouldBeNull();
    }

    /// <summary>
    /// An item sharing no bone with its wearer hangs from the wearer's attachment — `m_iParentAttachment`, one-based — so
    /// its root bone is the attachment's world matrix: at (100, 201, 300), turned as the wearer is (B82).
    /// </summary>
    [Test]
    public void Instances_AnItemHungFromAWearersAttachment_IsPlacedAtIt()
    {
        float[] root = ItemRoot(point: 1);

        root[3].ShouldBe(100f, 1e-3f);
        root[7].ShouldBe(201f, 1e-3f);
        root[11].ShouldBe(300f, 1e-3f);

        // Column 0, the item's forward, is the wearer's: +Y.
        root[0].ShouldBe(0f, 1e-3f);
        root[4].ShouldBe(1f, 1e-3f);
    }

    /// <summary>
    /// Point 2 is in the wearer's table, but on a bone the wearer does not have, so nothing is resolved for it and the item
    /// keeps the placement `CalcAbsolutePosition` gave it — its wearer's origin.
    /// </summary>
    [Test]
    public void Instances_AnItemHungFromAnUnresolvedAttachment_KeepsItsWearersOrigin()
    {
        float[] root = ItemRoot(point: 2);

        root[3].ShouldBe(100f, 1e-3f);
        root[7].ShouldBe(200f, 1e-3f);
        root[11].ShouldBe(300f, 1e-3f);
    }

    /// <summary>Poses the subject wearing an item hung from one of its attachments; returns the item's root bone.</summary>
    private static float[] ItemRoot(int point)
    {
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        const string Item = "models/items/spellbook.mdl";

        SceneProp[] props =
        [
            new(
                Entity,
                "models/props_gameplay/resupply_locker.mdl",
                ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
                new ScenePose { X = 100f, Y = 200f, Z = 300f, Yaw = 90f },
                null),
            new(
                Entity + 2,
                Item,
                ScenePropTrack.Classify(Item),
                new ScenePose(),
                AttachedTo: Entity,
                AttachmentPoint: point),
        ];

        models.Add(props, path => path == Item ? ItemModel() : Model());
        models.Instances(props, instances, seconds: 0d);

        return instances.Single(instance => instance.EntityIndex == Entity + 2).Bones.ShouldNotBeNull()[0];
    }

    /// <summary>A one-bone item whose bone the wearer does not have, so nothing merges.</summary>
    private static PropModels.ModelFrames ItemModel() =>
        Model() with { Skinned = SyntheticSkinnedModel.WithBones("mvm"), Attachments = null, Hitboxes = null };

    private static void Near(Vector3 actual, Vector3 expected)
    {
        actual.X.ShouldBe(expected.X, 1e-3f);
        actual.Y.ShouldBe(expected.Y, 1e-3f);
        actual.Z.ShouldBe(expected.Z, 1e-3f);
    }

    /// <summary>The subject at (100, 200, 300), yaw 90.</summary>
    private static SceneProp Subject() =>
        new(
            Entity,
            "models/props_gameplay/resupply_locker.mdl",
            ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
            new ScenePose { X = 100f, Y = 200f, Z = 300f, Yaw = 90f },
            null);

    /// <summary>The subject, posed by one pass.</summary>
    private static EntityModelSet Posed()
    {
        EntityModelSet models = new();

        SceneProp[] props = [Subject()];

        models.Add(props, _ => Model());
        models.Instances(props, [], seconds: 0d);

        return models;
    }

    /// <summary>One bone, two attachments (one on a bone that does not exist), and one hitbox.</summary>
    private static PropModels.ModelFrames Model() =>
        new(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
            [0],
            [true],
            Skinned: SyntheticSkinnedModel.WithBones("root"),
            Attachments:
            [
                new StudioAttachment("muzzle", 0, 0, [1f, 0f, 0f, 1f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f]),
                new StudioAttachment("orphan", 0, 5, [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f]),
            ],
            Hitboxes:
            [
                [new StudioHitbox(0, 0, new Vector3(-1f, -2f, -3f), new Vector3(1f, 2f, 3f), ContentsHitbox)],
            ]);
}
