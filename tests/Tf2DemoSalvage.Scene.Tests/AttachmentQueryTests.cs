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

    /// <summary>The subject, posed by one pass.</summary>
    private static EntityModelSet Posed()
    {
        EntityModelSet models = new();

        SceneProp[] props =
        [
            new(
                Entity,
                "models/props_gameplay/resupply_locker.mdl",
                ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
                new ScenePose { X = 100f, Y = 200f, Z = 300f, Yaw = 90f },
                null),
        ];

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
