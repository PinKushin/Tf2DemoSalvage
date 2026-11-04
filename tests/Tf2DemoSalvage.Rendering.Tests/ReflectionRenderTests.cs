using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The baked reflection, drawn on a real device and measured in pixels.
/// </summary>
/// <remarks>
/// **Everything else about this feature is falsifiable against map data; the shader is not.** The
/// lump reader, the name derivation, the face decode and the material parameters were each checked
/// against 43 real cubemaps. What none of that can say is whether the HLSL samples the cube, or
/// whether the constants reach it — and the failure there is a picture, which no assertion about a
/// byte can see.
///
/// This is the closest a test gets: render offscreen through the real pipeline and read the pixels
/// back. It cannot say the reflection looks RIGHT — that is a question for someone looking at the
/// screen, and this file does not pretend otherwise — but it can say the cube is being sampled at
/// all, which is the difference between a feature and a no-op.
///
/// **The discriminator is the surface normal.** A reflection vector is the view direction mirrored
/// about the normal, so two otherwise identical surfaces facing different ways sample different
/// texels of the cube and come out different colours. If the envmap branch never runs, the two are
/// identical — nothing else in this shader's world path varies with the normal.
/// </remarks>
public sealed class ReflectionRenderTests
{
    /// <summary>A perspective camera, because an identity matrix has no eye position.</summary>
    /// <remarks>
    /// **The existing offscreen tests use the identity matrix and therefore never reach this
    /// code.** Inverting the identity and taking its third row gives a w of zero — parallel rays,
    /// converging nowhere — so <c>EyePosition</c> correctly reports no camera and the shader
    /// correctly skips the reflection. A test written on that matrix would have measured nothing
    /// and passed.
    /// </remarks>
    private static float[] Camera =>
        new FreeCamera
        {
            Origin = (0f, -600f, 64f),
            Angles = (0f, 90f, 0f),
            Aspect = 1f,
        }.ToMatrix();

    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void ReflectionRender_AReflectiveSurface_ChangesColourWithItsNormal()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        if (Reflective(assets) is not { } material)
        {
            Assert.Ignore("no material on this map carries a baked cubemap");
            return;
        }

        (int R, int G, int B) facingUp = Draw(target, assets, material, (0f, 0f, 1f));
        (int R, int G, int B) facingSide = Draw(target, assets, material, (0f, -1f, 0f));

        TestContext.Out.WriteLine(
            $"material {material}: normal up {facingUp}, normal side {facingSide}");

        // Drawn at all, or the comparison below is between two blacks.
        (facingUp.R + facingUp.G + facingUp.B).ShouldBeGreaterThan(
            0, "the surface must be drawn before its reflection can be measured");

