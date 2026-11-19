using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Where a bone-merged entity's ORIGIN is — <c>C_BaseAnimating::GetAimEntOrigin</c>, which every bone it does not merge is
/// built from (B69).
/// </summary>
/// <remarks>
/// <c>CalcAbsolutePosition</c> sends a bone-merged entity to <c>MoveToAimEnt</c> (<c>c_baseentity.cpp:4387-4391</c>), and
/// the animating override decides the place (<c>c_baseanimating.cpp:5986-5999</c>):
///
/// <code>
///   if ( IsEffectActive( EF_BONEMERGE ) &amp;&amp; IsEffectActive( EF_BONEMERGE_FASTCULL ) &amp;&amp; (pMoveParent = GetMoveParent()) != NULL )
///   {
///       *pAbsOrigin = pMoveParent-&gt;WorldSpaceCenter();
///       *pAbsAngles = pMoveParent-&gt;GetRenderAngles();
///   }
///   else
///   {
///       if ( !m_pBoneMergeCache || !m_pBoneMergeCache-&gt;GetAimEntOrigin( pAbsOrigin, pAbsAngles ) )
///           BaseClass::GetAimEntOrigin( pAttachedTo, pAbsOrigin, pAbsAngles );   // the parent's abs origin and angles
///   }
/// </code>
///
/// Every wearable and every combat weapon sets <c>EF_BONEMERGE_FASTCULL</c> in its shared <c>Spawn</c>
/// (<c>econ_wearable.cpp:116</c>, <c>basecombatweapon_shared.cpp:229</c>). So an item whose bones match none of its
/// wearer's — <c>hwn_spellbook_complete.mdl</c>, one root bone <c>mvm</c> — merges nothing
/// (<c>bone_merge_cache.cpp:127</c> returns before copying) and is drawn from the middle of the wearer's hull, not his feet.
///
/// Subject: a player at (100, 200, 300) facing yaw 90, standing (hull 0..82, centre 41 up) and ducked (0..62, centre 31).
/// </remarks>
public sealed class BoneMergeAimEntConformanceTests
{
    private const string ItemModel = "models/workshop/test/item.mdl";
    private const string PlayerModel = "models/player/test.mdl";

    [Test]
    public void Absolute_AFastCullItemOnAStandingPlayer_IsAtHisHullCentreWithHisAngles()
    {
        ScenePose at = ParentChain.Absolute(Item(fastCull: true), Wearer(flags: 0));

        (at.X, at.Y, at.Z, at.Pitch, at.Yaw, at.Roll).ShouldBe((100f, 200f, 341f, 0f, 90f, 0f));
    }

    [Test]
    public void Absolute_AFastCullItemOnADuckedPlayer_IsAtHisDuckedHullCentre()
    {
        ScenePose at = ParentChain.Absolute(Item(fastCull: true), Wearer(flags: PlayerHull.DuckingFlag));

        at.Z.ShouldBe(331f);
    }

    /// <summary>The control, and the other branch: without the flag the base answer is the parent's own origin.</summary>
    [Test]
    public void Absolute_ABoneMergedItemWithoutFastCull_IsAtTheParentsOrigin()
    {
        ScenePose at = ParentChain.Absolute(Item(fastCull: false), Wearer(flags: 0));

        (at.X, at.Y, at.Z, at.Yaw).ShouldBe((100f, 200f, 300f, 90f));
    }

    /// <summary>A parent whose box is not carried centres on its origin — <c>WorldSpaceCenter</c> of no box.</summary>
    [Test]
    public void Absolute_AFastCullItemOnAParentWithNoBox_IsAtTheParentsOrigin()
    {
        SceneProp boxless = Player(flags: 0) with { Collision = null };

        ScenePose at = ParentChain.Absolute(Item(fastCull: true), entity => entity == 1 ? boxless : null);

        at.Z.ShouldBe(300f);
    }

    /// <summary>
    /// **The drawn output**: an unmerged model's vertices are placed by the entity's own transform, so the instance's
    /// translation is the aim-ent origin.
    /// </summary>
    [Test]
    public void Instances_AFastCullItemOnAStandingPlayer_IsDrawnAtHisHullCentre()
    {
        SceneProp player = Player(flags: 0);
        SceneProp item = Item(fastCull: true);

        EntityModelSet models = new();
        models.Add([player, item], _ => Baked());

        List<ModelInstance> drawn = [];
        models.Instances([player, item], drawn, seconds: 0d);

        ModelInstance instance = drawn.Single(model => model.ModelPath == ItemModel);
        (instance.Matrix[12], instance.Matrix[13], instance.Matrix[14]).ShouldBe((100f, 200f, 341f));
    }

