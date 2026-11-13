using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A model's <c>$envmapmask</c> and <c>mat_specular 0</c>, per material and per setting, drawn on a real device from
/// hand-built materials (B720 part 5).
/// </summary>
/// <remarks>
/// **What the engine does.** The non-bump VertexLitGeneric shader multiplies the cube sample by the mask's colour before
/// the tint, `specularFactor *= envmapMaskTexel.xyz` (`vertexlit_and_unlit_generic_ps2x.fxc:333-337`, `:457`; TF2's shipped
/// `vertexlit_and_unlit_generic_ps20b`: `texld r2, t0, s4` then `mul r0.xyz, r2, r0` before `mul r0.xyz, r0, c0`), alpha
/// unread. `mat_specular 0` undefines `$envmap` in two params passes only, beside a base texture
/// (`vertexlitgeneric_dx9_helper.cpp:214-218`, `lightmappedgeneric_dx9_helper.cpp:166-170`), so a material whose pass
/// keeps it still reflects. Which materials those are is `SpecularAndEnvMapMaskConformanceTests`' question; this is the bind's.
///
/// **Synthetic** (D38): uniform cube faces and an ambient-only light; each prediction is an equality with a control
/// drawn with the reflection whole or with none.
/// </remarks>
public sealed class SpecularAndEnvMapMaskRenderTests
{
    private static readonly MapEnvmapShading Plain =
        new((1f, 1f, 1f), 0f, 1f, MaskedByBaseAlpha: false, 1f, MaskedByNormalMapAlpha: false);

    [Test]
    public void DrawModelPose_EnvMapMaskTexture_MultipliesTheReflectionByItsColour()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        // Red, and alpha 0: the mask's alpha is not read.
        MapEnvmapShading masked = Plain with { MaskTexture = Texture(255, 0, 0, 0), MaskedByMaskTexture = true };

        (int R, int G, int B) none = Draw(target, Material(null), specular: true);
        (int R, int G, int B) whole = Draw(target, Material(Plain), specular: true);
        (int R, int G, int B) red = Draw(target, Material(masked), specular: true);

        TestContext.Out.WriteLine($"none {none}, whole {whole}, red mask {red}");