        (facingUp.R + facingUp.G + facingUp.B).ShouldNotBe(
            facingSide.R + facingSide.G + facingSide.B,
            "a reflection follows the normal, so two normals must sample different texels");
    }

    /// <summary>
    /// A bumped brush face reflects about its bump map: `worldSpaceNormal = mul( vNormal, i.tangentSpaceTranspose )`,
    /// `lightmappedgeneric_ps2_3_x.h:479`, so the tangent frame its vertex carries (B669) must change the picture.
    /// </summary>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void ReflectionRender_ABumpedBrushFaceWithAFrame_ReflectsAboutItsBumpMap()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        int? found = Enumerable.Range(0, assets.Cubemaps.Count).Cast<int?>().FirstOrDefault(index =>
            assets.Cubemaps[index!.Value] is not null && assets.Bumps[index.Value] is not null && assets.Textures[index.Value] is not null);

        if (found is not { } material)
        {
            Assert.Ignore("no material on this map is bumped and carries a baked cubemap");
            return;
        }

        // **One known normal, every texel the same**, so no filter or mip of a real map's can stand between the texel and the prediction: x 128, y 242, z 182, so w sends the mirror ray about 50 degrees up or down the cube.
        byte[] leaning = [.. Enumerable.Range(0, 16).SelectMany(static _ => new byte[] { 128, 242, 182, 255 })];
        MapTexture flatBump = new(4, 4, 4, 4, TextureImage.Rgba(leaning), IsTransparent: false);
        (float A, float B, float C) n = ((128 / 255f * 2f) - 1f, (242 / 255f * 2f) - 1f, (182 / 255f * 2f) - 1f);
        MapAssets known = MapAssets.ForMaterials(
            [assets.Textures[material]],
            ["LightmappedGeneric"],
            bumps: [new MapBump(flatBump, false)],
            cubemaps: [assets.Cubemaps[material]]);

        (float U, float V) uv = (0.5f, 0.5f);

        // The frame this quad carries: N (0 -1 0), S (1 0 0), and T = cross( N, S ) * w = (0 0 1) * w.
        (int R, int G, int B) plus = DrawBumped(target, known, 0, tangentW: 1f, uv);
        (int R, int G, int B) minus = DrawBumped(target, known, 0, tangentW: -1f, uv);

        // **The prediction: the same normals handed to the flat path.** `mul( vNormal, tangentSpaceTranspose )` for w = +1 and w = -1 is these two vectors, so each picture
        // is the other's to the count: resolution 1 of 255, the target's rounding. Sensitivity: the two pictures lie much further apart than that.
        (int R, int G, int B) flatPlus = DrawFlatAt(target, known, 0, Unit((n.A, -n.C, n.B)), uv);
        (int R, int G, int B) flatMinus = DrawFlatAt(target, known, 0, Unit((n.A, -n.C, -n.B)), uv);

        TestContext.Out.WriteLine(
            $"material {material}, texel normal {n}: frame +1 {plus}, -1 {minus}; flat at those normals {flatPlus}, {flatMinus}");

        Math.Max(Math.Abs(plus.R - minus.R), Math.Max(Math.Abs(plus.G - minus.G), Math.Abs(plus.B - minus.B))).ShouldBeGreaterThan(8, "the sign must turn the picture by far more than the resolution");

        Math.Abs(plus.R - flatPlus.R).ShouldBeLessThanOrEqualTo(1, "red, w +1");
        Math.Abs(plus.G - flatPlus.G).ShouldBeLessThanOrEqualTo(1, "green, w +1");
        Math.Abs(plus.B - flatPlus.B).ShouldBeLessThanOrEqualTo(1, "blue, w +1");
        Math.Abs(minus.R - flatMinus.R).ShouldBeLessThanOrEqualTo(1, "red, w -1");
        Math.Abs(minus.G - flatMinus.G).ShouldBeLessThanOrEqualTo(1, "green, w -1");
        Math.Abs(minus.B - flatMinus.B).ShouldBeLessThanOrEqualTo(1, "blue, w -1");
    }

    private static (float X, float Y, float Z) Unit((float X, float Y, float Z) v)
    {
        float length = MathF.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));

        return (v.X / length, v.Y / length, v.Z / length);
    }

    /// <summary>A quad with no frame at one texel, reflecting about the given normal.</summary>
    private static (int R, int G, int B) DrawFlatAt(
        OffscreenTarget target, MapAssets assets, int material, (float X, float Y, float Z) normal, (float U, float V) uv)
    {
        List<WorldVertex> vertices =
        [
            Vertex(-256f, 0f, -256f, uv.U, uv.V, normal),
            Vertex(256f, 0f, -256f, uv.U, uv.V, normal),
            Vertex(256f, 0f, 256f, uv.U, uv.V, normal),
            Vertex(-256f, 0f, -256f, uv.U, uv.V, normal),
            Vertex(256f, 0f, 256f, uv.U, uv.V, normal),
            Vertex(-256f, 0f, 256f, uv.U, uv.V, normal),
        ];

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(vertices, [new WorldBatch(material, 0, vertices.Count)], Camera, assets);

        return target.PixelAt(32, 32);
    }

    /// <summary>The mean colour across a baked cube's faces, to prove two cubes are not one picture.</summary>
    private static double CubeBrightness(MapCubemap cube) => cube.Faces.Average(face => Brightness(face));

    /// <summary>The reflective, opaque material whose cube looks least like a given one, and how far apart they are.</summary>
    private static (int Material, double Apart)? LeastLike(MapAssets assets, int material)
    {
        double own = CubeBrightness(assets.Cubemaps[material]!.Value);

        return Enumerable.Range(0, assets.Cubemaps.Count)
            .Where(index => assets.Cubemaps[index] is not null && assets.Textures[index] is { IsTranslucent: false, IsAdditive: false })
            .Select(index => (Material: index, Apart: Math.Abs(CubeBrightness(assets.Cubemaps[index]!.Value) - own)))
            .OrderByDescending(pair => pair.Apart)
            .Cast<(int Material, double Apart)?>()
            .FirstOrDefault();
    }

    /// <summary>A decal reflects its own cube too: `DrawDecalBatch` binds it, for the same reason as a blended run (B669).</summary>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void ReflectionRender_ADecalAfterAnotherReflectiveRun_ReflectsItsOwnCube()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        // A translucent material, because the decal pass blends by alpha and an opaque material's alpha is its mask, 0 at this pixel on this map.
        int? glass = Enumerable.Range(0, assets.Cubemaps.Count).Cast<int?>().FirstOrDefault(index =>
            assets.Cubemaps[index!.Value] is not null && assets.Textures[index.Value] is { IsTranslucent: true });

        if (glass is not { } decal || LeastLike(assets, decal) is not { } least)
        {
            Assert.Ignore("this map has no translucent and opaque pair that both carry a baked cubemap");
            return;
        }

        least.Apart.ShouldBeGreaterThan(4d, "the two runs' cubes must differ, or this measures nothing");

        // Wound the other way: the decal rasteriser culls, as the world pass's does not.
        List<WorldVertex> pane = [.. Enumerable.Reverse(FacingQuad(0f))];
        List<WorldVertex> behindTheCamera = FacingQuad(-2000f);
        List<WorldVertex> both = [.. behindTheCamera, .. pane];

        // One EMPTY world run: the world pass returns before its decals when it has no runs at all (`WorldRenderer.Draw`).
        target.Clear(0f, 0f, 0f);
        target.DrawWorld(pane, [new WorldBatch(decal, 0, 0)], Camera, assets, decals: [new WorldBatch(decal, 0, pane.Count, Category: SurfaceCategory.Overlay)]);
        (int R, int G, int B) alone = target.PixelAt(32, 32);

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(
            both,
            [new WorldBatch(least.Material, 0, behindTheCamera.Count)],
            Camera,
            assets,
            decals: [new WorldBatch(decal, behindTheCamera.Count, pane.Count, Category: SurfaceCategory.Overlay)]);
        (int R, int G, int B) after = target.PixelAt(32, 32);

        TestContext.Out.WriteLine($"decal {decal}, run before it {least.Material}, cubes apart {least.Apart:0.0}: alone {alone}, after {after}");

        (alone.R + alone.G + alone.B).ShouldBeGreaterThan(0, "the decal must be drawn before its reflection can be measured");
        after.ShouldBe(alone, "a run drawn out of sight cannot change what a decal reflects");
    }

    /// <summary>
    /// An additive brush run binds its own bump map: a brush vertex's `tangent.w` is not 0 now (B669), so the shader reads slot 4, and an additive
    /// run that bound nothing read the bump of whichever run drew last.
    /// </summary>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void ReflectionRender_AnAdditiveRunAfterABumpedRun_IsUnchangedByThatBump()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        // No shipped additive material on this map has a bump and a cube, so the map's real pair is made additive here.
        // One reflective bumped material, and the other bumped material whose texel is furthest from it at one uv.
        const float At = 0.5f;

        int[] bumped = [.. Enumerable.Range(0, assets.Bumps.Count).Where(index =>
            assets.Bumps[index] is not null && assets.Textures[index] is { IsTranslucent: false, IsAdditive: false })];
        int[] reflective = [.. bumped.Where(index => assets.Cubemaps[index] is not null)];

        if (reflective.Length == 0 || bumped.Length < 2)
        {
            Assert.Ignore("this map has no two bumped materials, one reflective");
            return;
        }

        int own = reflective[0];
        (int R, int G) Texel(int material)
        {
            MapTexture bump = assets.Bumps[material]!.Value.Texture;
            ReadOnlySpan<byte> rgba = bump.Image.ToRgba(bump.Width, bump.Height);
            int at = (((int)(At * bump.Height) * bump.Width) + (int)(At * bump.Width)) * 4;

            return (rgba[at], rgba[at + 1]);
        }

        (int R, int G) mine = Texel(own);
        int other = bumped.Where(index => index != own).MaxBy(index => Math.Abs(Texel(index).R - mine.R) + Math.Abs(Texel(index).G - mine.G));

        MapTexture additive = assets.Textures[own]!.Value with { IsAdditive = true, IsTranslucent = false };
        MapTexture opaque = assets.Textures[other]!.Value;

        MapAssets Pair(int bumpOfAdditive) => MapAssets.ForMaterials(
            [additive, opaque],
            ["LightmappedGeneric", "LightmappedGeneric"],
            bumps: [assets.Bumps[bumpOfAdditive], assets.Bumps[other]],
            cubemaps: [assets.Cubemaps[own], null]);

        static WorldVertex Framed(WorldVertex vertex) =>
            vertex with { U = At, V = At, TangentX = 1f, TangentY = 0f, TangentZ = 0f, TangentW = 1f };

        List<WorldVertex> pane = [.. FacingQuad(0f).Select(Framed)];
        List<WorldVertex> behindTheCamera = [.. FacingQuad(-2000f).Select(Framed)];
        List<WorldVertex> both = [.. behindTheCamera, .. pane];
        WorldBatch[] after = [new WorldBatch(1, 0, behindTheCamera.Count), new WorldBatch(0, behindTheCamera.Count, pane.Count)];

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(pane, [new WorldBatch(0, 0, pane.Count)], Camera, Pair(own));
        (int R, int G, int B) alone = target.PixelAt(32, 32);

        // The control: what a stale slot would draw, its bump the other material's.
        target.Clear(0f, 0f, 0f);
        target.DrawWorld(pane, [new WorldBatch(0, 0, pane.Count)], Camera, Pair(other));
        (int R, int G, int B) stale = target.PixelAt(32, 32);

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(both, after, Camera, Pair(own));
        (int R, int G, int B) drawn = target.PixelAt(32, 32);

        TestContext.Out.WriteLine($"additive {own}, bumped {other}: alone {alone}, with the other's bump {stale}, after the other's run {drawn}");

        stale.ShouldNotBe(alone, "the two bumps must look different, or this measures nothing");
        drawn.ShouldBe(alone, "a run drawn out of sight cannot change an additive run's bump");
    }

    /// <summary>
    /// A blended brush run reflects its OWN cube, whatever the last draw left in the slot (B669): `DynamicCmdsOut.BindTexture(
    /// pShader, SHADER_SAMPLER2, info.m_nEnvmap, ...)` runs on every draw of the shader (`lightmappedgeneric_dx9_helper.cpp:876`).
    /// </summary>
    /// <remarks>
    /// The translucent pass bound no cube at all, so the far pane read the cube of whichever opaque run drew last. The
    /// manipulation is that opaque run, out of sight behind the camera: it changes nothing a correct renderer shows.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void ReflectionRender_ABlendedRunAfterAnotherReflectiveRun_ReflectsItsOwnCube()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        int? glass = Enumerable.Range(0, assets.Cubemaps.Count).Cast<int?>().FirstOrDefault(index =>
            assets.Cubemaps[index!.Value] is not null && assets.Textures[index.Value] is { IsTranslucent: true });

        if (glass is not { } blended || LeastLike(assets, blended) is not { } least)
        {
            Assert.Ignore("this map has no translucent and opaque pair that both carry a baked cubemap");
            return;
        }

        // **The condition must be able to fail**: with two runs on one cube, a stale slot and a correct bind predict the same picture.
        least.Apart.ShouldBeGreaterThan(4d, "the two runs' cubes must differ, or this measures nothing");

        int other = least.Material;

        List<WorldVertex> pane = FacingQuad(0f);
        List<WorldVertex> behindTheCamera = FacingQuad(-2000f);

        List<WorldVertex> both = [.. behindTheCamera, .. pane];
        WorldBatch[] paneAlone = [new WorldBatch(blended, 0, pane.Count)];
        WorldBatch[] afterOpaque = [new WorldBatch(other, 0, behindTheCamera.Count), new WorldBatch(blended, behindTheCamera.Count, pane.Count)];

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(pane, paneAlone, Camera, assets);
        (int R, int G, int B) alone = target.PixelAt(32, 32);

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(both, afterOpaque, Camera, assets);
        (int R, int G, int B) after = target.PixelAt(32, 32);

        TestContext.Out.WriteLine($"translucent {blended}, opaque {other}: alone {alone}, after the opaque run {after}");

        (alone.R + alone.G + alone.B).ShouldBeGreaterThan(0, "the pane must be drawn before its reflection can be measured");
        after.ShouldBe(alone, "a run drawn out of sight cannot change what another run reflects");
    }

    /// <summary>A quad across the view at one depth along the camera's axis.</summary>
    private static List<WorldVertex> FacingQuad(float y)
    {
        (float X, float Y, float Z) facing = (0f, -1f, 0f);

        return
        [
            Vertex(-256f, y, -256f, 0f, 0f, facing),
            Vertex(256f, y, -256f, 1f, 0f, facing),
            Vertex(256f, y, 256f, 1f, 1f, facing),
            Vertex(-256f, y, -256f, 0f, 0f, facing),
            Vertex(256f, y, 256f, 1f, 1f, facing),
            Vertex(-256f, y, 256f, 0f, 1f, facing),
        ];
    }

    /// <summary>The quad of <see cref="Draw"/> facing the camera, at one texel of a normal map, with a frame.</summary>
    private static (int R, int G, int B) DrawBumped(OffscreenTarget target, MapAssets assets, int material, float tangentW, (float U, float V) uv)
    {
        (float X, float Y, float Z) facing = (0f, -1f, 0f);

        WorldVertex Corner(float x, float z) =>
            Vertex(x, 0f, z, uv.U, uv.V, facing) with { TangentX = 1f, TangentY = 0f, TangentZ = 0f, TangentW = tangentW };

        List<WorldVertex> vertices =
        [
            Corner(-256f, -256f),
            Corner(256f, -256f),
            Corner(256f, 256f),
            Corner(-256f, -256f),
            Corner(256f, 256f),
            Corner(-256f, 256f),
        ];

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(vertices, [new WorldBatch(material, 0, vertices.Count)], Camera, assets);

        return target.PixelAt(32, 32);
    }

    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void ReflectionRender_ASurfaceWithNoCubemap_DoesNotChangeWithItsNormal()
    {
        // **The control, and it is the half that makes the test above mean anything.** If some
        // other part of the shader varied with the normal, the difference measured there would not
        // be the reflection. A material with no cubemap must come out identical.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        if (Matte(assets) is not { } material)
        {
            Assert.Ignore("every material on this map carries a cubemap, so there is no control");
            return;
        }

        (int R, int G, int B) facingUp = Draw(target, assets, material, (0f, 0f, 1f));
        (int R, int G, int B) facingSide = Draw(target, assets, material, (0f, -1f, 0f));

        TestContext.Out.WriteLine(
            $"matte material {material}: normal up {facingUp}, normal side {facingSide}");

        facingUp.ShouldBe(
            facingSide, "nothing but the reflection varies with the normal on a world surface");
    }

    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void ReflectionRender_AModelsReflection_FollowsItsNormal()
    {
        // **The model path's own version of the first test in this file, and it could not pass
        // before.** `DrawModel` bound four texture slots of five and left the cube slot to whatever
        // the previous draw had put there — so a model material with a reflection sampled either
        // nothing or somebody else's cube. That was invisible for as long as no model material ever
        // resolved a cubemap, which was until `env_cubemap` started resolving.
        //
        // Same discriminator as the world test: nothing else in this shader varies with the normal.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        if (LocallyReflective(assets) is not { } material)
        {
            Assert.Ignore("no material on this map asks for the map's own cubemap");
            return;
        }

        // **The placement is chosen for the cube's INTERNAL contrast, not taken.** A cube baked in a
        // uniform dark corner reads the same in every direction, so a model standing by it draws
        // the same colour whatever its normal — correct code and a renderer that ignored the normal
        // entirely predict the same observation there. Measured on this map: the flattest cube
        // varies by under a level between faces and the most varied by over 180.
        //
        // This does still lean on the placement search putting the model at that cube: with the
        // search sabotaged to ignore position, a flat cube gets bound and this test goes red as
        // well as the placement one. That coupling is real rather than a flaw in the condition —
        // on the model path there is no way to observe the normal except through some bound cube,
        // and which one is bound is chosen by position.
        BspCubemap at = MostVaried(assets).Placement;

        (int R, int G, int B) facingCamera = DrawModelAt(target, assets, material, at, (0f, -1f, 0f));
        (int R, int G, int B) facingUp = DrawModelAt(target, assets, material, at, (0f, 0f, 1f));

        TestContext.Out.WriteLine(
            $"model material {material}: facing camera {facingCamera}, facing up {facingUp}");

        (facingCamera.R + facingCamera.G + facingCamera.B)
            .ShouldBeGreaterThan(0, "the model must be drawn before its reflection can be measured");

        facingCamera.ShouldNotBe(
            facingUp, "a reflection follows the normal, so two normals sample different texels");
    }

    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void ReflectionRender_AModelAtTwoPlacements_TakesTheNearerCubemap()
    {
        // **The assertion that the position actually chooses the cube**, which nothing else can
        // make. A model's `$envmap "env_cubemap"` is resolved per draw from where the model stands
        // (BspCubemaps.Closest, Valve's Cubemap_FindClosestCubemap reduced to its second pass), and
        // every part of that is unit-tested EXCEPT whether the renderer calls it with the model's
        // own position and binds the answer.
        //
        // **The confound this is built to avoid.** Simply moving the model changes the eye-to-
        // surface direction as well, so the reflection vector moves with it and the colour would
        // differ even against a single global cube. So the CAMERA moves with the model: same
        // offset, same normal, same reflection vector. The only thing left that can differ is which
        // cube is bound.
        //
        // **And the condition is chosen rather than taken.** Two placements whose cubes happen to
        // look alike would give equal pixels from correct code, so the pair is picked by measuring
        // the decoded faces and taking the two that differ most — CLAUDE.md's "enlarge the
        // condition" rather than weaken the assertion.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        if (LocallyReflective(assets) is not { } material)
        {
            Assert.Ignore("no material on this map asks for the map's own cubemap");
            return;
        }

        (MapPlacedCubemap First, MapPlacedCubemap Second) pair = MostUnalike(assets);

        // **The normal is part of the condition, and the first choice made this test blind.** With
        // the quad facing the camera the reflection vector points straight back along it, sampling
        // one texel of the cube's darkest face — where both cubes read (13, 3, 1) and correct code
        // and broken code predict the same observation. Facing up samples the lit part, where the
        // measured spread between two cubes is over 150 levels.
        (float X, float Y, float Z) up = (0f, 0f, 1f);

        (int R, int G, int B) atFirst =
            DrawModelAt(target, assets, material, pair.First.Placement, up);

        (int R, int G, int B) atSecond =
            DrawModelAt(target, assets, material, pair.Second.Placement, up);

        TestContext.Out.WriteLine(
            $"material {material}: at {pair.First.Placement} {atFirst}, " +
            $"at {pair.Second.Placement} {atSecond}");

        // Drawn at all, or the comparison below is between two blacks — which is exactly what this
        // path produced before, because DrawModel never bound the cube slot.
        (atFirst.R + atFirst.G + atFirst.B)
            .ShouldBeGreaterThan(0, "the model must be drawn before its reflection can be measured");

        atFirst.ShouldNotBe(
            atSecond,
            "the same model at two placements reflects two different cubes");
    }

    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void ReflectionRender_AModelWithNoCubemap_DoesNotChangeWithItsPlacement()
    {
        // **The control for the test above, and it is what separates "the cube is chosen by
        // position" from "anything at all varies with position".** A material that reflects nothing
        // must come out identical at both placements, with the camera moved the same way.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        if (Matte(assets) is not { } material)
        {
            Assert.Ignore("every material on this map reflects, so there is no control");
            return;
        }

        (MapPlacedCubemap First, MapPlacedCubemap Second) pair = MostUnalike(assets);

        (float X, float Y, float Z) up = (0f, 0f, 1f);

        DrawModelAt(target, assets, material, pair.First.Placement, up)
            .ShouldBe(
                DrawModelAt(target, assets, material, pair.Second.Placement, up),
                "a model reflecting nothing draws the same wherever it stands");
    }

    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void ReflectionRender_ANormalMapAlphaMask_ShinesWhereItsAlphaIsHigh()
    {
        // **The branch that a whole suite could not see.** Inverting this mask — writing
        // `1 - alpha` where Valve writes `alpha` — left all 522 viewer tests green, which is the
        // exact defect its own conformance test warns about: "puts the shine exactly where the
        // artist masked it out". Nothing else here varies with the bump map's ALPHA, so nothing
        // else could tell the two apart.
        //
        // **The condition is two texels of one real bump map**, chosen by reading its alpha
        // channel: the brightest and the dimmest. A correct mask makes the first reflect more; an
        // inverted one makes the second. The quad is drawn at a SINGLE uv per pass, all four
        // corners the same, so no interpolation blurs the two together.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        if (Masked(assets) is not { } material)
        {
            Assert.Ignore("no material on this map masks its reflection by its normal map's alpha");
            return;
        }

        if (MaskPair(assets.Bumps[material]!.Value.Texture, assets.Textures[material]!.Value)
            is not { } alpha)
        {
            Assert.Ignore("this material's bump alpha is flat, so the mask cannot be measured");
            return;
        }

        BspCubemap at = MostVaried(assets).Placement;

        (int R, int G, int B) shiny = DrawModelAt(target, assets, material, at, (0f, 0f, 1f), alpha.Opaque);
        (int R, int G, int B) dull = DrawModelAt(target, assets, material, at, (0f, 0f, 1f), alpha.Clear);

        TestContext.Out.WriteLine(
            $"masked material {material}: alpha high at {alpha.Opaque} {shiny}, " +
            $"alpha low at {alpha.Clear} {dull}");

        (shiny.R + shiny.G + shiny.B).ShouldBeGreaterThan(
            dull.R + dull.G + dull.B,
            "the normal map's alpha is the specular factor as-is: 1 reflects MOST, unlike " +
            "$basealphaenvmapmask, which is inverted");
    }

    /// <summary>
    /// Two texture coordinates whose bump ALPHA differs sharply and whose base COLOUR does not.
    /// </summary>
    /// <remarks>
    /// **The first version of this took the alpha extremes and nothing else, and it passed against
    /// an inverted mask.** Moving the texture coordinate moves the albedo as well, and the albedo
    /// is the larger term — so the two draws differed for a reason that had nothing to do with the
    /// mask, and the assertion measured the base texture. Correct and broken predicted the same
    /// observation, which is the classic wrong CONDITION rather than a weak assertion.
    ///
    /// Holding the colour still is what isolates the mask. Both coordinates land on texels the base
    /// texture paints the same, so the only surviving difference between the two draws is how much
    /// of the reflection the mask lets through.
    ///
    /// Null when the map has no such pair — a bump map with a flat alpha channel cannot measure
    /// this, and saying so is better than comparing two identical draws.
    /// </remarks>
    private static ((float U, float V) Opaque, (float U, float V) Clear)? MaskPair(
        MapTexture bump, MapTexture albedo)
    {
        ReadOnlySpan<byte> normals = bump.Image.ToRgba(bump.Width, bump.Height);
        int texels = normals.Length / 4;

        int most = -1;
        int least = -1;
        int widest = 0;

        for (int first = 0; first < texels; first++)
        {
            for (int second = 0; second < texels; second++)
            {
                int apart = normals[(first * 4) + 3] - normals[(second * 4) + 3];

                if (apart <= widest || !SameColour(first, second))
                {
                    continue;
                }

                widest = apart;
                most = first;
                least = second;
            }
        }

        // A hundred levels of alpha is enough to move the reflection well clear of the noise the
        // colour tolerance below admits.
        return widest >= 100 && most >= 0 ? (Coordinate(most), Coordinate(least)) : null;

        bool SameColour(int first, int second)
        {
            (int R, int G, int B) one = Albedo(first);
            (int R, int G, int B) two = Albedo(second);

            return Math.Abs(one.R - two.R) <= 2 &&
                Math.Abs(one.G - two.G) <= 2 &&
                Math.Abs(one.B - two.B) <= 2;
        }

        // The base texture sampled at the bump texel's own coordinate. The two need not be the same
        // size — they share the surface's uv, not a grid.
        (int R, int G, int B) Albedo(int texel)
        {
            (float U, float V) at = Coordinate(texel);

            int x = Math.Clamp((int)(at.U * albedo.Width), 0, albedo.Width - 1);
            int y = Math.Clamp((int)(at.V * albedo.Height), 0, albedo.Height - 1);
            int start = ((y * albedo.Width) + x) * 4;

            ReadOnlySpan<byte> pixels = albedo.Image.ToRgba(albedo.Width, albedo.Height);

            return start + 2 < pixels.Length
                ? (pixels[start], pixels[start + 1], pixels[start + 2])
                : (0, 0, 0);
        }

        (float U, float V) Coordinate(int texel) =>
            (((texel % bump.Width) + 0.5f) / bump.Width, ((texel / bump.Width) + 0.5f) / bump.Height);
    }

    /// <summary>The first material masking its reflection by its normal map's alpha, or null.</summary>
    private static int? Masked(MapAssets assets) =>
        Enumerable.Range(0, assets.LocalReflections.Count)
            .Cast<int?>()
            .FirstOrDefault(index =>
                assets.LocalReflections[index!.Value] is { MaskedByNormalMapAlpha: true } &&
                assets.Bumps[index.Value] is not null &&
                assets.Textures[index.Value] is not null);

    /// <summary>Draws a quad as a MODEL at one placement, with the camera fixed relative to it.</summary>
    /// <remarks>
    /// The camera offset is constant, so the view direction, the normal and therefore the whole
    /// reflection vector are identical between calls. Only the chosen cubemap can differ.
    /// </remarks>
    private static (int R, int G, int B) DrawModelAt(
        OffscreenTarget target,
        MapAssets assets,
        int material,
        BspCubemap at,
        (float X, float Y, float Z)? facing = null,
        (float U, float V)? texel = null)
    {
        (float X, float Y, float Z) normal = facing ?? (0f, -1f, 0f);

        // **One texel across the whole quad when a coordinate is given**, so a mask test reads the
        // alpha it chose rather than an interpolated average of the map. Otherwise the quad spans
        // the texture as usual.
        (float U, float V) a = texel ?? (0f, 0f);
        (float U, float V) b = texel ?? (1f, 0f);
        (float U, float V) c = texel ?? (1f, 1f);
        (float U, float V) d = texel ?? (0f, 1f);

        List<WorldVertex> vertices =
        [
            Vertex(-64f, 0f, -64f, a.U, a.V, normal),
            Vertex(64f, 0f, -64f, b.U, b.V, normal),
            Vertex(64f, 0f, 64f, c.U, c.V, normal),
            Vertex(-64f, 0f, -64f, a.U, a.V, normal),
            Vertex(64f, 0f, 64f, c.U, c.V, normal),
            Vertex(-64f, 0f, 64f, d.U, d.V, normal),
        ];

        // The model matrix: identity rotation, translation in row three. Standing exactly at the
        // placement, so `Closest` cannot answer anything but this one.
        float[] model =
        [
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            at.X, at.Y, at.Z, 1f,
        ];

        float[] camera = new FreeCamera
        {
            Origin = (at.X, at.Y - 300f, at.Z),
            Angles = (0f, 90f, 0f),
            Aspect = 1f,
        }.ToMatrix();

        target.Clear(0f, 0f, 0f);

        // **Both sides, because winding is not what this measures.** The model path culls back
        // faces where the world path does not, so a quad wound for the world test draws nothing
        // here — which reads identically to "the reflection is missing". Drawing both sides removes
        // the confound rather than hiding a defect: what is under test is which cubemap is bound,
        // and that is unaffected by which faces are kept.
        target.DrawModelPose(
            vertices,
            [new WorldBatch(material, 0, vertices.Count)],
            camera,
            model,
            assets,
            light: null,
            bothSides: true);

        return target.PixelAt(32, 32);
    }

    /// <summary>The two placements whose cubes differ most, by mean face colour.</summary>
    /// <remarks>
    /// **Chosen by measurement rather than by index**, because the test above needs a pair that a
    /// correct renderer will draw differently. Taking the first two would sometimes pick two cubes
    /// baked in the same grey corridor, and the resulting equal pixels would read as a defect.
    /// </remarks>
    private static (MapPlacedCubemap First, MapPlacedCubemap Second) MostUnalike(MapAssets assets)
    {
        IReadOnlyList<MapPlacedCubemap> placed = assets.PlacedCubemaps;

        (MapPlacedCubemap First, MapPlacedCubemap Second) best = (placed[0], placed[^1]);
        double furthest = -1;

        for (int first = 0; first < placed.Count; first++)
        {
            for (int second = first + 1; second < placed.Count; second++)
            {
                (double R, double G, double B) one = Mean(placed[first]);
                (double R, double G, double B) two = Mean(placed[second]);

                double apart =
                    Math.Abs(one.R - two.R) + Math.Abs(one.G - two.G) + Math.Abs(one.B - two.B);

                if (apart > furthest)
                {
                    furthest = apart;
                    best = (placed[first], placed[second]);
                }
            }
        }

        return best;
    }

    /// <summary>The placement whose own six faces differ most from each other.</summary>
    /// <remarks>
    /// The cube a normal-dependence test needs: one where the six directions are not all the same
    /// colour. Measured rather than assumed, so the test's sensitivity does not depend on which
    /// corner of which map happens to come first in the lump.
    /// </remarks>
    private static MapPlacedCubemap MostVaried(MapAssets assets)
    {
        MapPlacedCubemap best = assets.PlacedCubemaps[0];
        double widest = -1;

        foreach (MapPlacedCubemap placed in assets.PlacedCubemaps)
        {
            double[] faces = [.. placed.Faces.Select(face => Brightness(face))];
            double spread = faces.Max() - faces.Min();

            if (spread > widest)
            {
                widest = spread;
                best = placed;
            }
        }

        return best;
    }

    /// <summary>The mean colour of a cubemap's six faces.</summary>
    private static (double R, double G, double B) Mean(MapPlacedCubemap cubemap)
    {
        double red = 0;
        double green = 0;
        double blue = 0;
        long texels = 0;

        foreach (MapTexture face in cubemap.Faces)
        {
            ReadOnlySpan<byte> pixels = face.Image.ToRgba(face.Width, face.Height);

            for (int at = 0; at + 3 < pixels.Length; at += 4)
            {
                red += pixels[at];
                green += pixels[at + 1];
                blue += pixels[at + 2];
                texels++;
            }
        }

        return texels == 0 ? (0, 0, 0) : (red / texels, green / texels, blue / texels);
    }

    /// <summary>The DARKEST material asking for the map's own cubemap, or null.</summary>
    /// <remarks>
    /// **Darkest rather than first, because the reflection is ADDED to the diffuse and the channel
    /// saturates.** Taking the first such material gave one whose base texture is already near
    /// white: both placements rendered (255, 255, 255) and the difference the test exists to
    /// measure was clipped away — a condition where correct and broken predict the same
    /// observation. Choosing a dark base leaves headroom for the reflection to show in.
    /// </remarks>
    private static int? LocallyReflective(MapAssets assets)
    {
        int? darkest = null;
        double dimmest = double.MaxValue;

        for (int index = 0; index < assets.LocalReflections.Count; index++)
        {
            if (assets.LocalReflections[index] is null || assets.Textures[index] is not { } texture)
            {
                continue;
            }

            double brightness = Brightness(texture);

            if (brightness < dimmest)
            {
                dimmest = brightness;
                darkest = index;
            }
        }

        return darkest;
    }

    /// <summary>A texture's mean brightness over its opaque texels.</summary>
    private static double Brightness(MapTexture texture)
    {
        ReadOnlySpan<byte> pixels = texture.Image.ToRgba(texture.Width, texture.Height);
        double total = 0;
        long texels = 0;

        for (int at = 0; at + 3 < pixels.Length; at += 4)
        {
            total += pixels[at] + pixels[at + 1] + pixels[at + 2];
            texels++;
        }

        return texels == 0 ? double.MaxValue : total / (texels * 3);
    }

    /// <summary>Draws a full-view quad of one material with one normal, and reads the centre.</summary>
    private static (int R, int G, int B) Draw(
        OffscreenTarget target,
        MapAssets assets,
        int material,
        (float X, float Y, float Z) normal)
    {
        // A quad standing in front of the camera, which looks along +Y from y = -600.
        const float Y = 0f;

        List<WorldVertex> vertices =
        [
            Vertex(-256f, Y, -256f, 0f, 0f, normal),
            Vertex(256f, Y, -256f, 1f, 0f, normal),
            Vertex(256f, Y, 256f, 1f, 1f, normal),
            Vertex(-256f, Y, -256f, 0f, 0f, normal),
            Vertex(256f, Y, 256f, 1f, 1f, normal),
            Vertex(-256f, Y, 256f, 0f, 1f, normal),
        ];

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(vertices, [new WorldBatch(material, 0, vertices.Count)], Camera, assets);

        return target.PixelAt(32, 32);
    }

    private static WorldVertex Vertex(
        float x, float y, float z, float u, float v, (float X, float Y, float Z) normal) =>
        new(x, y, z, u, v, 0f, 0f, 0f)
        {
            NormalX = normal.X,
            NormalY = normal.Y,
            NormalZ = normal.Z,
        };

    /// <summary>The first material index carrying a baked cubemap, or null.</summary>
    private static int? Reflective(MapAssets assets) =>
        Enumerable.Range(0, assets.Cubemaps.Count)
            .Cast<int?>()
            .FirstOrDefault(index =>
                assets.Cubemaps[index!.Value] is not null &&
                assets.Textures[index.Value] is not null);

    /// <summary>The first material index with a texture and no reflection of any kind, or null.</summary>
    /// <remarks>
    /// **Both lists, not just <c>Cubemaps</c>.** A material asking for the literal
    /// <c>env_cubemap</c> also has a null entry there — it reflects the map's own cube, chosen per
    /// draw — so checking one list alone would hand the control tests a reflective material and
    /// they would fail for the right behaviour.
    /// </remarks>
    private static int? Matte(MapAssets assets) =>
        Enumerable.Range(0, assets.Cubemaps.Count)
            .Cast<int?>()
            .FirstOrDefault(index =>
                assets.Cubemaps[index!.Value] is null &&
                assets.LocalReflections[index.Value] is null &&
                assets.Textures[index.Value] is not null);

    /// <summary>Real map assets, because the shader clips on texture alpha.</summary>
    private static MapAssets? Assets
    {
        get
        {
            if (GameInstall.Root is not { } tf)
            {
                return null;
            }

            string map = Path.Combine(tf, "maps", "cp_process_final.bsp");

            if (!File.Exists(map))
            {
                return null;
            }

            // Shared: this draws pixels but does not assert on texture DETAIL, so the default size
            // serves and the load is one the rest of the suite has already paid for.
            return MapCache.Load();
        }
    }
}
