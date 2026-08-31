using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Packing entity models once and posing them with a matrix.
/// </summary>
/// <remarks>
/// **The arrangement is the engine's**: model-space vertices in a buffer that never changes, and a
/// matrix per instance handed to the shader. The first version of this transformed every vertex on
/// the processor each frame, which is exactly the work <c>LoadBoneMatrix</c> exists to avoid.
///
/// Tested with a fake loader, because reading a model needs the map's pakfile and the game's
/// archives while the parts worth checking are the packing and the matrix.
/// </remarks>
public sealed class EntityModelsTests
{
    [Test]
    public void EntityModels_AModel_IsPackedInItsOwnCoordinates()
    {
        // **Not moved to where the entity stands**, which is the whole difference from the version
        // this replaced. The vertex keeps the model's own coordinates and the matrix carries the
        // placement, so the buffer can be uploaded once and never touched again.
        EntityModelSet models = new();

        models.Add([Prop("models/props/crate.mdl", x: 100f, y: 200f, z: 30f)], OneTriangle);

        models.Vertices.Count.ShouldBe(3);
        models.Vertices[0].X.ShouldBe(1f, 1e-4f);
        models.Vertices[0].Y.ShouldBe(0f, 1e-4f);
        models.Vertices[0].Depth.ShouldBe(0f, 1e-4f);
    }

    [Test]
    public void AModelIsPackedOnce_HoweverManyEntitiesWearIt()
    {
        // A match carries many copies of one rocket. Packing per instance would multiply the
        // buffer by the number of entities and defeat the arrangement entirely.
        EntityModelSet models = new();

        models.Add(
            [
                Prop("models/props/crate.mdl", x: 10f),
                Prop("models/props/crate.mdl", x: 20f),
                Prop("models/props/crate.mdl", x: 30f),
            ],
            OneTriangle);

        models.Count.ShouldBe(1);
        models.Vertices.Count.ShouldBe(3);
    }

    [Test]
    public void EntityModels_EachInstance_CarriesItsOwnPlacement()
    {
        // Three entities, one model, three matrices. The translation lives in the last row.
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props =
        [
            Prop("models/props/crate.mdl", x: 10f),
            Prop("models/props/crate.mdl", x: 20f),
        ];

        models.Add(props, OneTriangle);
        models.Instances(props, instances);

        instances.Count.ShouldBe(2);
        instances[0].Matrix[12].ShouldBe(10f, 1e-4f);
        instances[1].Matrix[12].ShouldBe(20f, 1e-4f);
    }

    [Test]
    public void AnInstanceWhoseModelDidNotLoad_IsNotDrawn()
    {
        // Otherwise the renderer sets a matrix and draws nothing, once per frame per missing
        // model - invisible in the picture and pure cost.
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props = [Prop("models/props/missing.mdl")];

        models.Add(props, _ => null);
        models.Instances(props, instances);

        instances.ShouldBeEmpty();
    }

    [Test]
    public void Add_WithNoGeometrySourceSet_PacksNothingRatherThanThrowing()
    {
        // **Every frame before a map is read takes this path**, and there are a lot of them: the
        // viewer pumps frames from the moment the window opens. Answering nothing is what lets the
        // source be set once at map load rather than null-checked at each call — the null-object
        // shape D83 settled on.
        EntityModelSet models = new();

        Should.NotThrow(() => models.Add([Prop("models/props/crate.mdl")]));

        models.Vertices.ShouldBeEmpty();
    }

    [Test]
    public void Add_WithAGeometrySourceSet_PacksThroughIt()
    {
        // **The control for the case above**, and the one that makes it a measurement rather than
        // an assertion that packing never works. Same prop, same call, only the source differs.
        EntityModelSet models = new()
        {
            Geometry = OneTriangle,
        };

        models.Add([Prop("models/props/crate.mdl")]);

        models.Vertices.Count.ShouldBe(3);
    }

