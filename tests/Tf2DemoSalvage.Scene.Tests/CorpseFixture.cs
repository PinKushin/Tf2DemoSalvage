using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A corpse and its one-body model, shared by the Scene and Presentation suites (linked into the latter).</summary>
public static class CorpseFixture
{
    /// <summary>A corpse at the origin under entity 9.</summary>
    public static SceneProp Corpse() =>
        new(
            9,
            "models/player/soldier.mdl",
            SceneModelKind.Studio,
            new ScenePose { X = 0f, Y = 0f, Z = 0f },
            ClassName: RagdollProps.RagdollClassName);

    /// <summary>A one-bone model whose <c>.phy</c> declares a single body.</summary>
    /// <remarks>
    /// **One element is enough and two would test something else.** What is being asked here is
    /// whether production reaches the simulation at all, not whether a joint solves — that is
    /// covered where the joint lives.
    /// </remarks>
    public static PropModels.ModelFrames Frames()
    {
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithBones("bip_pelvis");

        PhysicsModel physics = PhysicsModel.From(
            [new PhysicsSolid(0, "bip_pelvis", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f)],
            [],
            1,
            checksum: 0,
            collisionRules: null,
            hulls: null,

            // Every solid the engine builds carries a mass center and hull inertia, and a ragdoll refuses one
            // that does not (B403): the bone, and a square inch per kilogram, in IVP metres.
            massProperties: [new PhysicsMassProperties(Vector3.Zero, Vector3.One / (IvpTransform.InchesPerMetre * IvpTransform.InchesPerMetre))]);

        RagdollBody ragdoll = RagdollBody.Build(physics, model.Bones)!;

        return new PropModels.ModelFrames(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)>
            {
                [0] = (0, 1, 0f),
            },
            [0],
            [true],
            Skinned: model,

            // **A real box, so the frustum can judge it.** An empty one is never culled
            // (`WorldSpaceBounds.IsPlaced`), which would make the out-of-view test pass for the
            // wrong reason. Thirty units about the origin, clear of a camera 200 units away.
            HeaderBounds: new StudioBox(-30f, -30f, -30f, 30f, 30f, 30f),
            Ragdoll: ragdoll);
    }
}
