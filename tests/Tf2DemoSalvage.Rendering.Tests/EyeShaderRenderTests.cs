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
        (0f, 0f, -Far), (0f, 0f, 1f), (1f / 1024f, 0f, 0f, 1.25f), (0f, -1f, 0f, 0.5f), (0f, 0f, 1f), 0.5f);

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

        EyeShading behind = new((0f, Far, 0f), (0f, 0f, 1f), (1f / 1024f, 0f, 0f, 1.25f), (0f, 0f, -1f, 0.5f), (0f, -1f, 0f), 0.5f);
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
    /// **The draw's mouth reaches only a material flagged `$clientShader MouthShader`** (B725 5e): studiorender sets the
    /// mouth up where the material's flag is set and the model has a mouth (`0x1800045be-0x1800045d8`, then
    /// `R_MouthSetupVertexShader` at `0x180004666`). A mouth facing the lamp at full weight, (0, −1, 0) and 1, lights the
    /// flagged teeth `0.1 + 1` = 1.1; unflagged, the material's own `$forward` (0, −1, 0) and `$illumfactor` 0.5 give 0.55.
    /// </remarks>
    [TestCase(true)]
    [TestCase(false)]
    public void DrawModelPose_TeethWithAndWithoutTheMouthFlag_TakeTheDrawsMouthOnlyWhenFlagged(bool flagged)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(
            target, Teeth(halfLambert: false, mouthShader: flagged), null, locals: [Lamp(0f, -Far, 0f)],
            mouth: new MouthShading((0f, -1f, 0f), 1f));

        int expected = Pixel(flagged ? Cube + 1f : (Cube + 1f) * 0.5f);
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"teeth flagged {flagged}: centre {centre}, predicted {expected}");
        Near(centre, expected, flagged ? "the draw's mouth" : "the material's own $forward and $illumfactor");
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

        EyeShading eye = new((0f, Far, -Far), (0f, 0f, 1f), (1f / 1024f, 0f, 0f, 0.875f), (0f, -1f, 0f, 0.5f), (0f, -1f, 0f), 0.5f);

        Draw(target, Eyes(), eye, locals: [Lamp(0f, -Far, 0f)]);

        int bent = Pixel(Cube + (2f / MathF.Sqrt(5f)));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"eyes: centre {centre}, predicted {bent}, unbent {Pixel(Cube + MathF.Sqrt(0.5f))}");
        Near(centre, bent, "lerp( base, iris, iris.a ) · DoLighting( bent normal )");
    }

    /// <remarks>
    /// **Eyes adds its glint, rendered for this eye and this view, times the ambient damping** (B725 item 3,
    /// `eyes_ps2x.fxc:56`, `eyes_dx8_dx9_helper.cpp:445-453`; the target from `studiorender.dll` `0x18001e020`,
    /// `0x18001f510`, `0x18001d600`, as <see cref="EyeGlint"/> ports them). The eye on the view's axis 600 units of radius
    /// (texels 4 pixels wide), forward at the camera, a lamp at the camera: the glint is at (0, 0), so texel (16, 16) is 139
    /// (`EyeGlintConformanceTests`) and the rest of its 3×3 is 0. `$glintu`/`$glintv` put that texel's centre `r / 32` =
    /// 18.75 units right of and above the eye's axis: pixel (34, 30). The iris's alpha is 0 over a black base, so the lit
    /// colour is 0 and the pixel is the glint alone, linear (the glint is not read through sRGB, `:378-381` enable it only
    /// on samplers 0 and 1).
    /// - Cube 0.1: luminance 0.1 &gt; 0.01, damping 1: 139/255.
    /// - Cube 0.005: damping `0.005 · SimpleSpline( 0.5 )` = 0.0025.
    /// - No lamp: no glint, 0 — the control that the pixel is the glint.
    /// - The eye 1e5 away: `ComputePixelWidthOfSphere` ≈ 1.5 pixels &lt; `r_eyeglintlodpixels` 20, so `vgui/black`: 0.
    /// </remarks>
    [TestCase(Cube, true, 1200f, 139f / 255f)]
    [TestCase(0.005f, true, 1200f, 139f / 255f * 0.0025f)]
    [TestCase(Cube, false, 1200f, 0f)]
    [TestCase(Cube, true, Far, 0f)]
    public void DrawModelPose_EyesFacingALampAtTheCamera_AddsTheGlintTimesTheDamping(float cube, bool lamp, float depth, float expected)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        EyeShading eye = new(
            (0f, depth, 0f), (0f, 0f, 1f), (1f / 1024f, 0f, 0f, 0.875f), (0f, -1f, 0f, 0.5f), (0f, -1f, 0f), 600f);
        AmbientCube dim = new((cube, cube, cube), (cube, cube, cube), (cube, cube, cube), (cube, cube, cube), (cube, cube, cube), (cube, cube, cube));

        Draw(target, Eyes(irisAlpha: 0), eye, locals: lamp ? [Lamp(0f, -300f, 0f)] : [], cube: dim);

        (int R, int G, int B) texel = target.PixelAt(34, 30);

        TestContext.Out.WriteLine($"eyes glint, cube {cube}, lamp {lamp}, depth {depth}: {texel}, predicted {Encode(expected)}");
        Near(texel, Encode(expected), "the glint texel times the damping");
    }

    /// <remarks>
    /// **The glint's switches** (`studiorender.dll` `0x18001e020`; the eye and lamp of the test above, pixel (34, 30) on
    /// texel (16, 16)):
    /// - `r_glint_alwaysdraw 1` skips the level of detail (`0x18001e071`-`0x18001e07c`): the eye 1e5 away, 1.5 pixels wide,
    ///   still draws 139, 195.
    /// - `r_glint_procedural` with a lamp of 0.1: the GPU pass gives `0.1 · 0.5438 · 255` = 14 (66 encoded); the CPU
    ///   regenerator (`0x18001e0ad`) gives 130 through mathlib's gamma tables (189) — `EyeGlintConformanceTests`.
    /// - `mat_wireframe` (with `sv_cheats`, `engine.dll` `0x1800f5855`) sets `bWireframe`, and the mesh setup skips the
    ///   glint (`0x18000f7d1`): the pixel, on the quad's diagonal edge, is the lit iris alone, 0 — 195 without it.
    /// </remarks>
    [TestCase(true, false, false, Far, 1f, 139f / 255f)]
    [TestCase(false, false, false, 1200f, 0.1f, 14f / 255f)]
    [TestCase(false, true, false, 1200f, 0.1f, 130f / 255f)]
    [TestCase(false, false, true, 1200f, 1f, 0f)]
    public void DrawModelPose_EyesUnderTheGlintSwitches_DrawAsTheEngineChooses(
        bool alwaysDraw, bool procedural, bool wireframe, float depth, float lamp, float expected)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        EyeShading eye = new(
            (0f, depth, 0f), (0f, 0f, 1f), (1f / 1024f, 0f, 0f, 0.875f), (0f, -1f, 0f, 0.5f), (0f, -1f, 0f), 600f);

        target.Glint = new GlintSwitches(alwaysDraw, procedural);
        target.Wireframe = wireframe ? 1 : 0;
        Draw(target, Eyes(irisAlpha: 0), eye, locals: [new LocalLight(0f, -300f, 0f, lamp, lamp, lamp, 1f, 0f, 0f, 0f)]);

        (int R, int G, int B) texel = target.PixelAt(34, 30);

        TestContext.Out.WriteLine(
            $"eyes glint, alwaysdraw {alwaysDraw}, procedural {procedural}, wireframe {wireframe}: {texel}, predicted {Encode(expected)}");
        Near(texel, Encode(expected), "the glint the switches choose");
    }

    /// <remarks>
    /// **Under `mat_wireframe` every mesh draws with the studio render's wireframe material** (B788; `studiorender.dll`
    /// `R_StudioSetupSkinAndLighting` `0x1800041ec`-`0x18000420e`, `MapAssets.Wireframe`), an eye's and a plain
    /// VertexLitGeneric's alike. `debugmrmwireframe` is the `Wireframe` shader, VertexLitGeneric's helper with
    /// `bVertexLitGeneric` false and no `$basetexture` (`wireframe_dx9.cpp:33`, `:43`, `:49`), so UNLIT white times
    /// `$color [0 1 1]`: (0, 255, 255) on the quad's diagonal edge (34, 30), whatever the cube's light (review 1, Fable: the
    /// first port lit it, (0, 89, 89)). Before, the model kept its own material: red 89 or the eye's black iris. The
    /// control is the same draw without wireframe, which is the material's own.
    /// </remarks>
    [TestCase("VertexLitGeneric")]
    [TestCase("Eyes")]
    public void DrawModelPose_UnderWireframe_DrawsTheStudioRendersWireframeMaterial(string shader)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        EyeShading? eye = shader == "Eyes" ? Below : null;
        MapAssets assets = Wireframed(shader, ignoresZ: true);

        Draw(target, assets, eye);
        (int R, int G, int B) own = target.PixelAt(34, 30);
        target.Wireframe = 1;
        Draw(target, assets, eye);
        (int R, int G, int B) wire = target.PixelAt(34, 30);

        TestContext.Out.WriteLine($"{shader} under wireframe: own {own}, wireframe {wire}, predicted (0, 255, 255)");
        own.G.ShouldBe(0, "the control: the material's own, which has no green");
        wire.ShouldBe((0, 255, 255), "unlit white times [0 1 1]");
        target.PixelAt(20, 20).ShouldBe((0, 0, 0), "edges only: the wireframe fill mode draws no interior");
    }

    /// <remarks>
    /// **A brush entity keeps its own material under `mat_wireframe`** (B788 review 1, Fable): the swap is the studio
    /// render's (`R_StudioSetupSkinAndLighting`), which never draws a brush model — `R_DrawBrushModel` binds each batch's
    /// own material (B659). The studio model of the same mesh is the control, which swaps.
    /// </remarks>
    [TestCase("offscreen/posed.mdl", 0, 255, 255)]
    [TestCase("*1", 89, 0, 0)]
    public void DrawModelPose_UnderWireframe_SwapsAStudioModelAndNotABrushEntity(string modelPath, int red, int green, int blue)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        target.Wireframe = 1;
        Draw(target, Wireframed("VertexLitGeneric", ignoresZ: true), null, modelPath: modelPath);

        target.PixelAt(34, 30).ShouldBe((red, green, blue), "the brush entity's own red, lit, is drawn: not swapped, not absent");
    }

    /// <remarks>
    /// **Only the wireframe material skips the depth test** (B788 review 4, Fable): with it loaded and `mat_wireframe` off,
    /// a second, different material drawn over the first at the same depth is refused by the strict test, so the first's
    /// red stays, (89, 0, 0), inside the quad. Every material taking `$ignorez` would let the blue through.
    /// </remarks>
    [Test]
    public void DrawModelPose_WithTheWireframeMaterialLoaded_OtherMaterialsStillTestDepth()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        MapAssets assets = MapAssets.ForMaterials(
            [Uniform4(255, 0, 0, 255), Uniform4(255, 255, 255, 255) with { Modulation = (0f, 1f, 1f, 1f) }, Uniform4(0, 0, 255, 255)],
            ["VertexLitGeneric", "Wireframe", "VertexLitGeneric"],
            wireframe: new WireframeMaterial(1, IgnoresZ: true),
            overrideMaterials: new Dictionary<string, int> { ["forced.vmt"] = 2 });

        Draw(target, assets, null, overrideMaterial: "forced.vmt");
        target.PixelAt(20, 20).ShouldBe((0, 0, 89), "the control: the forced draw on its own is blue");

        Draw(target, assets, null);
        Draw(target, assets, null, clear: false, overrideMaterial: "forced.vmt");

        target.PixelAt(20, 20).ShouldBe((89, 0, 0));
    }

    /// <remarks>
    /// **Without the wireframe material (no TF2 install), a model keeps its own under `mat_wireframe`** (B788 review 3,
    /// Sonnet): its red, lit, on the edge, (89, 0, 0).
    /// </remarks>
    [Test]
    public void DrawModelPose_UnderWireframeWithNoWireframeMaterial_KeepsItsOwn()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        target.Wireframe = 1;
        Draw(target, MapAssets.ForMaterials([Uniform4(255, 0, 0, 255)], ["VertexLitGeneric"]), null);

        target.PixelAt(34, 30).ShouldBe((89, 0, 0));
    }

    /// <remarks>
    /// **The wireframe material takes no fog** (B788 review 2): `Wireframe_DX9` sets `MATERIAL_VAR_NOFOG`
    /// (`wireframe_dx9.cpp:36`; what the flag does at draw is the closed shaderlib's, *interpolated* as no fog). Fog ending
    /// at 1 unit, blue, full density, with the quad 300 away: the model's own red is all fog, (0, 0, 255), the control; its
    /// wireframe edge is still (0, 255, 255). Reads the frame, so it covers the constant's upload and the shader's mode 2,
    /// not only `FogModeOf`.
    /// </remarks>
    [Test]
    public void DrawModelPose_UnderWireframeInFullFog_DrawsTheEdgeUnfogged()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        MapAssets assets = Wireframed("VertexLitGeneric", ignoresZ: true);
        Tf2DemoSalvage.Core.Scene.SceneFog fog = new(0f, 1f, 0f, 0f, 1f, 1f);

        Draw(target, assets, null, fog: fog);
        (int R, int G, int B) own = target.PixelAt(34, 30);
        target.Wireframe = 1;
        Draw(target, assets, null, fog: fog);
        (int R, int G, int B) wire = target.PixelAt(34, 30);

        TestContext.Out.WriteLine($"full blue fog: own {own}, wireframe {wire}");
        own.ShouldBe((0, 0, 255), "the control: the model's own material is all fog");
        wire.ShouldBe((0, 255, 255));
    }

    /// <remarks>
    /// **A model's frame is read after its material's proxy chain runs** (B788 review 3, Sonnet; B343): `$frame` is what the
    /// chain writes, so reading it before the chain gives the previous draw's. Material 0 animates `$basetexture` at one
    /// frame a second over red then green frames; at 1.5 seconds the chain writes frame 1, green, lit (0, 89, 0). Read before
    /// the chain, the frame is the earlier value, 0, red.
    /// </remarks>
    [Test]
    public void DrawModelPose_AnAnimatedMaterial_BindsTheFrameItsProxyChainWrote()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        IReadOnlyList<MaterialProxy> animated = VmtMaterial.Parse(System.Text.Encoding.UTF8.GetBytes("""
            "VertexLitGeneric" { "Proxies" { "AnimatedTexture" { "animatedtexturevar" "$basetexture" "animatedtextureframenumvar" "$frame" "animatedtextureframerate" 1 } } }
            """)).Proxies;
        MapAssets assets = MapAssets.ForMaterials(
            [Uniform4(255, 0, 0, 255)],
            ["VertexLitGeneric"],
            proxies: [animated],
            animationFrames: [[Uniform4(255, 0, 0, 255), Uniform4(0, 255, 0, 255)]]);

        target.Seconds = 1.5;
        Draw(target, assets, null);

        target.PixelAt(20, 20).ShouldBe((0, Encode(Cube), 0));
    }

    /// <remarks>
    /// **A forced material binds its OWN animation frame** (B788 review 2): the frame is the drawn material's, so a gold
    /// corpse's animated override animates. Material 2, forced, animates green frames over a blue still; the batch's own
    /// material 0 animates nothing. Lit green (0, 89, 0) on the quad; the batch's index would bind the blue still.
    /// </remarks>
    [Test]
    public void DrawModelPose_WithAForcedAnimatedMaterial_BindsTheForcedMaterialsFrame()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        MapAssets assets = MapAssets.ForMaterials(
            [Uniform4(255, 0, 0, 255), Uniform4(0, 0, 255, 255)],
            ["VertexLitGeneric", "VertexLitGeneric"],
            overrideMaterials: new Dictionary<string, int> { ["forced.vmt"] = 1 },
            animationFrames: [null, [Uniform4(0, 255, 0, 255), Uniform4(0, 255, 0, 255)]]);

        Draw(target, assets, null, overrideMaterial: "forced.vmt");

        target.PixelAt(20, 20).ShouldBe((0, Encode(Cube), 0));
    }

    /// <remarks>
    /// **`debugmrmwireframe` is `$ignorez 1`**, so its edges draw over what is in front of them (B788). The quad drawn solid
    /// first, then its wireframe at the same depth without clearing: with no depth test the edge is the wireframe's
    /// (0, 255, 255). **A depth-tested debug wireframe passes that equal depth too** (B803): it tests `LessEqual`, as
    /// `mat_wireframe 2`'s outline over the surface it outlines must; until B803 it took the opaque pass's strict `Less`
    /// and the solid red stayed. Which of the two hides behind a NEARER surface is
    /// `WorldWireframeConformanceTests.DrawWorld_AnEdgeBehindANearerFace_ShowsByItsWireframesIgnoreZ`.
    /// </remarks>
    [TestCase(true, 0)]
    [TestCase(false, 0)]
    public void DrawModelPose_TheWireframeMaterialOverADrawnSurface_PassesTheSurfacesOwnDepth(bool ignoresZ, int red)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        MapAssets assets = Wireframed("VertexLitGeneric", ignoresZ, decal: false);

        Draw(target, assets, null);
        target.Wireframe = 1;
        Draw(target, assets, null, clear: false);
        (int R, int G, int B) edge = target.PixelAt(34, 30);

        TestContext.Out.WriteLine($"wireframe over the solid, ignorez {ignoresZ}: {edge}");
        edge.R.ShouldBe(red);
    }

    /// <remarks>
    /// **A forced material wins over the wireframe one**: the swap needs `m_pForcedMaterial` (`+0x250`) null
    /// (`studiorender.dll` `0x1800041f2`), so a gold corpse's `ForcedMaterialOverride` draws gold, here a blue material 2:
    /// (0, 0, 89) on the edge, not the wireframe's (0, 255, 255). **And solid** (B808): the fill is the drawn material's own
    /// `MATERIAL_VAR_WIREFRAME` (`wireframe_dx9.cpp:37`), which a VertexLitGeneric material does not set, so the interior
    /// (20, 20) is blue too — before, every pass drew its wireframe twin and the interior was the clear black.
    /// </remarks>
    [Test]
    public void DrawModelPose_UnderWireframeWithAForcedMaterial_DrawsTheForcedOneSolid()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        MapAssets assets = MapAssets.ForMaterials(
            [Uniform4(255, 0, 0, 255), Uniform4(255, 255, 255, 255) with { Modulation = (0f, 1f, 1f, 1f) }, Uniform4(0, 0, 255, 255)],
            ["VertexLitGeneric", "Wireframe", "VertexLitGeneric"],
            wireframe: new WireframeMaterial(1, IgnoresZ: true),
            overrideMaterials: new Dictionary<string, int> { ["forced.vmt"] = 2 });
        int lit = Encode(Cube);

        target.Wireframe = 1;
        Draw(target, assets, null, overrideMaterial: "forced.vmt");

        (target.PixelAt(34, 30), target.PixelAt(20, 20)).ShouldBe(((0, 0, lit), (0, 0, lit)));
    }

    /// <remarks>
    /// **A brush entity draws solid under `mat_wireframe`, then is outlined with `debugworldwireframe`** (B803): the
    /// `R_DrawBrushModel` paths (`engine.dll` `FUN_1800dcb20`, `FUN_1800ddca0`, `FUN_1800e0920`) end in `FUN_1800dc720`, which
    /// binds `0x18069b3e0` and redraws the surfaces. The edge (34, 30) is `$color [0.9 0.5 0.5]`, unlit, (230, 128, 128)
    /// within a level; the interior (20, 20) the brush's own red, lit, (89, 0, 0). The studio model of the same mesh is the
    /// control: swapped to `debugmrmwireframe` (B788), not outlined, so its edge is cyan and its interior the clear black.
    /// </remarks>
    [TestCase("*1", 230, 128, 128, 89)]
    [TestCase("offscreen/posed.mdl", 0, 255, 255, 0)]
    public void DrawModelPose_UnderWireframe_OutlinesABrushEntityAndSwapsAStudioModel(
        string modelPath, int red, int green, int blue, int insideRed)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        MapAssets assets = MapAssets.ForMaterials(
            [
                Uniform4(255, 0, 0, 255),
                Uniform4(255, 255, 255, 255) with { Modulation = (0f, 1f, 1f, 1f), IsDecal = true },
                Uniform4(255, 255, 255, 255) with { Modulation = (0.9f, 0.5f, 0.5f, 1f), IsDecal = true },
            ],
            ["VertexLitGeneric", "Wireframe", "Wireframe"],
            wireframe: new WireframeMaterial(1, IgnoresZ: true),
            engineWireframes: new Dictionary<string, WireframeMaterial> { [MapAssets.WorldWireframePath] = new(2, IgnoresZ: true) });

        target.Wireframe = 1;
        Draw(target, assets, null, modelPath: modelPath, frontFacing: true);
        (int R, int G, int B) edge = target.PixelAt(34, 30);

        TestContext.Out.WriteLine($"{modelPath} under wireframe: edge {edge}, inside {target.PixelAt(20, 20)}");
        Math.Abs(edge.R - red).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(edge.G - green).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(edge.B - blue).ShouldBeLessThanOrEqualTo(1);
        target.PixelAt(20, 20).ShouldBe((insideRed, 0, 0));
    }

    /// <remarks>
    /// **`mat_wireframe 3` outlines a brush entity's polygons, not its triangles** (B803): `FUN_1800dc720`, at the end of the
    /// `R_DrawBrushModel` paths, hands the surfaces to `FUN_1800e6780` for 3, which draws each one's own vertex loop as
    /// lines with `debugworldwireframe`. The quad's diagonal (34, 30) is then the brush's own lit red (89, 0, 0) and its left
    /// edge (18, 30) the material's (230, 128, 128). Under 1 the diagonal is outlined too, the control that the probe lies
    /// on it. Drawn clockwise, the corners (c3, c2, c0) and (c2, c1, c0): the diagonal is c2 to c0, each triangle's.
    /// </remarks>
    [TestCase(1, 230, 128, 128)]
    [TestCase(3, 89, 0, 0)]
    public void DrawModelPose_UnderWireframeThree_OutlinesABrushEntitysPolygonWithoutItsDiagonal(int wireframe, int red, int green, int blue)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        MapAssets assets = MapAssets.ForMaterials(
            [
                Uniform4(255, 0, 0, 255),
                Uniform4(255, 255, 255, 255) with { Modulation = (0.9f, 0.5f, 0.5f, 1f), IsDecal = true },
            ],
            ["VertexLitGeneric", "Wireframe"],
            engineWireframes: new Dictionary<string, WireframeMaterial> { [MapAssets.WorldWireframePath] = new(1, IgnoresZ: true) });

        target.Wireframe = wireframe;
        Draw(target, assets, null, modelPath: "*1", frontFacing: true, faceEdges: [true, false, true, true, true, false]);
        (int R, int G, int B) diagonal = target.PixelAt(34, 30);
        (int R, int G, int B) edge = target.PixelAt(18, 30);

        TestContext.Out.WriteLine($"mat_wireframe {wireframe}: diagonal {diagonal}, edge {edge}");
        Math.Abs(diagonal.R - red).ShouldBeLessThanOrEqualTo(1, "the diagonal");
        Math.Abs(diagonal.G - green).ShouldBeLessThanOrEqualTo(1, "the diagonal");
        Math.Abs(diagonal.B - blue).ShouldBeLessThanOrEqualTo(1, "the diagonal");
        Math.Abs(edge.R - 230).ShouldBeLessThanOrEqualTo(1, "the left edge");
        Math.Abs(edge.G - 128).ShouldBeLessThanOrEqualTo(1, "the left edge");
        Math.Abs(edge.B - 128).ShouldBeLessThanOrEqualTo(1, "the left edge");
    }

    /// <remarks>
    /// **A brush entity's face seen from behind is outlined by no value** (B803 review 1, Fable): `R_DrawBrushModel`'s path
    /// `FUN_1800dcb20` tests each surface's plane against the eye, `dot − dist ≥ −0.01`, and builds the list it hands
    /// `FUN_1800dc720` from the surfaces that pass, so under 3 `FUN_1800e6780` never sees a back face; under 1 the redraw's
    /// back-face cull takes it too. The quad faces away from the camera (its normal +y, the camera at y −300, wound
    /// counter-clockwise on screen) and is drawn both-sided, so its solid shows; its left edge (18, 30) under 1 and 3 is the
    /// pixel the value 0 draws there. The front-facing quad of the test above is the control that the probe lies on the
    /// outline. Before review 1 the line list under 3 was never culled, and the edge was pink.
    ///
    /// **`$nocull` makes no exception** (B803 review 2, Fable, Sonnet): the cull's list comes from `FUN_1800df1a0`, which
    /// skips only surfaces with `0x20` in their flag byte (*interpolated* to be `SURFDRAW_TRANS`, the header being
    /// unpublished; the translucent brush path is `FUN_1800de800`), and `FUN_1800dcb20` tests every
    /// plane in it with no flag read, so a `$nocull` brush-entity face seen from behind is culled and not outlined. The
    /// world's walk is the one that keeps NOCULL (`R_DrawLeaf`, `0x200`).
    /// </remarks>
    [TestCase(1, false)]
    [TestCase(3, false)]
    [TestCase(3, true)]
    public void DrawModelPose_ABrushEntitysBackFaceUnderWireframe_IsNotOutlined(int wireframe, bool noCull)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        MapAssets assets = MapAssets.ForMaterials(
            [
                Uniform4(255, 0, 0, 255) with { IsNoCull = noCull },
                Uniform4(255, 255, 255, 255) with { Modulation = (0.9f, 0.5f, 0.5f, 1f), IsDecal = true },
            ],
            ["VertexLitGeneric", "Wireframe"],
            engineWireframes: new Dictionary<string, WireframeMaterial> { [MapAssets.WorldWireframePath] = new(1, IgnoresZ: true) });
        bool[] loop = [true, true, false, false, true, true];

        target.Wireframe = 0;
        Draw(target, assets, null, modelPath: "*1", faceEdges: loop, normalY: 1f);
        (int R, int G, int B) unlined = target.PixelAt(18, 30);

        target.Wireframe = wireframe;
        Draw(target, assets, null, modelPath: "*1", faceEdges: loop, normalY: 1f);

        TestContext.Out.WriteLine($"mat_wireframe {wireframe} on a back face: edge {target.PixelAt(18, 30)}, unlined {unlined}");
        target.PixelAt(18, 30).ShouldBe(unlined);
    }

    /// <remarks>
    /// **A brush entity like a real one: several faces to a run, front and back mixed, and a second run** (B803 review 2,
    /// Fable). Four quads side by side, each its own polygon: run one is front, back, front (vertices 0 to 17), run two
    /// one more front (18 to 23). Under 3 the three fronts are outlined and the back one is not, which needs the outline
    /// to split a run around a back face, to find a later run's first face, and to offset that run's lines. Each quad's
    /// columns are searched for `debugworldwireframe`'s (230, 128, 128).
    /// </remarks>
    [Test]
    public void DrawModelPose_ABrushEntityOfFrontAndBackFacesInTwoRuns_OutlinesOnlyItsFronts()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        List<WorldVertex> vertices =
        [
            .. BrushFace(-210f, 50f, front: true),
            .. BrushFace(-70f, 50f, front: false),
            .. BrushFace(70f, 50f, front: true),
            .. BrushFace(210f, 50f, front: true),
        ];

        target.Wireframe = 3;
        DrawBrush(target, vertices, [new WorldBatch(0, 0, 18), new WorldBatch(0, 18, 6)], Identity4);

        int[] outlined = [.. new[] { (3, 15), (19, 30), (34, 45), (49, 61) }.Select(columns => OutlinePixels(target, columns.Item1, columns.Item2))];

        TestContext.Out.WriteLine($"outline pixels per quad: {string.Join(", ", outlined)}");
        outlined.Select(count => count > 0).ShouldBe([true, false, true, true]);
    }

    /// <remarks>
    /// **The back-face test runs in the model's own space** (B803 review 2, Fable, Sonnet): a `*N` model turned a quarter
    /// about z and moved (40, 30, 0), one quad at the centre facing the camera or away from it. The front one is outlined
    /// under 3 and the back one is not. Testing against the world eye unmoved, or the placement read transposed, puts the
    /// eye on the wrong side of both planes and swaps the answers.
    /// </remarks>
    [TestCase(true, true)]
    [TestCase(false, false)]
    public void DrawModelPose_ATurnedAndMovedBrushEntityUnderWireframeThree_OutlinesItsFrontFaceOnly(bool front, bool outlined)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        float[] placement = [0f, 1f, 0f, 0f, -1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 40f, 30f, 0f, 1f];

        // Each corner moved into the model's space by the placement's inverse: x from y less 30, y from 40 less x.
        List<WorldVertex> model =
        [
            .. BrushFace(0f, 50f, front).Select(corner => corner with
            {
                X = corner.Y - 30f,
                Y = 40f - corner.X,
                NormalX = corner.NormalY,
                NormalY = -corner.NormalX,
                TangentX = corner.TangentY,
                TangentY = -corner.TangentX,
            }),
        ];

        target.Wireframe = 3;
        DrawBrush(target, model, [new WorldBatch(0, 0, 6)], placement);

        (OutlinePixels(target, 20, 44) > 0).ShouldBe(outlined);
    }

    /// <remarks>
    /// **A translucent brush face is culled by the translucent path's rule** (B803 review 3, Fable, Sonnet). `FUN_1800de800`
    /// collects a model's translucent surfaces through `FUN_1800dbdb0`, which keeps one that is NOCULL or whose plane-back
    /// flag matches the eye's side of its node's plane; `FUN_1800dcb20`, the opaque rule, reads no flag. So a translucent
    /// face seen from behind is outlined under 3 when its material is `$nocull` and not otherwise. Review 2 applied the
    /// opaque rule to it, and the `$nocull` one was not outlined.
    /// </remarks>
    [TestCase(false, false)]
    [TestCase(true, true)]
    public void DrawModelPose_ATranslucentBrushBackFaceUnderWireframeThree_IsOutlinedOnlyIfNoCull(bool noCull, bool outlined)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        target.Wireframe = 3;
        DrawBrush(
            target,
            [.. BrushFace(0f, 50f, front: false)],
            [new WorldBatch(0, 0, 6)],
            Identity4,
            Uniform4(255, 0, 0, 128) with { IsTranslucent = true, IsNoCull = noCull });

        (OutlinePixels(target, 20, 44) > 0).ShouldBe(outlined);
    }

    /// <remarks>
    /// **A brush face is culled by its own material's rule, not an override's** (B803 review 4, Fable): the engine's cull
    /// reads the surface's flags, set from its own material, and an entity's material override changes only what is bound.
    /// An opaque back face drawn with a translucent `$nocull` override is culled by the opaque rule and not outlined under
    /// 3, where the override's rule would keep it.
    /// </remarks>
    [Test]
    public void DrawModelPose_AnOverriddenBrushBackFaceUnderWireframeThree_IsCulledByItsOwnMaterial()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        target.Wireframe = 3;
        DrawBrush(target, [.. BrushFace(0f, 50f, front: false)], [new WorldBatch(0, 0, 6)], Identity4, overrideMaterial: "forced.vmt");

        OutlinePixels(target, 20, 44).ShouldBe(0);
    }

    /// <summary>A quad of a brush face at y 0 centred on x, its own polygon, facing the camera at y −300 or away from it.</summary>
    internal static WorldVertex[] BrushFace(float x, float half, bool front)
    {
        WorldVertex Corner(float dx, float z, bool edge) =>
            new(x + dx, 0f, z, 0f, 0f, 0f, 0f, 0f)
            {
                NormalX = 0f,
                NormalY = front ? -1f : 1f,
                NormalZ = 0f,
                TangentX = 1f,
                TangentW = 1f,
                FaceEdge = edge,
            };

        // Clockwise on screen for the front, as Draw's frontFacing, and counter-clockwise for the back; the diagonal unmarked.
        return front
            ?
            [
                Corner(-half, half, true), Corner(half, half, false), Corner(-half, -half, true),
                Corner(half, half, true), Corner(half, -half, true), Corner(-half, -half, false),
            ]
            :
            [
                Corner(-half, -half, true), Corner(half, -half, true), Corner(half, half, false),
                Corner(-half, -half, false), Corner(half, half, true), Corner(-half, half, true),
            ];
    }

    /// <summary>Draws <c>*1</c> both-sided, as <see cref="Draw"/> does, with a red face and <c>debugworldwireframe</c> loaded.</summary>
    internal static void DrawBrush(
        OffscreenTarget target,
        List<WorldVertex> vertices,
        WorldBatch[] batches,
        float[] placement,
        MapTexture? face = null,
        string? overrideMaterial = null)
    {
        // Material 2 is a translucent `$nocull` override, `forced.vmt`, for a draw that names it.
        MapAssets assets = MapAssets.ForMaterials(
            [
                face ?? Uniform4(255, 0, 0, 255),
                Uniform4(255, 255, 255, 255) with { Modulation = (0.9f, 0.5f, 0.5f, 1f), IsDecal = true },
                Uniform4(0, 0, 255, 128) with { IsTranslucent = true, IsNoCull = true },
            ],
            ["VertexLitGeneric", "Wireframe", "VertexLitGeneric"],
            engineWireframes: new Dictionary<string, WireframeMaterial> { [MapAssets.WorldWireframePath] = new(1, IgnoresZ: true) },
            overrideMaterials: new Dictionary<string, int> { ["forced.vmt"] = 2 });

        target.Clear(0f, 0f, 0f);
        target.DrawModelPose(
            vertices,
            batches,
            new FreeCamera { Origin = (0f, -300f, 0f), Angles = (0f, 90f, 0f), Aspect = 1f, NearZ = MathF.Min(FreeCamera.WorldNearPlane, 300f / 8f) }.ToMatrix(),
            placement,
            assets,
            light: Uniform,
            bothSides: true,
            toneMapScale: 1f,
            overrideMaterial: overrideMaterial,
            modelPath: "*1");
    }

    internal static readonly float[] Identity4 =[1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f];

    /// <summary>How many pixels in a band of columns are <c>debugworldwireframe</c>'s colour, within a level.</summary>
    private static int OutlinePixels(OffscreenTarget target, int fromColumn, int toColumn)
    {
        int count = 0;

        for (int y = 0; y < Size; y++)
        {
            for (int x = fromColumn; x <= toColumn; x++)
            {
                (int r, int g, int b) = target.PixelAt(x, y);

                if (Math.Abs(r - 230) <= 1 && Math.Abs(g - 128) <= 1 && Math.Abs(b - 128) <= 1)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <remarks>
    /// **A model's own decal keeps its material under `mat_wireframe`** (B803 review 1, Sonnet): `studiorender.dll`'s decal
    /// draw (`FUN_180007d20`) binds `debugmrmwireframe` (`+0x8a8`) in the decal's place only when the config's byte
    /// `+0x25` has `0x20`, `bWireframeDecals`, which `engine.dll`'s `UpdateStudioRenderConfig` (`FUN_1800f56c0`) sets from
    /// `r_modelwireframedecal` (the ConVar at `0x18069eec0`, constructed at `FUN_180007020`), not from `mat_wireframe`. The
    /// world decal's `debugdecalwireframe` swap does not reach a model either. A blue decal over the red model, the model
    /// not swapped (no studio wireframe material loaded), is the same pixel under 1 as under 0.
    /// </remarks>
    [Test]
    public void DrawPosedModelDecals_UnderWireframe_KeepTheirOwnMaterial()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        MapAssets assets = MapAssets.ForMaterials(
            [
                Uniform4(255, 0, 0, 255),
                Uniform4(0, 0, 255, 255) with { IsDecal = true },
                Uniform4(255, 255, 255, 255) with { Modulation = (1f, 1f, 0f, 1f), IsDecal = true },
            ],
            ["VertexLitGeneric", "VertexLitGeneric", "Wireframe"],
            engineWireframes: new Dictionary<string, WireframeMaterial> { [MapAssets.DecalWireframePath] = new(2, IgnoresZ: true) });

        (int R, int G, int B) Decalled(int wireframe)
        {
            target.Wireframe = wireframe;
            Draw(target, assets, null);
            target.DrawPosedModelDecals(DecalQuad(), [new WorldBatch(1, 0, 6)]);

            return target.PixelAt(20, 20);
        }

        (int R, int G, int B) off = Decalled(0);
        (int R, int G, int B) on = Decalled(1);

        TestContext.Out.WriteLine($"a model decal: {off} under 0, {on} under 1");
        off.B.ShouldBeGreaterThan(0, "the control: the decal draws blue over the red");
        on.ShouldBe(off);
    }

    /// <summary>The quad <see cref="Draw"/> draws, as a decal's corners over it, wound to face the camera (a decal culls).</summary>
    private static List<WorldVertex> DecalQuad()
    {
        WorldVertex Corner(float x, float z, float u, float v) =>
            new(x, 0f, z, u, v, 0f, 0f, 0f) { NormalY = -1f, NormalZ = 0f, TangentX = 1f, TangentW = 1f };

        return
        [
            Corner(-Half, Half, 0f, 1f), Corner(Half, Half, 1f, 1f), Corner(-Half, -Half, 0f, 0f),
            Corner(Half, Half, 1f, 1f), Corner(Half, -Half, 1f, 0f), Corner(-Half, -Half, 0f, 0f),
        ];
    }

    /// <remarks>
    /// **The wireframe material binds its own texture, not the swapped-out material's animation frame** (B788 review 1,
    /// Sonnet): `R_StudioSetupSkinAndLighting` replaces the material before any proxy or bind, so the frame the model's
    /// own `$basetexture` would animate to is never read. Material 0 animates red frames; the wireframe's white times
    /// `[0 1 1]` is (0, 255, 255) on the edge, and red times it would be black.
    /// </remarks>
    [Test]
    public void DrawModelPose_UnderWireframeOnAnAnimatedMaterial_BindsTheWireframesTexture()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        MapAssets assets = MapAssets.ForMaterials(
            [Uniform4(255, 0, 0, 255), Uniform4(255, 255, 255, 255) with { Modulation = (0f, 1f, 1f, 1f), IsDecal = true }],
            ["VertexLitGeneric", "Wireframe"],
            wireframe: new WireframeMaterial(1, IgnoresZ: true),
            animationFrames: [[Uniform4(255, 0, 0, 255), Uniform4(255, 0, 0, 255)], null]);

        target.Wireframe = 1;
        Draw(target, assets, null);

        target.PixelAt(34, 30).ShouldBe((0, 255, 255));
    }

    /// <remarks>The `Wireframe` shader sets `MATERIAL_VAR_NOFOG` (`wireframe_dx9.cpp:36`): fog mode 2, none (B788).</remarks>
    [Test]
    public void FogModeOf_TheWireframeShader_IsNone()
    {
        WorldRenderer.FogModeOf(Uniform4(255, 255, 255, 255), "Wireframe").ShouldBe(2f);
        WorldRenderer.FogModeOf(Uniform4(255, 255, 255, 255), "Wireframe_DX9").ShouldBe(2f, "a fallback suffix, as ModelMaterialShading reads it");
        WorldRenderer.FogModeOf(Uniform4(255, 255, 255, 255), "VertexLitGeneric").ShouldBe(0f, "the control");
    }

    /// <summary>
    /// Material 0 red (or an eye with a black iris), material 1 `debugmrmwireframe` as the VMT has it: white, `$color [0 1 1]`,
    /// `$decal 1`.
    /// </summary>
    private static MapAssets Wireframed(string shader, bool ignoresZ, bool decal = true) =>
        MapAssets.ForMaterials(
            [
                shader == "Eyes"
                    ? Uniform4(0, 0, 0, 255) with { Eye = new EyeMaterial(EyeShader.Eyes, HalfLambert: false, Iris: Iris(alpha: 0)) }
                    : Uniform4(255, 0, 0, 255),
                Uniform4(255, 255, 255, 255) with { Modulation = (0f, 1f, 1f, 1f), IsDecal = decal },
            ],
            [shader, "Wireframe"],
            wireframe: new WireframeMaterial(1, ignoresZ));

    /// <remarks>
    /// **An eye with no glint takes `vgui/black`, not the last eye's target** (`0x18001f55a`): the eye of the test above
    /// drawn with a lamp at the camera (195), then again without one, is 0 — the target, left as the first draw filled it,
    /// is not bound.
    /// </remarks>
    [Test]
    public void DrawModelPose_EyesWithoutAGlintAfterOneWith_TakeBlack()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        EyeShading eye = new(
            (0f, 1200f, 0f), (0f, 0f, 1f), (1f / 1024f, 0f, 0f, 0.875f), (0f, -1f, 0f, 0.5f), (0f, -1f, 0f), 600f);

        Draw(target, Eyes(irisAlpha: 0), eye, locals: [Lamp(0f, -300f, 0f)]);
        (int R, int G, int B) first = target.PixelAt(34, 30);

        Draw(target, Eyes(irisAlpha: 0), eye, locals: []);
        (int R, int G, int B) second = target.PixelAt(34, 30);

        TestContext.Out.WriteLine($"eyes glint then none: {first}, then {second}");
        Math.Abs(first.R - 195).ShouldBeLessThanOrEqualTo(1, "the control: the first draw has its glint");
        second.ShouldBe((0, 0, 0), "vgui/black");
    }

    /// <remarks>
    /// **The damping is `lum · SimpleSpline( lum / 0.01 )` below 0.01 and 1 above** (`eyes_dx8_dx9_helper.cpp:445-453`).
    /// Two lamps at the camera put two glints on texel (16, 16), 139 + 139, saturated to 255, so the pixel is the damping
    /// itself (linear, encoded):
    /// - Luminance 0.008: `SimpleSpline( 0.8 )` = 0.896, damping 0.007168, 20 (20.35) — where `lum · x` would give 19
    ///   (18.77) and `lum` alone 22; exact, since the spline is the only thing the row tells apart.
    /// - 0.0101, just above the threshold: 1, so 255.
    /// - 0.0099, just below: `0.0099 · 0.9997`, 25.
    /// </remarks>
    [TestCase(0.008f, 20, 0)]
    [TestCase(0.0101f, 255, 1)]
    [TestCase(0.0099f, 25, 1)]
    public void DrawModelPose_EyesWithASaturatedGlint_AreDampedBySimpleSplineUnderOneHundredth(float cube, int expected, int tolerance)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        EyeShading eye = new(
            (0f, 1200f, 0f), (0f, 0f, 1f), (1f / 1024f, 0f, 0f, 0.875f), (0f, -1f, 0f, 0.5f), (0f, -1f, 0f), 600f);
        AmbientCube dim = new((cube, cube, cube), (cube, cube, cube), (cube, cube, cube), (cube, cube, cube), (cube, cube, cube), (cube, cube, cube));

        Draw(target, Eyes(irisAlpha: 0), eye, locals: [Lamp(0f, -300f, 0f), Lamp(0f, -300f, 0f)], cube: dim);

        (int R, int G, int B) texel = target.PixelAt(34, 30);

        TestContext.Out.WriteLine($"eyes saturated glint, luminance {cube}: {texel}, predicted {expected}");
        Math.Abs(texel.R - expected).ShouldBeLessThanOrEqualTo(tolerance, "the saturated glint times the damping");
    }

    /// <remarks>
    /// **The glint's lights are the face shaders' four, the sun first** (`EyeLightCount`), each as the engine builds its
    /// `LightDesc_t` (`engine.dll` `0x1800f2010`): the sun `MATERIAL_LIGHT_DIRECTIONAL` with no terms of its own, which
    /// the all-zero rule makes `a0 = 1` (`0x1800f2186`-`0x1800f2196`), and no range that cuts (0x1800f22cd, 1.8e19); a
    /// lamp with its own terms, range and kind. A sun and five lamps keep the sun and three.
    /// </remarks>
    [Test]
    public void GlintLights_ASunAndFiveLamps_AreTheSunThenThreeLamps()
    {
        Span<GlintLight> lights = stackalloc GlintLight[4];
        LocalLight lamp = new(1f, 2f, 3f, 4f, 5f, 6f, 0f, 0.5f, 0.25f, 300f);
        LocalLight panel = lamp with { Directional = true, Direction = (0f, 0f, -1f) };

        int count = WorldRenderer.GlintLights(new SunLight(7f, 8f, 9f, 0f, 1f, 0f), [lamp, panel, lamp, lamp, lamp], lights);

        count.ShouldBe(4);
        lights[0].ShouldBe(new GlintLight(true, default, (0f, 1f, 0f), (7f, 8f, 9f), 0f, 1f, 0f, 0f));
        lights[1].ShouldBe(new GlintLight(false, (1f, 2f, 3f), lamp.Direction, (4f, 5f, 6f), 300f, 0f, 0.5f, 0.25f));
        lights[2].Directional.ShouldBeTrue();
        lights[2].Direction.ShouldBe((0f, 0f, -1f));
    }

    /// <remarks>
    /// **A colour mesh never reaches a face shader** (B725 open 5a). All three vertex shaders hand `DoLighting` a static
    /// colour of literal zero and read no colour stream (`eye_refract_vs20.fxc:157`, `Eyes_vs20.fxc:128-130`,
    /// `teeth_vs20.fxc:109-111`), so its static half is `GammaToLinear( 0 · cOverbright )` = 0 whatever `STATIC_LIGHT`
    /// says (`common_vs_fxc.h:868-875`, :900-903). A baked static prop's colour mesh of 0.5 on every vertex, which the
    /// generic draw ADDS to the cube and lamps (B424), leaves each face pixel as it is without one: EyeRefract under a lamp
    /// above 134, Teeth 0.55 (both predicted by the cases above); added, they would be 0.5 brighter.
    /// </remarks>
    [TestCase(false)]
    [TestCase(true)]
    public void DrawModelPose_AFaceShaderOverAColourMesh_TakesNoStaticLight(bool teeth)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        float[] baked = [.. Enumerable.Repeat(0.5f, 18)];

        if (teeth)
        {
            Draw(target, Teeth(halfLambert: false), null, locals: [Lamp(0f, -Far, 0f)], baked: baked);
        }
        else
        {
            Draw(target, EyeRefract(), Below, locals: [Lamp(0f, 0f, Far)], baked: baked);
        }

        int expected = teeth ? Pixel((Cube + 1f) * 0.5f) : Pixel(Cube + 1f);
        int added = teeth ? Pixel(((Cube + 1f) * 0.5f) + 0.5f) : Pixel(Cube + 1f + 0.5f);
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"teeth {teeth} over a colour mesh: centre {centre}, predicted {expected}, with the mesh added {added}");

        (added - expected).ShouldBeGreaterThan(1, "the instrument: an added colour mesh is told apart");
        Near(centre, expected, "the face's DoLighting is handed zero for its static colour");
    }

    /// <remarks>
    /// **A refraction view's height fog reaches the faces** (B725 open 5b). Each face pixel shader ends in
    /// `FinalOutput( result, CalcPixelFogFactor( PIXELFOGTYPE, … ), PIXELFOGTYPE, TONEMAP_SCALE_LINEAR )`
    /// (`eye_refract_ps2x.fxc:450-451`, `eyes_ps2x.fxc:66-67`, `teeth_ps2x.fxc:46-47`), the combo from
    /// `GetPixelFogCombo1( true )` (`eye_refract_helper.cpp:367`), so under a water volume's fog it is
    /// `lerp( colour, g_LinearFogColor, saturate( CalcWaterFogAlpha ) )` (`common_ps_fxc.h:213-230`, :260-263, :295-298).
    /// The water's surface at the eye's z (0) and a range of one unit: below the eye, `f = (0 − z) / (0 − z)` = 1 and
    /// `projZ · 1` saturates, so the factor is 1 and a pixel below the centre is the fog colour's own bytes (the sRGB
    /// decode in, the encode out, B653). Without the fog it is the lit iris, far from them.
    /// </remarks>
    [TestCase(EyeShader.EyeRefract)]
    [TestCase(EyeShader.Eyes)]
    [TestCase(EyeShader.Teeth)]
    public void DrawModelPose_AFaceUnderARefractionViewsHeightFog_TakesTheFogColour(EyeShader shader)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        VolumeFog fog = new(
            HeightFog: true, FogZ: 0f, Color: (10f / 255f, 26f / 255f, 58f / 255f), Start: 0f, End: 1f, MaxDensity: 1f);
        IReadOnlyList<LocalLight> lamp = [Lamp(0f, -Far, 0f)];

        switch (shader)
        {
            case EyeShader.Teeth:
                Draw(target, Teeth(halfLambert: false), null, locals: lamp, heightFog: fog);
                break;
            case EyeShader.Eyes:
                Draw(target, Eyes(), new((0f, Far, -Far), (0f, 0f, 1f), (1f / 1024f, 0f, 0f, 0.875f), (0f, -1f, 0f, 0.5f), (0f, -1f, 0f), 0.5f), locals: lamp, heightFog: fog);
                break;
            default:
                Draw(target, EyeRefract(), Below, locals: lamp, heightFog: fog);
                break;
        }

        // Eight rows down: about 57 units below the eye, inside the quad (which ends near row 46 at a 75° view).
        (int R, int G, int B) below = target.PixelAt(Centre, Centre + 8);

        TestContext.Out.WriteLine($"{shader} under full height fog: below the centre {below}, the fog (10, 26, 58)");

        below.ShouldBe((10, 26, 58), "full height fog: the volume's colour, decoded and encoded back");
    }

    /// <remarks>
    /// **A Teeth material with a `$bumpmap` lights per pixel** (B725 open 4, `teeth_bump`): `teeth.cpp:88`, `:101-137`,
    /// `:228-257` choose `teeth_bump_vs20`/`teeth_bump_ps20b`, whose pixel shader takes the normal from the bump map in the
    /// tangent frame (`teeth_bump_ps2x.fxc:65-67`) and lights it with `PixelShaderDoLighting( …, bHalfLambert = true )`
    /// (:74-78, `common_vertexlitgeneric_dx9.h:86-136`, :259-319) — half-Lambert, where plain Teeth passes false — then
    /// `( specular · base.a + base.rgb · diffuse ) · darkening` (:93), darkening `$illumfactor · saturate( N · $forward )`
    /// on the vertex normal (`teeth_bump_vs20.fxc:117`). Base grey 128 with alpha 0 (no specular); `$forward` (0, −1, 0),
    /// `$illumfactor` 1, so the darkening is 1.
    /// - A flat bump (128, 128, 255) and a lamp level with the surface (N · L = 0): `0.1 + (0 · 0.5 + 0.5)²` = 0.35; plain
    ///   Teeth would light 0.1.
    /// - A bump tilted to the tangent (255, 128, 128) and a lamp along +x (the tangent): the bumped normal faces it,
    ///   `0.1 + 1²`; the vertex normal alone would give 0.35.
    /// </remarks>
    [TestCase(false)]
    [TestCase(true)]
    public void DrawModelPose_TeethWithABumpMap_LightsTheBumpedNormalHalfLambert(bool tilted)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        MapTexture bump = tilted ? Uniform4(255, 128, 128, 255) : Uniform4(128, 128, 255, 255);

        Draw(target, TeethBump(bump, baseAlpha: 0), null, locals: [Lamp(Far, 0f, 0f)], mouth: new MouthShading((0f, -1f, 0f), 1f));

        // The flat texel is (0.0039, 0.0039, 1) before normalising: within the ±1 the pixel allows.
        int expected = Pixel(Cube + (tilted ? 1f : 0.25f));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"teeth bump, tilted {tilted}: centre {centre}, predicted {expected}, plain teeth {Pixel(Cube)}");
        Near(centre, expected, "PixelShaderDoLighting on the bumped normal, half-Lambert");
    }

    /// <remarks>
    /// **The specular is the base alpha's** (`teeth_bump_ps2x.fxc:85-93`): `PixelShaderDoSpecularLighting` with
    /// `$phongexponent` (`teeth.cpp:247`, declared default 100), `R = 2N(N · E) − E`, `pow( saturate( R · L ), k ) ·
    /// saturate( N · L ) · colour · atten` (`common_vertexlitgeneric_dx9.h:167-198`). A lamp of 0.5 at the camera on the flat
    /// bump: R · L = 1, so the specular is the lamp's 0.5; with base alpha 255 the pixel is `0.5 + 0.2158 · (0.1 + 0.5)`,
    /// with alpha 0 `0.2158 · 0.6`.
    /// </remarks>
    [TestCase(255)]
    [TestCase(0)]
    public void DrawModelPose_TeethWithABumpMap_AddTheSpecularByTheBaseAlpha(int baseAlpha)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(
            target, TeethBump(Uniform4(128, 128, 255, 255), (byte)baseAlpha), null,
            locals: [new LocalLight(0f, -Far, 0f, 0.5f, 0.5f, 0.5f, 1f, 0f, 0f, 0f)],
            mouth: new MouthShading((0f, -1f, 0f), 1f));

        float specular = 0.5f * baseAlpha / 255f;
        int expected = Encode(specular + (LinearGrey * (Cube + 0.5f)));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"teeth bump, base alpha {baseAlpha}: centre {centre}, predicted {expected}");
        Near(centre, expected, "specular · base.a + base · diffuse");
    }

    /// <remarks>
    /// **`$phongexponent` is the specular's, or the bump's alpha's where it is 0** (`teeth_bump_ps2x.fxc:69-71`,
    /// `teeth.cpp:247`). A lamp of 0.5 off the view, at (0, −1e5, 20306) — 11.5° up, N · L = 0.9808 on the flat bump
    /// (whose texel 128 tilts the normal 0.0039 along the tangent and the binormal), R · L = 0.9815 — with base alpha 255:
    /// `0.9815^k · 0.9808 · 0.5 + 0.2158 · (0.1 + 0.5 · (0.9808 · 0.5 + 0.5)²)`: 124 at k = 100, 171 at k = 30; at
    /// `$phongexponent` 0 with a bump alpha of 128, `k = lerp( 1, 150, 0.502 )` = 75.8, 136. Read from `$glossiness`'s slot
    /// as it was before teeth_bump took it, k would be 1: 205.
    /// </remarks>
    [TestCase(100f, 255, 124)]
    [TestCase(30f, 255, 171)]
    [TestCase(0f, 128, 136)]
    public void DrawModelPose_TeethWithABumpMapAndALampOffTheView_TakesTheSpecularExponent(float exponent, int bumpAlpha, int expected)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(
            target, TeethBump(Uniform4(128, 128, 255, (byte)bumpAlpha), 255, exponent), null,
            locals: [new LocalLight(0f, -Far, 20306f, 0.5f, 0.5f, 0.5f, 1f, 0f, 0f, 0f)],
            mouth: new MouthShading((0f, -1f, 0f), 1f));

        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"teeth bump, $phongexponent {exponent}, bump alpha {bumpAlpha}: centre {centre}, predicted {expected}");
        Near(centre, expected, "pow( R · L, k ) · N · L · colour · base.a + base · diffuse");
    }

    /// <remarks>
    /// **The darkening on the bump path** (`teeth_bump_vs20.fxc:117`): `$illumfactor · saturate( N · $forward )` on the vertex
    /// normal, here the draw's mouth: forward (0, −0.6, 0.8) against N (0, −1, 0) at illum 0.5 is 0.3. The flat bump, base
    /// alpha 0, a lamp along the tangent: `(0.1 + 0.502²) · 0.2158 · 0.3` = 42; undarkened 78.
    /// </remarks>
    [Test]
    public void DrawModelPose_TeethWithABumpMapAndADarkeningMouth_IsDarkenedByItsForwardAndIllum()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(
            target, TeethBump(Uniform4(128, 128, 255, 255), baseAlpha: 0), null, locals: [Lamp(Far, 0f, 0f)],
            mouth: new MouthShading((0f, -0.6f, 0.8f), 0.5f));

        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"teeth bump darkened 0.3: centre {centre}, predicted 42, undarkened 78");
        Near(centre, 42, "the lit bump times illumfactor · saturate( N · forward )");
    }

    /// <remarks>
    /// **The binormal is `(N × T) · w`** (`teeth_bump_vs20.fxc`, the skinning's `:740`): N (0, −1, 0) and T (1, 0, 0) make
    /// it +z, or −z where the tangent's w is −1. A bump tilted along it (128, 255, 128) and a lamp above (+z): with w +1 the
    /// bumped normal faces the lamp, `0.1 + 1²` = 134; with w −1 it faces away, `0.1 + 0²` = 40.
    /// </remarks>
    [TestCase(1f, 134)]
    [TestCase(-1f, 40)]
    public void DrawModelPose_TeethWithABumpAlongTheBinormal_TakesTheTangentsSign(float tangentW, int expected)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(
            target, TeethBump(Uniform4(128, 255, 128, 255), baseAlpha: 0), null, locals: [Lamp(0f, 0f, Far)],
            mouth: new MouthShading((0f, -1f, 0f), 1f), tangentW: tangentW);

        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"teeth bump along the binormal, w {tangentW}: centre {centre}, predicted {expected}");
        Near(centre, expected, "the bump's green along (N × T) · w");
    }

    /// <remarks>
    /// **Every light slot reaches the bump path**: four lamps along ±x and ±z on the flat bump, each `(±0.0039 · 0.5 +
    /// 0.5)²`, sum to 1: `0.2158 · 1.1` = 134. Without the fourth slot, 119.
    /// </remarks>
    [Test]
    public void DrawModelPose_TeethWithABumpMapUnderFourLamps_AddsEachSlot()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(
            target, TeethBump(Uniform4(128, 128, 255, 255), baseAlpha: 0), null,
            locals: [Lamp(Far, 0f, 0f), Lamp(0f, 0f, Far), Lamp(-Far, 0f, 0f), Lamp(0f, 0f, -Far)],
            mouth: new MouthShading((0f, -1f, 0f), 1f));

        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"teeth bump under four lamps: centre {centre}, predicted 134, three 119");
        Near(centre, 134, "the four slots' half-Lambert terms");
    }

    /// <summary>A Teeth material with a `$bumpmap`, flagged for the mouth, `$forward` (0, −1, 0), `$illumfactor` 1.</summary>
    private static MapAssets TeethBump(MapTexture bump, byte baseAlpha, float phongExponent = EyeMaterial.DefaultPhongExponent) =>
        MapAssets.ForMaterials(
            [Uniform4(128, 128, 128, baseAlpha) with
            {
                Eye = new EyeMaterial(
                    EyeShader.Teeth, HalfLambert: false, Forward: (0f, -1f, 0f), IllumFactor: 1f, MouthShader: true, BumpMap: bump,
                    PhongExponent: phongExponent),
            }],
            ["Teeth"]);

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

    /// <summary>A Teeth material, flagged <c>$clientShader MouthShader</c> as every shipped one is unless said otherwise.</summary>
    private static MapAssets Teeth(bool halfLambert, bool mouthShader = true) => MapAssets.ForMaterials(
        [Uniform4(128, 128, 128, 255) with
        {
            IsHalfLambert = halfLambert,
            Eye = new EyeMaterial(EyeShader.Teeth, HalfLambert: false, Forward: (0f, -1f, 0f), IllumFactor: 0.5f, MouthShader: mouthShader),
        }],
        ["Teeth"]);

    private static MapAssets Eyes(byte irisAlpha = 255) => MapAssets.ForMaterials(
        [Uniform4(0, 0, 0, 255) with { Eye = new EyeMaterial(EyeShader.Eyes, HalfLambert: false, Iris: Iris(alpha: irisAlpha)) }],
        ["Eyes"]);

    private static void Draw(
        OffscreenTarget target,
        MapAssets assets,
        EyeShading? eye,
        IReadOnlyList<LocalLight>? locals = null,
        MouthShading? mouth = null,
        float[]? baked = null,
        VolumeFog? heightFog = null,
        AmbientCube? cube = null,
        float tangentW = 1f,
        bool clear = true,
        string? overrideMaterial = null,
        string modelPath = "offscreen/posed.mdl",
        Tf2DemoSalvage.Core.Scene.SceneFog? fog = null,
        bool frontFacing = false,
        bool[]? faceEdges = null,
        float normalY = -1f)
    {
        WorldVertex Corner(float x, float z, float u, float v) =>
            new(x, 0f, z, u, v, 0f, 0f, 0f)
            {
                NormalX = 0f,
                NormalY = normalY,
                NormalZ = 0f,
                TangentX = 1f,
                TangentY = 0f,
                TangentZ = 0f,
                TangentW = tangentW,
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

        // Wound clockwise on screen, the engine's front (`MATERIAL_CULLMODE_CCW`), for a pass that culls back faces (B803);
        // the default quad is counter-clockwise from the camera and shows only because the draw is both-sided.
        if (frontFacing)
        {
            vertices.Reverse();
        }

        // Each corner's mark, in the order drawn, for a quad that is one polygon (B803).
        for (int at = 0; faceEdges is not null && at < vertices.Count; at++)
        {
            vertices[at] = vertices[at] with { FaceEdge = faceEdges[at] };
        }

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

        if (clear)
        {
            target.Clear(0f, 0f, 0f);
        }

        target.DrawModelPose(
            vertices,
            [new WorldBatch(0, 0, vertices.Count, Eyeball: eye is null ? -1 : 0)],
            camera,
            identity,
            assets,
            light: cube ?? Uniform,
            bothSides: true,
            locals: locals,
            eyes: eye is { } shading ? [shading] : null,
            mouth: mouth,
            bakedColours: baked,
            toneMapScale: 1f,
            heightFog: heightFog,
            clearDepth: clear,
            overrideMaterial: overrideMaterial,
            modelPath: modelPath,
            fog: fog);
    }

    private static void Near((int R, int G, int B) actual, int expected, string because)
    {
        Math.Abs(actual.R - expected).ShouldBeLessThanOrEqualTo(1, because);
        Math.Abs(actual.G - expected).ShouldBeLessThanOrEqualTo(1, because);
        Math.Abs(actual.B - expected).ShouldBeLessThanOrEqualTo(1, because);
    }
}
