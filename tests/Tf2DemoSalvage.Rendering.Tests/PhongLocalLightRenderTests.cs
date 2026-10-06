using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// <c>$phong</c> and <c>$rimlight</c> from a model's LOCAL lights, drawn on a real device (B170).
/// </summary>
/// <remarks>
/// **The engine sums the highlight over every light the model is given**, sun and lamps alike —
/// `PixelShaderDoSpecularLighting` (common_vertexlitgeneric_dx9.h:321) runs `SpecularAndRimTerms` once
/// per light with that light's colour times its per-vertex attenuation, and `PhongConformanceTests`
/// quotes it. So a light's contribution depends only on its colour, its attenuation and its direction:
/// a lamp and the sun that match in those three draw the same highlight. That identity is what most of
/// these tests measure, because it needs no knowledge of the texels under the pixel.
///
/// **The centre pixel of an ODD target lies on the camera's axis**, so its surface point is the origin
/// and the eye direction is exactly (0, −1, 0). The rim tests predict their numbers from that.
/// </remarks>
public sealed class PhongLocalLightRenderTests
{
    /// <summary>Odd, so pixel 32's centre is the viewport's centre.</summary>
    private const int Size = 65;

    private const int Centre = 32;

    /// <summary>How far away a lamp is put, so its direction is the same across the pixel.</summary>
    private const float Far = 1.0e5f;

    /// <summary>Tilted 80 degrees off the view axis: a strong highlight and a strong rim.</summary>
    private static readonly (float X, float Y, float Z) Grazing = (0.985f, -0.174f, 0f);

    /// <summary>Half facing the eye and tilted up, so the rim's cube term (which takes N.z) is lit.</summary>
    private static readonly (float X, float Y, float Z) Upward = (0f, -0.5f, 0.8660254f);

    /// <summary>A dim, uniform cube, so the lamps' and the sun's diffuse are applied and are all that varies.</summary>
    private static readonly AmbientCube Neutral =
        new((0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f),
            (0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f));

    [Test]
    public void PhongRender_OneLocalPointLightAndNoSun_MatchesTheSameLightAsTheSun()
    {
        // **The whole defect in one row.** A point lamp with real falloff — a quarter of its 2.0 arrives —
        // against the sun at the 0.5 that lamp delivers, from the same direction. `PixelShaderDoSpecularLight`
        // receives `vLightColor * fAtten` and a direction and nothing else, so the two pixels must agree:
        // the diffuse because both already reach the model, the highlight and the rim because the engine
        // sums them over every light. Before B170's residual the lamp drew neither.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null || Subject("models/player/scout/scout_red") is not { } subject)
        {
            Assert.Ignore("no Direct3D, or the map or the game is not installed");
            return;
        }

        (MapAssets assets, int material) = subject;
        (float X, float Y, float Z) toward = Mirrored(Grazing);

        // Quadratic falloff only: 1 / (q d²) with d = 1e5 is exactly 0.25.
        LocalLight lamp = Lamp(toward, 2f) with { Constant = 0f, Quadratic = 4f / (Far * Far) };
        SunLight sun = new(0.5f, 0.5f, 0.5f, -toward.X, -toward.Y, -toward.Z);

        (int R, int G, int B) bySun = Draw(target, assets, material, Grazing, sun: sun);
        (int R, int G, int B) bySunUnshone = Draw(target, assets, material, Grazing, sun: sun, phong: false);
        (int R, int G, int B) byLamp = Draw(target, assets, material, Grazing, locals: [lamp]);

        TestContext.Out.WriteLine($"scout_red: by the sun {bySun} (no phong {bySunUnshone}), by the lamp {byLamp}");

        // The control: there is a highlight to match, or agreement would be two blanks agreeing.
        Sum(Minus(bySun, bySunUnshone)).ShouldBeGreaterThan(30, "the sun draws a highlight here");

        Near(byLamp, bySun, 1, "a lamp delivering the sun's light from the sun's direction draws the sun's pixel");
    }