        whole.G.ShouldBeGreaterThan(none.G + 8, "the control: the reflection adds light");
        red.ShouldBe((whole.R, none.G, none.B), "the red channel passes, green and blue are masked");
    }

    /// <remarks>
    /// **`$selfillum_envmapmask_alpha` lights the surface from the mask's alpha** (B720): `a = 8 * mask.a`,
    /// `diffuse = max( 0, 1 - a ) * diffuse + a * selfIllumTint * albedo` (`vertexlit_and_unlit_generic_ps2x.fxc:431-437`).
    /// The mask's colour is black so the reflection adds nothing and the pixel is the diffuse term alone. Alpha 0 is no glow:
    /// the same as the parameter off. Alpha 255 is `a = 8`: eight times the albedo, past white. Without the parameter the
    /// alpha is read by nothing.
    /// </remarks>
    [Test]
    public void DrawModelPose_SelfIllumFromTheMasksAlpha_LightsByEightTimesIt()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        MapEnvmapShading Masked(byte alpha, bool glow) =>
            Plain with { MaskTexture = Texture(0, 0, 0, alpha), MaskedByMaskTexture = true, SelfIllumFromMaskAlpha = glow };

        (int R, int G, int B) off = Draw(target, Material(Masked(255, glow: false)), specular: true);
        (int R, int G, int B) none = Draw(target, Material(Masked(0, glow: true)), specular: true);
        (int R, int G, int B) full = Draw(target, Material(Masked(255, glow: true)), specular: true);

        TestContext.Out.WriteLine($"parameter off, alpha 255 {off}; on, alpha 0 {none}; on, alpha 255 {full}");

        off.G.ShouldBeLessThan(200, "the control: the diffuse term alone is well below white");
        none.ShouldBe(off, "alpha 0 is no glow");
        full.ShouldBe((255, 255, 255), "eight times the albedo is past white");
    }

    /// <summary>A loaded mask the setting does not sample masks nothing.</summary>
    [Test]
    public void DrawModelPose_EnvMapMaskTextureNotSampled_LeavesTheReflectionWhole()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        MapEnvmapShading unsampled = Plain with { MaskTexture = Texture(0, 0, 0, 255) };

        (int R, int G, int B) none = Draw(target, Material(null), specular: true);
        (int R, int G, int B) whole = Draw(target, Material(Plain), specular: true);
        (int R, int G, int B) drawn = Draw(target, Material(unsampled), specular: true);

        whole.G.ShouldBeGreaterThan(none.G + 8, "the control: the reflection adds light");
        drawn.ShouldBe(whole, "a black mask nothing samples");
    }

    /// <remarks>
    /// A `$phong` + `$basemapalphaphongmask 1` material with no bump map: the skin init loads no mask while `mat_phong` is on;
    /// under `mat_phong 0` the generic init loads it and the non-bump shader samples it (`SpecularAndEnvMapMaskConformanceTests`).
    /// </remarks>
    [Test]
    public void DrawModelPose_SkinMaterialSamplingItsMaskUnderPhongOff_MasksThenOnly()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        // Base alpha 255, so the skin's base-alpha mask passes the whole reflection; the mask is black.
        MapEnvmapShading skin = Plain with
        {
            SkinMask = EnvMapMask.BaseAlpha,
            MaskTexture = Texture(0, 0, 0, 255),
            Generic = new MapGenericReflection((1f, 1f, 1f), 0f, 1f, BlackBase: false, MaskedByMaskTexture: true),
            GenericWithBumpMapOff = new MapGenericReflection((1f, 1f, 1f), 0f, 1f, BlackBase: false),
            GenericWithBothOff = new MapGenericReflection((1f, 1f, 1f), 0f, 1f, BlackBase: false, MaskedByMaskTexture: true),
        };

        (int R, int G, int B) none = Draw(target, Material(null), specular: true);
        (int R, int G, int B) whole = Draw(target, Material(Plain), specular: true);
        (int R, int G, int B) phongOff = Draw(target, Material(skin, skinShader: true), specular: true, phong: false);
        (int R, int G, int B) bumpOff = Draw(target, Material(skin, skinShader: true), specular: true, bumpMap: false);

        TestContext.Out.WriteLine($"none {none}, whole {whole}; mat_phong 0 {phongOff}, mat_bumpmap 0 {bumpOff}");

        whole.G.ShouldBeGreaterThan(none.G + 8, "the control");
        phongOff.ShouldBe(none, "mat_phong 0: the black mask hides the reflection");
        bumpOff.ShouldBe(whole, "mat_bumpmap 0: the skin init loaded no mask");
    }

    [Test]
    public void DrawModelPose_WithSpecularOff_DropsOnlyAnEnvmapItsParamsPassUndefines()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        (int R, int G, int B) none = Draw(target, Material(null), specular: false);
        (int R, int G, int B) whole = Draw(target, Material(Plain), specular: true);
        (int R, int G, int B) undefined = Draw(target, Material(Plain with { UndefinedWithoutSpecular = true }), specular: false);
        (int R, int G, int B) kept = Draw(target, Material(Plain), specular: false);

        TestContext.Out.WriteLine($"none {none}, whole {whole}; mat_specular 0: undefined {undefined}, kept {kept}");

        whole.G.ShouldBeGreaterThan(none.G + 8, "the control");
        undefined.ShouldBe(none, "the params pass undefined the envmap");
        kept.ShouldBe(whole, "a params pass that tests no UseSpecular() keeps it");
    }

    /// <remarks>
    /// Under `mat_specular 0` a `$basemapalphaphongmask 1` skin material keeps its envmap while `mat_phong` is on (the skin
    /// params pass) and loses it under `mat_phong 0` (the generic one); the bind takes that setting's answer.
    /// </remarks>
    [Test]
    public void DrawModelPose_SkinMaterialWithSpecularOff_TakesThatSettingsParamsPass()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        MapEnvmapShading skin = Plain with
        {
            SkinMask = EnvMapMask.BaseAlpha,
            Generic = new MapGenericReflection((1f, 1f, 1f), 0f, 1f, BlackBase: false, UndefinedWithoutSpecular: true),
            GenericWithBumpMapOff = new MapGenericReflection((1f, 1f, 1f), 0f, 1f, BlackBase: false),
            GenericWithBothOff = new MapGenericReflection((1f, 1f, 1f), 0f, 1f, BlackBase: false, UndefinedWithoutSpecular: true),
        };

        (int R, int G, int B) none = Draw(target, Material(null), specular: false);
        (int R, int G, int B) skinOn = Draw(target, Material(skin, skinShader: true), specular: true);
        (int R, int G, int B) whole = Draw(target, Material(Plain), specular: true);
        (int R, int G, int B) skinOff = Draw(target, Material(skin, skinShader: true), specular: false);
        (int R, int G, int B) phongOff = Draw(target, Material(skin, skinShader: true), specular: false, phong: false);
        (int R, int G, int B) bumpOff = Draw(target, Material(skin, skinShader: true), specular: false, bumpMap: false);

        TestContext.Out.WriteLine($"none {none}, skin {skinOn}, whole {whole}; mat_specular 0: skin {skinOff}, mat_phong 0 {phongOff}, mat_bumpmap 0 {bumpOff}");

        skinOn.G.ShouldBeGreaterThan(none.G + 8, "the control");
        skinOff.ShouldBe(skinOn, "the skin params pass keeps the envmap");
        phongOff.ShouldBe(none, "mat_phong 0: the generic params pass undefines it");
        bumpOff.ShouldBe(whole, "mat_bumpmap 0, mat_phong 1: the skin params pass again, drawn the generic way");
    }

    /// <remarks>
    /// **Read raw, not through the sRGB curve** (`vertexlitgeneric_dx9_helper.cpp:620` enables sampler 4 with no sRGB
    /// read). A 128 texel then multiplies by 128/255, exactly as an envmap tint of 128/255 does (the mask comes before the
    /// tint, and contrast 0 and saturation 1 are linear), so the masked draw must equal the tinted one; read as sRGB it
    /// would be 0.216, a tint control of its own that must differ.
    /// </remarks>
    [Test]
    public void DrawModelPose_AMidGreyEnvMapMask_MultipliesByItsRawValue()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        const float raw = 128f / 255f;
        float curved = MathF.Pow((raw + 0.055f) / 1.055f, 2.4f);

        MapEnvmapShading masked = Plain with { MaskTexture = Texture(128, 128, 128, 255), MaskedByMaskTexture = true };

        (int R, int G, int B) drawn = Draw(target, Material(masked), specular: true);
        (int R, int G, int B) rawTint = Draw(target, Material(Plain with { Tint = (raw, raw, raw) }), specular: true);
        (int R, int G, int B) curvedTint = Draw(target, Material(Plain with { Tint = (curved, curved, curved) }), specular: true);

        TestContext.Out.WriteLine($"mask 128 {drawn}; tint 128/255 {rawTint}; tint sRGB(128) {curvedTint}");

        rawTint.G.ShouldBeGreaterThan(curvedTint.G + 8, "the control: the two readings are told apart");
        drawn.ShouldBe(rawTint);
    }

    /// <remarks>
    /// The lightmapped params pass undefines the envmap under `mat_specular 0` beside a base texture
    /// (`lightmappedgeneric_dx9_helper.cpp:166-170`); a world face takes the same bind as a model. A baked cube, a
    /// LightmappedGeneric material, through `DrawWorld`.
    /// </remarks>
    [Test]
    public void DrawWorld_WithSpecularOff_DropsOnlyAnEnvmapItsParamsPassUndefines()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        (int R, int G, int B) none = DrawFace(target, null, specular: false);
        (int R, int G, int B) whole = DrawFace(target, Plain, specular: true);
        (int R, int G, int B) undefined = DrawFace(target, Plain with { UndefinedWithoutSpecular = true }, specular: false);
        (int R, int G, int B) kept = DrawFace(target, Plain, specular: false);

        TestContext.Out.WriteLine($"world face: none {none}, whole {whole}; mat_specular 0: undefined {undefined}, kept {kept}");

        whole.G.ShouldBeGreaterThan(none.G + 8, "the control");
        undefined.ShouldBe(none);
        kept.ShouldBe(whole);
    }

    /// <remarks>
    /// **A world face's `$envmapmask` is read at its own coordinate** (B720, `lightmappedgeneric_vs20.fxc:196-205`): the
    /// incoming coordinate through `$envmapmasktransform`, which the base's transform does not move. The face's coordinate is
    /// u 0.25; the base transform moves the base to 0.75; the mask is red over its left half and green over its right. Read
    /// at its own coordinate (presence 3, the second set at identity) the mask passes red; read at the base's it would pass
    /// green. The unmasked reflection is the control.
    /// </remarks>
    [Test]
    public void DrawWorld_AnEnvMapMask_IsReadAtItsOwnCoordinateNotTheBases()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        byte[] halves = [.. Enumerable.Range(0, 16).SelectMany(static texel => texel % 4 < 2 ? new byte[] { 255, 0, 0, 255 } : [0, 255, 0, 255])];
        MapEnvmapShading masked = Plain with
        {
            MaskTexture = new MapTexture(4, 4, 4, 4, TextureImage.Rgba(halves), IsTransparent: false),
            MaskedByMaskTexture = true,
            MaskAtItsOwnCoordinate = true,
        };
        TextureTransform shift = new((1f, 0f, 0f, 0.5f), (0f, 1f, 0f, 0f));

        (int R, int G, int B) none = DrawFace(target, null, specular: true, u: 0.25f, baseTransform: shift);
        (int R, int G, int B) whole = DrawFace(target, Plain, specular: true, u: 0.25f, baseTransform: shift);
        (int R, int G, int B) drawn = DrawFace(target, masked, specular: true, u: 0.25f, baseTransform: shift);

        TestContext.Out.WriteLine($"world face, mask at u 0.25, base at 0.75: none {none}, whole {whole}, masked {drawn}");

        whole.G.ShouldBeGreaterThan(none.G + 8, "the control: the reflection adds light");
        drawn.ShouldBe((whole.R, none.G, none.B), "the mask's own coordinate is its red half");
    }

    /// <summary>One LightmappedGeneric face toward the camera, white lightmap, centre pixel read.</summary>
    private static (int R, int G, int B) DrawFace(
        OffscreenTarget target, MapEnvmapShading? shading, bool specular, float u = 0.5f, TextureTransform? baseTransform = null)
    {
        WorldVertex Corner(float x, float z) =>
            new(x, 0f, z, u, 0.5f, 0f, 0f, 0f) { NormalX = 0f, NormalY = -1f, NormalZ = 0f };

        byte[] face = [.. Enumerable.Range(0, 16).SelectMany(static _ => new byte[] { 100, 100, 100, 255 })];
        MapTexture[] faces = [.. Enumerable.Range(0, 6).Select(_ => new MapTexture(4, 4, 4, 4, TextureImage.Rgba(face), IsTransparent: false))];
        byte[] grey = [.. Enumerable.Range(0, 16).SelectMany(static _ => new byte[] { 64, 64, 64, 255 })];

        MapAssets assets = MapAssets.ForMaterials(
            [new MapTexture(4, 4, 4, 4, TextureImage.Rgba(grey), IsTransparent: false) { BaseTransform = baseTransform }],
            ["LightmappedGeneric"],
            cubemaps: [shading is { } value ? new MapCubemap(faces, value) : null]);

        List<WorldVertex> vertices = [Corner(-256f, -256f), Corner(256f, -256f), Corner(256f, 256f), Corner(-256f, -256f), Corner(256f, 256f), Corner(-256f, 256f)];
        float[] camera = new FreeCamera { Origin = (0f, -600f, 0f), Angles = (0f, 90f, 0f), Aspect = 1f }.ToMatrix();

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(vertices, [new WorldBatch(0, 0, vertices.Count)], camera, assets, specular: specular);

        return target.PixelAt(32, 32);
    }

    private static MapTexture Texture(byte red, byte green, byte blue, byte alpha) =>
        new(4, 4, 4, 4, TextureImage.Rgba(Enumerable.Range(0, 16).SelectMany(_ => new[] { red, green, blue, alpha }).ToArray()), IsTransparent: false);

    /// <summary>One VertexLitGeneric material, grey with alpha 255, over a uniform cube; none for a null shading.</summary>
    private static MapAssets Material(MapEnvmapShading? shading, bool skinShader = false)
    {
        byte[] face = [.. Enumerable.Range(0, 16).SelectMany(static _ => new byte[] { 100, 100, 100, 255 })];
        MapTexture[] faces = [.. Enumerable.Range(0, 6).Select(_ => new MapTexture(4, 4, 4, 4, TextureImage.Rgba(face), IsTransparent: false))];
        byte[] grey = [.. Enumerable.Range(0, 16).SelectMany(static _ => new byte[] { 128, 128, 128, 255 })];

        return MapAssets.ForMaterials(
            [new MapTexture(4, 4, 4, 4, TextureImage.Rgba(grey), IsTransparent: false, IsSkinShader: skinShader)],
            ["VertexLitGeneric"],
            cubemaps: [shading is { } value ? new MapCubemap(faces, value) : null]);
    }

    /// <summary>A quad facing the camera, drawn as a model under an ambient light, centre pixel read.</summary>
    private static (int R, int G, int B) Draw(OffscreenTarget target, MapAssets assets, bool specular, bool phong = true, bool bumpMap = true)
    {
        static WorldVertex Corner(float x, float z, float u, float v) =>
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

        float[] camera = new FreeCamera { Origin = (0f, -300f, 0f), Angles = (0f, 90f, 0f), Aspect = 1f }.ToMatrix();

        target.Clear(0f, 0f, 0f);
        target.DrawModelPose(
            vertices,
            [new WorldBatch(0, 0, vertices.Count)],
            camera,
            identity,
            assets,
            light: new AmbientCube((0.2f, 0.2f, 0.2f), (0.2f, 0.2f, 0.2f), (0.2f, 0.2f, 0.2f), (0.2f, 0.2f, 0.2f), (0.2f, 0.2f, 0.2f), (0.2f, 0.2f, 0.2f)),
            bothSides: true,
            phong: phong,
            specular: specular,
            bumpMap: bumpMap);

        return target.PixelAt(32, 32);
    }
}
