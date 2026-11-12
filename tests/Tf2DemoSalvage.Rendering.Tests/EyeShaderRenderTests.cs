using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>The <c>EyeRefract</c>, <c>Eyes</c> and <c>Teeth</c> shaders (B725).</summary>
/// <remarks>
/// **Read** (published source): `eye_refract_vs20.fxc` builds the normal `normalize( worldPos - $eyeorigin )` (:126), bends
/// it toward the socket's left `normalize( -$irisu )` by `-0.5 · (N · left)` (:152-153) and lights the bent normal per
/// vertex (`DoLightingUnrolled`, :157) — or under a `$lightwarptexture` only its ambient (:165), the pixel shader adding
/// each light as `atten · colour · 2 · warp( CosineTermInternal( unbent N ) )` (`eye_refract_ps2x.fxc:146-179`). The iris is
/// `tex2D( iris, dot( $irisu|$irisv, (pos, 1) ) · 0.5 + 0.25 )` (:203-205, :225, :317; dilation 0.5 leaves it), the result
/// `iris · light · lerp( $ambientocclcolor, 1, occlusion )` (:386-439). `teeth_vs20.fxc:107-121`: `DoLighting` without
/// half-Lambert, times `$illumfactor · saturate( N · $forward )`. `eyes_vs20.fxc`: the normal
/// `normalize( n - 0.5 (n · $eyeup) $eyeup )`, `n = pos - $eyeorigin`; `eyes_ps2x.fxc:54-55` `lerp( base, iris, iris.a ) · light`.
///
/// **Synthetic** (D38). A 256-unit quad facing the camera 300 units away; its vertex normals face the camera (−y), so the
/// generic vertex-lit draw would light it by them. The eye's centre is 1e5 units away, so its normal is the same at every
/// corner. Textures 4×4: the iris's left two columns black, its right two grey 128 (linear 0.2158605); the projection puts
/// the centre at column 3 (`u = 1.25 · 0.5 + 0.25 = 0.875`) where the mesh's own UV, 0.5, would mix black and grey. The
/// cornea is flat (128, 128) with no parallax (blue 0) and no iris mask (alpha 0), no cube is bound, and the iris's alpha
/// (the cornea noise) is 0, so the glint, the iris highlight and the reflection are all zero. Uniform cube 0.1. Pixel 32 of
/// the 64-pixel target is the surface point (0, 0, 0) (B686).
/// </remarks>
public sealed class EyeShaderRenderTests
{
    private const int Size = 64;

    private const int Centre = 32;

    private const float Half = 128f;

    private const float LinearGrey = 0.2158605f;

    private const float Cube = 0.1f;

    private const float Far = 1.0e5f;

    private static readonly AmbientCube Uniform =
        new((Cube, Cube, Cube), (Cube, Cube, Cube), (Cube, Cube, Cube), (Cube, Cube, Cube), (Cube, Cube, Cube), (Cube, Cube, Cube));

    /// <summary>The eye 1e5 units below: its normal is +z.</summary>
    private static readonly EyeShading Below = new(
        (0f, 0f, -Far), (0f, 0f, 1f), (1f / 1024f, 0f, 0f, 1.25f), (0f, -1f, 0f, 0.5f));

    /// <remarks>
    /// **The normal is the eye's, not the mesh's.** A lamp straight in front lights the vertex normals fully, but the eye's
    /// normal (+z) not at all: `0.1` alone, 40. Above, the eye's normal faces it: `0.1 + 1`, 134. With `$halflambert`, in
    /// front: `0.1 + (0 · 0.5 + 0.5)²` = 0.35.
    /// </remarks>
    [TestCase(false, false, 40)]
    [TestCase(true, false, 134)]
    [TestCase(false, true, 78)]
    public void DrawModelPose_EyeRefractUnderALamp_LightsTheNormalFromTheEyesCentre(bool above, bool halfLambert, int arithmetic)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(target, EyeRefract(halfLambert: halfLambert), Below, locals: [above ? Lamp(0f, 0f, Far) : Lamp(0f, -Far, 0f)]);