    [Test]
    public void PhongRender_TwoLocalLights_AddTheirHighlights()
    {
        // `specularLighting += localSpecularTerm` per light (common_vertexlitgeneric_dx9.h:343), then one
        // mask, Fresnel and boost for the sum — all linear, so on a material without a rim (whose max()
        // is not) each lamp's highlight is the difference it makes, and two lamps make the sum of those.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null || Subject("models/props_spytech/TV001") is not { } subject)
        {
            Assert.Ignore("no Direct3D, or the map or the game is not installed");
            return;
        }

        (MapAssets assets, int material) = subject;
        (float X, float Y, float Z) along = Mirrored(Grazing);

        // Eight degrees round from the mirrored view, toward the normal: still in front of the surface.
        const float Turn = -8f * MathF.PI / 180f;
        (float X, float Y, float Z) beside = (
            (along.X * MathF.Cos(Turn)) - (along.Y * MathF.Sin(Turn)),
            (along.X * MathF.Sin(Turn)) + (along.Y * MathF.Cos(Turn)),
            0f);

        LocalLight first = Lamp(along, 0.3f);
        LocalLight second = Lamp(beside, 0.3f);

        (int R, int G, int B) one = Highlight(target, assets, material, Grazing, [first]);
        (int R, int G, int B) other = Highlight(target, assets, material, Grazing, [second]);
        (int R, int G, int B) both = Highlight(target, assets, material, Grazing, [first, second]);
        (int R, int G, int B) lit = Draw(target, assets, material, Grazing, locals: [first, second]);

        TestContext.Out.WriteLine($"TV001: one lamp {one}, the other {other}, both {both}, drawn {lit}");

        Sum(one).ShouldBeGreaterThan(15, "a lamp along the mirrored view draws a highlight");
        Sum(other).ShouldBeGreaterThan(15, "and so does one eight degrees from it");
        Math.Max(lit.R, Math.Max(lit.G, lit.B)).ShouldBeLessThan(255, "unclipped, or a sum cannot be read back");

        Near(both, Plus(one, other), 2, "two lamps' highlights add");
    }

    [Test]
    public void PhongRender_ALocalLightBehindTheSurface_AddsNoHighlight()
    {
        // `specularLighting *= saturate(dot( vWorldNormal, vLightDir ))` (common_vertexlitgeneric_dx9.h:186),
        // per light. At a grazing normal a lamp can sit almost exactly along the mirrored view (R·L 0.97)
        // and still be behind the surface (N·L −0.075): the engine draws exactly nothing from it.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null || Subject("models/props_spytech/TV001") is not { } subject)
        {
            Assert.Ignore("no Direct3D, or the map or the game is not installed");
            return;
        }

        (MapAssets assets, int material) = subject;

        (int R, int G, int B) behind = Highlight(target, assets, material, Grazing, [Lamp(Unit((0.1f, 0.995f, 0f)), 0.5f)]);
        (int R, int G, int B) inFront = Highlight(target, assets, material, Grazing, [Lamp(Mirrored(Grazing), 0.5f)]);

        TestContext.Out.WriteLine($"TV001: lamp behind {behind}, lamp along the mirrored view {inFront}");

        Sum(inFront).ShouldBeGreaterThan(15, "the control: the same lamp in front draws a highlight");
        behind.ShouldBe((0, 0, 0), "a lamp behind the surface adds nothing, however well it lines up");
    }