    /// <summary>
    /// **The spellbook's case, SKINNED**: its only bone, <c>mvm</c>, matches nothing on the wearer, so the merge copies
    /// nothing and the bone is built from the entity's own transform — the aim-ent origin and the wearer's angles. A muzzle
    /// one unit along that bone, under a wearer facing yaw 90, stands one unit along +Y from the hull centre.
    /// </summary>
    [Test]
    public void AttachmentPosition_ASkinnedFastCullItemMatchingNoBone_HangsFromTheHullCentre()
    {
        SceneProp player = Player(flags: 0);
        SceneProp item = Item(fastCull: true);

        EntityModelSet models = new();
        models.Add(
            [player, item],
            path => path == ItemModel
                ? Baked() with
                {
                    Skinned = SyntheticSkinnedModel.WithBones("mvm"),
                    Attachments = [new StudioAttachment("muzzle", 0, 0, [1f, 0f, 0f, 1f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f])],
                }
                : Baked() with { Skinned = SyntheticSkinnedModel.WithBones("bip_pelvis") });
        models.Instances([player, item], [], seconds: 0d);

        (float X, float Y, float Z) muzzle = models.AttachmentPosition(2, "muzzle").ShouldNotBeNull();
        muzzle.X.ShouldBe(100f, 1e-3f, $"{muzzle}");
        muzzle.Y.ShouldBe(201f, 1e-3f, $"{muzzle}");
        muzzle.Z.ShouldBe(341f, 1e-3f, $"{muzzle}");
    }

    /// <summary>
    /// **A merged item that names <c>m_iParentAttachment</c> is NOT hung from it**: <c>CalcAbsolutePosition</c> returns
    /// through <c>MoveToAimEnt</c> (<c>c_baseentity.cpp:4387-4391</c>) before <c>GetParentToWorldTransform</c>, the only
    /// reader of the attachment (<c>:4330-4342</c>). The wearer's attachment 1 is a unit along his forward, so honouring it
    /// would put the item's muzzle at (100, 202, 300); the engine's answer is the hull centre's, (100, 201, 341).
    /// </summary>
    [Test]
    public void AttachmentPosition_AMergedItemNamingAParentAttachment_IgnoresIt()
    {
        SceneProp player = Player(flags: 0);
        SceneProp item = Item(fastCull: true) with { AttachmentPoint = 1 };

        EntityModelSet models = new();
        models.Add(
            [player, item],
            path => path == ItemModel
                ? Baked() with
                {
                    Skinned = SyntheticSkinnedModel.WithBones("mvm"),
                    Attachments = [Muzzle(0)],
                }
                : Baked() with { Skinned = SyntheticSkinnedModel.WithBones("bip_pelvis"), Attachments = [Muzzle(0)] });
        models.Instances([player, item], [], seconds: 0d);

        (float X, float Y, float Z) muzzle = models.AttachmentPosition(2, "muzzle").ShouldNotBeNull();
        muzzle.X.ShouldBe(100f, 1e-3f, $"{muzzle}");
        muzzle.Y.ShouldBe(201f, 1e-3f, $"{muzzle}");
        muzzle.Z.ShouldBe(341f, 1e-3f, $"{muzzle}");
    }