    [Test]
    public void EntityModels_AFailedModel_IsNotRetriedEveryFrame()
    {
        // Asking again sixty times a second buries the log in one repeated line, which is how a
        // real missing asset stops being noticeable.
        EntityModelSet models = new();
        int attempts = 0;

        SceneProp[] props = [Prop("models/props/missing.mdl")];

        for (int frame = 0; frame < 5; frame++)
        {
            models.Add(
                props,
                _ =>
                {
                    attempts++;
                    return null;
                });
        }

        attempts.ShouldBe(1);
    }

    [Test]
    public void SpritesAreNotHandedToTheLoader_ButBrushModelsAre()
    {
        // **This test asserted that `*3` was withheld too, and that was right until it wasn't.**
        // The original reasoning — "neither is a .mdl, and giving either to a studio loader draws
        // nothing while reporting nothing" — was sound when the loader could only read .mdl files.
        // A `*3` now resolves to geometry the map itself built from its models lump (B71), so
        // withholding it is what draws nothing: it is the door.
        //
        // The sprite half is unchanged and still load-bearing. A .spr is a camera-facing quad with
        // no geometry of its own, and nothing has been built to make one.
        //
        // Kept as one test rather than split, because the point is the boundary between the two
        // and a boundary is only visible with both sides in it.
        EntityModelSet models = new();
        List<string> asked = [];

        models.Add(
            [Prop("*3"), Prop("sprites/glow06.spr"), Prop("models/props/crate.mdl")],
            path =>
            {
                asked.Add(path);
                return OneTriangle(path);
            });

        asked.ShouldBe(["*3", "models/props/crate.mdl"]);
    }

    [Test]
    public void EntityModels_EveryBatch_CoversTheVerticesItClaims()
    {
        // Batches index into one shared buffer, so a wrong offset draws another model's triangles
        // with this model's texture - which looks like a texture bug and is an arithmetic one.
        EntityModelSet models = new();

        SceneProp[] props = [Prop("models/props/crate.mdl"), Prop("models/props/barrel.mdl")];

        models.Add(
            props,
            path => path.Contains("barrel", StringComparison.Ordinal)
                ? new PropModels.ModelFrames(
                    [new PropVertex[] { new(0f, 0f, 0f, 0f, 0f, MaterialIndex: 7) }],
                    new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)>
                    {
                        [0] = (0, 1, 0f),
                    },
                    [0],
                    [true])
                : OneTriangle(path));

        List<WorldBatch> all =
            [.. props.SelectMany(prop => models.Batches(prop.ModelPath))];

        all.Sum(batch => batch.VertexCount).ShouldBe(models.Vertices.Count);

