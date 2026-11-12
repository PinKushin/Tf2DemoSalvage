using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>The face shaders' terms the first fixture held at zero, and their light list (B725 review).</summary>
/// <remarks>
/// **Read** (published source): `eye_refract_ps2x.fxc` — the parallax `( tv.xy · cornea.b · $parallaxstrength ) /
/// ( 1 − tv.z )`, x negated, taken off the sphere coordinate (:214-225); the dilation
/// `uv · lerp( 1, saturate( |uv| / 0.2 ), saturate( $dilation ) · 2.5 − 1.25 )` about 0.5 (:306-311); the cornea noise,
/// the iris's alpha, added as `0.1` to the colour and to the cornea normal (:228-243, :436); the cube
/// `$glossiness · texCUBE( reflect( view, cornea normal ) ) · light` (:393-442); the glint `pow( R · L, 128 )` (:414-425);
/// the ray cast against the sphere `$eyeballradius` about `$eyeorigin` (:100-107, :184-196); the cone of darkness
/// `pow( 1 − saturate( ( −tL.z − 0.25 ) / 0.75 ), 4 )` (:354). `teeth_vs20.fxc:84-89` normalises the flexed normal BEFORE
/// skinning and never after (`SkinPositionAndNormal`, `common_vs_fxc.h:661-697`, rotates by the blend matrix only).
///
/// **The light list**: the sun first where the model sees it, then the lamps, four in all (the engine's four slots,
/// `g_nLightCount`). *Interpolated*: the sun's rank among the four is not read (B170); first is ours.
///
/// **Synthetic** (D38), the first fixture's quad and camera: a 256-unit quad facing the camera 300 units away, cube 0.1, the
/// eye 1e5 below (N = +z) with T = +x, B = +y (`$irisv` (0, −1, 0)), so the view at the centre is (0, 1, 0) in tangent
/// space too. Every prediction is *arithmetic* on the lines cited.
/// </remarks>
public sealed class EyeShaderTermsRenderTests
{
    private const int Size = 64;

    private const int Centre = 32;

    private const float Half = 128f;

    private const float LinearGrey = 0.2158605f;

    private const float Cube = 0.1f;

    private const float Far = 1.0e5f;

    /// <summary>128/255 − 0.5: what a "flat" 8-bit cornea texel is off flat.</summary>
    private const float FlatCornea = (128f / 255f) - 0.5f;

    private static readonly AmbientCube Uniform =
        new((Cube, Cube, Cube), (Cube, Cube, Cube), (Cube, Cube, Cube), (Cube, Cube, Cube), (Cube, Cube, Cube), (Cube, Cube, Cube));

    /// <remarks>
    /// **The sun and four lamps of distinct colours, all from above** (N · L = 1), no warp: the vertex sums the sun (0.2, 0, 0)
    /// and the first THREE lamps (0, 0.3, 0), (0, 0, 0.4), (0.1, 0.1, 0.1) — four slots — and drops the fourth (0.5 white).
    /// `0.1 + (0.3, 0.4, 0.5)`. Under a warp of 64 the same four come per pixel through `2 · warp`: `0.1 + 0.50196 · …`.
    /// </remarks>
    [TestCase(false)]
    [TestCase(true)]
    public void DrawModelPose_EyeRefractUnderTheSunAndFourLamps_SumsTheSunAndTheFirstThree(bool warp)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        LocalLight[] lamps =
        [
            Coloured(0f, 0f, Far, (0f, 0.3f, 0f)),
            Coloured(0f, 0f, Far, (0f, 0f, 0.4f)),
            Coloured(0f, 0f, Far, (0.1f, 0.1f, 0.1f)),
            Coloured(0f, 0f, Far, (0.5f, 0.5f, 0.5f)),
        ];

        Draw(target, EyeRefract(warp: warp), Below(), lamps, sun: new SunLight(0.2f, 0f, 0f, 0f, 0f, -1f));