    /// <summary>
    /// **Without fast-cull, a merge that matched aligns its FIRST merged bone** — <c>CBoneMergeCache::GetAimEntOrigin</c>
    /// (<c>bone_merge_cache.cpp:227-259</c>): <c>mEntity = mFollowBone * Inverse( mBoneLocal )</c>, the local being the
    /// sequence's first animation at frame 0 (<c>SetupSingleBoneMatrix</c>, <c>bone_setup.cpp:570-611</c>). The item's
    /// <c>bip_pelvis</c> sits 10 up in its own rest pose and the wearer's is at his origin, so the entity stands 10 below
    /// him, (100, 200, 290); its unmatched <c>mvm</c> root carries the muzzle one unit along the wearer's yaw-90 forward.
    /// The parent's origin would answer (100, 201, 300) and fast-cull (100, 201, 341).
    /// </summary>
    [Test]
    public void AttachmentPosition_AMergeWithoutFastCullThatMatched_AlignsItsFirstMergedBone()
    {
        SceneProp player = Player(flags: 0);
        SceneProp item = Item(fastCull: false);
        PropModels.SkinnedModel skeleton = SyntheticSkinnedModel.WithBones("bip_pelvis", "mvm");

        EntityModelSet models = new();
        models.Add(
            [player, item],
            path => path == ItemModel
                ? Baked() with
                {
                    Skinned = skeleton with
                    {
                        Bones = [skeleton.Bones[0] with { Position = (0f, 0f, 10f) }, skeleton.Bones[1] with { Parent = -1 }],
                    },
                    Attachments = [Muzzle(1)],
                }
                : Baked() with { Skinned = SyntheticSkinnedModel.WithBones("bip_pelvis") });
        models.Instances([player, item], [], seconds: 0d);

        (float X, float Y, float Z) muzzle = models.AttachmentPosition(2, "muzzle").ShouldNotBeNull();
        muzzle.X.ShouldBe(100f, 1e-3f, $"{muzzle}");
        muzzle.Y.ShouldBe(201f, 1e-3f, $"{muzzle}");
        muzzle.Z.ShouldBe(290f, 1e-3f, $"{muzzle}");

        // **Its abs origin is that aim-ent origin for every reader** (`MoveToAimEnt`, c_baseentity.cpp:4294-4301), the
        // `PATTACH_ABSORIGIN_FOLLOW` control point among them — not the parent's (100, 200, 300).
        ParticleControlPoint origin = models.AbsOrigin(2).ShouldNotBeNull();
        origin.At.X.ShouldBe(100f, 1e-3f, $"{origin.At}");
        origin.At.Y.ShouldBe(200f, 1e-3f, $"{origin.At}");
        origin.At.Z.ShouldBe(290f, 1e-3f, $"{origin.At}");
    }

    /// <summary>
    /// **An aligned merge is lit where it stands unless it is an owned econ item** (B69): only
    /// <c>CEconEntity::OnInternalDrawModel</c> moves the lighting origin, to the owner's <c>WorldSpaceCenter</c>
    /// (econ_entity.cpp:1949-1962); a corpse's worn gib has no owner and is lit at its render origin, the aim-ent's. The item
    /// of the case above, ownerless, is lit at (100, 200, 290); owned, at its owner's <c>WorldSpaceCenter</c> — his hull's
    /// middle, 300 + 41 standing, 300 + 31 ducked (B69 f) — whether it merges with fast-cull or not.
    /// </summary>
    [TestCase(false, false, 0, 290f)]
    [TestCase(true, false, 0, 341f)]
    [TestCase(true, true, 0, 341f)]
    [TestCase(true, true, PlayerHull.DuckingFlag, 331f)]
    public void IlluminationPoint_AnAlignedMerge_IsItsAimEntOriginUnlessAnOwnedEconItem(bool owned, bool fastCull, int flags, float z)
    {
        SceneProp player = Player(flags);
        SceneProp item = owned ? Item(fastCull) with { OwnedBy = 1, ItemDefinitionIndex = 5 } : Item(fastCull);
        PropModels.SkinnedModel skeleton = SyntheticSkinnedModel.WithBones("bip_pelvis", "mvm");

        EntityModelSet models = new();
        models.Add(
            [player, item],
            path => path == ItemModel
                ? Baked() with
                {
                    Skinned = skeleton with
                    {
                        Bones = [skeleton.Bones[0] with { Position = (0f, 0f, 10f) }, skeleton.Bones[1] with { Parent = -1 }],
                    },
                }
                : Baked() with { Skinned = SyntheticSkinnedModel.WithBones("bip_pelvis") });
        models.Instances([player, item], [], seconds: 0d);

        models.IlluminationPoint(item, item.Pose).ShouldBe((100f, 200f, z));
    }