    /// <remarks>
    /// **Measured on the code before B170's residual was fixed**, with the sun the only direct light and a
    /// uniform cube — which leaves nothing the fix may change: one light, a white tint, no rim mask, and a
    /// cube that reads the same along the eye as against it. A second sun term, a lost one, or a changed
    /// order of the same multiplications moves these.
    /// </remarks>
    [TestCase("models/player/scout/scout_red", 36, 25, 26)]
    [TestCase("models/props_spytech/TV001", 41, 40, 40)]
    [TestCase("models/props_gameplay/bottle001", 207, 204, 199)]
    public void PhongRender_TheSunAlone_DrawsWhatItDrewBefore(string name, int red, int green, int blue)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null || Subject(name) is not { } subject)
        {
            Assert.Ignore("no Direct3D, or the map or the game is not installed");
            return;
        }

        (MapAssets assets, int material) = subject;
        (float X, float Y, float Z) toward = Mirrored(Grazing);

        (int R, int G, int B) drawn = Draw(
            target, assets, material, Grazing, sun: new SunLight(0.5f, 0.5f, 0.5f, -toward.X, -toward.Y, -toward.Z));

        TestContext.Out.WriteLine($"{name}: {drawn}");

        Near(drawn, (red, green, blue), 1, "the sun's highlight is unchanged");
    }

    [Test]
    public void RimRender_NoDirectLight_TakesTheCubeFacingTheEye()
    {
        // `specularLighting += (vRimAmbientCubeColor * g_fRimBoost) * saturate(fRimMultiply * worldSpaceNormal.z)`
        // (skin_ps20b.fxc:362), with `vRimAmbientCubeColor = PixelShaderAmbientLight(vEyeDir, cAmbientCube)`
        // (:186) and vEyeDir pointing from the surface TO the eye (skin_vs20.fxc:135). No light is involved at
        // all, so a model in a room with no sun keeps it.
        //
        // The eye is at −Y, so the cube's −Y face is the one it samples: lit only there, the rim is
        // 255 × boost × (1 − N·V)⁴ × N.z; lit only on +Y, it is exactly nothing.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null || Subject("models/player/scout/scout_red") is not { } subject)
        {
            Assert.Ignore("no Direct3D, or the map or the game is not installed");
            return;
        }

        (MapAssets assets, int material) = subject;
        MapRimLight rim = assets.Phong[material]!.Value.Rim!.Value;

        rim.MaskControl.ShouldBe(0f, "the control on the mask: none, so it is 1");

        (int R, int G, int B) eyeSide = Highlight(target, assets, material, Upward, [], Lit(negativeY: true));
        (int R, int G, int B) farSide = Highlight(target, assets, material, Upward, [], Lit(negativeY: false));

        float predicted = 255f * rim.Boost * AmbientRim(Upward);

        TestContext.Out.WriteLine($"scout_red rim with no light: eye side {eyeSide} (predicted {predicted:0.00}), far side {farSide}");

        Near(eyeSide, predicted, "the cube face toward the eye lights the rim");
        farSide.ShouldBe((0, 0, 0), "the face away from the eye is not what the rim samples");
    }

    [Test]
    public void RimRender_ARimMaskOfZero_RemovesTheAmbientRim()
    {
        // `float fRimMultiply = fRimMask * fRimFresnel;` (skin_ps20b.fxc:353), and it is fRimMultiply that the
        // cube term saturates — so where the exponent map's alpha masks the rim out, the cube half goes too.
        // Every c_ weapon states `$rimmask 1`.
        //
        // **One texel, read exactly**: every vertex takes the same coordinate, so the sampler has no
        // derivative, reads mip 0, and at a texel's centre returns that texel — which the test reads too.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null || Subject("models/weapons/c_models/c_scattergun/c_scattergun") is not { } subject)
        {
            Assert.Ignore("no Direct3D, or the map or the game is not installed");
            return;
        }

        (MapAssets assets, int material) = subject;
        MapRimLight rim = assets.Phong[material]!.Value.Rim!.Value;
        MapTexture exponents = assets.PhongExponentMaps[material]!.Value;

        rim.MaskControl.ShouldBe(1f, "the scattergun masks its rim by the exponent map's alpha");

        byte[] rgba = exponents.Image.ToRgba(exponents.Width, exponents.Height).ToArray();
        int masked = Texel(rgba, alpha => alpha == 0);
        int open = Texel(rgba, alpha => alpha == rgba.Where((_, at) => at % 4 == 3).Max());

        (float U, float V) At(int texel) => (
            ((texel % exponents.Width) + 0.5f) / exponents.Width,
            ((texel / exponents.Width) + 0.5f) / exponents.Height);

        float openMask = rgba[(open * 4) + 3] / 255f;

        (int R, int G, int B) maskedRim = Highlight(target, assets, material, Upward, [], Lit(negativeY: true), At(masked));
        (int R, int G, int B) openRim = Highlight(target, assets, material, Upward, [], Lit(negativeY: true), At(open));

        float predicted = 255f * rim.Boost * AmbientRim(Upward) * openMask;

        TestContext.Out.WriteLine(
            $"c_scattergun rim with no light: mask 0 {maskedRim}, mask {openMask:0.###} {openRim} (predicted {predicted:0.00})");

        Near(openRim, predicted, "the control: an unmasked texel takes the cube's rim, scaled by its mask");
        maskedRim.ShouldBe((0, 0, 0), "a texel whose rim mask is zero takes none of the cube's rim");
    }

    [Test]
    public void RimRender_ATintedMaterial_TintsItsRim()
    {
        // `float3 result = specularLighting*vSpecularTint + ...` (skin_ps20b.fxc:365) comes AFTER the rim is
        // folded in with max() and the cube's rim is added (:359, :362), so `$phongtint` colours the rim as
        // well as the highlight. This material's tint is (1, 0.39, 0) and its phong boost 0, so what it
        // shines is all rim: 255 × (N·L) × 0.5 × (1 − N·V)⁴ in red, 0.39 of that in green, none in blue.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null ||
            Subject("models/workshop/player/items/pyro/hwn2023_fiercesome_fluorescence/hwn2023_fiercesome_fluorescence_1")
                is not { } subject)
        {
            Assert.Ignore("no Direct3D, or the map or the game is not installed");
            return;
        }

        (MapAssets assets, int material) = subject;
        MapPhong phong = assets.Phong[material]!.Value;

        phong.Boost.ShouldBe(0f, "the control: no highlight, so the max() is the rim's");
        phong.Tint.Blue.ShouldBe(0f);

        (float X, float Y, float Z) toward = Mirrored(Grazing);
        (float X, float Y, float Z) normal = Unit(Grazing);
        float facing = -normal.Y;

        // N·L is N·R, which is N·V; the rim's exponent is moot with L on R.
        float rim = 255f * facing * 0.5f * MathF.Pow(1f - facing, 4f);

        (int R, int G, int B) shine = Highlight(
            target, assets, material, Grazing, [], Neutral, sun: new SunLight(0.5f, 0.5f, 0.5f, -toward.X, -toward.Y, -toward.Z));

        TestContext.Out.WriteLine($"fiercesome_fluorescence_1 rim: {shine}, predicted red {rim:0.00}");

        Near(shine, (rim, rim * phong.Tint.Green, rim * phong.Tint.Blue), 1, "the rim takes the material's tint");
    }

    /// <summary>What <c>mat_phong</c> removes: the pixel with it, less the pixel without.</summary>
    private static (int R, int G, int B) Highlight(
        OffscreenTarget target,
        MapAssets assets,
        int material,
        (float X, float Y, float Z) normal,
        LocalLight[] locals,
        AmbientCube? cube = null,
        (float U, float V)? texel = null,
        SunLight? sun = null) =>
        Minus(
            Draw(target, assets, material, normal, sun, locals, cube, phong: true, texel),
            Draw(target, assets, material, normal, sun, locals, cube, phong: false, texel));

    /// <summary>A quad facing the camera, drawn as a model with the given normal and lights; the centre pixel.</summary>
    private static (int R, int G, int B) Draw(
        OffscreenTarget target,
        MapAssets assets,
        int material,
        (float X, float Y, float Z) normal,
        SunLight? sun = null,
        LocalLight[]? locals = null,
        AmbientCube? cube = null,
        bool phong = true,
        (float U, float V)? texel = null)
    {
        WorldVertex Corner(float x, float z, float u, float v) =>
            new(x, 0f, z, texel?.U ?? u, texel?.V ?? v, 0f, 0f, 0f)
            {
                NormalX = normal.X,
                NormalY = normal.Y,
                NormalZ = normal.Z,
            };

        List<WorldVertex> vertices =
        [
            Corner(-64f, -64f, 0f, 0f),
            Corner(64f, -64f, 1f, 0f),
            Corner(64f, 64f, 1f, 1f),
            Corner(-64f, -64f, 0f, 0f),
            Corner(64f, 64f, 1f, 1f),
            Corner(-64f, 64f, 0f, 1f),
        ];

        float[] identity =
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
            identity,
            assets,
            light: cube ?? Neutral,
            bothSides: true,
            sun: sun,
            phong: phong,
            locals: locals);

        // In linear units of 1/255: the target stores through the sRGB curve (B476), and every prediction and every
        // sum of highlights here is linear light, so the stored byte is decoded before it is compared.
        (int r, int g, int b) = target.PixelAt(Centre, Centre);

        static int Linear(int stored) => (int)Math.Round(SrgbTarget.Decode(stored) * 255.0);

        return (Linear(r), Linear(g), Linear(b));
    }

    /// <summary>A point lamp far along a direction from the origin, with no falloff and no range.</summary>
    private static LocalLight Lamp((float X, float Y, float Z) toward, float brightness) =>
        new(toward.X * Far, toward.Y * Far, toward.Z * Far, brightness, brightness, brightness, 1f, 0f, 0f, 0f);

    /// <summary>A cube lit white on one face of the Y pair and dark everywhere else.</summary>
    private static AmbientCube Lit(bool negativeY) =>
        new(default, default,
            negativeY ? default : (1f, 1f, 1f),
            negativeY ? (1f, 1f, 1f) : default,
            default, default);

    /// <summary>The eye reflected through a normal, for the eye at (0, −1, 0): Valve's <c>2N(N·V) − V</c>.</summary>
    private static (float X, float Y, float Z) Mirrored((float X, float Y, float Z) normal)
    {
        (float X, float Y, float Z) n = Unit(normal);
        float twice = 2f * -n.Y;

        return (twice * n.X, (twice * n.Y) + 1f, twice * n.Z);
    }

    /// <summary><c>saturate((1 − N·V)⁴ × N.z)</c> for the eye at (0, −1, 0): the cube rim's weight, unmasked.</summary>
    private static float AmbientRim((float X, float Y, float Z) normal)
    {
        (float X, float Y, float Z) n = Unit(normal);

        return Math.Clamp(MathF.Pow(1f + n.Y, 4f) * n.Z, 0f, 1f);
    }

    private static (float X, float Y, float Z) Unit((float X, float Y, float Z) v)
    {
        float length = MathF.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));

        return (v.X / length, v.Y / length, v.Z / length);
    }

    /// <summary>The first texel whose alpha passes, as an index.</summary>
    private static int Texel(byte[] rgba, Func<byte, bool> alpha)
    {
        for (int texel = 0; (texel * 4) + 3 < rgba.Length; texel++)
        {
            if (alpha(rgba[(texel * 4) + 3]))
            {
                return texel;
            }
        }

        throw new InvalidOperationException("no texel has that alpha");
    }

    private static (int R, int G, int B) Minus((int R, int G, int B) a, (int R, int G, int B) b) =>
        (a.R - b.R, a.G - b.G, a.B - b.B);

    private static (int R, int G, int B) Plus((int R, int G, int B) a, (int R, int G, int B) b) =>
        (a.R + b.R, a.G + b.G, a.B + b.B);

    private static int Sum((int R, int G, int B) pixel) => pixel.R + pixel.G + pixel.B;

    private static void Near((int R, int G, int B) actual, (int R, int G, int B) expected, int within, string because)
    {
        actual.R.ShouldBeInRange(expected.R - within, expected.R + within, because);
        actual.G.ShouldBeInRange(expected.G - within, expected.G + within, because);
        actual.B.ShouldBeInRange(expected.B - within, expected.B + within, because);
    }

    /// <summary>Each channel within one step of a predicted real value, which the 8-bit target rounds.</summary>
    private static void Near((int R, int G, int B) actual, (float R, float G, float B) expected, int within, string because)
    {
        actual.R.ShouldBeInRange((int)MathF.Floor(expected.R) - within + 1, (int)MathF.Ceiling(expected.R) + within - 1, because);
        actual.G.ShouldBeInRange((int)MathF.Floor(expected.G) - within + 1, (int)MathF.Ceiling(expected.G) + within - 1, because);
        actual.B.ShouldBeInRange((int)MathF.Floor(expected.B) - within + 1, (int)MathF.Ceiling(expected.B) + within - 1, because);
    }

    private static void Near((int R, int G, int B) actual, float grey, string because) =>
        Near(actual, (grey, grey, grey), 1, because);

    /// <summary>The map with the models whose materials these tests draw, and one material of them by name.</summary>
    private static (MapAssets Assets, int Material)? Subject(string name)
    {
        if (GameInstall.Root is null)
        {
            return null;
        }

        MapAssets assets = MapCache.Load(entityModels: Models);
        int material = Enumerable.Range(0, assets.Materials.Count)
            .FirstOrDefault(index => string.Equals(assets.Materials[index].Name, name, StringComparison.OrdinalIgnoreCase), -1);

        material.ShouldBeGreaterThanOrEqualTo(0, $"{name} is loaded with its model");
        assets.Phong[material].ShouldNotBeNull($"{name} asks for $phong");

        return (assets, material);
    }

    /// <summary>A player, a weapon and a tinted cosmetic, whose materials are what TF2 draws phong on.</summary>
    private static readonly IReadOnlyCollection<string> Models =
    [
        "models/player/scout.mdl",
        "models/props_gameplay/cap_point_base.mdl",
        "models/weapons/c_models/c_scattergun.mdl",
        "models/workshop/player/items/pyro/hwn2023_fiercesome_fluorescence/hwn2023_fiercesome_fluorescence.mdl",
    ];
}
