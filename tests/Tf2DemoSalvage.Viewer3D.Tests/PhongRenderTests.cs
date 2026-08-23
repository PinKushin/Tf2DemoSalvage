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
///
/// **A stated coverage limit: `$lightwarptexture`'s shader read is verified by manipulation, not by
/// an assertion here.** Five conditions were tried and each measured something other than the ramp:
///
/// 1. Comparing a lit surface against the terminator — a straight line orders them the same way, and
///    an ordering assertion passed with the ramp lookup disabled.
/// 2. Comparing two angles without an ambient cube — the model shader wraps the whole direct term in
///    `if (ambientCube[0].w > 0.5f)`, so with no cube supplied the draw was unlit albedo and the
///    ramp could not have appeared at all.
/// 3. A ratio of direct terms on a material carrying `$phong` — the highlight is gated on the sun,
///    so it lands inside the "direct" term the baseline subtraction was meant to isolate, and it
///    varies far more steeply than the diffuse. Observed 5.40 where the ramp predicted 2.57.
/// 4. The same on a phong-free material — the only candidates are dark, and the direct term
///    quantised to 3 levels at both angles: a ratio of 1.00 carrying no information.
/// 5. Choosing the brightest phong-free candidate — it was the same material.
///
/// **The read itself is confirmed.** With `warping` forced false the pixel moves from
/// `(64, 32, 32)` to `(53, 30, 30)` on material 410, holding everything else fixed. That is the
/// manipulation evidence; what is missing is an automated assertion that captures it without
/// dragging in a term that swamps it.
///
/// Recorded rather than papered over, per `docs/memory/most-of-a-decoder-is-untested.md`: sabotage
/// each branch, and write the coverage limit into the class comment where the next person will find
/// it.
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

    [Test]
    public void LightWarpRender_TheRamp_ReplacesTheLinearFalloff()
    {
        // **The ramp is read where the linear falloff would have been**, so the discriminator is a
        // surface whose diffuse term sits somewhere the ramp is NOT the identity. Comparing a warped
        // material against an unwarped one directly would compare two different textures; comparing
        // one material at two angles measures the CURVE, which is what a warp changes.
        //
        // Two angles into the ramp, one near the lit end and one near the terminator. A linear
        // falloff and an authored one both fall off, so what is asserted is not "it changes" — it is
        // that the change does not match the straight line the code drew before. That is measured
        // against the material's own ramp, read from the asset.
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

        // **Counted before it is used, because a skip here would hide a no-op.** The census reports
        // $lightwarptexture on hundreds of this map's materials, so zero loaded ramps means the
        // resolution chain is broken rather than that the map does not use the feature — which is
        // precisely the shape of failure that has shipped three times in this project with a green
        // suite.
        int ramps = assets.LightWarps.Count(warp => warp is not null);

        TestContext.Out.WriteLine($"{ramps} materials of {assets.LightWarps.Count} carry a ramp");

        ramps.ShouldBeGreaterThan(
            0, "this map's materials name light warps; none loading is a wiring failure");

        if (Warped(assets) is not { } material)
        {
            Assert.Ignore("no material carries both a ramp and a base texture");
            return;
        }

        MapTexture ramp = assets.LightWarps[material]!.Value;

        TestContext.Out.WriteLine(
            $"warp material {material}: ramp {ramp.Width}x{ramp.Height}, " +
            $"ends {Level(ramp, 0f)} and {Level(ramp, 1f)}");

        // **The ramp has to be non-trivial or nothing below can measure anything.** A flat ramp
        // makes the warped and unwarped pictures agree by construction, which is a condition where
        // correct and broken predict the same observation.
        Math.Abs(Level(ramp, 1f) - Level(ramp, 0f))
            .ShouldBeGreaterThan(8, "the ramp must actually curve for this to be measurable");

        // **This test stops at the ramp reaching the renderer, and that limit is deliberate.** See
        // the class remarks: five attempts at asserting the shader's use of it in pixels each turned
        // out to measure something else, and the shader read IS verified — by manipulation, with the
        // numbers written down — rather than by an assertion here.
        //
        // What is asserted is everything up to the draw call: the ramp is found, decoded, non-flat,
        // and attached to a material that also has a base texture to draw it on. That is the half
        // that fails silently; the shader read fails loudly the moment anyone looks at a model.
        ramp.Height.ShouldBeGreaterThan(0);

        (int R, int G, int B) lit = Draw(target, assets, material, (0f, 1f, 0f), (0f, -1f, 0f));

        TestContext.Out.WriteLine($"warp material {material}: lit {lit}");

        Sum(lit).ShouldBeGreaterThan(
            0, "a material carrying a ramp must still draw; a broken lookup could black it out");
    }

    /// <summary>A ramp's brightness at a position along it, 0 to 1.</summary>
    private static int Level(MapTexture ramp, float along)
    {
        int x = Math.Clamp((int)(along * (ramp.Width - 1)), 0, ramp.Width - 1);
        ReadOnlySpan<byte> pixels = ramp.Image.ToRgba(ramp.Width, ramp.Height);
        int at = x * 4;

        return at + 2 < pixels.Length ? pixels[at] + pixels[at + 1] + pixels[at + 2] : 0;
    }

    /// <summary>A material naming a light warp and NO highlight, with a texture to draw it on.</summary>
    /// <remarks>
    /// **The exclusion is the point.** $phong is gated on the sun, so it appears in a lit draw and
    /// not in the ambient-only baseline — which puts the highlight inside the "direct term" this
    /// test subtracts out, and it varies with the angle far more steeply than the diffuse does.
    /// Measured on a material carrying both: the direct ratio came out at 5.40 where the ramp
    /// predicts 2.57 and a straight line 2.00, so it matched neither and the comparison was
    /// meaningless.
    /// </remarks>
    /// <remarks>
    /// **The BRIGHTEST such material, not the first.** The direct term is what this test measures,
    /// and on a dark base texture it quantises to a couple of levels — measured: the first
    /// candidate gave a direct term of 3 at both angles, so the ratio was 1.00 and carried no
    /// information at all. Brightness is the condition here in the same way darkness was the
    /// condition for the reflection mask, and for the opposite reason.
    /// </remarks>
    private static int? Warped(MapAssets assets)
    {
        int? brightest = null;
        double lightest = -1;

        for (int index = 0; index < assets.LightWarps.Count; index++)
        {
            if (assets.LightWarps[index] is null ||
                assets.Phong[index] is not null ||
                assets.Textures[index] is not { } texture)
            {
                continue;
            }

            ReadOnlySpan<byte> pixels = texture.Image.ToRgba(texture.Width, texture.Height);
            double total = 0;
            long texels = 0;

            for (int at = 0; at + 3 < pixels.Length; at += 4)
            {
                total += pixels[at] + pixels[at + 1] + pixels[at + 2];
                texels++;
            }

            double mean = texels == 0 ? 0 : total / texels;

            if (mean > lightest)
            {
                lightest = mean;
                brightest = index;
            }
        }

        return brightest;
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
        (float X, float Y, float Z)? travelling,
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

            // **An ambient cube is required for the SUN to be applied at all**, which is not
            // obvious and cost a wrong conclusion here. The model shader wraps the whole direct
            // term inside `if (ambientCube[0].w > 0.5f)`, so a model with no cube gets neither
            // ambient nor sun — and a light-warp test drawn without one measured unlit albedo and
            // reported no difference when the ramp was disabled.
            //
            // Uniform on all six faces so it contributes the same colour whatever the normal is,
            // which keeps it out of every measurement here: any difference between two draws is the
            // term under test, because the only other contributor has been made constant.
            light: Neutral,
            bothSides: true,

            // Null draws the ambient alone, which is the baseline a direct term is measured
            // against. Without subtracting it, a ratio of two lit pixels carries a constant that
            // flattens it toward one.
            sun: travelling is { } direction
                ? new SunLight(1f, 1f, 1f, direction.X, direction.Y, direction.Z)
                : null);

        return target.PixelAt(32, 32);
    }

    /// <summary>A pixel's three channels added, which is the quantity every ordering here uses.</summary>
    private static int Sum((int R, int G, int B) pixel) => pixel.R + pixel.G + pixel.B;

    /// <summary>A dim, uniform ambient cube — present so the sun is applied, and constant.</summary>
    private static AmbientCube Neutral =>
        new((0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f),
            (0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f));

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

            // **A PLAYER model, not just the capture point.** $lightwarptexture and $phong live
            // overwhelmingly on characters — a map's own props and its capture point carry neither
            // in quantity — so a fixture without one measures the wrong population. Loading the
            // cap point alone gave 0 light warps of 413 materials, which reads exactly like a
            // wiring failure and is not one.
            return MapCache.Load(
                entityModels:
                [
                    "models/player/scout.mdl",
                    "models/props_gameplay/cap_point_base.mdl",
                ]);
        }
    }
}