    /// <summary>
    /// **A merged item's root bone is the WEARER's** — <c>C_BaseAnimating::GetRootBone</c> (<c>c_baseanimating.cpp:2250-2259</c>)
    /// answers <c>CBoneMergeCache::GetRootBone</c>, the follow's bone its first merged bone pairs with
    /// (<c>bone_merge_cache.cpp:261-273</c>); a <c>PATTACH_ROOTBONE_FOLLOW</c> effect stands there (B69). The wearer's
    /// <c>bip_pelvis</c> is at his origin, (100, 200, 300), facing yaw 90; the item's bone 0 is an unmatched <c>mvm</c> at the
    /// hull centre, and its bone 1 the merged one. Matching nothing, there is no root (<c>:266-267</c>).
    /// </summary>
    [TestCase("bip_pelvis", true)]
    [TestCase("mvm_lid", false)]
    public void RootBone_AMergedItem_IsTheWearersBoneItsFirstMergedBonePairsWith(string bone, bool matches)
    {
        SceneProp player = Player(flags: 0);
        // Fast-cull, so the item's unmerged bone 0, `mvm`, hangs from the hull centre and is not the wearer's bone.
        SceneProp item = Item(fastCull: true);
        PropModels.SkinnedModel skeleton = SyntheticSkinnedModel.WithBones("mvm", bone);

        EntityModelSet models = new();
        models.Add(
            [player, item],
            path => path == ItemModel
                ? Baked() with { Skinned = skeleton with { Bones = [skeleton.Bones[0], skeleton.Bones[1] with { Parent = -1 }] } }
                : Baked() with { Skinned = SyntheticSkinnedModel.WithBones("bip_pelvis") });
        models.Instances([player, item], [], seconds: 0d);

        ParticleControlPoint? root = models.RootBone(2);

        if (!matches)
        {
            root.ShouldBeNull();
            return;
        }

        ParticleControlPoint at = root.ShouldNotBeNull();
        at.At.X.ShouldBe(100f, 1e-3f, $"{at.At}");
        at.At.Y.ShouldBe(200f, 1e-3f, $"{at.At}");
        at.At.Z.ShouldBe(300f, 1e-3f, $"{at.At}");
        at.Forward.Y.ShouldBe(1f, 1e-3f, "yaw 90: the bone's forward is the wearer's");
    }

    /// <summary>
    /// **A weapon's view-model extra wearable is posed by the first-person pass under its own entity index** (B69, B771):
    /// <c>CTFWearableVM</c> follows the view model bone-merged (<c>econ_wearable.cpp:185-192</c>), and its <c>attach_fob_v</c> —
    /// where a weapon's view-model effect hangs (<c>econ_entity.cpp:1616-1645</c>) — answers after that pass. It is NOT
    /// corrected for the view model's lens: <c>FormatViewModelAttachment</c> is overridden only by <c>C_BaseViewModel</c> and
    /// <c>C_ViewmodelAttachmentModel</c>, the <c>C_BaseAnimating</c> body being empty (<c>c_baseanimating.h:456</c>), so the
    /// wearable's point is the same with a projection as without. The control: the view model's own attachment moves.
    /// </summary>
    [Test]
    public void AttachmentPoint_AViewModelWearableAfterTheFirstPersonPass_IsItsOwnAndUncorrected()
    {
        const int Wearable = 77;
        SceneProp arms = new(
            ViewmodelScene.ArmsEntityIndex, PlayerModel, SceneModelKind.Studio, new ScenePose { X = 100f, Y = 200f, Z = 300f, Yaw = 90f });
        SceneProp fob = new(
            Wearable, ItemModel, SceneModelKind.Studio, new ScenePose { X = 100f, Y = 200f, Z = 300f, Yaw = 90f },
            AttachedTo: ViewmodelScene.ArmsEntityIndex, BoneMerged: true);
        StudioAttachment fobV = Muzzle(0) with { Name = "attach_fob_v" };

        (ParticleControlPoint? Fob, ParticleControlPoint? Arms) Posed(ViewmodelProjection? projection)
        {
            EntityModelSet models = new() { ViewmodelProjection = projection };
            models.Add(
                [arms, fob],
                _ => Baked() with { Skinned = SyntheticSkinnedModel.WithBones("bip_pelvis"), Attachments = [fobV] });
            models.Instances([arms, fob], [], seconds: 0d, pass: EntityModelSet.ViewmodelPass);

            return (models.AttachmentPoint(Wearable, "attach_fob_v"), models.AttachmentPoint(ViewmodelScene.ArmsEntityIndex, "attach_fob_v"));
        }

        (ParticleControlPoint? plainFob, ParticleControlPoint? plainArms) = Posed(null);
        (ParticleControlPoint? fobThrough, ParticleControlPoint? armsThrough) =
            Posed(new ViewmodelProjection((0f, 0f, 0f), (0f, -1f, 0f), (0f, 0f, 1f), (1f, 0f, 0f), 75f, 54f));

        ParticleControlPoint at = plainFob.ShouldNotBeNull("posed under its own index");
        at.At.Y.ShouldBe(201f, 1e-3f, $"{at.At}");
        fobThrough.ShouldNotBeNull().At.ShouldBe(at.At, "a wearable's attachments are not corrected");
        armsThrough.ShouldNotBeNull().At.ShouldNotBe(plainArms.ShouldNotBeNull().At, "the control: the view model's are");
    }

