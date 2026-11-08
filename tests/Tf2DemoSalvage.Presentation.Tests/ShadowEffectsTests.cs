using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

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
    /// **Each view's decals come from the surfaces THAT view draws** (`R_DrawWorldLists` hands `RenderShadows` its own
    /// surfaces, `engine.dll` `FUN_1800e7010`, finding 107; every world view runs it, `viewrender.cpp:5487-5573`, `:4926`).
    /// One box's shadow is linked to the floor, face 0. A monitor that draws the floor gets the floor's decal, exactly the
    /// corners a build over face 0 cuts; a main view and a 3D sky that do not draw it get none, and a water view's face 0
    /// adds nothing, since it draws the main view's runs. Then the other way round: the main view and the sky draw the floor
    /// and the monitor does not.
    /// </remarks>
    [Test]
    public void BuildViews_EachViewsOwnSurfaces_CutIntoThatViewsList()
    {
        ShadowReceivers receivers = FloorUnderABox();
        List<ShadowVertex> floor = [];

        receivers.Build([0], [], floor);
        floor.ShouldNotBeEmpty("the control: the box's shadow is cut on the floor");

        List<ShadowVertex> world = [], monitor = [], sky = [];

        ShadowEffects.BuildViews(
            receivers, [View(ShadowViewKind.Monitor, [0]), View(ShadowViewKind.Sky, []), View(ShadowViewKind.Water, [0]), View(ShadowViewKind.Main, [])],
            world, monitor, sky);

        monitor.ShouldBe(floor);
        (world.Count, sky.Count).ShouldBe((0, 0));

        ShadowEffects.BuildViews(
            receivers, [View(ShadowViewKind.Monitor, []), View(ShadowViewKind.Sky, [0]), View(ShadowViewKind.Main, [0])], world, monitor, sky);

        world.ShouldBe(floor);
        sky.ShouldBe(floor);
        monitor.ShouldBeEmpty();
    }

    private static ShadowWorldView View(ShadowViewKind kind, int[] surfaces) => new(kind, default, default, null, surfaces, []);

    /// <summary>A 32-unit box on an 800-unit floor, face 0, its shadow linked there by the production casters and receivers.</summary>
    private static ShadowReceivers FloorUnderABox()
    {
        ShadowEntity box = new(1, null, ShadowCast.RenderToTexture, Vector3.Zero, (0f, 0f, 0f), new Vector3(-16f, -16f, 0f), new Vector3(16f, 16f, 32f));
        Core.Scene.ScreenFadeView screen = new((0f, 0f, 300f), (0f, 0f, -1f), (1f, 0f, 0f), 1f, 64f);
        CastShadow shadow = new ShadowCasters().Frame([box], new Vector3(0f, 0f, -1f), 50f, default, screen).Single();

        // Clockwise seen from above, as a BSP face is wound.
        Vector3[] corners = [new(-400f, -400f, 0f), new(-400f, 400f, 0f), new(400f, 400f, 0f), new(400f, -400f, 0f)];
        DecalFace face = new(0, corners, Vector3.UnitZ, 0f, default, default, (0, 0), (0, 0), false, false, false, default);
        ShadowReceivers receivers = new(OneLeaf(), new DecalWorld([], [[0], [0]], [face]), [SurfaceShadowBits.DynamicShadows]);

        receivers.Begin();
        receivers.Project(shadow);
        return receivers;
    }

    /// <summary>One node on x = 0, front leaf 0 and back leaf 1, both empty.</summary>
    private static Content.Bsp.BspLeafTree OneLeaf()
    {
        byte[] plane = new byte[20];
        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(0), 1f);

        byte[] node = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -0 - 1);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -1 - 1);

        return Content.Bsp.BspLeafTree.FromLumps(node, plane, new byte[64]);
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
