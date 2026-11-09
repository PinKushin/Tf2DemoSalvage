using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A brush entity's Water face drawn inside a water view samples a snapshot of the target that view is drawing into
/// (B663). **NOT PARITY: this pins an interpolation** (D244, RISKS B663).
/// </summary>
/// <remarks>
/// The engine draws the face inside the refraction view (DF_DRAW_ENTITITES, <c>viewrender.cpp:6033</c>), and inside the
/// reflection view with <c>$reflectentities</c>. It binds the live target it is drawing into (shaderapidx9
/// <c>0x1800149b0</c>, no guard), which D3D9 leaves undefined. The lead's pick: the face samples a copy of the target as
/// it stands at the view's first water bind, taken once per view and only when a face reads a target. Every other target
/// is sampled live. Hand-built subjects (D38): a red floor under the world water, and small faces inside the views.
/// </remarks>
public sealed class WaterViewSnapshotConformanceTests
{
    private const int Floor = 0;
    private const int Water = 1;
    private const int ReflectingWater = 2;
    private const int Unlit = 3;

    private static readonly MapAssets Assets = MapAssets.ForMaterials(
        [ModulateToneMapConformanceTests.Grey(200), ModulateToneMapConformanceTests.Grey(40), ModulateToneMapConformanceTests.Grey(40),
            ModulateToneMapConformanceTests.Grey(255)],
        ["LightmappedGeneric", "Water", "Water", "UnlitGeneric"],
        [null, WaterMaterial(reflect: false), WaterMaterial(reflect: true), null]);

    private static readonly FreeCamera Above = new() { Origin = (0f, 0f, 200f), Angles = (89f, 0f, 0f), Aspect = 1f };