    /// <summary>
    /// **<c>mBoneLocal</c> takes the animation only for a bone its sequence weighs** — <c>SetupSingleBoneMatrix</c>
    /// (<c>bone_setup.cpp:570-611</c>): <c>if ( panim &amp;&amp; seqdesc.weight( iBone ) &gt; 0 )</c> the animation, else
    /// identity for a delta, else the rest pose. Rest 3 up, animated 10 up.
    /// </summary>
    [TestCase(true, 1f, false, 10f, TestName = "SingleBone_AnAnimatedWeighedBone_IsTheAnimation")]
    [TestCase(true, 0f, false, 3f, TestName = "SingleBone_AnAnimatedBoneWeighedZero_IsTheRestPose")]
    [TestCase(true, 0f, true, 0f, TestName = "SingleBone_AnAnimatedBoneWeighedZeroInADelta_IsIdentity")]
    [TestCase(false, 1f, false, 3f, TestName = "SingleBone_ABoneTheAnimationLacks_IsTheRestPose")]
    [TestCase(false, 1f, true, 0f, TestName = "SingleBone_ABoneADeltaLacks_IsIdentity")]
    public void SingleBone_ByAnimationWeightAndDelta_IsTheEnginesChoice(bool animated, float weight, bool delta, float z)
    {
        StudioBone rest = SyntheticSkinnedModel.WithBones("bip_pelvis").Bones[0] with { Position = (0f, 0f, 3f) };
        StudioBonePose? pose = animated ? new StudioBonePose(0, (0f, 0f, 10f), (0f, 0f, 0f, 1f)) : null;

        EntityModelSet.SingleBone(rest, pose, weight, delta).Position.ShouldBe((0f, 0f, z));
    }

    /// <summary>A <c>muzzle</c> a unit along a bone's X with no turn of its own.</summary>
    private static StudioAttachment Muzzle(int bone) =>
        new("muzzle", 0, bone, [1f, 0f, 0f, 1f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f]);

    /// <summary><c>PlayerProps</c> hands every player his hull, so a child can find its centre.</summary>
    [TestCase(0, 82f, TestName = "Collision_AStandingPlayer_IsTheStandingHull")]
    [TestCase(PlayerHull.DuckingFlag, 62f, TestName = "Collision_ADuckedPlayer_IsTheDuckedHull")]
    public void Collision_APlayer_IsHisHull(int flags, float height)
    {
        SceneCollision hull = PlayerHull.Collision(flags);

        (hull.SolidType, hull.Mins, hull.Maxs, hull.BoundsInEntitySpace)
            .ShouldBe((2, (-24f, -24f, 0f), (24f, 24f, height), false));
    }

    private static SceneProp Item(bool fastCull) =>
        new(2, ItemModel, SceneModelKind.Studio, new ScenePose(), AttachedTo: 1, BoneMerged: true, BoneMergeFastCull: fastCull);

    private static SceneProp Player(int flags) =>
        new(
            1,
            PlayerModel,
            SceneModelKind.Studio,
            new ScenePose { X = 100f, Y = 200f, Z = 300f, Yaw = 90f, Flags = flags },
            Collision: PlayerHull.Collision(flags));

    private static System.Func<int, SceneProp?> Wearer(int flags)
    {
        SceneProp player = Player(flags);

        return entity => entity == 1 ? player : null;
    }

    private static PropModels.ModelFrames Baked() =>
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
            [true]);
}