        float scale = warp ? 2f * 64f / 255f : 1f;
        (int R, int G, int B) expected = (
            Pixel(Cube + (scale * 0.3f)), Pixel(Cube + (scale * 0.4f)), Pixel(Cube + (scale * 0.5f)));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"sun and four lamps, warp {warp}: centre {centre}, predicted {expected}");
        Near(centre, expected, "the sun, lamps 0-2, not lamp 3");
    }

    /// <remarks>
    /// **The parallax.** The iris here is rows: 0-1 black, 2-3 grey. The sphere coordinate's v is `0.75 · 0.5 + 0.25` =
    /// 0.625, row 2, grey; with the cornea's blue at 255 and `$parallaxstrength` 0.25 the tangent view shifts it by
    /// `tv.y · 1 · 0.25 / ( 1 − tv.z )`. **The tangent view is the vertex shader's, interpolated and not renormalised**
    /// (`eye_refract_ps2x.fxc:135`): at the centre it is the mean of two opposite corners', `(0, 300 / 350.8, 0)`, so the
    /// shift is 0.2138, to v 0.4112 — 0.1448 of the way from row 1 (black) to row 2 by the filter. Lit `0.1 + 1` from above.
    /// </remarks>
    [TestCase(0)]
    [TestCase(255)]
    public void DrawModelPose_EyeRefractWithACorneaHeight_ShiftsTheIrisByTheParallax(int height)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(
            target,
            EyeRefract(cornea: (128, 128, (byte)height, 0), iris: IrisRows()),
            Below() with { IrisV = (0f, -1f, 0f, 0.75f) },
            [Lamp(0f, 0f, Far)]);

        float tangentViewY = 300f / MathF.Sqrt((Half * Half) + (300f * 300f) + (Half * Half));
        float v = 0.625f - (tangentViewY * (height / 255f) * 0.25f);
        float weight = Math.Clamp((v - 0.375f) / 0.25f, 0f, 1f);
        int expected = Encode(LinearGrey * weight * (Cube + 1f));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"parallax height {height}: centre {centre}, predicted {expected} (row weight {weight})");
        Near(centre, expected, "the iris at the sphere coordinate less the parallax");
    }

    /// <remarks>
    /// **The pupil's dilation.** The iris coordinate is 0.125 right of centre (`$irisu` offset 0.75 → `0.75 · 0.5 + 0.25`):
    /// at `$dilation` 0.5 the factor is `lerp( 1, …, 0 )` = 1, column 2's centre, grey. At 1 it is
    /// `lerp( 1, 0.625, 1.25 )` = 0.53125, so u = 0.5 + 0.0664 = 0.5664, 0.765625 of the way from column 1 (black) to
    /// column 2 (grey) by the bilinear filter: `0.765625 · grey`, lit `0.1 + 1`.
    /// </remarks>
    [TestCase(0.5f)]
    [TestCase(1f)]
    public void DrawModelPose_EyeRefractDilated_ScalesTheIrisAboutItsCentre(float dilation)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(
            target,
            EyeRefract(dilation: dilation),
            Below() with { IrisU = (1f / 1024f, 0f, 0f, 0.75f), IrisV = (0f, -1f, 0f, 0.5f) },
            [Lamp(0f, 0f, Far)]);

        float offset = 0.125f * (dilation > 0.75f ? (1.25f * (0.125f / 0.2f)) - 0.25f : 1f);
        float weight = (0.5f + offset - 0.375f) / 0.25f;
        int expected = Encode(LinearGrey * weight * (Cube + 1f));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"dilation {dilation}: centre {centre}, predicted {expected} (filter weight {weight})");
        Near(centre, expected, "the iris coordinate scaled about 0.5");
    }

    /// <remarks>
    /// **The cornea noise**, the iris's alpha (255 here): `0.1` added to the iris colour, then lit `0.1 + 1`. (It also tilts
    /// the cornea normal by 0.1 in both tangent axes, which turns the glint away from the lamp above: none.)
    /// </remarks>
    [Test]
    public void DrawModelPose_EyeRefractWhoseIrisHasAlpha_AddsTheCorneaNoise()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(target, EyeRefract(iris: IrisColumns(255)), Below(), [Lamp(0f, 0f, Far)]);

        int expected = Encode((LinearGrey + 0.1f) * (Cube + 1f));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"cornea noise: centre {centre}, predicted {expected}");
        Near(centre, expected, "( iris + 0.1 · noise ) · light");
    }

    /// <remarks>
    /// **The cube**, uniform 100 (sRGB, linear 0.12744), `$glossiness` 1: added as `cube · light` = `0.12744 · 1.1` over the
    /// iris's `grey · 1.1`.
    /// </remarks>
    [Test]
    public void DrawModelPose_EyeRefractWithACube_AddsTheReflectionTimesTheLight()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(target, EyeRefract(cube: true), Below(), [Lamp(0f, 0f, Far)]);

        float cube = Decode(100);
        int expected = Encode((LinearGrey + cube) * (Cube + 1f));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"cube: centre {centre}, predicted {expected}");
        Near(centre, expected, "iris · light + glossiness · cube · light");
    }

    /// <remarks>
    /// **The cube sampler is an sRGB read whichever cube is bound** (`EnableSRGBRead( SHADER_SAMPLER2, true )`,
    /// `eye_refract_helper.cpp:217`), so an `$envmap env_cubemap` eye reflecting the map's placed 8-bit cube decodes it
    /// exactly as its own cube: uniform 100, linear 0.12744, added as `cube · light` over `grey · 1.1`. Keyed on the
    /// material's own cube, the placed one was read raw (0.392).
    /// </remarks>
    [Test]
    public void DrawModelPose_EyeRefractOnTheMapsPlacedByteCube_DecodesItThroughSrgb()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(target, EyeRefract(cube: true, placedCube: true), Below(), [Lamp(0f, 0f, Far)]);

        int expected = Encode((LinearGrey + Decode(100)) * (Cube + 1f));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"placed cube: centre {centre}, predicted {expected}, raw {Encode((LinearGrey + (100f / 255f)) * (Cube + 1f))}");
        Near(centre, expected, "iris · light + the placed cube decoded · light");
    }

    /// <remarks>
    /// **The control: a half-float cube is linear already** — TF2's eye cube is 8-bit (B725), and a hardware sRGB read of a
    /// float format is no conversion — so a uniform 0.25 is added raw: `( grey + 0.25 ) · 1.1`. Decoded as sRGB it would be
    /// 0.0508.
    /// </remarks>
    [Test]
    public void DrawModelPose_EyeRefractWithAHalfFloatCube_ReadsItRaw()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(target, EyeRefract(cube: true, cubeFace: HalfFloat4(0.25f)), Below(), [Lamp(0f, 0f, Far)]);

        int expected = Encode((LinearGrey + 0.25f) * (Cube + 1f));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"half-float cube: centre {centre}, predicted {expected}, decoded {Encode((LinearGrey + 0.0508f) * (Cube + 1f))}");
        Near(centre, expected, "iris · light + the raw half-float cube · light");
    }

    /// <remarks>
    /// **The cornea's normal steers the glint.** Cornea (128, 255): tangent normal `normalize( (c, 0.5, 1) )`, c = 128/255 −
    /// 0.5; in world `normalize( c T + 0.5 B + N )`. The view (0, 1, 0) reflects about it to R, and a lamp of colour 0.1
    /// placed along R adds `pow( 1, 128 ) · 0.1`; its N · L is negative, so the diffuse is the cube alone. With a flat cornea
    /// the same lamp is ~53° off R: nothing.
    /// </remarks>
    [TestCase(128, false)]
    [TestCase(255, true)]
    public void DrawModelPose_EyeRefractWithATiltedCornea_ReflectsTheGlintFromWhereTheTiltPoints(int green, bool glints)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        float c = FlatCornea;
        (float X, float Y, float Z) n = Normalise((c, 0.5f, 1f));
        float vDotN = n.Y;
        (float X, float Y, float Z) r = Normalise((-2f * vDotN * n.X, 1f - (2f * vDotN * n.Y), -2f * vDotN * n.Z));

        LocalLight along = Coloured(r.X * Far, r.Y * Far, r.Z * Far, (0.1f, 0.1f, 0.1f));

        Draw(target, EyeRefract(cornea: (128, (byte)green, 0, 0)), Below(), [along]);

        int expected = Encode((LinearGrey * Cube) + (glints ? 0.1f : 0f));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"cornea green {green}: centre {centre}, predicted {expected}");
        Near(centre, expected, "the glint follows the cornea's normal");
    }

    /// <remarks>
    /// **The ray cast.** The eye's centre at the quad's (0, 0, 0), radius 100: the centre's ray from (0, −300, 0) meets the
    /// sphere at (0, −100, 0), so `$irisv` (0, −1, 0, −99.25) projects it to 0.75 (sphere v 0.625, row 2, grey); without the
    /// ray cast the quad's own point projects to −99.25, clamped to row 0, black. Lit by the cube alone through a unit bent
    /// normal: 0.1.
    /// </remarks>
    [TestCase(false)]
    [TestCase(true)]
    public void DrawModelPose_EyeRefractWithTheRayCast_ProjectsTheIrisFromTheSphere(bool rayCast)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        EyeShading centred = new((0f, 0f, 0f), (0f, 0f, 1f), (1f / 1024f, 0f, 0f, 1.25f), (0f, -1f, 0f, -99.25f));

        Draw(target, EyeRefract(iris: IrisRows(), rayCast: rayCast, radius: 100f), centred, []);

        int expected = rayCast ? Pixel(Cube) : 0;
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"ray cast {rayCast}: centre {centre}, predicted {expected}");
        Near(centre, expected, "the iris projected from the ray's hit on the sphere");
    }

    /// <remarks>
    /// **The iris highlight under the cone of darkness.** The cornea's mask 1; a lamp of colour 1 behind the surface at
    /// L = (0.78062, 0, −0.625): tangent light (0.78062, 0, −0.625), so the cone is `pow( 1 − ( 0.625 − 0.25 ) / 0.75, 4 )` =
    /// 0.0625, the flattened light `normalize( (0.39031, 0, 0.625) )`, and the iris normal `(−2.5c, −2.5c, 1) / |(c, c, 1)|`.
    /// The diffuse is the cube alone (N · L negative), and the ambient iris term `( n · (0, −1, 0) ) · 0.1 · 0.5` is added too.
    /// </remarks>
    [Test]
    public void DrawModelPose_EyeRefractLitFromBehind_DarkensTheIrisHighlightByTheCone()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        (float X, float Y, float Z) light = (MathF.Sqrt(1f - (0.625f * 0.625f)), 0f, -0.625f);

        Draw(
            target,
            EyeRefract(cornea: (128, 128, 0, 255)),
            Below(),
            [Coloured(light.X * Far, light.Y * Far, light.Z * Far, (1f, 1f, 1f))]);

        float c = FlatCornea;
        float length = MathF.Sqrt((c * c) + (c * c) + 1f);
        (float X, float Y, float Z) iris = (-2.5f * c / length, -2.5f * c / length, 1f / length);
        (float X, float Y, float Z) flattened = Normalise((light.X * 0.5f, 0f, 0.625f));
        float facing = MathF.Pow(MathF.Abs((iris.X * flattened.X) + (iris.Z * flattened.Z)), 6f) * 0.5f;
        float cone = MathF.Pow(1f - ((0.625f - 0.25f) / 0.75f), 4f);
        float ambient = Math.Clamp(-iris.Y, 0f, 1f) * Cube * 0.5f;

        int expected = Encode(LinearGrey * (Cube + (facing * cone) + ambient));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"cone: centre {centre}, predicted {expected}, without the cone {Encode(LinearGrey * (Cube + facing + ambient))}");
        Near(centre, expected, "the highlight times the cone of darkness");
    }

    /// <remarks>
    /// **Teeth normalise before skinning, and never after.** One bone scaling by 2: the skinned normal is (0, −2, 0), so
    /// `DoLighting` gives the cube `4 · 0.1` and the lamp in front `2 · 1`, 2.4; times `saturate( N · (0, −0.25, 0.968) )` =
    /// 0.5: 1.2. Normalised after, it would be `( 0.1 + 1 ) · 0.25`.
    /// </remarks>
    [Test]
    public void DrawModelPose_TeethOnAScalingBone_LightTheUnnormalisedSkinnedNormal()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        float[] doubling = [2f, 0f, 0f, 0f, 0f, 2f, 0f, 0f, 0f, 0f, 2f, 0f];

        Draw(
            target,
            Teeth(),
            null,
            [Lamp(0f, -Far, 0f)],
            mouth: new MouthShading((0f, -0.25f, MathF.Sqrt(1f - 0.0625f)), 1f),
            bones: [doubling]);

        int expected = Pixel(((4f * Cube) + 2f) * 0.5f);
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"teeth on a scaling bone: centre {centre}, predicted {expected}, normalised after {Pixel((Cube + 1f) * 0.25f)}");
        Near(centre, expected, "teeth_vs20's normal: normalised before the bones, scaled by them");
    }

    /// <remarks>
    /// **The face shaders compile when a face material loads, not at the first eye drawn** — a compile there is a stall in
    /// the middle of playback; the engine binds precompiled shaders from the material's snapshot. A table without one
    /// compiles none.
    /// </remarks>
    [TestCase(true)]
    [TestCase(false)]
    public void UploadTextures_AFaceMaterial_CompilesTheFaceShadersBeforeAnyDraw(bool face)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        MapAssets assets = face
            ? EyeRefract()
            : MapAssets.ForMaterials([Uniform4(128, 128, 128, 255)], ["VertexLitGeneric"]);

        target.UploadCompilesFaceShaders(assets).ShouldBe(face);
    }

    private static OffscreenTarget? Target()
    {
        OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
        }

        return target;
    }

    private static EyeShading Below() =>
        new((0f, 0f, -Far), (0f, 0f, 1f), (1f / 1024f, 0f, 0f, 1.25f), (0f, -1f, 0f, 0.5f));

    private static int Pixel(float light) => Encode(LinearGrey * light);

    private static int Encode(float linear)
    {
        linear = Math.Clamp(linear, 0f, 1f);
        float encoded = linear <= 0.0031308f ? 12.92f * linear : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;
        return (int)MathF.Round(encoded * 255f);
    }

    private static float Decode(byte value)
    {
        float v = value / 255f;
        return v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
    }

    private static (float X, float Y, float Z) Normalise((float X, float Y, float Z) v)
    {
        float length = MathF.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));
        return (v.X / length, v.Y / length, v.Z / length);
    }

    private static LocalLight Lamp(float x, float y, float z) => Coloured(x, y, z, (1f, 1f, 1f));

    private static LocalLight Coloured(float x, float y, float z, (float R, float G, float B) colour) =>
        new(x, y, z, colour.R, colour.G, colour.B, 1f, 0f, 0f, 0f);

    private static MapTexture Uniform4(byte r, byte g, byte b, byte a) =>
        new(4, 4, 4, 4, TextureImage.Rgba(Enumerable.Range(0, 16).SelectMany(_ => new[] { r, g, b, a }).ToArray()), IsTransparent: false);

    /// <summary>Columns 0-1 black, 2-3 grey 128.</summary>
    private static MapTexture IrisColumns(byte alpha) =>
        new(4, 4, 4, 4, TextureImage.Rgba(Enumerable.Range(0, 16).SelectMany(texel => texel % 4 < 2
            ? new byte[] { 0, 0, 0, alpha }
            : new byte[] { 128, 128, 128, alpha }).ToArray()), IsTransparent: false);

    /// <summary>Rows 0-1 black, 2-3 grey 128, alpha 0.</summary>
    private static MapTexture IrisRows() =>
        new(4, 4, 4, 4, TextureImage.Rgba(Enumerable.Range(0, 16).SelectMany(texel => texel / 4 < 2
            ? new byte[] { 0, 0, 0, 0 }
            : new byte[] { 128, 128, 128, 0 }).ToArray()), IsTransparent: false);

    private static MapAssets EyeRefract(
        bool warp = false,
        (byte R, byte G, byte B, byte A)? cornea = null,
        MapTexture? iris = null,
        float dilation = 0.5f,
        bool cube = false,
        bool rayCast = false,
        float radius = 0.5f,
        MapTexture? cubeFace = null,
        bool placedCube = false)
    {
        (byte R, byte G, byte B, byte A) texel = cornea ?? (128, 128, 0, 0);

        EyeMaterial eye = new(
            EyeShader.EyeRefract,
            HalfLambert: false,
            Cornea: Uniform4(texel.R, texel.G, texel.B, texel.A),
            AmbientOcclusion: Uniform4(255, 255, 255, 255),
            Dilation: dilation,
            AmbientOcclusionColour: (0.5f, 0.5f, 0.5f),
            EyeballRadius: radius,
            RaytraceSphere: rayCast);

        MapCubemap? reflection = null;
        MapTexture face = cubeFace ?? Uniform4(100, 100, 100, 255);
        MapEnvmapShading shading = new((1f, 1f, 1f), 0f, 1f, MaskedByBaseAlpha: false, 1f, MaskedByNormalMapAlpha: false);

        if (cube && !placedCube)
        {
            reflection = new MapCubemap([face, face, face, face, face, face], shading);
        }

        // An `$envmap env_cubemap` material: the shading only, and the map's one placed cube, nearest wherever it stands.
        return MapAssets.ForMaterials(
            [(iris ?? IrisColumns(0)) with { Eye = eye }],
            ["EyeRefract"],
            cubemaps: reflection is null ? null : [reflection],
            lightWarps: warp ? [Uniform4(64, 64, 64, 255)] : null,
            localReflections: placedCube ? [shading] : null,
            placedCubemaps: placedCube ? [new MapPlacedCubemap(new BspCubemap(0, -100, 0, 32), [face, face, face, face, face, face])] : null);
    }

    /// <summary>A 4×4 face of half floats, every texel (<paramref name="value"/>, ×3, 1).</summary>
    private static MapTexture HalfFloat4(float value)
    {
        ushort colour = BitConverter.HalfToUInt16Bits((Half)value);
        ushort one = BitConverter.HalfToUInt16Bits((Half)1f);
        byte[] texels = new byte[16 * 8];

        for (int texel = 0; texel < 16; texel++)
        {
            for (int channel = 0; channel < 4; channel++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(texels.AsSpan((texel * 8) + (channel * 2)), channel < 3 ? colour : one);
            }
        }

        return new MapTexture(4, 4, 4, 4, new TextureImage(VtfFormat.Rgba16161616F, [texels]), IsTransparent: false);
    }

    private static MapAssets Teeth() => MapAssets.ForMaterials(
        [Uniform4(128, 128, 128, 255) with { Eye = new EyeMaterial(EyeShader.Teeth, HalfLambert: false) }],
        ["Teeth"]);

    private static void Draw(
        OffscreenTarget target,
        MapAssets assets,
        EyeShading? eye,
        IReadOnlyList<LocalLight> locals,
        SunLight? sun = null,
        MouthShading? mouth = null,
        IReadOnlyList<float[]>? bones = null)
    {
        WorldVertex Corner(float x, float z, float u, float v) =>
            new(x, 0f, z, u, v, 0f, 0f, 0f)
            {
                NormalX = 0f,
                NormalY = -1f,
                NormalZ = 0f,
                TangentX = 1f,
                TangentY = 0f,
                TangentZ = 0f,
                TangentW = 1f,
                WeightA = bones is null ? 0f : 1f,
            };

        List<WorldVertex> vertices =
        [
            Corner(-Half, -Half, 0f, 0f),
            Corner(Half, -Half, 1f, 0f),
            Corner(Half, Half, 1f, 1f),
            Corner(-Half, -Half, 0f, 0f),
            Corner(Half, Half, 1f, 1f),
            Corner(-Half, Half, 0f, 1f),
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
            NearZ = MathF.Min(FreeCamera.WorldNearPlane, 300f / 8f),
        }.ToMatrix();

        target.Clear(0f, 0f, 0f);
        target.DrawModelPose(
            vertices,
            [new WorldBatch(0, 0, vertices.Count, Eyeball: eye is null ? -1 : 0)],
            camera,
            identity,
            assets,
            light: Uniform,
            bothSides: true,
            sun: sun,
            locals: locals,
            bones: bones,
            eyes: eye is { } shading ? [shading] : null,
            mouth: mouth);
    }

    private static void Near((int R, int G, int B) actual, int expected, string because) =>
        Near(actual, (expected, expected, expected), because);

    private static void Near((int R, int G, int B) actual, (int R, int G, int B) expected, string because)
    {
        Math.Abs(actual.R - expected.R).ShouldBeLessThanOrEqualTo(1, because);
        Math.Abs(actual.G - expected.G).ShouldBeLessThanOrEqualTo(1, because);
        Math.Abs(actual.B - expected.B).ShouldBeLessThanOrEqualTo(1, because);
    }
}
