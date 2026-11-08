using System;
using System.Linq;

using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.ProjectedShadows;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The frame's shadows reach the device every frame, and none at all when nothing can cast them (B702).</summary>
public sealed class ShadowEffectsTests
{
    private static readonly FreeCamera Camera = new() { Origin = (0f, 0f, 0f), Aspect = 1f };

    /// <remarks>
    /// No map to land on, with render-to-texture on or off: the device is still told, with no shadows, so the last frame's
    /// are not drawn again.
    /// </remarks>
    [TestCase(false)]
    [TestCase(true)]
    public void Step_DisabledOrWithNoMap_HandsTheDeviceNoShadows(bool enabled)
    {
        FakeFrameDevice device = new();

        new ShadowEffects(new EntityModelSet()).Step(new ShadowStep(enabled, null, null, null, 0, [], null, Camera, 720), device);

        device.Shadows.ShouldBe([ShadowFrame.None]);
    }

    /// <remarks>
    /// **`r_shadowrendertotexture 0` draws blobs, not nothing** (`ShutdownRenderToTextureShadows`, `GetActualShadowCastType`,
    /// `clientshadowmgr.cpp:1449-1476`, `:2217-2236`): over a map, a studio prop's shadow is handed over as the blob, cast
    /// straight down whatever the controller says (`GetShadowDirection`, `:1666-1674`). With render-to-texture on, the
    /// control, the same prop's shadow is not a blob.
    /// </remarks>
    [TestCase(false, true)]
    [TestCase(true, false)]
    public void Step_OverAMapWithRenderToTextureOnOrOff_HandsOverABlobCastStraightDownOnlyWhenOff(bool enabled, bool blob)
    {
        FakeFrameDevice device = new();
        Content.Bsp.BspLeafTree.FromLumps(Node(), Plane(), new byte[64]).ShouldNotBeNull();
        LoadedMap map = SyntheticMap.Read(lumps: new System.Collections.Generic.Dictionary<int, byte[]>
        {
            [1] = Plane(), [5] = Node(), [10] = new byte[64],
        });
        Core.Scene.SceneProp prop = new(
            7, "models/props/crate.mdl", Core.Scene.SceneModelKind.Studio, new Core.Scene.ScenePose { X = 100f }, null);

        map.Level.Leaves.ShouldNotBeNull("the control: the map has a tree, so the step does not return early");

        new ShadowEffects(new EntityModelSet()).Step(
            new ShadowStep(enabled, null, map, new DecalWorld([], [[], []], []), 0, [prop], null, Camera, 720), device);

        CastShadow shadow = device.Shadows.Single().Casters.Single();

        shadow.Blob.ShouldBe(blob);

        if (blob)
        {
            shadow.Projection.Direction.ShouldBe(-System.Numerics.Vector3.UnitZ);
        }
    }

    private static byte[] Plane()
    {
        byte[] plane = new byte[20];
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(0), 1f);
        return plane;
    }

    private static byte[] Node()
    {
        byte[] node = new byte[32];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -2);
        return node;
    }

    /// <remarks>
    /// **The step asks the posed model set**: a prop drawn at a blend of 159 (a fade band, `EntityFadeWiringTests`) leaves its
    /// shadow a falloff bias of 96 (`SetFalloffBias( 255 - m_nRenderFXBlend )`, `c_baseentity.cpp:3548-3552`), and the
    /// gather the step runs carries it; the opaque control carries 0.
    /// </remarks>
    [Test]
    public void Gather_OverAPosedModelSet_CarriesEachPropsFalloffBias()
    {
        EntityModelSet models = new() { ViewOrigin = (0f, 0f, 0f) };
        Core.Scene.SceneProp faded = new(
            1, "models/props/crate.mdl", Core.Scene.SceneModelKind.Studio,
            new Core.Scene.ScenePose { X = 200f, FadeMinimumDistance = 100f, FadeMaximumDistance = 300f }, null);
        Core.Scene.SceneProp opaque = faded with { EntityIndex = 2, Pose = new Core.Scene.ScenePose { X = 4000f } };
        Core.Scene.SceneProp[] props = [faded, opaque];

        models.Add(props, Tf2DemoSalvage.Scene.Tests.ModelFramesFixture.OneTriangle);
        models.Instances(props, []);

        new ShadowEffects(models).Gather(props, null).Select(entity => entity.FalloffBias).ShouldBe([(byte)96, (byte)0]);
    }

    /// <remarks>
    /// **The step asks the corpse physics where a corpse is** (B702, finding 107): a corpse stepped a second in an empty world
    /// casts from its simulated root, well below its death spot at the origin, as a ragdoll with its 66 physics updates.
    /// </remarks>
    [Test]
    public void Gather_OverASteppedCorpse_CastsFromItsSimulatedRoot()
    {
        Core.Scene.SceneProp corpse = Tf2DemoSalvage.Scene.Tests.CorpseFixture.Corpse() with { FirstTick = 66 };
        PropModels.ModelFrames frames = Tf2DemoSalvage.Scene.Tests.CorpseFixture.Frames();
        EntityModelSet models = new() { Geometry = _ => frames };
        Core.Scene.SceneProp[] drawn = [corpse];
        models.Add(drawn, _ => frames);
        models.CurrentTick = 132d;
        models.Instances(drawn, [], seconds: 2d);

        System.Numerics.Vector3 root = models.Corpses.Roots[corpse.EntityIndex];
        root.Z.ShouldBeLessThan(-100f, "the control: the root fell from the death spot");

        ShadowEntity entity = new ShadowEffects(models).Gather(drawn, null).Single();

        (entity.RenderOrigin, entity.Ragdoll, entity.PhysicsUpdates).ShouldBe((root, true, 66));
    }

    /// <remarks>
    /// `LevelInitPreEntity`: the first sky-ambient light, × 3 + 0.3 — 0.1 becomes 0.6, 153; 0.3 becomes 1.2, clamped to
    /// 255; a later sky-ambient light is not read. With none the ambient is zero: 0.3, 76.
    /// </remarks>
    [Test]
    public void LevelColour_TheFirstSkyAmbientLight_IsTimesThreePlusPointThree()
    {
        Content.Bsp.BspWorldLight Light(Content.Bsp.WorldLightKind kind, float red, float green) =>
            new((0f, 0f, 0f), (red, green, 0f), (0f, 0f, 0f), kind);

        ShadowEffects.LevelColour(
            [Light(Content.Bsp.WorldLightKind.Point, 9f, 9f), Light(Content.Bsp.WorldLightKind.SkyAmbient, 0.1f, 0.3f),
                Light(Content.Bsp.WorldLightKind.SkyAmbient, 0f, 0f)])
            .ShouldBe(((byte)153, (byte)255, (byte)76));

        ShadowEffects.LevelColour([]).ShouldBe(((byte)76, (byte)76, (byte)76));
    }
}
