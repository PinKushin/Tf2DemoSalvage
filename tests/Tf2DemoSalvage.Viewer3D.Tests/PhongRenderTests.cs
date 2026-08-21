using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// <c>$phong</c> drawn on a real device and measured in pixels.
/// </summary>
/// <remarks>
/// **The discriminator is the LIGHT's direction, and it has to be, because everything else about a
/// highlight is shared with terms this renderer already draws.** A brighter surface could be the
/// ambient cube, the sun's diffuse, or a reflection. What only a specular term does is move: turn
/// the light and the highlight goes somewhere else, on geometry that has not moved and with an eye
/// that has not moved.
///
/// So each test here holds the model, the camera and the normal still and changes exactly one
/// thing.
///
/// **What this cannot say is whether it looks right.** It says the term is computed, responds to the
/// light, and is masked and shaped the way Valve's is. Whether a soldier reads like TF2 is a
/// question for someone looking at the screen, and `PhongConformanceTests` is what pins the
/// arithmetic to the SDK.
/// </remarks>
public sealed class PhongRenderTests
{
    [Test]
    public void PhongRender_TurningTheLight_MovesTheHighlight()
    {
        // **The whole claim in one row.** Same model, same camera, same normal, same everything
        // except which way the sun comes from. A diffuse term would change too — so the control
        // below is what makes this mean "specular" rather than "lit".
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets || Phonged(assets) is not { } material)
        {
            Assert.Ignore("the map or the game is not installed, or no material asks for $phong");
            return;
        }

        // Straight down the eye's own axis, so the mirrored view vector points back at the light and
        // the highlight is at its strongest; then ninety degrees away, where it cannot be.
        (int R, int G, int B) facing = Draw(target, assets, material, (0f, 1f, 0f));
        (int R, int G, int B) across = Draw(target, assets, material, (0f, 0f, -1f));

        TestContext.Out.WriteLine(
            $"phong material {material}: light along the view {facing}, light across it {across}");