    private static readonly float[] Identity = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f];

    /// <summary>A small face 2 units under the world water, inside the refraction view's clip.</summary>
    private static readonly List<WorldVertex> FaceBelow = Square(-2f, 60f, (1f, 1f, 1f), facingUp: true);

    [Test]
    public void DrawWaterWorld_BrushWaterInsideTheRefractionView_SamplesTheViewsSnapshot()
    {
        Run? withFace = Draw(Refraction(), view => view.Kind == WaterViewKind.Refraction, (target, _) =>
            target.DrawModelInCurrentView(FaceBelow, [new(Water, 0, 6)], Identity));
        Run? without = Draw(Refraction(), _ => false, (_, _) => { });

        if (withFace is not { } face || without is not { } control)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        TestContext.Out.WriteLine($"REFRACTION: FACE {face}; CONTROL {control}");

        face.Inside.ShouldNotBe(control.Inside, "the brush water face did not draw inside the refraction view");
        (face.Inside.R - face.Inside.G).ShouldBeGreaterThan(20, "the face did not sample the view's red content");
        face.Edge.ShouldBe(control.Edge, "the view's image changed away from the face");
        face.Expensive.ShouldBe(control.Expensive + 1, "the face did not take the Water shader's expensive pass");
        face.Snapshots.ShouldBe(1, "one water face in one view is one snapshot");
        control.Snapshots.ShouldBe(0, "a view with no water face took a snapshot");
    }

    /// <summary>Two faces in one view share the view's one snapshot.</summary>
    [Test]
    public void DrawWaterWorld_TwoBrushWaterFacesInOneView_TakeOneSnapshot()
    {
        Run? run = Draw(Refraction(), view => view.Kind == WaterViewKind.Refraction, (target, _) =>
        {
            target.DrawModelInCurrentView(FaceBelow, [new(Water, 0, 6)], Identity);
            target.DrawModelInCurrentView(Square(-1f, 30f, (1f, 1f, 1f), facingUp: true), [new(Water, 0, 6)], Identity);
        });

        if (run is not { } two)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        TestContext.Out.WriteLine($"TWO FACES: {two}");

        two.Snapshots.ShouldBe(1, "two water faces in one view copied the target twice");
    }

    /// <summary>
    /// The copy is taken at the view's first water bind, so an entity drawn before the face in the same view is in it:
    /// a green square under the face turns the face green-dominant over the red floor.
    /// </summary>
    [Test]
    public void DrawWaterWorld_EntityDrawnBeforeTheFace_IsInTheSnapshot()
    {
        Run? run = Draw(Refraction(), view => view.Kind == WaterViewKind.Refraction, (target, _) =>
        {
            target.DrawModelInCurrentView(Square(-3f, 100f, (0f, 1f, 0f), facingUp: true), [new(Unlit, 0, 6)], Identity);
            target.DrawModelInCurrentView(FaceBelow, [new(Water, 0, 6)], Identity);
        });

        if (run is not { } green)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        TestContext.Out.WriteLine($"GREEN ENTITY THEN FACE: {green}");

        green.Inside.G.ShouldBeGreaterThan(green.Inside.R + 20, "the snapshot was taken before the entity drawn ahead of the face");
    }

    /// <summary>The reflection arm: a reflecting face inside the reflection view snapshots the reflection, once.</summary>
    [Test]
    public void DrawWaterWorld_BrushWaterInsideTheReflectionView_SamplesTheViewsSnapshot()
    {
        // Above the water, facing down at the mirrored camera, so the reflection view keeps it and sees its front.
        List<WorldVertex> faceAbove = Square(2f, 60f, (1f, 1f, 1f), facingUp: false);

        Run? withFace = Draw(Reflection(), view => view.Kind == WaterViewKind.Reflection, (target, _) =>
            target.DrawModelInCurrentView(faceAbove, [new(ReflectingWater, 0, 6)], Identity), reflectionTarget: true);
        Run? without = Draw(Reflection(), _ => false, (_, _) => { }, reflectionTarget: true);

        if (withFace is not { } face || without is not { } control)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        TestContext.Out.WriteLine($"REFLECTION: FACE {face}; CONTROL {control}");

        face.Inside.ShouldNotBe(control.Inside, "the reflecting face did not draw inside the reflection view");
        face.Edge.ShouldBe(control.Edge, "the reflection view's image changed away from the face");
        face.Snapshots.ShouldBe(1, "one reflecting face in the reflection view is one snapshot");
        control.Snapshots.ShouldBe(0, "a reflection view with no water face took a snapshot");
    }

    private readonly record struct Run(
        (int R, int G, int B) Inside, (int R, int G, int B) Edge, int Snapshots, int Expensive);

    /// <summary>The world (red floor, world water) through its views, with <paramref name="entities"/> in the chosen view.</summary>
    private static Run? Draw(
        WaterDraw frame, Func<WaterView, bool> inView, Action<OffscreenTarget, WaterView> entities, bool reflectionTarget = false)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            return null;
        }

        List<WorldVertex> world = [.. Square(-5f, 400f, (1f, 0f, 0f), facingUp: true), .. Square(0f, 400f, (1f, 1f, 1f), facingUp: true)];
        WorldBatch[] batches = [new(Floor, 0, 6), new(frame.Material, 6, 6)];

        target.Clear(0f, 0f, 0f);
        (int expensive, _, _) = target.DrawWaterWorld(world, batches, Above, Assets, frame with
        {
            DrawEntities = view =>
            {
                if (inView(view))
                {
                    entities(target, view);
                }

                return false;
            },
        });

        return new Run(
            target.WaterTargetPixel(!reflectionTarget, 512, 512),
            target.WaterTargetPixel(!reflectionTarget, 40, 40),
            target.WorldViewSnapshots,
            expensive);
    }

    private static WaterDraw Refraction() => Plan(Water, new(CheapWater: false, Refract: true, Reflect: false,
        ReflectEntities: false, DrawWaterSurface: true, OpaqueWater: false));

    private static WaterDraw Reflection() => Plan(ReflectingWater, new(CheapWater: false, Refract: true, Reflect: true,
        ReflectEntities: true, DrawWaterSurface: true, OpaqueWater: false));

    private static WaterDraw Plan(int material, WaterRenderInfo info) =>
        new(WaterViews.Plan(info, new WaterFrame(false, false, 0f, false), ViewClears.Depth), material, 0f, [], [], null, null);

    private static MapWater WaterMaterial(bool reflect) =>
        MapWater.From(VmtMaterial.Parse(Encoding.UTF8.GetBytes(
            "\"Water\"\n{\n\"$refracttexture\" \"_rt_WaterRefraction\"\n\"$refractamount\" \"0\"\n\"$abovewater\" \"1\"\n" +
            (reflect ? "\"$reflecttexture\" \"_rt_WaterReflection\"\n\"$reflectamount\" \"0\"\n\"$forceexpensive\" \"1\"\n" : "") +
            "\"$fogcolor\" \"{51 43 13}\"\n\"$fogstart\" \"-100\"\n\"$fogend\" \"400\"\n}")), null);

    /// <summary>A square at height <paramref name="z"/>, wound to face up or down (the water cull keeps only its front).</summary>
    private static List<WorldVertex> Square(float z, float half, (float Red, float Green, float Blue) colour, bool facingUp)
    {
        (float r, float g, float b) = colour;
        float normal = facingUp ? 1f : -1f;
        List<WorldVertex> square =
        [
            new(-half, -half, z, 0f, 0f, 0f, 0f, 0f, r, g, b, NormalZ: normal),
            new(half, half, z, 4f, 4f, 0f, 0f, 0f, r, g, b, NormalZ: normal),
            new(half, -half, z, 4f, 0f, 0f, 0f, 0f, r, g, b, NormalZ: normal),
            new(-half, -half, z, 0f, 0f, 0f, 0f, 0f, r, g, b, NormalZ: normal),
            new(-half, half, z, 0f, 4f, 0f, 0f, 0f, r, g, b, NormalZ: normal),
            new(half, half, z, 4f, 4f, 0f, 0f, 0f, r, g, b, NormalZ: normal),
        ];

        if (!facingUp)
        {
            (square[1], square[2]) = (square[2], square[1]);
            (square[4], square[5]) = (square[5], square[4]);
        }

        return square;
    }
}