        foreach (WorldBatch batch in all)
        {
            (batch.FirstVertex + batch.VertexCount)
                .ShouldBeLessThanOrEqualTo(models.Vertices.Count);
        }
    }

    /// <summary>A triangle whose first corner sits one unit along the model's own X.</summary>
    /// <remarks>
    /// One baked frame, which is what a model that does not animate has. The frame machinery is
    /// exercised against real models in the content tests; here it should stay out of the way.
    /// </remarks>
    /// <summary>That an instance carries the two-pass flag its MODEL declared.</summary>
    /// <remarks>
    /// **The join, which neither side's tests can see.** `StudioModel` reads the flag out of a real
    /// `.mdl` and passes whether or not anything carries it forward; `RenderGroups` decides from a
    /// boolean and passes whether or not that boolean ever came from a file. Between them is this
    /// assignment, and an assignment that was never written is exactly the shape of no-op this
    /// project has shipped three times with a green suite — see
    /// `docs/memory/output-level-assertion-or-it-is-not-done.md`.
    ///
    /// **The control is the second prop**, packed by the same call from a model that declares
    /// nothing. Without it, an implementation that hardcoded `TwoPass: true` would pass — and a
    /// hardcoded `false` is the more likely mistake, since it is the parameter's default.
    /// </remarks>
    [Test]
    public void Instances_AModelDeclaringMostlyOpaque_CarryTheTwoPassFlag()
    {
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props =
        [
            Prop("models/props/glass.mdl", x: 10f, entity: 1),
            Prop("models/props/crate.mdl", x: 20f, entity: 2),
        ];

        models.Add(
            props,
            path => path.Contains("glass", StringComparison.Ordinal)
                ? OneTriangle(path) with { TwoPass = true }
                : OneTriangle(path));

        models.Instances(props, instances);

        instances.Count.ShouldBe(2);

        instances[0].TwoPass.ShouldBeTrue("the glass model declared $mostlyopaque");
        instances[1].TwoPass.ShouldBeFalse("the crate declared nothing");
    }

    private static PropModels.ModelFrames OneTriangle(string path) =>
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

    [Test]
    public void AWornItem_IsDrawnWhereItsWearerIs()
    {
        // **A bone-merged entity has no position of its own and is not sent one.** FollowEntity
        // zeroes local origin and angles, because the client takes the parent's bone matrices
        // outright (shared/baseentity_shared.cpp:2360). So a hat recorded at (0,0,0) must end up
        // wherever the player is, and drawing it at its own pose puts every cosmetic in the match
        // in one heap at the map origin.
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props =
        [
            Prop("models/player/scout.mdl", x: 500f, entity: 7),
            Prop("models/player/items/hat.mdl", entity: 40, attachedTo: 7),
        ];

        models.Add(props, OneTriangle);
        models.Instances(props, instances);

        instances.Count.ShouldBe(2);

        // The hat's own pose is the origin; its wearer stands at 500.
        instances[1].Matrix[12].ShouldBe(500f, 1e-4f);
    }

    [Test]
    public void AWornItemWhoseWearerIsNotDrawn_IsNotDrawnEither()
    {
        // **The control, and it is the one that decides where the failure shows.** Without it a
        // hat whose wearer is dead, out of the visible set or failed to load keeps the only pose
        // it has — the world origin — and hangs in mid-air near the middle of the map. That reads
        // as a stray prop rather than as a missing player.
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props = [Prop("models/player/items/hat.mdl", entity: 40, attachedTo: 7)];

        models.Add(props, OneTriangle);
        models.Instances(props, instances);

        instances.ShouldBeEmpty();
    }

    [Test]
    public void Instances_ASkinnedModel_IsPosedAndPlacedWithoutThrowing()
    {
        // **This is the test that was missing, and its absence shipped a crash on the first frame
        // of playback.** Every other case in this file loads OneTriangle, which is BAKED — so
        // nothing here had ever driven Instances() with a model carrying a skeleton, and the whole
        // skinned path was reachable only by launching the viewer.
        //
        // What it caught, once written: PlacementOf handed a sixteen-float model matrix to
        // something expecting Valve's twelve-float matrix3x4_t, and Array.CopyTo threw
        // `Destination array was not long enough` the instant a demo played. The length mismatch
        // was the lucky half — two forms of the same size would have transposed silently and drawn
        // a plausible wrong pose.
        //
        // Note it does not need bones to be useful: the placement is computed before any bone
        // count is consulted, so even a skeleton of one bone exercises the crossing.
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props = [Prop("models/player/scout.mdl", x: 300f, y: -40f, z: 12f, entity: 5)];

        models.Add(props, OneSkinnedBone);
        models.Instances(props, instances, seconds: 0.25d);

        instances.Count.ShouldBe(1);

        // Bones came back, so the pose path ran rather than being skipped.
        instances[0].Bones.ShouldNotBeNull();
        instances[0].Bones!.Count.ShouldBe(1);

        // **The model matrix is identity, and that is the D88 contract.** A skinned model's bones
        // are in WORLD space, so placing it again would move it twice. The control is the baked
        // case above, whose matrix carries its position — if this ever starts carrying 300 too,
        // the two conventions have been crossed back.
        instances[0].Matrix[12].ShouldBe(0f, 1e-4f);
        instances[0].Matrix[13].ShouldBe(0f, 1e-4f);
        instances[0].Matrix[14].ShouldBe(0f, 1e-4f);

        // And the placement reached the BONES instead — which is the other half of the same claim,
        // and what makes the identity above correct rather than merely empty.
        instances[0].Bones![0][3].ShouldBe(300f, 1e-3f);
        instances[0].Bones![0][7].ShouldBe(-40f, 1e-3f);
        instances[0].Bones![0][11].ShouldBe(12f, 1e-3f);
    }

    [Test]
    public void Instances_AWeaponMergedOntoAPlayer_IsWhereThePlayerIsRatherThanAtTheOrigin()
    {
        // **This is the seam that broke, and nothing covered it.** WeaponMergeContentTests drives
        // AnimatingEntity directly with real TF2 models and passes either way; the defect was in
        // how EntityModelSet FEEDS it. So this is the same claim one layer up, where the wiring is.
        //
        // What went wrong: a followed entity's placement was taken from its own networked pose,
        // which FollowEntity zeroes — so the weapon built at the map origin. Valve resolves it
        // instead: CalcAbsolutePosition branches on EF_BONEMERGE into MoveToAimEnt, which sets the
        // absolute origin to the PARENT's (c_baseentity.cpp:4387, :4294). Eight of nine weapons in
        // a real demo were drawn in the middle of the map.
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props =
        [
            Prop("models/player/demo.mdl", x: 800f, y: 250f, entity: 2),

            // Its own pose is the origin, exactly as the wire sends a bone-merged entity.
            Prop("models/weapons/c_models/c_stickybomb.mdl", entity: 359, attachedTo: 2),
        ];

        // **The weapon has a bone the player does NOT, and that is what makes this test able to
        // fail.** Its first attempt gave both models a single bone called `root`, so the merge
        // supplied the position and the test passed with the defect reverted — insensitive to the
        // manipulation it was written for. A real weapon shares two bones of five with its wielder;
        // the unshared ones are the only place the entity's own placement shows.
        models.Add(
            props,
            path => path.Contains("player", StringComparison.Ordinal)
                ? Skinned("weapon_bone")
                : Skinned("weapon_bone", "muzzle"));

        models.Instances(props, instances);

        instances.Count.ShouldBe(2);
        instances[1].Bones.ShouldNotBeNull();

        // The MATCHED bone, which the merge places. This passes either way and is here as the
        // control that the merge itself works.
        instances[1].Bones![0][3].ShouldBe(800f, 1e-3f);

        // The unmatched CHILD bone, which rides its merged parent — Valve's GetBone( parent ) at
        // c_baseanimating.cpp:1595. Also passes either way; kept because it is the behaviour B180
        // was about and it should stay true.
        instances[1].Bones![1][3].ShouldBe(800f, 1e-3f);
    }

    [Test]
    public void Instances_AWornItemSharingNoBoneName_IsStillPlacedOnItsWearer()
    {
        // **The case that exposes the entity's own placement, and the two above do not.** A bone
        // that merges takes the wearer's matrix; an unmatched bone with a matched PARENT rides that
        // parent. Only an unmatched ROOT is built from the entity's own placement — so only an item
        // sharing NO name with its wearer can tell a resolved placement from an unresolved one.
        //
        // It is not a contrived shape, and it is deliberately not named after one item. A halo, an
        // MvM canteen and a spy's sapper all merge nothing with what they hang from (B82) —
        // `mvm_flask_generic` is in the very demo that showed the defect. The spellbook is the
        // usual example in this repository and is a poor one: its single bone called `mvm` makes it
        // unusual rather than representative, and a test that rested on it would invite the reply
        // that the item is abnormal.
        //
        // What the case actually is: any item whose ROOT bone finds no counterpart.
        //
        // Valve resolves it in CalcAbsolutePosition, which branches on EF_BONEMERGE into
        // MoveToAimEnt and sets the absolute origin to the PARENT's (c_baseentity.cpp:4387, :4294).
        // Reading the networked LOCAL pose instead gives (0,0,0), because FollowEntity zeroes it —
        // and the item is drawn in the middle of the map.
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props =
        [
            Prop("models/player/demo.mdl", x: 800f, y: 250f, entity: 2),
            Prop("models/player/items/worn.mdl", entity: 41, attachedTo: 2),
        ];

        models.Add(
            props,
            path => path.Contains("player/items", StringComparison.Ordinal)
                ? Skinned("its_own_root")
                : Skinned("bip_head"));

        models.Instances(props, instances);

        instances.Count.ShouldBe(2);
        instances[1].Bones.ShouldNotBeNull();

        instances[1].Bones![0][3].ShouldBe(
            800f,
            1e-3f,
            "an item that merges nothing is placed by its own entity transform, which for a " +
            "followed entity is its parent's — otherwise it draws at the map origin");
    }

    /// <summary>A skinned model carrying the named bones and one triangle.</summary>
    private static PropModels.ModelFrames Skinned(params string[] bones) =>
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
            Skinned: SyntheticSkinnedModel.WithBones(bones));

    /// <summary>A model with one bone and one triangle weighted to it.</summary>
    /// <remarks>
    /// The smallest thing that is genuinely SKINNED rather than baked — which is the only property
    /// the test above needs, and the property nothing else in this file has.
    /// </remarks>
    private static PropModels.ModelFrames? OneSkinnedBone(string path) =>
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
            Skinned: SyntheticSkinnedModel.WithOneBone());

    [Test]
    public void Instances_APropAtKRenderNone_IsNotDrawn()
    {
        // **`C_BaseEntity::ShouldDraw`'s first test** (`c_baseentity.cpp:1447`): *"Some rendermodes
        // prevent rendering"*. Eighteen `func_door`s on `cp_fulgur` declare `rendermode 10`, their
        // brushwork is painted `METAL/CHICKEN_WIRE001`, and drawing it stood a coarse wire panel in
        // every setup gate's doorway in front of the grate props (B240).
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props = [Prop("models/props/crate.mdl") with { Pose = Hidden() }];

        models.Add(props, OneTriangle);
        models.Instances(props, instances);

        instances.ShouldBeEmpty();
    }

    [Test]
    public void Instances_APropAtAnyOtherRenderMode_IsDrawn()
    {
        // **The control, and it is the one that keeps this from deleting the game.** Only
        // `kRenderNone` refuses; every other mode is a BLEND and still draws, differently. A rule
        // written as "the mode is not normal" would remove 410 of a real match's 1,973 entities
        // rather than the 118 that ask for it.
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props =
        [
            Prop("models/props/crate.mdl") with
            {
                Pose = new ScenePose { Scale = 1f, RenderMode = 4 },
            },
        ];

        models.Add(props, OneTriangle);
        models.Instances(props, instances);

        instances.ShouldHaveSingleItem();
    }

    [Test]
    public void Instances_AChildOfAKRenderNoneParent_StillFindsItsParent()
    {
        // **The case the first attempt at this broke, and it broke it completely** (B240). Putting
        // the render-mode test in `EntityState.IsDrawn` removed the invisible doors from the SCENE,
        // and every grate prop is parented to one — so the children lost the transform they hang
        // off and every gate vanished. The owner: *"now no gate is drawing at all"*.
        //
        // `CalcAbsolutePosition` (`c_baseentity.cpp:4350`) composes a child onto its parent's
        // transform without ever asking whether the parent renders. So the parent must stay in the
        // list, and only its DRAWING is refused.
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props =
        [
            Prop("models/props/door.mdl", x: 100f, entity: 5) with { Pose = Hidden(100f) },
            Prop("models/props/grate.mdl", entity: 6, attachedTo: 5),
        ];

        models.Add(props, OneTriangle);
        models.Instances(props, instances);

        instances.ShouldHaveSingleItem("the door is refused and the grate is not");
    }

    /// <summary>A pose at <c>kRenderNone</c> — the mode eighteen of cp_fulgur's doors declare.</summary>
    private static ScenePose Hidden(float x = 0f) =>
        new() { X = x, Scale = 1f, RenderMode = 10 };

    private static SceneProp Prop(
        string model,
        float x = 0f,
        float y = 0f,
        float z = 0f,
        float yaw = 0f,
        int entity = 1,
        int? attachedTo = null) =>
        new(
            entity,
            model,
            ScenePropTrack.Classify(model),
            new ScenePose { X = x, Y = y, Z = z, Yaw = yaw },
            attachedTo);
}