        (facing.R + facing.G + facing.B).ShouldBeGreaterThan(
            across.R + across.G + across.B,
            "a specular highlight is strongest when the light lies along the reflected view");
    }

    [Test]
    public void PhongRender_AMaterialWithoutPhong_DoesNotMoveWithTheLight()
    {
        // **The control, and it is what separates the highlight from the diffuse.** The sun's
        // diffuse term also varies with its direction, so the test above on its own would pass
        // against a renderer with no specular at all. This one holds a material that asks for no
        // phong under exactly the same two lights.
        //
        // It is NOT asserted equal: N.L still changes, so the diffuse legitimately moves. What is
        // asserted is that it moves LESS — the specular is the larger swing, and by a margin no
        // rounding can supply.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets ||
            Phonged(assets) is not { } lit ||
            Matte(assets) is not { } dull)
        {
            Assert.Ignore("the map or the game is not installed, or the pair does not exist");
            return;
        }

        int phonged = Swing(target, assets, lit);
        int plain = Swing(target, assets, dull);

        TestContext.Out.WriteLine($"swing with phong {phonged}, without {plain}");

        phonged.ShouldBeGreaterThan(
            plain, "the highlight moves with the light more than the diffuse alone does");
    }

    [Test]
    public void PhongRender_ALightBehindTheSurface_LeavesNoHighlight()
    {
        // **The N·L mask, and reaching it takes a condition the other tests cannot supply.**
        // `SpecularAndRimTerms` masks the highlight with `saturate(dot(vWorldNormal, vLightDir))`,
        // and dropping that line changed nothing in either test above — measured, not assumed.
        //
        // The arithmetic says why, and it is worth writing down because it looks like a gap in the
        // tests and is really a property of the geometry. On a quad facing the camera the mirrored
        // view vector R equals the normal N, so `dot(R, L) > 0` implies `dot(N, L) > 0`: the two
        // agree everywhere and the mask is provably inert. More generally R is the mirror of the eye
        // about N, so the angle between N and R equals the angle between N and E — for any
        // front-facing surface, a light straight along R still has N·L ≥ 0.
        //
        // **The mask only bites at a grazing normal**, where R has swung far from N and a light can
        // sit near R and still be behind the surface. So: tilt the normal 80° off the view axis, and
        // put the light where R·L ≈ 0.97 and N·L ≈ −0.08.
        //
        // Correct code returns exactly zero there. Code without the mask returns 0.97^5 ≈ 0.85 of
        // the light — a bright highlight on the unlit side of a model, which reads as a material
        // property rather than as a defect.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets || Phonged(assets) is not { } material)
        {
            Assert.Ignore("the map or the game is not installed, or no material asks for $phong");
            return;
        }

        // N = (sin 80°, −cos 80°, 0): steeply tilted, still facing the eye at (0, −1, 0).
        (float X, float Y, float Z) grazing = (0.985f, -0.174f, 0f);

        // The light TRAVELS this way, so it arrives from −(this) ≈ (0.1, 0.995, 0) — behind the
        // surface by a whisker and almost exactly along the mirrored view.
        (int R, int G, int B) behind = Draw(target, assets, material, (-0.1f, -0.995f, 0f), grazing);

        // **The positive control, and the first attempt at it was not one.** A light straight ahead
        // — travelling (0, 1, 0) — gives no highlight at this normal either, because R has swung to
        // (0.343, 0.939, 0) and a light from (0, −1, 0) is on the wrong side of it. Both draws came
        // out identical and the test would have "passed" while measuring nothing.
        //
        // The control has to put the light ALONG R: travelling −R, so it arrives exactly down the
        // mirrored view with N·L = 0.175, comfortably in front.
        (int R, int G, int B) front = Draw(target, assets, material, (-0.343f, -0.939f, 0f), grazing);

        TestContext.Out.WriteLine($"grazing normal: light behind {behind}, light in front {front}");

        (front.R + front.G + front.B).ShouldBeGreaterThan(
            behind.R + behind.G + behind.B,
            "a light behind the surface contributes no highlight, however well it lines up with " +
            "the reflected view");
    }

    [Test]
    public void RimRender_AGrazingSurface_IsBrighterThanOneFacingTheEye()
    {
        // **The rim's discriminator is the ANGLE TO THE EYE, not the light**, which is what makes it
        // a different term from the highlight beside it. `Fresnel4` is `(1 - N·V)²²`, so a surface
        // facing the camera contributes nothing and one seen edge-on contributes most.
        //
        // Both draws use the same light, in the same place, so the specular is held as still as it
        // can be — and what is left moving is the rim.
        //
        // The light is aimed along the mirrored view of the GRAZING normal, so the grazing case has
        // a highlight too. That is deliberate: it means the test cannot pass merely because a
        // grazing surface happens to catch more of something. Both cases get their specular; only
        // the rim differs.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets || Rimmed(assets) is not { } material)
        {
            Assert.Ignore("the map or the game is not installed, or no material asks for $rimlight");
            return;
        }

        (float X, float Y, float Z) travelling = (-0.343f, -0.939f, 0f);

        (int R, int G, int B) grazing =
            Draw(target, assets, material, travelling, (0.985f, -0.174f, 0f));

        (int R, int G, int B) headOn = Draw(target, assets, material, travelling, (0f, -1f, 0f));

        TestContext.Out.WriteLine($"rim material {material}: grazing {grazing}, head-on {headOn}");

        (grazing.R + grazing.G + grazing.B).ShouldBeGreaterThan(
            headOn.R + headOn.G + headOn.B,
            "Fresnel4 is (1 - N.V) to the fourth, so an edge-on surface takes the rim and a " +
            "surface facing the camera takes none of it");
    }

    /// <summary>The first material asking for a rim light, with a texture to draw it on.</summary>
    private static int? Rimmed(MapAssets assets) =>
        Enumerable.Range(0, assets.Phong.Count)
            .Cast<int?>()
            .FirstOrDefault(index =>
                assets.Phong[index!.Value] is { Rim: not null } &&
                assets.Textures[index.Value] is not null);

    /// <summary>How much a material's centre pixel changes between the two light directions.</summary>
    private static int Swing(OffscreenTarget target, MapAssets assets, int material)
    {
        (int R, int G, int B) facing = Draw(target, assets, material, (0f, 1f, 0f));
        (int R, int G, int B) across = Draw(target, assets, material, (0f, 0f, -1f));

        return Math.Abs(
            (facing.R + facing.G + facing.B) - (across.R + across.G + across.B));
    }

    /// <summary>Draws a quad as a model under one sun direction, and reads the centre.</summary>
    /// <remarks>
    /// The quad faces the camera and the camera is fixed, so the only variable is the light. The sun
    /// is given as the direction it TRAVELS, which is what the renderer's constant means and the
    /// negation of the direction toward it.
    /// </remarks>
    private static (int R, int G, int B) Draw(
        OffscreenTarget target,
        MapAssets assets,
        int material,
        (float X, float Y, float Z) travelling,
        (float X, float Y, float Z)? facing = null)
    {
        (float X, float Y, float Z) normal = facing ?? (0f, -1f, 0f);

        List<WorldVertex> vertices =
        [
            Vertex(-64f, 0f, -64f, 0f, 0f, normal),
            Vertex(64f, 0f, -64f, 1f, 0f, normal),
            Vertex(64f, 0f, 64f, 1f, 1f, normal),
            Vertex(-64f, 0f, -64f, 0f, 0f, normal),
            Vertex(64f, 0f, 64f, 1f, 1f, normal),
            Vertex(-64f, 0f, 64f, 0f, 1f, normal),
        ];

        float[] model =
        [
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, 1f,
        ];

        float[] camera = new FreeCamera
        {
            Origin = (0f, -300f, 0f),
            Angles = (0f, 90f, 0f),
            Aspect = 1f,
        }.ToMatrix();

        target.Clear(0f, 0f, 0f);
        target.DrawModelPose(
            vertices,
            [new WorldBatch(material, 0, vertices.Count)],
            camera,
            model,
            assets,
            light: null,
            bothSides: true,
            sun: new SunLight(1f, 1f, 1f, travelling.X, travelling.Y, travelling.Z));

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

    /// <summary>The first material asking for a highlight, with a texture to draw it on.</summary>
    private static int? Phonged(MapAssets assets) =>
        Enumerable.Range(0, assets.Phong.Count)
            .Cast<int?>()
            .FirstOrDefault(index =>
                assets.Phong[index!.Value] is not null &&
                assets.Textures[index.Value] is not null);

    /// <summary>The first material with a texture and no highlight of any kind.</summary>
    private static int? Matte(MapAssets assets) =>
        Enumerable.Range(0, assets.Phong.Count)
            .Cast<int?>()
            .FirstOrDefault(index =>
                assets.Phong[index!.Value] is null &&
                assets.Cubemaps[index.Value] is null &&
                assets.LocalReflections[index.Value] is null &&
                assets.Textures[index.Value] is not null);

    /// <summary>Real map assets, loaded with a model so prop materials are in the table.</summary>
    private static MapAssets? Assets
    {
        get
        {
            if (Tf2Install.Folder is not { } tf ||
                !File.Exists(Path.Combine(tf, "maps", "cp_process_final.bsp")))
            {
                return null;
            }

            return MapCache.Load(entityModels: ["models/props_gameplay/cap_point_base.mdl"]);
        }
    }
}