        float wrapInFront = halfLambert ? 0.25f : 0f;
        int expected = Pixel(Cube + (above ? 1f : wrapInFront));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"above {above}, half-Lambert {halfLambert}: centre {centre}, predicted {expected}");

        expected.ShouldBe(arithmetic, "the arithmetic the prediction rests on");
        Near(centre, expected, "the projected iris (grey) times the light at the eye's normal");
    }

    /// <remarks>
    /// **Bent toward the socket's left.** The eye 1e5 units below and to −x: N = (√½, 0, √½); left = −`$irisu` = (−1, 0, 0),
    /// `-0.5 · (N · left)` = 0.35355, so the bent normal is `normalize( (√½ − 0.35355, 0, √½) )` = (0.4472, 0, 0.8944). A
    /// lamp above: `0.1 + 0.8944`; unbent it would be `0.1 + 0.7071`.
    /// </remarks>
    [Test]
    public void DrawModelPose_EyeRefractBesideItsEye_LightsTheNormalBentTowardTheSocket()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        EyeShading aside = Below with { Origin = (-Far, 0f, -Far) };

        Draw(target, EyeRefract(), aside, locals: [Lamp(0f, 0f, Far)]);

        int bent = Pixel(Cube + (2f / MathF.Sqrt(5f)));
        int unbent = Pixel(Cube + MathF.Sqrt(0.5f));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"bent: centre {centre}, predicted {bent}, unbent {unbent}");

        (bent - unbent).ShouldBeGreaterThanOrEqualTo(5, "the two are told apart");
        Near(centre, bent, "DoLighting on the bent normal");
    }

    /// <remarks>
    /// **Under a light warp the vertex carries only the ambient**, and the lamp comes per pixel as `atten · colour · 2 ·
    /// warp( cosine )`: a warp of 64 (raw, 0.25098) and the lamp above: `0.1 + 2 · 0.25098` = 0.60196.
    /// </remarks>
    [Test]
    public void DrawModelPose_EyeRefractWithALightWarp_AddsTheLampThroughTheWarp()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(target, EyeRefract(warp: true), Below, locals: [Lamp(0f, 0f, Far)]);

        int expected = Pixel(Cube + (2f * 64f / 255f));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"warp: centre {centre}, predicted {expected}");
        Near(centre, expected, "the ambient per vertex, the lamp through the warp per pixel");
    }

    /// <remarks>
    /// **The occlusion map colours the light**: black there, so `lerp( $ambientocclcolor, 1, 0 )` = 0.5, the lamp above:
    /// `(0.1 + 1) · 0.5`.
    /// </remarks>
    [Test]
    public void DrawModelPose_EyeRefractWhereTheOcclusionMapIsBlack_TakesTheOcclusionColour()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(target, EyeRefract(occluded: true), Below, locals: [Lamp(0f, 0f, Far)]);

        int expected = Pixel((Cube + 1f) * 0.5f);
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"occluded: centre {centre}, predicted {expected}");
        Near(centre, expected, "the light times $ambientocclcolor");
    }

    /// <remarks>
    /// **The glint, and with the cornea's mask the iris's highlight and its ambient term** (`eye_refract_ps2x.fxc:324-377`,
    /// :414-425). The eye 1e5 behind the quad, so N = (0, −1, 0) faces the camera; `$irisv` (0, 0, −1) makes the socket up
    /// +z, T = up × N = (1, 0, 0), B = N × T = (0, 0, 1). The cornea's (128, 128) is c = 128/255 − 0.5 off flat: its world
    /// normal is `normalize( c T + c B + N )`, the view (0, 1, 0) reflects about it to R, and a lamp of colour 0.1 at the
    /// camera adds `pow( R · L, 128 ) · 0.1`. The diffuse is `0.1 + 0.1` (N · L = 1). With the mask 1 the iris also takes
    /// `|n_iris · (0, 0, 1)|⁶ · 0.5 · 0.1` (the flattened light is +z, no cone of darkness) and
    /// `(n_iris · −tangentView) · 0.1 · 0.5` (the cube's luminance 0.1), `n_iris = (−2.5c, −2.5c, 1) / |(c, c, 1)|`.
    /// </remarks>
    [TestCase(false)]
    [TestCase(true)]
    public void DrawModelPose_EyeRefractFacingALampAtTheCamera_AddsTheGlintAndTheMaskedIrisHighlight(bool masked)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        EyeShading behind = new((0f, Far, 0f), (0f, 0f, 1f), (1f / 1024f, 0f, 0f, 1.25f), (0f, 0f, -1f, 0.5f));
        LocalLight dim = new(0f, -Far, 0f, 0.1f, 0.1f, 0.1f, 1f, 0f, 0f, 0f);

        Draw(target, EyeRefract(corneaMask: masked), behind, locals: [dim]);

        float c = (128f / 255f) - 0.5f;
        float length = MathF.Sqrt((c * c) + (c * c) + 1f);

        // R = V − 2 (V · n) n, V = (0, 1, 0), n = normalize( (c, −1, c) ); L = (0, −1, 0), so R · L = −R.y.
        float ny = -1f / length;
        float reflectedY = 1f - (2f * ny * ny);
        float glint = MathF.Pow(Math.Clamp(-reflectedY, 0f, 1f), 128f) * 0.1f;

        float highlight = masked ? (MathF.Pow(1f / length, 6f) * 0.5f * 0.1f) + ((1f / length) * Cube * 0.5f) : 0f;
        float linear = (LinearGrey * (Cube + 0.1f + highlight)) + glint;
        int expected = Encode(linear);
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"glint, masked {masked}: centre {centre}, predicted {expected}, without the glint {Encode(LinearGrey * (Cube + 0.1f + highlight))}");
        Near(centre, expected, "iris · (light + iris highlight) + glint");
    }

    /// <remarks>
    /// **Teeth darken by the mouth's forward.** The vertex normal faces a lamp in front: `0.1 + 1`, times
    /// `saturate( N · forward )`. The draw's mouth forward (0, −0.5, 0.866) gives 0.5: 0.55. Without one, the material's
    /// own `$forward` (0, −1, 0) and `$illumfactor` 0.5: also 0.55. The generic draw would be 1.1.
    /// </remarks>
    [TestCase(true)]
    [TestCase(false)]
    public void DrawModelPose_TeethFacingALamp_IsDarkenedByForwardAndIllumFactor(bool fromTheDraw)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        MouthShading? mouth = fromTheDraw ? new MouthShading((0f, -0.5f, MathF.Sqrt(0.75f)), 1f) : null;

        Draw(target, Teeth(halfLambert: false), null, locals: [Lamp(0f, -Far, 0f)], mouth: mouth);

        int expected = Pixel((Cube + 1f) * 0.5f);
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"teeth, mouth from the draw {fromTheDraw}: centre {centre}, predicted {expected}");
        Near(centre, expected, "DoLighting times $illumfactor times saturate( N · $forward )");
    }

    /// <remarks>
    /// **Teeth never wrap**: a material flagged `$halflambert`, a lamp level with the surface (N · L = 0) and forward along
    /// the normal: `0.1`, 40, where the wrap would add 0.25.
    /// </remarks>
    [Test]
    public void DrawModelPose_TeethMarkedHalfLambert_LightsWithoutTheWrap()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(
            target, Teeth(halfLambert: true), null, locals: [Lamp(-Far, 0f, 0f)],
            mouth: new MouthShading((0f, -1f, 0f), 1f));

        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"teeth half-Lambert: centre {centre}, predicted {Pixel(Cube)}");
        Near(centre, Pixel(Cube), "teeth_vs20 passes false for half-Lambert");
    }

    /// <remarks>
    /// **Eyes: the iris over the base by the iris's alpha, lit at the eye's normal bent along `$eyeup`.** The eye 1e5 behind
    /// and below, n = (0, −1, 1)·1e5, `$eyeup` +z: `n − 0.5 (n · up) up` = (0, −1, 0.5), normalised (0, −0.8944, 0.4472). A
    /// lamp in front: `0.1 + 0.8944` on the grey iris (alpha 1) over a black base; unbent, `0.1 + 0.7071`. The generic draw
    /// would show the black base.
    /// </remarks>
    [Test]
    public void DrawModelPose_EyesBeforeALamp_DrawsTheIrisLitAtTheBentNormal()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        EyeShading eye = new((0f, Far, -Far), (0f, 0f, 1f), (1f / 1024f, 0f, 0f, 0.875f), (0f, -1f, 0f, 0.5f));

        Draw(target, Eyes(), eye, locals: [Lamp(0f, -Far, 0f)]);

        int bent = Pixel(Cube + (2f / MathF.Sqrt(5f)));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"eyes: centre {centre}, predicted {bent}, unbent {Pixel(Cube + MathF.Sqrt(0.5f))}");
        Near(centre, bent, "lerp( base, iris, iris.a ) · DoLighting( bent normal )");
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

    private static int Pixel(float light) => Encode(LinearGrey * light);

    private static int Encode(float linear)
    {
        float encoded = linear <= 0.0031308f ? 12.92f * linear : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;
        return (int)MathF.Round(encoded * 255f);
    }

    private static LocalLight Lamp(float x, float y, float z) =>
        new(x, y, z, 1f, 1f, 1f, 1f, 0f, 0f, 0f);

    private static MapTexture Uniform4(byte r, byte g, byte b, byte a) =>
        new(4, 4, 4, 4, TextureImage.Rgba(Enumerable.Range(0, 16).SelectMany(_ => new[] { r, g, b, a }).ToArray()), IsTransparent: false);

    /// <summary>The iris: columns 0-1 black, 2-3 grey 128, all with one alpha.</summary>
    private static MapTexture Iris(byte alpha) =>
        new(4, 4, 4, 4, TextureImage.Rgba(Enumerable.Range(0, 16).SelectMany(texel => texel % 4 < 2
            ? new byte[] { 0, 0, 0, alpha }
            : new byte[] { 128, 128, 128, alpha }).ToArray()), IsTransparent: false);

    private static MapAssets EyeRefract(bool halfLambert = false, bool warp = false, bool occluded = false, bool corneaMask = false)
    {
        EyeMaterial eye = new(
            EyeShader.EyeRefract,
            halfLambert,
            Cornea: Uniform4(128, 128, 0, corneaMask ? (byte)255 : (byte)0),
            AmbientOcclusion: occluded ? Uniform4(0, 0, 0, 255) : Uniform4(255, 255, 255, 255),
            AmbientOcclusionColour: (0.5f, 0.5f, 0.5f));

        return MapAssets.ForMaterials(
            [Iris(alpha: 0) with { IsHalfLambert = halfLambert, Eye = eye }],
            ["EyeRefract"],
            lightWarps: warp ? [Uniform4(64, 64, 64, 255)] : null);
    }

    private static MapAssets Teeth(bool halfLambert) => MapAssets.ForMaterials(
        [Uniform4(128, 128, 128, 255) with
        {
            IsHalfLambert = halfLambert,
            Eye = new EyeMaterial(EyeShader.Teeth, HalfLambert: false, Forward: (0f, -1f, 0f), IllumFactor: 0.5f),
        }],
        ["Teeth"]);

    private static MapAssets Eyes() => MapAssets.ForMaterials(
        [Uniform4(0, 0, 0, 255) with { Eye = new EyeMaterial(EyeShader.Eyes, HalfLambert: false, Iris: Iris(alpha: 255)) }],
        ["Eyes"]);

    private static void Draw(
        OffscreenTarget target,
        MapAssets assets,
        EyeShading? eye,
        IReadOnlyList<LocalLight>? locals = null,
        MouthShading? mouth = null)
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
            locals: locals,
            eyes: eye is { } shading ? [shading] : null,
            mouth: mouth);
    }

    private static void Near((int R, int G, int B) actual, int expected, string because)
    {
        Math.Abs(actual.R - expected).ShouldBeLessThanOrEqualTo(1, because);
        Math.Abs(actual.G - expected).ShouldBeLessThanOrEqualTo(1, because);
        Math.Abs(actual.B - expected).ShouldBeLessThanOrEqualTo(1, because);
    }
}
