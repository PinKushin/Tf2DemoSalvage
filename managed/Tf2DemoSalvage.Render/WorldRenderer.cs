using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Diagnostics;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// Draws a map with its own textures and its own baked lighting.
/// </summary>
/// <remarks>
/// **Two samplers and a multiply, which is what Source itself does.** The base texture gives a
/// surface its colour and the lightmap gives it its light; multiplying them is the whole shading
/// model for world geometry in a Source map, because every light was baked at compile time.
///
/// **The lightmap is not doubled here, and that took a wrong turn to establish.** Source's shaders
/// multiply an LDR lightmap by two, so doing the same looked obviously right — and produced a map
/// washed out to white. That factor applies to the raw linear samples; by the time they reach this
/// shader <c>BspLightmaps</c> has already applied the exponent and the gamma curve, so the range is
/// spent. Doubling again is the same scaling twice.
///
/// **One draw call per material, not per face.** A map has thirteen thousand faces and two hundred
/// materials, so vertices are sorted into runs sharing a texture. The lightmap atlas is bound once
/// for all of them, which is the reason it is an atlas.
///
/// **Positions arrive already in clip space**, as they do for the point renderer, because projecting
/// them is the caller's job and is tested as ordinary arithmetic rather than through a GPU. That
/// caller was `TopDownCamera` until D98 removed it; the view matrix now comes from `ViewCamera`.
/// </remarks>
internal sealed unsafe class WorldRenderer : IDisposable
{
    /// <summary>Bytes per vertex: position, texture, lightmap, blend, colour, step and normal.</summary>
    /// <remarks>
    /// **The normal arrived for entity lighting**, since a model has no lightmap and is lit from
    /// its leaf's ambient cube evaluated against the surface normal. Brush surfaces carry theirs
    /// too rather than a placeholder: they already know it, and a free camera will want it.
    /// </remarks>
    /// <remarks>
    /// Twenty-one floats: fifteen for the vertex itself and six for where the same vertex sits in
    /// the NEXT animation frame, so the shader can blend between two baked frames. A model that
    /// does not animate carries its own position in both and blends to itself.
    /// </remarks>
    private const int VertexStride = sizeof(float) * 27;

    /// <summary>Most bones one model may be skinned by.</summary>
    /// <remarks>
    /// TF2's player models carry between 73 and 92 bones, so 128 covers them with room to spare.
    /// Three float4 rows each is 384 constants, well inside a constant buffer's 4,096.
    /// </remarks>
    private const int MaxBones = 128;

    /// <summary>The cutoff an alpha-tested material gets when it names none.</summary>
    /// <remarks>
    /// **Half, and it is the API's default rather than a choice made here.** Valve calls
    /// <c>AlphaFunc</c> only when <c>$alphatestreference</c> is above zero
    /// (<c>BaseVSShader.cpp:927</c>), so a material that states nothing keeps whatever the shader
    /// API was already set to. That value is not in <c>source-sdk-2013</c> — the shader API is
    /// closed — so this one number is INTERPOLATED where the rest of the alpha-test behaviour is
    /// read from published source, and it is the historical Direct3D default that Source's own
    /// documentation and every reimplementation agree on.
    /// </remarks>
    private const float DefaultAlphaTestReference = 0.5f;

    private static readonly string ShaderSource = ShaderText.Replace(
        "MaxBones", MaxBones.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private const string ShaderText = """
        struct VsIn
        {
            float3 pos : POSITION;
            float2 uv  : TEXCOORD0;
            float2 luv : TEXCOORD1;
            float  a   : TEXCOORD2;
            float3 vc  : TEXCOORD3;
            float3 nrm : TEXCOORD5;
            float  ls  : TEXCOORD4;

            // Where this same vertex sits one animation frame later. Blending toward it is what
            // turns thirty baked poses a second into continuous motion.
            float3 nextPos : TEXCOORD6;
            float3 nextNrm : TEXCOORD7;

            // Which bones move this vertex and by how much, for a model the GPU skins rather than
            // one whose frames were baked. A baked model carries zeroes and is not skinned.
            float3 bones   : TEXCOORD8;
            float3 weights : TEXCOORD9;
        };

        struct VsOut
        {
            float4 pos : SV_POSITION;
            float2 uv  : TEXCOORD0;

            // **The second texture's own coordinate**, because a material transforms its two
            // textures independently over one incoming coordinate — Valve's vertex shader writes
            // baseTexCoord and baseTexCoord2 from the same v.vTexCoord0 through different matrices
            // (unlittwotexture_vs20.fxc:63). A capture point beam holds its colour still while the
            // stripes scroll across it, which one shared coordinate cannot express.
            float2 uv2 : TEXCOORD6;
            float2 luv : TEXCOORD1;
            float  a   : TEXCOORD2;
            float3 vc  : TEXCOORD3;
            float3 nrm : TEXCOORD5;
            float  ls  : TEXCOORD4;

            // **World position, for the reflection vector.** The vertex shader already computes
            // this on the way to clip space; it was simply thrown away. A cubemap sample is the
            // direction from the eye to this point, mirrored about the normal, so the pixel shader
            // needs the point rather than only its depth.
            float3 wpos : TEXCOORD8;

            // **Each local light's attenuation, computed per VERTEX as the engine computes it.**
            // `VertexAttenInternal` (common_vs_fxc.h:762) runs in Valve's vertex shader and the
            // result is interpolated across the triangle; only the light DIRECTION and the N·L are
            // per pixel, in `PixelShaderDoGeneralDiffuseLight`. Four lights fit one float4, so
            // matching that split costs a single interpolant.
            //
            // **It is also the cheaper half, which is why it is not a trade.** The maths is the
            // same either way — a length, a dot and a reciprocal per light — but here it runs once
            // per vertex instead of once per covered pixel, which on an ordinary model is around
            // twenty times fewer, and it removes a per-pixel branch with it.
            //
            // The visible difference is that interpolation is LINEAR and attenuation is a curve, so
            // a large triangle near a lamp shades slightly flat across its middle. That is what
            // TF2 looks like, and a smoother result here would be this renderer diverging from the
            // game it is reproducing.
            float4 lampAtten : TEXCOORD10;
        };

        cbuffer Camera : register(b0)
        {
            row_major float4x4 viewProjection;

            // x: a debug view that replaces the texture with a flat category colour. Turning the
            //    map into "this is world, this is terrain, this is a prop, this is missing" answers
            //    in one glance what a textured picture hides.
            // y: reserved, always zero. Was a cutting plane in DEPTH, described as "world height
            //    inverted" — which it is only under an orthographic top-down projection, and that
            //    went with D98. Removed 2026-08-26 (B213); the slot remains so the register layout
            //    does not shift.
            float4 surfaceColours;

            // **Where the camera is, in world units.** Needed for the reflection vector and for
            // nothing else — a cubemap sample is the direction from the eye to the surface,
            // mirrored about the surface normal, so without this every reflective surface shows
            // the same texel wherever it is looked at from.
            //
            // Not plumbed from the cameras: it is recovered from the matrix above, which already
            // determines it. See EyePosition.
            float4 eyePosition;

            // **Every debug mode packed into one register, which is Valve's own discipline.**
            // `common_vs_fxc.h` gives a shader twelve float4s for everything it needs, so a
            // register per debug feature would run out long before the features do.
            //
            // x: mat_drawflat — the material's texture is replaced by flat white, leaving the
            //    lighting and the geometry. Answers "is that shape in the model or in the texture".
            // y: mat_luxels — Valve's luxel grid drawn at the LIGHTMAP coordinate, so one square is
            //    one baked lighting sample. Answers "how coarse is the light here", which is what
            //    a soft-edged shadow that should be sharp actually is.
            // z: mat_normalmaps — the surface normal drawn as colour, the standard tangent-space
            //    encoding, so a flat surface reads lilac and a wrongly-decoded one does not.
            // w: mat_bumpbasis — which of the three lightmap basis vectors the surface leans on,
            //    as red, green and blue. Grey means it leans evenly, which is what flat looks like.
            float4 debugModes;

            // **The fifth float4, and the budget says that is still Valve's shape.**
            // `common_vs_fxc.h` gives a shader twelve of its own; this is the fifth used here, so
            // there was no reason to conflate two independent cvars into one component to save a
            // register we have. The owner confirmed the ceiling: "the float4s can get up to 12 i
            // think before we are matching valve".
            //
            // x: mat_showlowresimage — the material is drawn from the tiny copy of itself that
            //    every VTF stores ahead of its mip chain, rather than from the texture.
            // y, z, w: unused, and left named rather than removed so the next debug mode costs an
            //    assignment instead of a constant-buffer change on both sides.
            float4 debugModes2;
        };

        // **The model transform, which is Valve's own shape.** IMaterialSystem::LoadBoneMatrix
        // hands bone matrices to the shader as constants and the GPU transforms model-space
        // vertices - which is why the engine draws a hundred animated models without noticing.
        // Rebuilding vertices on the processor every frame is the thing that path exists to avoid.
        //
        // A rigid entity is the one-bone case: one matrix for the whole model. Skinning adds more
        // matrices and a weight per vertex; nothing about this arrangement changes.
        //
        // Identity for the map's own geometry, which is already in world space.
        cbuffer Model : register(b2)
        {
            row_major float4x4 model;

            // **The ambient cube of the leaf this model stands in**, in the shader's own order:
            // +X, -X, +Y, -Y, +Z, -Z. A model has no lightmap, so this is the light it gets - and
            // without it every entity draws at full brightness, which is what made a medkit a pale
            // square instead of a teal case.
            //
            // w of the first entry is 1 when the cube is real. An unlit model is drawn at full
            // brightness deliberately, since a model lit by a cube nobody measured would be black.
            float4 ambientCube[6];

            // **The sun, and whether this model can see it.** rgb is the light's own intensity,
            // linear, straight from the map's emit_skylight; w is 1 when the model's position
            // traced to sky. Valve's own description of a sky light carries the condition in
            // parentheses - "surface must trace to SKY texture" - so applying it without the
            // trace lights the inside of every building.
            float4 sunColour;

            // xyz is the direction the light TRAVELS, as the map stores it, so a surface facing
            // into it takes the dot product against its negation.
            float4 sunDirection;

            // **How far between this baked frame and the next.** x is the blend, nought at the
            // frame itself and approaching one at the next. Baking gives a pose per frame and an
            // animation authored at ten frames a second would otherwise step ten times a second
            // against a sixty hertz display, which reads as a stutter rather than as animation.
            //
            // Valve blends in BONE space, slerping quaternions in CalcBoneQuaternion. This blends
            // positions, which is what a vertex-baked format can do; the two differ only where a
            // bone turns far enough in one frame for the chord to cut the arc, and a pickup
            // spinning at ten frames a second turns twelve degrees between frames.
            float4 frameBlend;

            // **How many bones this draw is skinned by, or zero for a model that is not.** A prop
            // has its frames baked and blends between them; a player has hundreds of animations
            // over ninety bones, which is gigabytes baked, so the GPU transforms it instead. Both
            // paths share one shader because the alternative is two that agree until one gains a
            // feature.
            float4 skinning;

            // **The direct lights near this model, at most four** — the engine's `locallight[]`.
            // `PixelShaderDoLightingLinear` adds an ambient cube and then up to four of these, each
            // shaded against the surface normal, and `PixelShaderDoSpecularLight` runs once per
            // light with that light's attenuation. So a lamp reaching a model this way gives it
            // shape and a highlight; the same lamp folded into the cube gives it neither, which is
            // the whole of B170's missing term.
            //
            // xyz is where the light is, world space. w is unused — it held a per-slot "is this
            // live" flag until the shader was made to match Valve's, which uses a COUNT and nested
            // ifs instead. Left rather than repacked: the layout test pins the buffer's size, and
            // shuffling fields to reclaim four floats is how the material buffer got its strobe.
            float4 localLightPosition[4];

            // rgb is the light's own intensity, linear and in the ambient cube's scale. w is the
            // cutoff range, squared, or zero for no cutoff at all — which is what every light on
            // cp_process carries, so reading zero as a real radius extinguishes the map.
            float4 localLightColour[4];

            // xyz is Valve's attenuation denominator, `a0 + a1*d + a2*d^2`, already carrying vrad's
            // all-zero rule so a light with no terms arrives as a constant one rather than as a
            // division by nothing.
            //
            // **[0].w is how many lights this draw has**, which is Valve's `nNumLights`. They get it
            // as a compile-time `NUM_LIGHTS` and build a shader permutation per count; this renderer
            // has no permutation system, so it arrives as a constant and the ifs are dynamic. That
            // is the one place this departs, and it is a departure in MECHANISM rather than in
            // result — the same lights contribute the same amounts either way.
            //
            // Packed into a spare channel rather than given a float4 of its own, which is Valve's
            // own idiom: `PixelShaderDoLightingLinear` unpacks its fourth light out of the .w
            // channels of the first three for exactly this reason.
            float4 localLightFalloff[4];
        };

        // **Per material rather than per frame.** A detail texture's scale, strength and combine
        // mode belong to the material, so this is rewritten between draws - around two hundred
        // times a frame, which is nothing next to the draw calls themselves.
        cbuffer Material : register(b1)
        {
            // x: how many times the detail tiles per tile of the base texture, across
            // y: how strongly it is applied
            // z: which of the twelve combine modes to use, or -1 for no detail at all
            // w: the same tiling, down - a material may scale the two independently
            float4 detail;

            // The colour the sampled detail is multiplied by before it is combined.
            float4 detailTint;

            // x: 1 when the material has a bump map, 0 otherwise
            // y: 1 when that bump map is self-shadowing rather than a normal map
            // z: 1 when parts of the surface light themselves
            // w: 1 when the material is ALPHA TESTED and its alpha is a cut-out
            float4 bump;

            // The colour the self-illuminated part is tinted by.
            float4 selfIllumTint;

            // **The two texture coordinate transforms, two rows each.** Uploaded exactly as the
            // engine does — CBaseVSShader::SetVertexShaderTextureTransform sends mat[0] and mat[1]
            // (BaseVSShader.cpp:307) and the vertex shader dots each against the incoming
            // coordinate. The fourth component is the translation, which is what a scrolling
            // material writes and why the coordinate is extended to a float4 below.
            //
            // Identity is (1,0,0,0) and (0,1,0,0), which is Valve's own fallback for a material
            // with no transform. A zeroed row would send every coordinate to the first texel.
            float4 baseTransform0;
            float4 baseTransform1;
            float4 secondTransform0;
            float4 secondTransform1;

            // **The modulation colour**, which is $color and $alpha after their proxies have run.
            // Valve's pixel shader multiplies by g_DiffuseModulation; without it the capture point
            // signs never pulse, which reads as a dead scene rather than a missing feature.
            float4 modulation;

            // x: how the material's two textures combine.
            //    0 mixes them by the vertex alpha, which is what a WorldVertexTransition
            //      displacement is: dirt under grass, the vertices saying how much of each.
            //    1 MULTIPLIES them, which is UnLitTwoTexture — Valve's own pixel shader is
            //      `baseColor * baseColor2 * g_DiffuseModulation` with alpha forced to one
            //      (stdshaders/unlittwotexture_ps2x.fxc).
            //
            // The distinction has to reach the shader because the two look nothing alike: a
            // capture point's beam is stripes TIMES a colour, and mixed by alpha instead it is
            // whichever of the two the vertices happen to ask for.
            float4 combine;

            // **The baked reflection's shading, xyz tint and w contrast.** Packed together because
            // a constant buffer is sized in whole float4s and these are three floats and one.
            //
            // Contrast is normal at ZERO — `lerp(reflection, reflection * reflection, contrast)` —
            // which is the opposite end from the saturation below. Getting the pair the same way
            // round greys out or squares every reflection on the map, and neither is an error.
            float4 envmapTint;

            // x: saturation, normal at ONE. `lerp(greyscale, reflection, saturation)`.
            // y: 1 when the base texture's alpha masks the reflection, INVERTED — an opaque texel
            //    reflects least, and Valve annotated their own line "Reversing alpha blows!"
            // z: 1 when this material has a cubemap bound at all, 0 otherwise. A material without
            //    one still gets a sampler bound, because the slot is set once per draw and a stale
            //    cube from the previous material would otherwise reflect on a matte surface.
            // w: $fresnelreflection, and ONE means no falloff. The Schlick term is remapped as
            //    `schlick * (1 - w) + w`, which is Valve's own arithmetic, and the parameter's
            //    declaration reads "1.0 == mirror, 0.0 == water". Applying raw Schlick instead made
            //    every reflection invisible head-on, which is B125.
            float4 envmapControl;

            // **The specular highlight, $phong.** 330 of cp_process's materials ask for it, and a
            // model without it reads as flat colour.
            //
            // x: the exponent, 5 by default — broad rather than tight, which is TF2's look.
            // y: $phongboost. One calibration with the mask: the parameter's own declaration says
            //    "specular mask channel should be authored to account for this".
            // z: 1 when this material has phong at all.
            // w: 1 when the mask is the BASE texture's alpha rather than the bump map's, which the
            //    flag's declaration also asserts means there is no normal map at all.
            float4 phongControl;

            // xyz: $phongfresnelranges, ALREADY ENCODED as ((mid-min)*2, mid, (max-mid)*2), which is
            //      what the shader's remap expects and is not the triple written in the VMT. Valve
            //      states the encoding in a comment beside their own code. Feeding the raw numbers
            //      returns 0.5 head-on instead of 0, so the highlight never fades and every model
            //      wears a uniform sheen.
            // w: 1 when the material carries a $lightwarptexture. It rides here rather than in a
            //    row of its own because a constant buffer is sized in whole float4s and this slot
            //    was spare — the warp is not otherwise related to phong.
            float4 phongFresnel;

            // xyz: $phongtint, white unless the material names one. w: unused.
            //
            // $phongalbedotint is NOT here, and deliberately: it does nothing without
            // $phongexponenttexture, because the tint is read from that texture's green channel.
            // Honouring the boolean alone tints every highlight by the base texture.
            float4 phongTint;

            // **The rim light, $rimlight.** 301 materials, and it is what separates a model from
            // the background it stands against.
            //
            // x: the exponent, 4 by default — a DIFFERENT default from phong's 5, applied to the
            //    same L.R.
            // y: $rimlightboost, which scales the half of the rim that comes from the ambient cube
            //    rather than from the light.
            // z: 1 when this material has a rim at all. Only ever set alongside phong, because the
            //    rim lives in the Skin shader and VertexLitGeneric reaches it on $phong alone.
            // w: unused.
            float4 rimControl;

            // **What this BATCH is, for the category view, and it is per batch rather than per
            // material** (B219). Written straight into the mapped buffer after the material's own
            // constants are copied, because two batches of the same material can be different
            // categories — a texture used on both a wall and a displacement.
            //
            // rgb is the colour, w says whether one was supplied at all.
            float4 categoryColour;

            // **How the modulation is applied to the albedo** (B331), which is a question TF2's
            // painted items make load-bearing.
            //
            // x: `$blendtintbybasealpha` — 1 to tint only where the base texture's ALPHA says,
            //    0 to multiply the tint across the whole surface, which is every other material.
            // y: `$blendtintcoloroverbase`, Valve's `g_fTintReplacementControl` — 0 multiplies the
            //    tint into the albedo and keeps its detail, 1 replaces the albedo with the flat
            //    colour. Zero is the common case and TF2's cosmetics use it.
            //
            // **Appended rather than folded into an existing float4**, and every array feeding this
            // buffer had to grow with it. The comment on `SetMaterial`'s length check records what
            // happened the last time only two of three did.
            float4 tintControl;
        };

        // **Valve's overbright.** A lightmap is stored halved so that light brighter than white
        // survives eight bits, and the shader doubles it back - Source's own shaders multiply an
        // LDR lightmap by two for exactly this reason. Both halves have to be present or the map
        // is out by a factor of two in one direction.
        static const float OverbrightScale = 2.0f;

        Texture2D    albedoMap   : register(t0);
        Texture2D    lightMap    : register(t1);
        Texture2D    blendMap    : register(t2);
        Texture2D    detailMap   : register(t3);
        Texture2D    bumpMap     : register(t4);

        // The map's own baked reflection for this material, six faces in Valve's order — which is
        // +X, -X, +Y, -Y, +Z, -Z and therefore D3D's order unchanged.
        TextureCube  envMap      : register(t5);

        // **A ramp, not a picture**: one row of texels the diffuse term indexes. Sampled with the
        // CLAMP sampler, because wrapping would send a surface at the end of the curve back to the
        // other end of it.
        Texture2D    lightWarp   : register(t6);

        // **Valve's own measurement grid, for the category view.** Bound once for the whole frame
        // rather than per material: it replaces the material's texture instead of joining it, so
        // there is nothing per-batch to say about it.
        Texture2D    devMap      : register(t7);

        // **Valve's own luxel grid**, `debug/debugluxels`, which ships in the Half-Life 2 archives
        // TF2 mounts. Separate from devMap because they are sampled with different coordinates —
        // this one at the LIGHTMAP coordinate, so a cell is a baked sample rather than a texture
        // tile — and one texture cannot be both.
        Texture2D    luxelMap    : register(t8);

        // **The material's own thumbnail, for `mat_showlowresimage`.** Per material rather than per
        // frame, unlike devMap and luxelMap: every texture carries a different one, so this is the
        // only debug substitution that has to be rebound with the material.
        Texture2D    lowResMap   : register(t9);

        // **`$selfillummask`: which parts of the surface light themselves, where a texture says
        // so** (B327). Sampled on the BASE texture's coordinates, not a set of its own, and read
        // only where phongTint.w is 1 — otherwise the base map's alpha decides, which is what the
        // engine's own `lerp( baseColor.aaa, mask, control )` collapses to.
        Texture2D    selfIllumMask : register(t10);

        // **`$phongexponenttexture`: three unrelated controls in one image** (B334). Red is the
        // exponent as `1 + 149 * r`, green is how much of the albedo tints the highlight, and alpha
        // masks the rim. Sampled on the BASE texture's coordinates (`skin_ps20b.fxc:253`), not a
        // set of its own — and always bound, flat white where the material names none, which is
        // what the engine substitutes and is the neutral for all three.
        Texture2D    specExpMap  : register(t11);

        SamplerState wrapSampler : register(s0);
        SamplerState clampSampler: register(s1);

        // Transcribed from TextureCombine in Valve's common_ps_fxc.h. The modes are numbered by
        // the engine and the numbers appear in materials, so they stay as numbers here.
        float4 CombineDetail(float4 albedo, float4 detailColour, int mode, float blend)
        {
            if (mode == 7)
            {
                float3 selected = lerp(detailColour.r, detailColour.a, albedo.a);
                albedo.rgb *= lerp(float3(1, 1, 1), 2.0f * selected, blend);
            }
            if (mode == 0)
            {
                albedo.rgb *= lerp(float3(1, 1, 1), 2.0f * detailColour.rgb, blend);
            }
            if (mode == 1)
            {
                albedo.rgb += blend * detailColour.rgb;
            }
            if (mode == 2)
            {
                albedo.rgb = lerp(albedo.rgb, detailColour.rgb, blend * detailColour.a);
            }
            if (mode == 3)
            {
                albedo = lerp(albedo, detailColour, blend);
            }
            if (mode == 4)
            {
                albedo.rgb = lerp(albedo.rgb, detailColour.rgb, blend * (1.0f - albedo.a));
                albedo.a = detailColour.a;
            }
            if (mode == 8)
            {
                albedo = lerp(albedo, albedo * detailColour, blend);
            }
            if (mode == 9)
            {
                albedo.a = lerp(albedo.a, albedo.a * detailColour.a, blend);
            }
            if (mode == 11)
            {
                // No blend factor in this one, deliberately: Valve's line is a bare multiply.
                albedo.rgb *= dot(detailColour.rgb, 2.0f / 3.0f);
            }
            return albedo;
        }

        // Modes 5 and 6 are self-illumination and are added AFTER the lightmap, which is why they
        // are a second function rather than two more cases above.
        float3 CombineDetailAfterLighting(float3 lit, float4 detailColour, int mode, float blend)
        {
            if (mode == 5)
            {
                lit += blend * detailColour.rgb;
            }
            if (mode == 6)
            {
                float multiplier = (blend >= 0.5f) ? 1.0f / blend : 4.0f * blend;
                float offset = (blend >= 0.5f) ? 1.0f - multiplier : -0.5f * multiplier;
                lit += saturate(multiplier * detailColour.rgb + offset);
            }
            return lit;
        }

        // **Bone matrices, which is Valve's own arrangement.** IMaterialSystem::LoadBoneMatrix
        // hands these to the shader as constants and the GPU moves model-space vertices by them -
        // the reason the engine draws a hundred animated players without noticing. Three rows of
        // four, row major, the same 3x4 the studio format stores.
        cbuffer Bones : register(b3)
        {
            float4 boneRows[MaxBones * 3];
        };

        float3 SkinPosition(float3 position, float3 bones, float3 weights, float count)
        {
            if (count < 1.0f)
            {
                return position;
            }

            float3 moved = float3(0.0f, 0.0f, 0.0f);
            float total = weights.x + weights.y + weights.z;

            if (total <= 0.0f)
            {
                return position;
            }

            [unroll]
            for (int slot = 0; slot < 3; slot++)
            {
                float weight = slot == 0 ? weights.x : (slot == 1 ? weights.y : weights.z);

                if (weight <= 0.0f)
                {
                    continue;
                }

                int bone = (int)(slot == 0 ? bones.x : (slot == 1 ? bones.y : bones.z));

                if (bone < 0 || bone >= (int)count)
                {
                    continue;
                }

                float4 wide = float4(position, 1.0f);
                int row = bone * 3;

                moved += (weight / total) * float3(
                    dot(boneRows[row], wide),
                    dot(boneRows[row + 1], wide),
                    dot(boneRows[row + 2], wide));
            }

            return moved;
        }

        // **One local light's attenuation at a world point — Valve's VertexAttenInternal.**
        // `common_vs_fxc.h:762`, minus the spot cone and the directional bypass: the sun travels
        // its own path here and a spotlight's cone is not decoded yet, so both would be dead code
        // pretending to be parity.
        //
        // Returns zero for a light beyond its range. Valve does not cull in the shader at all,
        // because `LightDesc_t::ComputeLightAtPoints` culled on the CPU before the light was
        // chosen — this project culls there too, at the model's sample point, so this is the same
        // test applied where a large model can extend past it. Every light on cp_process carries a
        // range of zero, which means no cutoff, so it is inert on that map either way.
        float LampAttenuation(int lamp, float3 world)
        {
            float3 toLamp = localLightPosition[lamp].xyz - world;
            float distanceSquared = dot(toLamp, toLamp);

            // **Clamped, not offset**: MaxSIMD( Four_Ones, dist2 ) in lightdesc.cpp. The ambient
            // reconstruction uses 1 / (dist + 1) and the two are easy to conflate.
            distanceSquared = max(1.0f, distanceSquared);

            // Strictly less than, and a range of zero means no cutoff at all — which is what every
            // light on cp_process carries, so reading zero as a real radius extinguishes the map.
            if (localLightColour[lamp].w != 0.0f && distanceSquared >= localLightColour[lamp].w)
            {
                return 0.0f;
            }

            // `1 / dot( atten.xyz, vDist )` where vDist is dst(dist2, 1/dist) = (1, d, d²).
            return 1.0f / dot(
                localLightFalloff[lamp].xyz,
                float3(1.0f, sqrt(distanceSquared), distanceSquared));
        }

        VsOut VsMain(VsIn input)
        {
            VsOut output;
            // **World space in, clip space out.** The vertices are uploaded once in the map's own
            // coordinates and this matrix is the only thing that changes when the view does, so a
            // resize costs 64 bytes instead of rebuilding a couple of million vertices.
            // Model space to world, then world to clip. The map's geometry passes an identity
            // model matrix, so it costs one multiply and keeps a single path for both.
            // **Blend toward the next baked frame before anything else.** Baking stores a pose
            // per animation frame; without this the model steps between them at the animation's
            // own frame rate, which for a pickup authored at ten frames a second is a visible
            // stutter against a sixty hertz display. A still model carries the same position in
            // both and blends to itself.
            float3 posed = lerp(input.pos, input.nextPos, frameBlend.x);
            float3 posedNormal = lerp(input.nrm, input.nextNrm, frameBlend.x);

            // A skinned model is moved by its bones instead of blended between baked frames. The
            // two are exclusive: a model is either baked or skinned, never both.
            if (skinning.x >= 1.0f)
            {
                posed = SkinPosition(input.pos, input.bones, input.weights, skinning.x);

                // The normal turns with the bones but is not translated by them, so it is skinned
                // about the origin and normalised afterwards.
                posedNormal = normalize(SkinPosition(
                    input.nrm, input.bones, input.weights, skinning.x)
                    - SkinPosition(float3(0.0f, 0.0f, 0.0f), input.bones, input.weights, skinning.x));
            }

            float4 world = mul(float4(posed, 1.0f), model);
            output.pos = mul(world, viewProjection);
            output.wpos = world.xyz;

            // **Valve's own shape, transcribed from `cloak_vs20.fxc:105`:**
            //
            //     o.lightAtten = float4(0,0,0,0);
            //     #if ( NUM_LIGHTS > 0 )
            //         o.lightAtten.x = GetVertexAttenForLight( worldPos, 0, false );
            //     #endif
            //     ... and so on to .w
            //
            // Written out by swizzle with a literal light number, never a loop over an index. This
            // was written as a loop first and it does not compile — `output.lampAtten[i]` indexes a
            // vector by a variable, which HLSL cannot use as an l-value (X3500), and the early-out
            // inside stops the forced unroll (X3511). Reading Valve's shader first would have
            // skipped that entirely, which is the whole argument for reading it first.
            //
            // The count is dynamic here where theirs is a compile-time permutation; see the
            // `localLightFalloff` comment for why, and for what that does and does not change.
            float lamps = localLightFalloff[0].w;

            output.lampAtten = float4(0.0f, 0.0f, 0.0f, 0.0f);

            if (lamps > 0.5f)
            {
                output.lampAtten.x = LampAttenuation(0, world.xyz);

                if (lamps > 1.5f)
                {
                    output.lampAtten.y = LampAttenuation(1, world.xyz);

                    if (lamps > 2.5f)
                    {
                        output.lampAtten.z = LampAttenuation(2, world.xyz);

                        if (lamps > 3.5f)
                        {
                            output.lampAtten.w = LampAttenuation(3, world.xyz);
                        }
                    }
                }
            }
            // **Both coordinate sets, from one incoming pair, exactly as the engine builds them.**
            // The coordinate is extended to a float4 with w = 1 so the transform's fourth column
            // translates — that is what a scrolling material writes into, and with an identity
            // transform this is the coordinate unchanged.
            float4 coordinate = float4(input.uv, 0.0f, 1.0f);

            output.uv = float2(dot(coordinate, baseTransform0), dot(coordinate, baseTransform1));
            output.uv2 = float2(dot(coordinate, secondTransform0), dot(coordinate, secondTransform1));
            output.luv = input.luv;
            output.a = input.a;
            output.vc = input.vc;

            // The normal is in the model's own space, so it turns with the model. Rotation only:
            // the translation would move a direction, and the scale cancels once it is normalised.
            output.nrm = normalize(mul(float4(posedNormal, 0.0f), model).xyz);
            output.ls = input.ls;
            return output;
        }

        // g_localBumpBasis from Valve's bumpvects.h, to the float. Hard-coded there rather than
        // computed, so copied rather than derived.
        static const float3 bumpBasis[3] =
        {
            float3( 0.81649661064147949f,  0.0f,                0.57735025882720947f),
            float3(-0.40824821591377258f,  0.70710676908493042f, 0.57735025882720947f),
            float3(-0.40824821591377258f, -0.70710676908493042f, 0.57735025882720947f),
        };

        // Mixes the three directional lightmaps for one surface normal. Transcribed from
        // lightmappedgeneric_ps2_3_x.h; the managed BumpedLight carries the same arithmetic and is
        // where it is actually tested.
        float3 CombineBumped(float3 normal, float3 first, float3 second, float3 third, bool ssbump)
        {
            if (ssbump)
            {
                // An ssbump texel already holds three weights: no dots, no squaring, and
                // deliberately no normalising, so weights summing to two give twice the light.
                return normal.x * first + normal.y * second + normal.z * third;
            }

            float3 weights;
            weights.x = saturate(dot(normal, bumpBasis[0]));
            weights.y = saturate(dot(normal, bumpBasis[1]));
            weights.z = saturate(dot(normal, bumpBasis[2]));
            weights *= weights;

            float total = weights.x + weights.y + weights.z;

            if (total <= 0.0f)
            {
                // Valve divides here unchecked and a GPU returns NaN, which spreads through the
                // frame as a pixel nothing explains. Costs one comparison.
                return float3(0.0f, 0.0f, 0.0f);
            }

            float3 mixed = weights.x * first + weights.y * second + weights.z * third;

            // **The division is what makes this a mix rather than a scale.** The three squared
            // weights come to a third for a normal straight out of the surface, so without it the
            // wall ripples with light rather than with shape.
            return mixed / total;
        }

        // **One local light's diffuse contribution — Valve's PixelShaderDoGeneralDiffuseLight.**
        // `common_vertexlitgeneric_dx9.h:124`: normalise the direction here, take the DiffuseTerm
        // against the normal, and multiply by the attenuation the vertex shader handed over.
        //
        // Zero attenuation covers both an empty slot and a light culled by range, so this tests one
        // number rather than re-reading the flag and the cutoff.
        float3 LampDiffuse(int lamp, float attenuation, float3 wpos, float3 normal)
        {
            if (attenuation <= 0.0f)
            {
                return float3(0.0f, 0.0f, 0.0f);
            }

            float towards = dot(normal, normalize(localLightPosition[lamp].xyz - wpos));

            // The same DiffuseTerm the sun takes, so a half-Lambert material shades both the same
            // way — Valve applies it inside DoLightInternal for every light, warp included.
            bool warping = phongFresnel.w > 0.5f;
            float wrapped = saturate(towards * 0.5f + 0.5f);

            float falloff = combine.y > 0.5f
                ? (warping ? wrapped : wrapped * wrapped)
                : saturate(towards);

            float3 direct = warping
                ? 2.0f * lightWarp.Sample(clampSampler, float2(falloff, 0.5f)).rgb
                : float3(falloff, falloff, falloff);

            return localLightColour[lamp].rgb * attenuation * direct;
        }

        float4 PsMain(VsOut input) : SV_TARGET
        {
            // **Two textures mixed by the vertex's alpha, which is what terrain is.** A
            // WorldVertexTransition material carries dirt and grass, and a displacement's vertices
            // say how much of each. Where a material has only one texture the second is bound to
            // the same image, so the mix is an identity and costs a sample.
            float4 first = albedoMap.Sample(wrapSampler, input.uv);

            // Sampled with its OWN coordinate, which is the point of carrying two: the beam's
            // stripes scroll across a colour that stays put.
            float4 second = blendMap.Sample(wrapSampler, input.uv2);
            // **The height cut was clipped here until 2026-08-26** (B213). Its own comment said
            // "the cut is on depth, which is height" — an equivalence that holds ONLY under the
            // orthographic top-down projection D98 deleted. Under a perspective camera `pos.z` is
            // distance from the eye, so the control cut away whatever was nearest rather than
            // whatever was highest, which is why the owner's verdict was that it "never worked in
            // the first place".
            //
            // `surfaceColours.y` is `mat_phong` now (B170). It was reserved and read by nothing when
            // the height cut went, and it is the right home for a material-feature switch because
            // this register already holds `mat_specular` and `mat_fullbright`. It is still always
            // WRITTEN rather than skipped: the tail of a mapped buffer holds whatever the last frame
            // put there (`docs/memory/padding-is-not-zero.md`), and a component that is sometimes
            // not written is a switch that sometimes turns itself on.

            // **Multiplied for UnLitTwoTexture, mixed by vertex alpha for everything else.** Valve's
            // shader is `baseColor * baseColor2 * g_DiffuseModulation`, and a capture point's beam
            // is exactly that: scrolling stripes times a team colour. Mixed by alpha instead, the
            // beam is whichever of the two the vertices ask for — which is how it came out as a
            // grey striped column on BLU, whose material happens to name the stripes first.
            // Valve's line is `baseColor * baseColor2 * g_DiffuseModulation`, so the modulation
            // colour belongs on the multiply — it is what $color and $alpha drive, and what a Sine
            // proxy pulses.
            float4 albedo = combine.x > 0.5f
                ? first * second
                : lerp(first, second, saturate(input.a));

            // **Every material, not only the two-texture ones.** The multiply above used to carry
            // `* modulation` inside its branch, which meant $color and $alpha reached exactly the
            // materials drawn by UnLitTwoTexture and no others — so a tinted haze or a coloured
            // glow on any ordinary shader was decoded, uploaded, and then multiplied by nothing.
            //
            // g_DiffuseModulation is not a two-texture idea. LightmappedGeneric, VertexLitGeneric
            // and UnlitGeneric all fold it into albedo the same way, which is why it is applied
            // here, once, after whichever combine produced the colour. Alpha goes with it: the
            // alpha test below reads albedo.a, and in the engine the test sees the shader's OUTPUT
            // alpha, modulation included.
            // **`$blendtintbybasealpha` puts the tint only where the artist masked it** (B331), and
            // without it a painted hat is tinted end to end instead of on its tintable region.
            // Valve's branch, verbatim:
            //
            //   if (bBlendTintByBaseAlpha)
            //   {
            //       float3 tintedColor = albedo * g_DiffuseModulation.rgb;
            //       tintedColor = lerp(tintedColor, g_DiffuseModulation.rgb, g_fTintReplacementControl);
            //       albedo = lerp(albedo, tintedColor, baseColor.a);
            //   }
            //   else
            //       albedo = albedo * g_DiffuseModulation.rgb;
            //
            // `skin_ps20b.fxc:317-326`. Three things in it are easy to lose:
            //
            // - **The base's ALPHA is the mask, and it is the UNMODULATED alpha** — `baseColor.a`,
            //   the texture's own, not `albedo.a` after the multiply. They differ the moment a
            //   material's `$alpha` is anything but one.
            // - **`$blendtintcoloroverbase` lerps between MULTIPLYING the tint in and REPLACING the
            //   albedo with it**, which is `g_fTintReplacementControl`. Zero — the common case, and
            //   this hat's — keeps the texture's detail under the colour; one paints it flat.
            // - **Alpha is modulated either way.** Valve's branch touches `.rgb` only, and the
            //   alpha test below reads the shader's output alpha with modulation folded in.
            if (tintControl.x > 0.5f)
            {
                float3 tinted = albedo.rgb * modulation.rgb;

                tinted = lerp(tinted, modulation.rgb, tintControl.y);

                albedo = float4(lerp(albedo.rgb, tinted, first.a), albedo.a * modulation.a);
            }
            else
            {
                albedo *= modulation;
            }

            // **The detail goes in before the lighting, as Valve's shader does it.** It modifies
            // the albedo - the surface's own colour - and the lightmap then multiplies the result.
            // Applied after the light instead, the pattern would sit on top of the shading rather
            // than in it, and would be as bright in shadow as in sun.
            int mode = (int)detail.z;
            float4 detailColour =
                detailTint * detailMap.Sample(wrapSampler, input.uv * float2(detail.x, detail.w));

            if (mode >= 0)
            {
                albedo = CombineDetail(albedo, detailColour, mode, detail.y);
            }

            // **Alpha-tested foliage, which is what a bush IS.** Source draws leaves and grates as
            // flat cards whose texture alpha cuts out the shape. Drawn opaque, the cut-out region
            // renders as its RGB - which is black - so every bush and tree became a solid black
            // card the size of its quad. Opaque materials have their alpha forced to one on upload,
            // so this clip only ever fires on materials that asked for it.
            //
            // **After the combine, not before.** Four of the twelve modes write alpha, and alpha is
            // what this reads - so clipping first would test a value the material never asked to be
            // tested, and cut away pixels the engine keeps.
            // **Only when the material asked for it**, which is what the engine does: alpha
            // testing happens because a VMT says $alphatest, not because a texture happens to
            // carry an alpha channel.
            //
            // This used to clip unconditionally, relying on opaque textures having their alpha
            // flattened to 255 on upload. That holds for most of the map and fails for anything
            // whose alpha is kept for another reason - a self-illuminated material, or a model
            // texture with an unused alpha channel full of zeros. Every entity model in the demo
            // was discarded pixel by pixel while its geometry, transform and draw call were all
            // correct.
            // bump.w carries the alpha-test CUTOFF: zero for a surface that is not alpha tested,
            // otherwise the value to clip at. GEQUAL in the engine, so a texel exactly at the
            // reference is kept - which is why the subtraction is clipped rather than compared.
            if (bump.w > 0.0f)
            {
                clip(albedo.a - bump.w);
            }

            // **The category view draws Valve's dev grid, tinted by what the surface IS.**
            //
            // The owner's reasoning, and it is about comparison rather than looks: "if our
            // placeholders match valves, and our colors match valves then things become easily
            // compared and you only have one legend to remember". A capture from this viewer and a
            // shot of the same spot in Hammer or in the game's dev mode then read the same way, and
            // nobody has to hold two vocabularies at once.
            //
            // **The grid is what a flat colour cannot give.** A solid shape says a surface exists;
            // it says nothing about its scale, its orientation, or whether its texture coordinates
            // are sane — and a wrongly-scaled or mirrored surface is exactly the defect a category
            // view gets reached for. Valve's measure textures carry printed dimensions for that
            // reason.
            //
            // Multiplied rather than replaced, so the category tint survives: the grid says how
            // big and which way up, the tint says brushwork, terrain, prop, overlay or missing.
            if (surfaceColours.x > 0.5f)
            {
                float3 grid = devMap.Sample(wrapSampler, input.uv).rgb;

                // (the substitution and its reasoning continue below)

                // **The grid contributes STRUCTURE, the tint contributes COLOUR, and mixing those
                // two jobs is what the first attempt got wrong.** `dev_measuregeneric01` is orange,
                // so multiplying a tint by it dragged every category toward orange and the view
                // reported one thing where it should report five. Valve's dev set is not one hue —
                // Half-Life 2 ships twenty-four and TF2 adds blu and red variants — so picking a
                // "neutral" one would work until somebody's install resolved a different candidate.
                //
                // Taking luminance makes it independent of which texture was found. Rec.601, the
                // same weights the envmap saturation uses above, because a grid whose lines are
                // green should not read as brighter than one whose lines are blue.
                float ink = dot(grid, float3(0.299f, 0.587f, 0.114f));

                // Remapped rather than used raw: at full range the printed dimensions go black and
                // the tint disappears with them. This keeps the grid legible while leaving the
                // category colour the dominant reading, which is the order of importance here.
                //
                // **Substituted into the ALBEDO rather than returned, so the lighting still runs.**
                // This returned here, and the result was a flat picture: no shadow, no shading, so
                // a cylinder and a flat panel of the same category were the same shape on screen
                // and telling one piece of geometry from another was hard exactly where it matters.
                // The owner's words — "stuff stops having shadows so actually differentiating stuff
                // becomes kinda hard".
                //
                // Falling through costs nothing extra: the lighting path already ends in
                // `albedo.rgb * light * input.vc`, and in this view input.vc IS the category
                // colour. So grid times lighting times category comes out of the arithmetic that
                // is already there, and mat_fullbright still works on top of it.
                //
                // This is the same lesson as mat_fullbright's: substitute at the point the value is
                // USED, not one step later where it has stopped being the same quantity.
                albedo.rgb = 0.40f + (0.60f * ink);

                // **The category's own colour, applied HERE rather than baked into the vertices**
                // (B219). The grid says how big and which way up; this says what the surface IS.
                // Multiplied rather than replacing, so both survive — the arrangement the vertex
                // colour already had, moved to where changing it costs a constant write instead of
                // rebuilding every vertex in the map.
                //
                // `w` says whether a category was supplied at all, so without one this is an
                // identity rather than a black surface.
                if (categoryColour.w > 0.5f)
                {
                    albedo.rgb *= categoryColour.rgb;
                }
            }
            float3 light;

            if (bump.x > 0.5f && input.ls > 0.0f)
            {
                // **Set 0 is not read here, and that is the trap.** When a face is bump lit the
                // engine reads sets 1, 2 and 3 - the three ARE the lighting. Treating the flat set
                // as a base with the others adding to it gives a plausible picture that is roughly
                // twice as bright and flat where it should be shaped.
                float3 first  = lightMap.Sample(clampSampler, input.luv + float2(input.ls, 0)).rgb * OverbrightScale;
                float3 second = lightMap.Sample(clampSampler, input.luv + float2(input.ls * 2, 0)).rgb * OverbrightScale;
                float3 third  = lightMap.Sample(clampSampler, input.luv + float2(input.ls * 3, 0)).rgb * OverbrightScale;

                float4 texel = bumpMap.Sample(wrapSampler, input.uv);

                // An ssbump is sampled raw; an ordinary normal map is signed and needs decoding.
                // Applying the signed decode to an ssbump sends a flat 128 to zero and the surface
                // goes black exactly where it should be evenly lit.
                float3 normal = bump.y > 0.5f ? texel.rgb : texel.rgb * 2.0f - 1.0f;

                light = CombineBumped(normal, first, second, third, bump.y > 0.5f);
            }
            else
            {
                light = lightMap.Sample(clampSampler, input.luv).rgb * OverbrightScale;
            }

            // **No doubling here.** Source's own shaders multiply an LDR lightmap by two, but that
            // applies to the raw linear samples. BspLightmaps has already taken the sample through
            // its exponent and the gamma curve into display space, so doubling again is the second
            // half of a scaling that was already applied - measured as a map washed out to white.
            // **The vertex colour is a static prop's lightmap.** It is white for everything that
            // has a real one, so this multiply is an identity for brushwork and the whole map goes
            // through one shader rather than two.
            // **A model is lit by its leaf's ambient cube, not by a lightmap.** Valve's
            // VertexShaderAmbientLight, transcribed: the squared normal weights the three axis
            // pairs, so a surface facing along an axis takes that face alone and the sum is one
            // for a unit normal.
            //
            // ambientCube[0].w says whether a cube was supplied. Without one the model keeps its
            // full brightness rather than going black, because a model lit by a cube nobody
            // measured is worse than a model that is merely too bright.
            if (ambientCube[0].w > 0.5f)
            {
                float3 nSquared = input.nrm * input.nrm;
                int3 isNegative = (int3)(input.nrm < 0.0f);

                light = nSquared.x * ambientCube[isNegative.x].rgb +
                        nSquared.y * ambientCube[isNegative.y + 2].rgb +
                        nSquared.z * ambientCube[isNegative.z + 4].rgb;

                // **The direct term, added to the ambient one rather than replacing it.** The cube
                // is the shade; the sun is what makes daylight bright. istudiorender.h describes
                // the cube as "ambient, and lights that aren't in locallight[]", so the two are
                // meant to sum.
                //
                // Lambert against the surface: a face turned away from the sun takes none of it,
                // which is what gives a model its shape instead of a flat wash.
                if (sunColour.w > 0.5f)
                {
                    float towardsSun = dot(input.nrm, -sunDirection.xyz);

                    // **Half-Lambert where the material asks for it**, which is Valve's own
                    // wrap from common_vs_fxc.h:826 — map −1..1 onto 0..1 and square it:
                    //
                    //     NDotL = NDotL * 0.5 + 0.5;
                    //     NDotL = NDotL * NDotL;
                    //
                    // A surface facing directly away from the sun then keeps a quarter of it
                    // instead of none, which is what stops a character's shaded side going black.
                    // Applied to the DIRECT term only: the routine sits inside DoLightInternal, so
                    // the ambient cube above is untouched.
                    float wrapped = saturate(towardsSun * 0.5f + 0.5f);

                    // **The half-Lambert square is SKIPPED when a light warp is present**, and this
                    // line used to square unconditionally. Valve's DiffuseTerm
                    // (common_vertexlitgeneric_dx9.h:97):
                    //
                    //     if ( bHalfLambert )
                    //     {
                    //         fResult = saturate(NDotL * 0.5 + 0.5);
                    //         if ( !bDoLightingWarp )
                    //             fResult *= fResult;          // Square
                    //     }
                    //
                    // The ramp is authored to carry that curve, so applying both squares the
                    // falloff twice — darkening every shaded side, uniformly enough to read as
                    // heavy art direction rather than as a defect.
                    //
                    // Changing a path that was correct for a year is D46: where this project's code
                    // diverges from Valve's, this project's code changes.
                    bool warping = phongFresnel.w > 0.5f;

                    float falloff = combine.y > 0.5f
                        ? (warping ? wrapped : wrapped * wrapped)
                        : saturate(towardsSun);

                    // **And the lookup is DOUBLED**, so a mid-grey ramp is neutral rather than a
                    // white one: `fOut = 2.0f * tex1D( lightWarpSampler, fResult )`. Missing the
                    // factor of two halves every model's diffuse — uniformly, so nothing looks
                    // wrong, only dim.
                    float3 direct = warping
                        ? 2.0f * lightWarp.Sample(clampSampler, float2(falloff, 0.5f)).rgb
                        : float3(falloff, falloff, falloff);

                    light += sunColour.rgb * direct;
                }

                // **And the lamps, each shaded against the normal and added** — the other half of
                // what the engine gives a model. `PixelShaderDoLightingLinear` accumulates the cube
                // and then up to four of these, so a light is in exactly one of the two and never
                // both; `LevelLighting.LightingAt` is what keeps that true on the way in.
                //
                // **The split is Valve's**: attenuation came from the vertex shader and was
                // interpolated (`VertexAttenInternal`), and only the direction and the N·L are per
                // pixel — `PixelShaderDoGeneralDiffuseLight` normalises `vPosition - worldPos` here
                // and multiplies by the attenuation it was handed.
                // **`PixelShaderDoLightingLinear`'s own nesting**, which tests a COUNT rather than
                // a flag per light and so skips the whole tail in one branch:
                //
                //     if ( nNumLights > 0 ) { ... lightAtten.x ... cLightInfo[0] ...
                //         if ( nNumLights > 1 ) { ... lightAtten.y ... } }
                //
                // Explicit swizzles and literal light numbers throughout, as they have them.
                float lamps = localLightFalloff[0].w;

                if (lamps > 0.5f)
                {
                    light += LampDiffuse(0, input.lampAtten.x, input.wpos, input.nrm);

                    if (lamps > 1.5f)
                    {
                        light += LampDiffuse(1, input.lampAtten.y, input.wpos, input.nrm);

                        if (lamps > 2.5f)
                        {
                            light += LampDiffuse(2, input.lampAtten.z, input.wpos, input.nrm);

                            if (lamps > 3.5f)
                            {
                                light += LampDiffuse(3, input.lampAtten.w, input.wpos, input.nrm);
                            }
                        }
                    }
                }
            }

            // **mat_fullbright, and it is a texture SUBSTITUTION in the engine rather than a
            // branch.** Valve replaces the lightmap with TEXTURE_LIGHTMAP_FULLBRIGHT for 1 and the
            // albedo with TEXTURE_GREY for 2 (BaseVSShader.cpp:1094, ishaderdynamic.h:60), which is
            // why both compose with everything else a material does — a substituted albedo still
            // gets its detail texture, its envmap and its alpha test. Substituting the VALUES here
            // rather than binding replacement textures reaches the same place without shipping two
            // more textures to look up per draw.
            //
            // **mat_drawflat: the texture goes, the lighting and the geometry stay.** Applied
            // before the fullbright substitutions below because it is the same KIND of thing — a
            // replacement of the albedo — and two of them fighting over the same channel would be a
            // silent precedence bug. Flat white rather than grey, which is what distinguishes it
            // from mat_fullbright 2: this one keeps the surface at full brightness so the shape
            // reads, that one dims it so the lighting reads.
            if (debugModes.x > 0.5f)
            {
                albedo.rgb = float3(1.0f, 1.0f, 1.0f);
            }

            // **mat_showlowresimage: the material drawn from its own thumbnail.** Sampled at the
            // ordinary texture coordinate, so it tiles exactly as the material does and the picture
            // differs only in resolution — which is the comparison the view exists to make.
            //
            // After mat_drawflat rather than before, so that with both on the more specific one
            // wins. Valve's are independent cvars and says nothing about the combination; the
            // choice is ours, and "the one carrying real data beats flat white" is the useful way
            // round.
            //
            // A material with no thumbnail keeps its texture rather than turning black. Every
            // shipped VTF measured carries one, but a VTF is allowed not to, and a debug view that
            // silently blanks a surface would be reporting a defect it invented.
            if (debugModes2.x > 0.5f)
            {
                float4 thumbnail = lowResMap.Sample(wrapSampler, input.uv);

                if (thumbnail.a > 0.0f)
                {
                    albedo.rgb = thumbnail.rgb;
                }
            }

            // **mat_normalmaps: the normal drawn as colour instead of lit with.** The standard
            // tangent-space encoding, so a flat surface is lilac (0.5, 0.5, 1) and a wrongly
            // decoded one is not — which is the whole value of it, because a wrong decode produces
            // plausible shading rather than an error.
            // **mat_bumpbasis: which of Valve's three basis vectors a surface leans on.** The
            // weights are already computed for bumped lighting — `saturate(dot(normal, bumpBasis[i]))`
            // squared and normalised — so this shows the quantity the lighting actually uses rather
            // than a separate calculation that could disagree with it.
            //
            // Red, green and blue are basis 0, 1 and 2 (`bumpvects.h`). A flat surface leans evenly
            // and comes out grey; a strongly bumped one takes a hue. It answers whether a normal
            // map is doing anything at all, which a lit picture cannot separate from the lighting
            // simply being uneven.
            if (debugModes.w > 0.5f)
            {
                float4 texel = bumpMap.Sample(wrapSampler, input.uv);
                float3 normal = bump.y > 0.5f ? texel.rgb : texel.rgb * 2.0f - 1.0f;

                float3 weights;
                weights.x = saturate(dot(normal, bumpBasis[0]));
                weights.y = saturate(dot(normal, bumpBasis[1]));
                weights.z = saturate(dot(normal, bumpBasis[2]));
                weights *= weights;

                float total = weights.x + weights.y + weights.z;

                // The same guard the lighting path carries: Valve divides here unchecked and a GPU
                // answers NaN, which spreads through a frame as pixels nothing explains.
                return float4(total > 0.0f ? weights / total : float3(0.0f, 0.0f, 0.0f), 1.0f);
            }

            if (debugModes.z > 0.5f)
            {
                // **Shown as STORED, for both kinds, and the ternary that was here was a no-op** —
                // both of its branches returned the same expression. Raw is right either way, and
                // for opposite reasons: a normal map read raw is the lilac that says "flat", and an
                // ssbump holds three light weights rather than a direction, so decoding it would
                // send a flat 128 to zero and read as black. Neither wants the signed decode the
                // lighting path applies, which is the point of looking at it.
                return float4(bumpMap.Sample(wrapSampler, input.uv).rgb, 1.0f);
            }

            // **mat_luxels: Valve's grid at the LIGHTMAP coordinate, so a square is a sample.**
            // Sampled with input.luv rather than input.uv — that is the entire difference between
            // this and the category view's grid, and it is what makes it report lightmap density
            // instead of texture scale.
            if (debugModes.y > 0.5f)
            {
                // **Scaled by the ATLAS's own size, so one grid cell is exactly one luxel.** The
                // first version multiplied by a flat 64, which draws a grid of no particular
                // meaning — and a debug view whose squares do not correspond to the thing being
                // measured is worse than none, because it looks like a measurement.
                //
                // The size is asked of the texture rather than passed in a constant: the shader
                // already has the lightmap bound, and a second source of truth for the atlas
                // dimensions is one that can disagree with the first.
                float2 atlas;
                lightMap.GetDimensions(atlas.x, atlas.y);

                albedo.rgb = luxelMap.Sample(wrapSampler, input.luv * atlas).rgb;
            }

            // Grey is 128/255, which is what TEXTURE_GREY holds.
            if (surfaceColours.w > 1.5f)
            {
                albedo.rgb = float3(0.5019608f, 0.5019608f, 0.5019608f);
            }
            else if (surfaceColours.w > 0.5f)
            {
                // **OverbrightScale, not one, and the measurement is what said so.** Substituting
                // the RESULT with 1 made a lit wall DARKER — (255,180,4) against (255,255,8) —
                // because a Source lightmap texel is scaled by 2 after sampling, so a fully lit
                // surface already sits above unity. Valve replaces the TEXTURE
                // (TEXTURE_LIGHTMAP_FULLBRIGHT) and the shader's own arithmetic still runs over it,
                // so the equivalent here is a white sample carried through the same scale.
                //
                // The general form: when copying a substitution, substitute at the point Valve does.
                // One step later is a different quantity that happens to have the same name.
                light = float3(OverbrightScale, OverbrightScale, OverbrightScale);
            }

            float3 lit = albedo.rgb * light * input.vc;

            // **Not in the category view.** Four of the twelve detail modes are applied AFTER the
            // lighting, so they run past the albedo substitution above and put texture back into a
            // picture whose whole job is to have none — which is what left the floors of cp_process
            // showing concrete while the buildings showed the grid. A detail texture is texture
            // information by definition, so the category view wants none of it.
            //
            // The pre-lighting combine needs no guard: the substitution happens after it and
            // overwrites the result.
            if (mode >= 0 && surfaceColours.x < 0.5f)
            {
                lit = CombineDetailAfterLighting(lit, detailColour, mode, detail.y);
            }

            // **The base texture's alpha decides which parts light themselves**, one being fully
            // unlit and zero normally lit. Applied after the lightmap, because the whole point is
            // that these parts ignore it.
            // Suppressed in the category view for the same reason as the detail combine: this
            // replaces the lit colour with the MATERIAL's own tint, so a self-illuminated surface
            // would report its material instead of its category — a lamp housing reading as
            // something other than the prop it is.
            //
            // **`$selfillummask` REPLACES that alpha where a material names one** (B327), which is
            // Valve's own arrangement written as one lerp rather than a branch:
            //
            //   float3 vSelfIllumMask = tex2D( SelfIllumMaskSampler, i.baseTexCoord.xy );
            //   vSelfIllumMask = lerp( baseColor.aaa, vSelfIllumMask, g_SelfIllumMaskControl );
            //   diffuseComponent = lerp( diffuseComponent, g_SelfIllumTint * albedo, vSelfIllumMask );
            //
            // `vertexlit_and_unlit_generic_ps2x.fxc:441-443`. Two details are Valve's and both are
            // easy to lose: the mask is sampled on the BASE coordinates rather than a set of its
            // own, and it is a full RGB rather than one channel — a mask can tint which parts glow
            // per channel, so collapsing it to a scalar would be a different effect.
            if (bump.z > 0.5f && surfaceColours.x < 0.5f)
            {
                float3 illumMask = lerp(
                    albedo.aaa,
                    selfIllumMask.Sample(wrapSampler, input.uv).rgb,
                    phongTint.w);

                lit = lerp(lit, selfIllumTint.rgb * albedo.rgb, illumMask);
            }

            // **The specular highlight, added like the reflection and for the same reason.** Valve's
            // line is `result = specularLighting*vSpecularTint + envMapColor + diffuseComponent`
            // (skin_ps20b.fxc:365).
            //
            // Gated on the SUN, which is the only direct light a model gets here. In the engine the
            // term is summed over the light cache's local lights as well, and those do not reach a
            // model in this renderer — so a highlight appears where the sun reaches and nowhere
            // else. That is a smaller effect than TF2's, and it is the honest one to draw with what
            // is decoded: a fabricated light would put highlights where no light is.
            // `surfaceColours.y` is mat_phong, and it gates the whole term rather than scaling it:
            // Valve's switch removes the feature, it does not attenuate it.
            if (surfaceColours.y > 0.5f &&
                phongControl.z > 0.5f && sunColour.w > 0.5f && eyePosition.w > 0.5f)
            {
                float3 toEye = normalize(eyePosition.xyz - input.wpos);
                float3 phongNormal = normalize(input.nrm);

                // The direction TOWARD the light. sunDirection is the direction the light travels.
                float3 toLight = -normalize(sunDirection.xyz);

                // **The EYE reflected through the normal, dotted with the light** — Valve's own
                // form, with `reflect( -vEyeDir, vWorldNormal )` left commented out beside it:
                //
                //     float3 vReflect = 2 * vWorldNormal * dot( vWorldNormal , vEyeDir ) - vEyeDir;
                //     float LdotR = saturate(dot( vReflect, vLightDir ));
                //     specularLighting = pow( LdotR, fSpecularExponent );
                float3 mirrored = (2.0f * phongNormal * dot(phongNormal, toEye)) - toEye;

                // **The exponent map, and its three sentinels** (B334). Sampled once on the base
                // coordinates and read three ways, exactly as `skin_ps20b.fxc:253-276` does.
                float4 specExp = specExpMap.Sample(wrapSampler, input.uv);

                // A NEGATIVE constant is the request to read the map, and its magnitude is the
                // scale — 149 ordinarily, or `$phongexponentfactor` where the material states one:
                //
                //     fSpecExp = (g_EyePos_SpecExponent.w >= 0.0) ? g_EyePos_SpecExponent.w
                //                                                 : (1.0f + 149.0f * vSpecExpMap.r);
                //
                // With no exponent texture the slot holds flat white, so this is `1 + 149` = 150 —
                // which is the engine's effective default for a phong material that states no
                // exponent, and NOT the 5 the parameter declares.
                float specularExponent = phongControl.x >= 0.0f
                    ? phongControl.x
                    : (1.0f + (-phongControl.x * specExp.r));

                float highlight = pow(saturate(dot(mirrored, toLight)), specularExponent);

                // **Masked by N.L, which is the half easy to drop.** Without it a highlight appears
                // on the side of the model facing AWAY from the light, which reads as a material
                // property rather than as a defect.
                highlight *= saturate(dot(phongNormal, toLight));

                float3 phong = highlight * sunColour.rgb;

                // The mask: the bump map's alpha, or the base texture's when the material says it
                // has no normal map. Valve selects both the mask and the normal with one lerp.
                float phongMask = phongControl.w > 0.5f
                    ? albedo.a
                    : bumpMap.Sample(wrapSampler, input.uv).a;

                // $phongfresnelranges, whose triple arrives pre-encoded. The expression is Valve's:
                //     f = saturate(1 - dot(N, V)); f = f*f - 0.5;
                //     ranges.y + (f >= 0 ? ranges.z : ranges.x) * f
                float edge = saturate(1.0f - dot(phongNormal, toEye));
                edge = (edge * edge) - 0.5f;

                phongMask *= phongFresnel.y +
                    ((edge >= 0.0f ? phongFresnel.z : phongFresnel.x) * edge);

                // **A NEGATIVE red is the request to tint by the albedo**, through the map's green
                // channel — `vSpecularTint = (g_SpecularTint.r >= 0.0) ? g_SpecularTint.rgb :
                // lerp( float3(1,1,1), baseColor.rgb, vSpecExpMap.g )`, `skin_ps20b.fxc:275-276`.
                // The lerp runs against the ALBEDO before any lighting, which is what makes a gold
                // weapon's highlight gold rather than white.
                float3 specularTint = phongTint.r >= 0.0f
                    ? phongTint.rgb
                    : lerp(float3(1.0f, 1.0f, 1.0f), albedo.rgb, specExp.g);

                float3 shine = phong * phongMask * phongControl.y * specularTint;

                // **The rim, folded in with MAX rather than added** — Valve's own line and their own
                // reason (skin_ps20b.fxc:359): "Fold rim lighting into specular term by using the
                // max so that we don't really add light twice". Adding double-counts on the
                // silhouette of anything shiny, which is precisely where both terms peak, and it
                // reads as a blown edge rather than as a wrong operator.
                if (rimControl.z > 0.5f)
                {
                    // The rim's own exponent, on the same L.R, with the same N.L mask.
                    float rim = pow(saturate(dot(mirrored, toLight)), rimControl.x);
                    rim *= saturate(dot(phongNormal, toLight));

                    // **$rimmask: the exponent map's ALPHA, selected by a control that is zero
                    // unless all three conditions hold** — `fRimMask = lerp( 1.0f, vSpecExpMap.a,
                    // g_RimMaskControl )`, `skin_ps20b.fxc:257`. At zero the lerp is 1 and this
                    // costs nothing, which is exactly why the parameter was inert rather than
                    // missing for as long as no exponent texture was read.
                    rim *= lerp(1.0f, specExp.a, rimControl.w);

                    // **Fresnel4, not the ranged one**, and Valve annotates the difference:
                    // "modulated with tint, mask and traditional Fresnel (not using Fresnel
                    // ranges)". A material's $phongfresnelranges must not widen its silhouette
                    // light, because that is not a control the artist has.
                    float edging = saturate(1.0f - dot(phongNormal, toEye));
                    edging = edging * edging;
                    edging = edging * edging;

                    shine = max(shine, rim * edging * sunColour.rgb);

                    // **And the half that needs no direct light at all**: the ambient cube sampled
                    // along the EYE, biased upward by the normal's height. This is what lets a model
                    // catch its surroundings on the edge in shade, and it matters more here than in
                    // the engine — TF2 gives a model several lights and this renderer gives it one.
                    if (ambientCube[0].w > 0.5f)
                    {
                        float3 alongView = -toEye;
                        float3 viewSquared = alongView * alongView;
                        int3 negative = alongView < 0.0f;

                        float3 surroundings =
                            viewSquared.x * ambientCube[negative.x].rgb +
                            viewSquared.y * ambientCube[negative.y + 2].rgb +
                            viewSquared.z * ambientCube[negative.z + 4].rgb;

                        shine += surroundings * rimControl.y * saturate(edging * phongNormal.z);
                    }
                }

                lit += shine;
            }

            // **The baked reflection, ADDED rather than blended.** Valve's line is
            // `result = diffuseComponent + specularLighting` (lightmappedgeneric_ps2_3_x.h:548) —
            // not a lerp and not a multiply. A reflection makes a surface brighter; blending
            // instead darkens every reflective surface toward the cubemap's average, which reads as
            // a wash rather than as shine and is the failure that looks almost right.
            //
            // Addition is also what makes "no cubemap" correct rather than merely absent: the term
            // starts black, so a material without one adds nothing.
            if (envmapControl.z > 0.5f && eyePosition.w > 0.5f)
            {
                float3 toEye = eyePosition.xyz - input.wpos;
                float3 eyeDirection = normalize(toEye);
                float3 surfaceNormal = normalize(input.nrm);

                // The mirrored view direction. reflect() takes the INCIDENT direction, which is
                // from the eye toward the surface, so the eye vector is negated.
                float3 reflection = reflect(-eyeDirection, surfaceNormal);

                float3 specular = envMap.Sample(wrapSampler, reflection).rgb;

                // **The mask comes FIRST, before the tint and therefore before contrast.** Both of
                // Valve's shaders order it `specularLighting *= specularFactor` and only then the
                // tint (lightmappedgeneric_ps2_3_x.h:535, vertexlit_and_unlit_generic_ps2x.fxc:457).
                // Contrast squares, and squaring is not linear, so masking after it is a different
                // picture -- this used to mask last.
                //
                // **envmapControl.y is a MODE, not a flag, because the two masks pull opposite
                // ways.** 1 is $basealphaenvmapmask and 2 is $normalmapalphaenvmapmask. They are
                // mutually exclusive by construction -- the shader declares
                // `SKIP: $NORMALMAPALPHAENVMAPMASK && $BASEALPHAENVMAPMASK` -- so a mode is exactly
                // the right shape and a pair of flags would admit a state Valve forbids.
                if (envmapControl.y > 1.5f)
                {
                    // **NOT inverted**, which is the whole reason this is a separate branch:
                    // `specularFactor = normalTexel.a`, assigned rather than subtracted from one
                    // (vertexlit_and_unlit_generic_bump_ps2x.fxc:169). An alpha of 1 reflects MOST.
                    //
                    // Sampled here rather than reused from the bumped-lighting branch above,
                    // because that branch only runs for a world face with three lightmap sets --
                    // and this mask is on MODELS, which have none. Valve samples the bump once and
                    // takes .a for the mask regardless of whether it lights with it.
                    specular *= bumpMap.Sample(wrapSampler, input.uv).a;
                }
                else if (envmapControl.y > 0.5f)
                {
                    // **Inverted, and Valve said so: "Reversing alpha blows!"** An opaque texel
                    // reflects LEAST. Getting this backwards puts the shine exactly where the
                    // artist masked it out.
                    specular *= 1.0f - albedo.a;
                }

                specular *= envmapTint.rgb;

                // **Contrast then saturation, in that order.** Squaring is not linear, so the order
                // is part of the specification rather than an implementation detail (lines 537-544).
                specular = lerp(specular, specular * specular, envmapTint.w);

                // Rec.601 luma, not a third each. The weights sum to one, so a grey reflection is
                // unchanged either way -- which is why an average passes a casual check and greens
                // what should stay red.
                float grey = dot(specular, float3(0.299f, 0.587f, 0.114f));
                specular = lerp(float3(grey, grey, grey), specular, envmapControl.x);

                // **Schlick, and then Valve throws most of it away.** The engine computes the same
                // fifth power and immediately remaps it (lightmappedgeneric_ps2_3_x.h:532):
                //
                //     fresnel = fresnel * g_OneMinusFresnelReflection + g_FresnelReflection;
                //
                // packed from ONE material parameter as [0, 0, 1-R, R]
                // (lightmappedgeneric_dx9_helper.cpp:728), so the term is `schlick * (1 - R) + R`
                // for R = $fresnelreflection. **R defaults to 1** -- "1.0 == mirror, 0.0 == water"
                // -- which collapses the whole thing to a constant.
                //
                // **This shader applied raw Schlick, and that was the reason nothing looked
                // reflective.** A surface viewed anywhere near head-on keeps a few percent of its
                // reflection under it, so a flat capture-point disc seen from standing height
                // reflected essentially nothing -- while every assertion about the cube being
                // sampled passed, because at a grazing angle it is.
                //
                // envmapControl.w is R. It is 1 for every model material regardless of the VMT,
                // because VertexLitGeneric's envmap block has no Fresnel term at all.
                float schlick = pow(saturate(1.0f - dot(surfaceNormal, eyeDirection)), 5.0f);
                specular *= (schlick * (1.0f - envmapControl.w)) + envmapControl.w;

                // **mat_specular, and Valve's own wording for it is "get rid of envmap".** The
                // engine does it one level up, by undefining $envmap when the config says
                // UseSpecular() is false (lightmappedgeneric_dx9_helper.cpp:166,
                // BaseVSShader.cpp:2148) — which is why changing it reloads every material. Here
                // the envmap is per-material constants rather than a compiled-in branch, so the
                // same result comes from not adding the term. The observable output is identical;
                // what differs is that ours costs nothing to toggle.
                // **Not in the category view**, now that it is lit rather than returning early. A
                // reflection is a picture of somewhere else added on top, which is noise in a view
                // whose whole job is to say what a surface IS — and a strongly reflective prop
                // would read as a different category. The lighting is wanted for shape; the
                // reflection is not.
                if (surfaceColours.z > 0.5f && surfaceColours.x < 0.5f)
                {
                    lit += specular;
                }
            }

            return float4(lit, albedo.a);
        }
        """;

    private ComPtr<ID3D11VertexShader> _vertexShader;
    private ComPtr<ID3D11PixelShader> _pixelShader;
    private ComPtr<ID3D11InputLayout> _layout;
    private ComPtr<ID3D11Buffer> _vertices;
    private ComPtr<ID3D11Buffer> _camera;
    private ComPtr<ID3D11Buffer> _model;

    /// <summary>The bone matrices skinning the current draw.</summary>
    private ComPtr<ID3D11Buffer> _bones;

    private Dictionary<string, IReadOnlyList<IReadOnlyList<WorldBatch>>> _modelBatches =
        new(StringComparer.OrdinalIgnoreCase);
    private ComPtr<ID3D11SamplerState> _wrapSampler;
    private ComPtr<ID3D11SamplerState> _clampSampler;

    /// <summary>Edge of the chequer drawn where a material could not be resolved.</summary>
    private const int MissingSize = 32;

    /// <summary>Squares across the chequer, as Source draws it.</summary>
    private const int MissingSquares = 4;

    /// <summary>
    /// Uploads one texture, forcing an opaque material to be opaque.
    /// </summary>
    /// <remarks>
    /// **The shader clips on alpha, so a material that never asked for that must not carry it.** A
    /// VTF's alpha channel is only meaningful when the material says <c>$alphatest</c> or
    /// <c>$translucent</c>; plenty of opaque textures store something else there, or nothing, and
    /// clipping on it would punch holes through solid walls. Forcing it here means one shader path
    /// serves both without a per-batch switch.
    /// </remarks>
    /// <summary>Uploads one texture, for a caller outside this type's material table.</summary>
    /// <param name="device">The device.</param>
    /// <param name="context">The device context.</param>
    /// <param name="texture">The image, or null for no texture.</param>
    /// <returns>A view, or a default handle when there was nothing to upload.</returns>
    /// <remarks>
    /// **Exposed for the 2D skybox, whose materials are not in the map's table at all** — sky
    /// brushes carry `tools/toolsskybox` and the sky itself comes from `worldspawn`'s `skyname`
    /// (B303). Sharing this rather than copying it keeps one answer to what a VTF becomes.
    /// </remarks>
    internal static ComPtr<ID3D11ShaderResourceView> UploadTexture(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, MapTexture? texture) =>
        Upload(device, context, texture);

    private static ComPtr<ID3D11ShaderResourceView> Upload(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, MapTexture? texture)
    {
        if (texture is not { } present)
        {
            return default;
        }

        // **Alpha is uploaded as it was authored, and nothing is flattened.**
        //
        // This used to force alpha to 255 for anything not transparent or self-illuminated, and
        // the reason was a workaround: the pixel shader clipped on alpha unconditionally, so an
        // opaque material whose texture carried a stray alpha channel would have been cut away
        // entirely.
        //
        // That clip is now gated on the material's own $alphatest flag, which is what the engine
        // does - EnableAlphaTest( IS_FLAG_SET(MATERIAL_VAR_ALPHATEST) ) - so the workaround
        // protects nothing and costs the alpha that decals and translucent materials need to blend
        // with. A decal drawn against a flattened alpha paints its whole quad as solid colour,
        // which is what made the patch under a health pack look like a placeholder marker.
        return CreateTexture(device, context, present.Width, present.Height, present.Image);
    }

    /// <summary>Builds the missing-material chequer: magenta and black, like the engine's.</summary>
    private static byte[] Missing()
    {
        byte[] pixels = new byte[MissingSize * MissingSize * 4];
        int square = MissingSize / MissingSquares;

        for (int y = 0; y < MissingSize; y++)
        {
            for (int x = 0; x < MissingSize; x++)
            {
                bool magenta = ((x / square) + (y / square)) % 2 == 0;
                int at = ((y * MissingSize) + x) * 4;

                pixels[at + 0] = magenta ? (byte)255 : (byte)0;
                pixels[at + 1] = 0;
                pixels[at + 2] = magenta ? (byte)255 : (byte)0;
                pixels[at + 3] = 255;
            }
        }

        return pixels;
    }

    /// <summary>Rasteriser state that draws both sides of a triangle.</summary>
    /// <remarks>
    /// **D3D culls back faces by default, and displacement terrain wound the other way.** The
    /// quads that come straight out of the BSP wind one way; the grid this project builds when it
    /// subdivides a displacement winds the other, so every terrain triangle was discarded and the
    /// ground of mid and second rendered as background. It looked exactly like a black texture.
    ///
    /// Culling is turned off rather than the winding corrected, because the winding is not the
    /// thing being relied on: which faces to draw is already decided by their NORMAL, in
    /// BspGeometry and MapWorldBuilder, where a downward-facing surface is dropped. Asking the
    /// rasteriser to make the same decision from vertex order is a second source of truth that can
    /// disagree with the first - and did.
    /// </remarks>
    private ComPtr<ID3D11RasterizerState> _bothSides;

    /// <summary>Back faces culled, for models — Source culls them and their materials expect it.</summary>
    private ComPtr<ID3D11RasterizerState> _modelCull;

    /// <summary>The same state wound the other way, for a mirrored viewmodel.</summary>
    private ComPtr<ID3D11RasterizerState> _viewmodelCull;

    private readonly List<ComPtr<ID3D11ShaderResourceView>> _textures = [];

    /// <summary>Which table index each whole-model override material took, by VMT path.</summary>
    /// <remarks>
    /// **A path in and an ordinary material index out**, so an override needs no second bind path of
    /// its own — see <see cref="MapAssets.OverrideMaterials"/>, which appends them to the same table
    /// every face and every model batch indexes. `DrawModel` substitutes the index and everything
    /// downstream follows: textures, detail, bump, cubemap, light warp, constants, proxies, blend
    /// and depth.
    /// </remarks>
    private IReadOnlyDictionary<string, int> _overrideMaterials =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Which materials the engine adds rather than paints, by index.</summary>
    /// <remarks>
    /// **Drawn in a second pass with additive blending, which is what the engine does.** Source
    /// returns BT_ADD for <c>$additive</c>, so a light cone brightens what it covers and its dark
    /// parts contribute nothing. Drawn opaque it is a solid black cone; skipped entirely - which
    /// this did first - the glow is simply missing. Neither is what the map says.
    ///
    /// Five of cp_process_f12's 285 materials are additive.
    /// </remarks>
    private readonly HashSet<int> _additive = [];

    /// <summary>Materials blended with what is behind them, drawn last and sorted.</summary>
    private readonly HashSet<int> _translucent = [];

    /// <summary>Materials that mark a surface rather than being one — <c>$decal</c>.</summary>
    /// <remarks>
    /// **The key that makes depth state a property of the MATERIAL (B135).** In Source every shader
    /// declares its own render state in a <c>SHADOW_STATE</c> block and the material system applies
    /// it on bind, so passes can be reordered freely and none inherits anything. This project set
    /// state per pass instead, and the same defect appeared twice from opposite directions: a
    /// translucent pass leaving a read-only depth state behind, so models drew without depth writes
    /// (B72); and an overlay pass leaving the same state behind, so static props did (B135).
    ///
    /// <c>SetMaterial</c> now chooses the depth state from this and the blend sets above, which is
    /// the engine's arrangement rather than a rule about who tidies up.
    /// </remarks>
    private readonly HashSet<int> _decalMaterials = [];

    /// <summary>Materials that multiply what is behind them; the value says whether it doubles.</summary>
    private readonly Dictionary<int, bool> _modulate = [];

    /// <summary>Materials that draw from both sides — the engine's MATERIAL_VAR_NOCULL.</summary>
    private readonly HashSet<int> _noCull = [];

    /// <summary>The translucent batches, farthest first.</summary>
    private IReadOnlyList<WorldBatch> _sortedTranslucent = [];

    /// <summary>The decal batches, drawn over the world with a depth bias.</summary>
    private IReadOnlyList<WorldBatch> _decals = [];

    /// <summary>Depth state for an opaque pass: tested and written, nearer wins.</summary>
    /// <remarks>
    /// **Owned here because the props pass has to ESTABLISH it, not inherit it (B135).** The overlay
    /// pass immediately before sets depth writes off — correctly, a marking must not occlude — and
    /// the props pass then ran with that state, so static props stopped writing depth altogether.
    /// Two props no longer occluded each other, and whichever drew last won per pixel: on
    /// cp_process's mid, the rocks behind a shipping container drew straight through it.
    ///
    /// Exactly the defect B72 records, in the other direction — there a translucent pass left a
    /// read-only state behind and the model pass inherited it. The rule it produced is the one that
    /// was broken here: **a pass that depends on a state establishes it.**
    /// </remarks>
    private ComPtr<ID3D11DepthStencilState> _depthWrite;

    /// <summary>Static props, drawn after the overlays.</summary>
    /// <remarks>
    /// **Their own list because the engine draws them in their own pass (B135).**
    /// <c>CBaseWorldView::DrawExecute</c> runs <c>DrawWorld</c> — surfaces and their overlay
    /// fragments — and then <c>DrawOpaqueRenderables</c>, which is where static props and brush
    /// models go. Batched with the world they were in the depth buffer before the overlays, so a
    /// biased overlay could paint over a pipe an inch in front of the wall it marks.
    /// </remarks>
    private IReadOnlyList<WorldBatch> _props = [];

    /// <summary>Depth state for the overlay pass, built from <see cref="DecalState"/>.</summary>
    /// <remarks>
    /// Tested, never written, compared <c>LessEqual</c>. The values and the reasoning behind each
    /// are on <see cref="DecalState"/>, which is also what
    /// <c>DecalRenderStateConformanceTests</c> compares against Valve's — deliberately one place,
    /// so a number and its justification cannot drift apart.
    ///
    /// Valve's decal shaders set this for sprayed decals rather than for overlays, so applying it
    /// here is an interpolation (D44) rather than a transcription. The reasoning stands on its own:
    /// nothing that marks a surface should occlude what stands in front of it.
    /// </remarks>
    private ComPtr<ID3D11DepthStencilState> _decalDepth;

    /// <summary>Rasteriser state for the overlay pass, built from <see cref="DecalState"/>.</summary>
    /// <remarks>
    /// Back faces culled, no constant bias, Valve's slope-scaled term. Each of those three and its
    /// reason are on <see cref="DecalState"/>.
    ///
    /// **The history worth keeping here is what the constant bias did when it was applied.** At
    /// 262144/2²⁴ it is 1.6% of the depth range, and this projection is orthographic over a whole
    /// map's height — so 1.6% of a 1,600-unit range is **twenty-five world units**, taller than a
    /// health pack. The visible result was a marking painted over the pickup standing on it, with
    /// the pack's shape faintly showing through, reported as "the health packs are not drawing" and
    /// chased through the model pipeline for an evening. Comparing against TF2 itself is what
    /// settled it: in game the pack sits clearly on top of a much smaller patch.
    /// </remarks>
    private ComPtr<ID3D11RasterizerState> _decalOffset;

    /// <summary>Blend state that ADDS a fragment to what is already there.</summary>
    private ComPtr<ID3D11BlendState> _addBlend;
    private readonly List<ComPtr<ID3D11ShaderResourceView>> _blendTextures = [];
    private ComPtr<ID3D11ShaderResourceView> _lightmap;
    private ComPtr<ID3D11ShaderResourceView> _white;

    /// <summary>A genuinely white 1x1, for a material that legitimately has no base texture.</summary>
    /// <remarks>
    /// **<see cref="_white"/> is the magenta chequer despite its name**, so anything wanting a
    /// neutral has to use this instead. See B62: a `Water` material declares no `$basetexture` and
    /// binding the chequer for it drew a broken-content marker on a healthy map.
    /// </remarks>
    private ComPtr<ID3D11ShaderResourceView> _flatWhite;

    /// <summary>Materials with no base texture whose SHADER does not want one.</summary>
    /// <remarks>
    /// Water, today. Kept apart from <see cref="_chequered"/> because the two mean opposite things:
    /// one is content that failed to load, the other is content that was never supposed to be there.
    /// </remarks>
    private readonly HashSet<int> _untextured = [];

    /// <summary>Material indices seen outside the table, so each is reported once.</summary>
    private readonly HashSet<int> _unindexed = [];

    /// <summary>Valve's measurement grid, drawn under the category tint.</summary>
    private ComPtr<ID3D11ShaderResourceView> _devGrid;

    /// <summary>Materials that resolved to nothing and draw as the missing-material chequer.</summary>
    /// <remarks>
    /// **Kept so the category view can show Valve's chequer rather than a colour standing in for
    /// it.** An unresolved material is magenta-and-black chequered in the engine and in every Source
    /// tool, and that pattern is the signal — a flat colour saying "missing" is a translation of it,
    /// and the owner's rule is that where Valve has a real appearance we use it: "if valves stuff is
    /// suppose to be checkered then we need to do that".
    /// </remarks>
    private readonly HashSet<int> _chequered = [];

    /// <summary>Valve's luxel grid, sampled at lightmap coordinates for mat_luxels.</summary>
    private ComPtr<ID3D11ShaderResourceView> _luxelGrid;

    /// <summary>The detail pattern for each material, empty where it has none.</summary>
    /// <summary>Each material's VTF thumbnail, for <c>mat_showlowresimage</c>.</summary>
    /// <remarks>
    /// Indexed by material like <see cref="_textures"/>, with a default handle where the file
    /// carried no thumbnail. Kept as its own list rather than folded into the texture list because
    /// the two are bound to different slots and only one of them is ever drawn.
    /// </remarks>
    private readonly List<ComPtr<ID3D11ShaderResourceView>> _thumbnails = [];

    private readonly List<ComPtr<ID3D11ShaderResourceView>> _details = [];

    /// <summary>The bump map for each material, empty where it has none.</summary>
    private readonly List<ComPtr<ID3D11ShaderResourceView>> _bumps = [];

    /// <summary>The baked reflection for each material, as a cube view, or a null handle.</summary>
    private readonly List<ComPtr<ID3D11ShaderResourceView>> _cubemaps = [];

    /// <summary>
    /// Every cubemap the map baked, uploaded, in the same order as <see cref="_placements"/>.
    /// </summary>
    /// <remarks>
    /// **Indexed by PLACEMENT, not by material**, which is the whole difference between this and
    /// <see cref="_cubemaps"/>. A brush face reflects the cube vbsp named in its material; a model
    /// reflects whichever placement it stands nearest, so the same material on two props in two
    /// rooms takes two different cubes and neither can be attached to the material.
    /// </remarks>
    private readonly List<ComPtr<ID3D11ShaderResourceView>> _placedCubemaps = [];

    /// <summary>Where each of <see cref="_placedCubemaps"/> stands, in world units.</summary>
    private readonly List<BspCubemap> _placements = [];

    /// <summary>Model paths whose chosen cubemap has been reported, so each is said once.</summary>
    private readonly HashSet<string> _reportedCubemap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Material indices whose reflection parameters have been reported (B170).</summary>
    private readonly HashSet<int> _reportedEnvmap = [];


    /// <summary>Model paths whose drawn material indices have been reported (B170).</summary>
    private readonly HashSet<string> _reportedBatchMaterials =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The materials that reflect the map's own cubemap rather than one of their own.
    /// </summary>
    /// <remarks>
    /// These asked for the literal <c>env_cubemap</c>. <c>LightmappedGeneric</c> refuses that on
    /// brushwork and <c>VertexLitGeneric</c> keeps it, so in practice this is every reflective
    /// model material on the map — and the reason a prop needs a search a wall does not.
    /// </remarks>
    private readonly HashSet<int> _usesLocalCubemap = [];

    /// <summary>The authored lighting ramp for each material, empty where there is none.</summary>
    private readonly List<ComPtr<ID3D11ShaderResourceView>> _lightWarps = [];

    /// <summary>Each material's <c>$selfillummask</c>, or a null handle where it has none.</summary>
    private readonly List<ComPtr<ID3D11ShaderResourceView>> _selfIllumMasks = [];

    /// <summary>The fixed multiplier the exponent map's red channel is scaled by.</summary>
    /// <remarks>
    /// <c>fSpecExp = 1.0f + 149.0f * vSpecExpMap.r</c> (<c>skin_ps20b.fxc:268</c>), so the map's
    /// 0..1 spans exponents 1 to 150. <c>$phongexponentfactor</c> replaces this number for the 60
    /// materials that state one.
    /// </remarks>
    private const float ExponentFromMapScale = 149f;

    /// <summary>Each material's <c>$phongexponenttexture</c>, or a null handle (B334).</summary>
    /// <remarks>
    /// **A null handle here means WHITE, not "skip"**, because that is what the engine binds when a
    /// material names no exponent texture (<c>skin_dx9_helper.cpp:565</c>). The shader reads the
    /// slot unconditionally and its three channels all rest at 1, which is exactly the neutral the
    /// arithmetic wants — an exponent of 150, an untouched tint and an unmasked rim. A sampler read
    /// with no view bound returns ZERO in D3D11, which would give an exponent of 1 and a flooded
    /// highlight, so <see cref="_flatWhite"/> is bound in its place.
    ///
    /// **<c>_flatWhite</c> and not <c>_white</c>**, which despite its name is Valve's magenta
    /// chequer — <c>docs/memory/a-neutral-default-must-be-neutral.md</c> is about exactly this
    /// substitution going wrong once already.
    /// </remarks>
    private readonly List<ComPtr<ID3D11ShaderResourceView>> _phongExponentMaps = [];

    /// <summary>Each material's <c>$colortint_base</c>, null where it is not tintable (B330).</summary>
    /// <remarks>
    /// **Non-null is what marks a material as running TF2's paint chain**, so this doubles as the
    /// gate: a material with no `$colortint_base` never builds the variable table and its proxies
    /// cost nothing.
    /// </remarks>
    private readonly List<(float Red, float Green, float Blue)?> _tintBases = [];

    /// <summary>Each material's declared numeric parameters, for its proxy chain (B340).</summary>
    /// <remarks>
    /// Null for the great majority — a material running no proxy has nothing to look up. What this
    /// buys is that a proxy reading a declared CONSTANT finds it, which the engine's `FindVar`
    /// does and a table seeded from proxy outputs alone does not.
    /// </remarks>
    private readonly List<IReadOnlyDictionary<string, (float Red, float Green, float Blue)>?>
        _variables = [];

    /// <summary>Each material's base-texture animation frames, empty where it has none (B341).</summary>
    /// <remarks>
    /// **Uploaded per material rather than per texture path, and that is a deliberate limit.** The
    /// 152 materials animating `$basetexture` mostly animate their own texture, so the duplication
    /// is small; the 6,735 animating `$detail` nearly all animate ONE file — 121 frames of TF2's
    /// fire sheet — and doing those this way would decode it thousands of times. Those need a cache
    /// keyed by path, which B338 records as the design and this is not.
    /// </remarks>
    private readonly List<ComPtr<ID3D11ShaderResourceView>[]> _animationFrames = [];

    /// <summary>Each material's animation rate, from its <c>AnimatedTexture</c> proxy (B341).</summary>
    private readonly List<float> _animationRates = [];

    /// <summary>Each material's <c>$color</c> alone, which <c>$color2</c> multiplies against.</summary>
    private readonly List<(float Red, float Green, float Blue)> _colourFactors = [];

    /// <summary>The proxies each material runs, empty for the great majority.</summary>
    private IReadOnlyList<IReadOnlyList<MaterialProxy>> _proxies = [];

    /// <summary>Playback time, standing in for the engine's <c>gpGlobals-&gt;curtime</c>.</summary>
    /// <remarks>
    /// **The one input every time-driven proxy takes.** Demo playback time rather than wall clock,
    /// so a paused demo holds its beams still and a seek moves them to where they were — which is
    /// what makes a capture point look the same on two viewings of the same tick.
    /// </remarks>
    public double Seconds { get; set; }

    /// <summary>Scale, blend factor, mode and tint per material, in the shader's own layout.</summary>
    /// <remarks>
    /// **Eight floats each, built once at upload.** A mode of -1 means the material has no detail,
    /// which is what the shader tests; packing it into the same buffer keeps the draw loop free of
    /// a branch and the shader free of a second constant.
    /// </remarks>
    private readonly List<float[]> _detailParameters = [];

    /// <summary>The per-material constants, rewritten between draws.</summary>
    private ComPtr<ID3D11Buffer> _material;

    /// <summary>Source-alpha blending, for translucent materials.</summary>
    private ComPtr<ID3D11BlendState> _alphaBlend;

    /// <summary>Multiplies the framebuffer by the texture, for Source's Modulate shader.</summary>
    private ComPtr<ID3D11BlendState> _modulateBlend;

    /// <summary>The same, doubled, for $mod2x.</summary>
    private ComPtr<ID3D11BlendState> _modulateTwiceBlend;

    /// <summary>Depth tested but not written, so a blended surface does not occlude.</summary>
    private ComPtr<ID3D11DepthStencilState> _depthReadOnly;

    private IReadOnlyList<WorldBatch> _batches = [];

    /// <summary>The world runs this view can see, or null to draw every one.</summary>
    /// <remarks>
    /// **Null and empty mean different things here, and conflating them is a black screen.** Null is
    /// "nobody culled" — no map visibility, or a camera set through the matrix overload — and falls
    /// back to the batches built at load. Empty is "this eye sees no world", which is a real answer
    /// for a camera pointed into the void.
    ///
    /// Set on a view change, like the camera, because it is a function of the camera.
    ///
    /// **The OPAQUE world pass only.** The translucent runs are depth-sorted once at upload and the
    /// additive ones walk the full list; both therefore keep drawing everything. That is the
    /// conservative direction — more work, never a missing surface — and culling them properly means
    /// re-sorting by depth per view, which is its own piece of work.
    /// </remarks>
    public IReadOnlyList<WorldBatch>? VisibleBatches { get; set; }

    /// <summary>What the world pass will actually draw.</summary>
    private IReadOnlyList<WorldBatch> Drawn => VisibleBatches ?? _batches;

    /// <summary>How many world batches were uploaded, for the cull's report to compare against.</summary>
    public int BatchCount => _batches.Count;

    /// <summary>Where this reports what it drew, and what it silently could not.</summary>
    /// <remarks>
    /// **Two categories rather than one, because this writes to two areas (D83).** Most of what it
    /// says is `render`; the cubemap refusal is an `assets` fact and was written as one. A logger's
    /// category is the old area string, so keeping both preserves the file exactly — and merging
    /// them would quietly reclassify a line that somebody may be grepping for.
    /// </remarks>
    private readonly ILogger _render;

    private readonly ILogger _assets;

    private WorldRenderer(
        ILoggerFactory loggers,
        ComPtr<ID3D11VertexShader> vertexShader,
        ComPtr<ID3D11PixelShader> pixelShader,
        ComPtr<ID3D11InputLayout> layout,
        ComPtr<ID3D11SamplerState> wrapSampler,
        ComPtr<ID3D11SamplerState> clampSampler)
    {
        ArgumentNullException.ThrowIfNull(loggers);

        _render = loggers.CreateLogger("render");
        _assets = loggers.CreateLogger("assets");
        _vertexShader = vertexShader;
        _pixelShader = pixelShader;
        _layout = layout;
        _wrapSampler = wrapSampler;
        _clampSampler = clampSampler;
    }

    /// <summary>Whether a map has been uploaded.</summary>
    public bool HasMap => _batches.Count > 0;

    /// <summary>Compiles the shaders and creates the samplers.</summary>
    /// <param name="device">Device to create resources on.</param>
    /// <param name="loggers">Where to report what was drawn, and what silently was not.</param>
    /// <returns>The renderer.</returns>
    public static WorldRenderer Create(ComPtr<ID3D11Device> device, ILoggerFactory loggers)
    {
        using D3DCompiler compiler = D3DCompiler.GetApi();

        ComPtr<ID3D10Blob> vertexBytecode = Compile(compiler, "VsMain", "vs_5_0");
        ComPtr<ID3D10Blob> pixelBytecode = Compile(compiler, "PsMain", "ps_5_0");

        ComPtr<ID3D11VertexShader> vertexShader = default;
        SilkMarshal.ThrowHResult(device.CreateVertexShader(
            vertexBytecode.GetBufferPointer(),
            vertexBytecode.GetBufferSize(),
            ref Unsafe.NullRef<ID3D11ClassLinkage>(),
            ref vertexShader));

        ComPtr<ID3D11PixelShader> pixelShader = default;
        SilkMarshal.ThrowHResult(device.CreatePixelShader(
            pixelBytecode.GetBufferPointer(),
            pixelBytecode.GetBufferSize(),
            ref Unsafe.NullRef<ID3D11ClassLinkage>(),
            ref pixelShader));

        byte* position = (byte*)SilkMarshal.StringToPtr("POSITION");
        byte* texcoord = (byte*)SilkMarshal.StringToPtr("TEXCOORD");

        InputElementDesc[] elements =
        [
            new()
            {
                SemanticName = position,
                Format = Silk.NET.DXGI.Format.FormatR32G32B32Float,
                AlignedByteOffset = 0,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = texcoord,
                SemanticIndex = 0,
                Format = Silk.NET.DXGI.Format.FormatR32G32Float,
                AlignedByteOffset = sizeof(float) * 3,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = texcoord,
                SemanticIndex = 1,
                Format = Silk.NET.DXGI.Format.FormatR32G32Float,
                AlignedByteOffset = sizeof(float) * 5,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = texcoord,
                SemanticIndex = 2,
                Format = Silk.NET.DXGI.Format.FormatR32Float,
                AlignedByteOffset = sizeof(float) * 7,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = texcoord,
                SemanticIndex = 3,
                Format = Silk.NET.DXGI.Format.FormatR32G32B32Float,
                AlignedByteOffset = sizeof(float) * 8,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = texcoord,
                SemanticIndex = 4,
                Format = Silk.NET.DXGI.Format.FormatR32Float,
                AlignedByteOffset = sizeof(float) * 11,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = texcoord,
                SemanticIndex = 5,
                Format = Silk.NET.DXGI.Format.FormatR32G32B32Float,
                AlignedByteOffset = sizeof(float) * 12,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = texcoord,
                SemanticIndex = 6,
                Format = Silk.NET.DXGI.Format.FormatR32G32B32Float,
                AlignedByteOffset = sizeof(float) * 15,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = texcoord,
                SemanticIndex = 7,
                Format = Silk.NET.DXGI.Format.FormatR32G32B32Float,
                AlignedByteOffset = sizeof(float) * 18,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = texcoord,
                SemanticIndex = 8,
                Format = Silk.NET.DXGI.Format.FormatR32G32B32Float,
                AlignedByteOffset = sizeof(float) * 21,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = texcoord,
                SemanticIndex = 9,
                Format = Silk.NET.DXGI.Format.FormatR32G32B32Float,
                AlignedByteOffset = sizeof(float) * 24,
                InputSlotClass = InputClassification.PerVertexData,
            },
        ];

        ComPtr<ID3D11InputLayout> layout = default;

        fixed (InputElementDesc* first = elements)
        {
            SilkMarshal.ThrowHResult(device.CreateInputLayout(
                first,
                (uint)elements.Length,
                vertexBytecode.GetBufferPointer(),
                vertexBytecode.GetBufferSize(),
                ref layout));
        }

        SilkMarshal.Free((nint)position);
        SilkMarshal.Free((nint)texcoord);
        vertexBytecode.Dispose();
        pixelBytecode.Dispose();

        // **Wrap for the texture, clamp for the lightmap.** A wall repeats its texture, so its
        // coordinates run well outside 0..1 and must wrap. A lightmap coordinate never should, and
        // wrapping one would pull in a completely unrelated face's light from the far side of the
        // atlas.
        RasterizerDesc rasterizer = new()
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
        };

        ComPtr<ID3D11RasterizerState> bothSides = default;
        SilkMarshal.ThrowHResult(device.CreateRasterizerState(in rasterizer, ref bothSides));

        // **Models cull their back faces, and the winding is read from the engine rather than
        // guessed.** `imaterialsystem.h:180` defines MATERIAL_CULLMODE_CCW as "this culls polygons
        // with counterclockwise winding", and it is the mode the engine restores to after
        // temporarily flipping for a mirrored view (c_baseviewmodel.cpp:375-379,
        // econ_entity.cpp:862-868). Front faces are therefore CLOCKWISE, which is D3D's default
        // FrontCounterClockwise = false with the back faces culled.
        //
        // Drawn both-sided, a capture point's hologram shows the far side of its disc through the
        // near side and the sign is unreadable — which is what remained once the blending was
        // right.
        //
        // TF2VIEW_MODEL_CULL overrides it for experiments only. The default is what the engine
        // does; the override exists to test a hypothesis by eye, never to stand in for reading
        // what the engine does.
        RasterizerDesc modelRasterizer = rasterizer;

        modelRasterizer.CullMode = Environment.GetEnvironmentVariable("TF2VIEW_MODEL_CULL") switch
        {
            "none" => CullMode.None,
            "front" => CullMode.Front,
            _ => CullMode.Back,
        };

        ComPtr<ID3D11RasterizerState> culled = default;
        SilkMarshal.ThrowHResult(device.CreateRasterizerState(in modelRasterizer, ref culled));

        // **The mirror of the above, for a viewmodel.** Same state with the winding reversed,
        // created once rather than switched per draw: a rasterizer state is immutable in D3D11 and
        // building one mid-frame would be the expensive way to say the same thing.
        RasterizerDesc mirroredRasterizer = modelRasterizer;
        mirroredRasterizer.CullMode = CullMode.Front;

        ComPtr<ID3D11RasterizerState> mirrored = default;
        SilkMarshal.ThrowHResult(
            device.CreateRasterizerState(in mirroredRasterizer, ref mirrored));

        // **The overlay pass's rasteriser, entirely from DecalState.** Back faces culled, no
        // constant bias, Valve's slope-scaled term — each value's reasoning, its citation and the
        // measurement behind it live on that type, which is also what the conformance test compares
        // against Valve's own numbers. Nothing here is a literal, deliberately: a value the test
        // reads and a value the state is built from have to be the same value.
        RasterizerDesc biased = rasterizer;

        biased.CullMode = DecalState.Cull;
        biased.DepthBias = DecalState.ConstantBias;
        biased.SlopeScaledDepthBias = DecalState.SlopeScaledBias;

        ComPtr<ID3D11RasterizerState> decalOffset = default;
        SilkMarshal.ThrowHResult(device.CreateRasterizerState(in biased, ref decalOffset));

        // **A wireframe twin of every state, because `mat_wireframe` changes the FILL and nothing
        // else.** Valve's is `MATERIAL_FILLMODE_WIREFRAME`, applied to whatever is being drawn, so
        // each pass keeps its own culling and its own depth bias and differs only in fill. Building
        // one shared wireframe state instead would quietly answer a different question — "what is
        // in the vertex buffer" rather than "what is being drawn" — and the difference between
        // those two is exactly what a missing-geometry hunt turns on.
        //
        // Created up front because a D3D11 rasteriser state is immutable; making one mid-frame is
        // the expensive way to say the same thing.
        Dictionary<nint, ComPtr<ID3D11RasterizerState>> wireframe = [];

        void AddWire(ComPtr<ID3D11RasterizerState> solid, RasterizerDesc description)
        {
            description.FillMode = FillMode.Wireframe;

            ComPtr<ID3D11RasterizerState> wire = default;
            SilkMarshal.ThrowHResult(device.CreateRasterizerState(in description, ref wire));

            wireframe[(nint)solid.Handle] = wire;
        }

        AddWire(bothSides, rasterizer);
        AddWire(culled, modelRasterizer);
        AddWire(mirrored, mirroredRasterizer);
        AddWire(decalOffset, biased);

        return new WorldRenderer(
            loggers,
            vertexShader,
            pixelShader,
            layout,
            Sampler(device, TextureAddressMode.Wrap),
            Sampler(device, TextureAddressMode.Clamp))
        {
            _bothSides = bothSides,
            _modelCull = culled,
            _viewmodelCull = mirrored,
            _decalOffset = decalOffset,
            _wireframeFor = wireframe,
        };
    }

    /// <summary>Whether every pass draws in wireframe — Valve's <c>mat_wireframe</c>.</summary>
    /// <remarks>
    /// **The instrument that separates "not drawn" from "drawn invisibly".** Those two produce the
    /// identical picture — an absent surface — and every other diagnostic in this renderer answers
    /// one of them at a time. A wireframe answers it directly: an edge on screen means the triangle
    /// reached the rasteriser, whatever the material then did with it.
    ///
    /// `FCVAR_CHEAT` in the engine, gated behind `sv_cheats` by `WireFrameMode()`
    /// (<c>game/client/view.h:68</c>). Not gated here: there is no server to protect and no player
    /// to gain an advantage over, so the gate would be ceremony. That is a deliberate divergence
    /// rather than an oversight.
    /// </remarks>
    public bool Wireframe { get; set; }

    /// <summary>Whether world surfaces and their overlays draw — Valve's <c>r_drawworld</c>.</summary>
    /// <remarks>
    /// Overlays are governed by this rather than by <see cref="DrawEntities"/>, because the engine
    /// draws them inside `DrawWorld` alongside the surfaces they mark, before any renderable.
    /// </remarks>
    public bool DrawWorld { get; set; } = true;

    /// <summary>Whether static props and models draw — Valve's <c>r_drawentities</c>.</summary>
    public bool DrawEntities { get; set; } = true;

    /// <summary>The wireframe twin of each solid rasteriser state, by handle.</summary>
    private Dictionary<nint, ComPtr<ID3D11RasterizerState>> _wireframeFor = [];

    /// <summary>Picks the wireframe twin of a state when wireframe is on.</summary>
    private ComPtr<ID3D11RasterizerState> Raster(ComPtr<ID3D11RasterizerState> solid) =>
        Wireframe && _wireframeFor.TryGetValue((nint)solid.Handle, out ComPtr<ID3D11RasterizerState> wire)
            ? wire
            : solid;

    /// <summary>Uploads a map's geometry, textures and lighting.</summary>
    /// <param name="device">Device to create resources on.</param>
    /// <param name="context">Context, for filling the vertex buffer.</param>
    /// <param name="vertices">Every triangle corner, sorted into material runs.</param>
    /// <param name="batches">The runs.</param>
    /// <param name="assets">The map's textures and lightmap atlas.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// Uploaded once per map rather than per frame. The geometry does not move, the lighting is
    /// baked and the textures do not change, so a frame is a handful of draw calls over resources
    /// that are already resident.
    /// </remarks>
    public void UploadMap(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        IReadOnlyList<WorldVertex> vertices,
        IReadOnlyList<WorldBatch> batches,
        MapAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        UploadTextures(device, context, assets);
        UploadGeometry(device, vertices, batches);
    }

    /// <summary>Uploads a map's textures, replacing anything already there.</summary>
    /// <param name="device">Device to create the textures on.</param>
    /// <param name="context">Context to generate their mip chains on.</param>
    /// <param name="assets">The map's textures and lightmap atlas.</param>
    /// <exception cref="ArgumentNullException"><paramref name="assets"/> is null.</exception>
    /// <remarks>
    /// **Separated from the geometry because only the geometry depends on the camera.** The
    /// projection is baked into the vertices, so a resize rebuilds them - and it used to rebuild
    /// these as well: 208 textures decoded, uploaded and mipped, every time the viewport changed
    /// size. Entering full screen fires several resizes in a row, which is how a map that loads in
    /// two seconds turned into a viewer redrawing itself once a second.
    /// </remarks>
    public void UploadTextures(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, MapAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        ReleaseTextures();
        TextureUploads++;

        if (_addBlend.Handle is null)
        {
            // SRC_ONE, DEST_ONE: exactly what the engine's BT_ADD does.
            BlendDesc description = BlendStates.Additive;

            ComPtr<ID3D11BlendState> state = default;

            SilkMarshal.ThrowHResult(device.CreateBlendState(in description, ref state));

            _addBlend = state;
        }

        if (_alphaBlend.Handle is null)
        {
            // Source-alpha over one-minus-source-alpha, which is what BT_BLEND means — and the
            // equation is PUBLISHED rather than interpolated, which this comment used to deny.
            // SetDefaultBlendingShadowState is indeed closed, but BlendType_t is declared in
            // public/shaderlib/BaseShader.h with `src * srcAlpha + dst * (1-srcAlpha)` written
            // beside BT_BLEND. See BlendStates and docs/findings/17-translucency.md.
            BlendDesc description = BlendStates.Translucent;

            ComPtr<ID3D11BlendState> blend = default;

            SilkMarshal.ThrowHResult(device.CreateBlendState(in description, ref blend));

            _alphaBlend = blend;
        }

        if (_modulateBlend.Handle is null)
        {
            // **DEST_COLOR times zero-source: the framebuffer multiplied by the texture.** That is
            // what Source's Modulate shader does, and it is the one blend mode this project had no
            // state for — so every Modulate material was drawn opaque and covered what it was meant
            // to shade. White leaves the destination alone and black blacks it out.
            BlendDesc description = BlendStates.Modulate;

            ComPtr<ID3D11BlendState> blend = default;

            SilkMarshal.ThrowHResult(device.CreateBlendState(in description, ref blend));

            _modulateBlend = blend;
        }

        if (_modulateTwiceBlend.Handle is null)
        {
            // **$mod2x, which doubles the product.** DEST_COLOR against SRC_COLOR sums to twice the
            // product, so a texel of mid grey leaves the destination unchanged and the material can
            // brighten as well as darken — which is the whole reason a mapper reaches for it.
            BlendDesc description = default;

            description.RenderTarget[0].BlendEnable = 1;
            description.RenderTarget[0].SrcBlend = Blend.DestColor;
            description.RenderTarget[0].DestBlend = Blend.SrcColor;
            description.RenderTarget[0].BlendOp = BlendOp.Add;
            description.RenderTarget[0].SrcBlendAlpha = Blend.One;
            description.RenderTarget[0].DestBlendAlpha = Blend.Zero;
            description.RenderTarget[0].BlendOpAlpha = BlendOp.Add;
            description.RenderTarget[0].RenderTargetWriteMask = (byte)ColorWriteEnable.All;

            ComPtr<ID3D11BlendState> blend = default;

            SilkMarshal.ThrowHResult(device.CreateBlendState(in description, ref blend));

            _modulateTwiceBlend = blend;
        }

        if (_depthReadOnly.Handle is null)
        {
            // **Test against depth, do not write it.** A translucent surface must not stop what is
            // behind it from drawing, which is the same reason the engine turns off alpha writes
            // for one. Writing depth here is what makes a pane of glass erase the room beyond it.
            DepthStencilDesc depth = default;

            depth.DepthEnable = 1;
            depth.DepthWriteMask = DepthWriteMask.Zero;
            depth.DepthFunc = ComparisonFunc.LessEqual;

            ComPtr<ID3D11DepthStencilState> state2 = default;

            SilkMarshal.ThrowHResult(device.CreateDepthStencilState(in depth, ref state2));

            _depthReadOnly = state2;
        }

        if (_depthWrite.Handle is null)
        {
            // Tested and written, nearer wins — what an opaque pass needs, so the props pass can
            // establish it rather than inherit whatever the overlay pass left behind.
            DepthStencilDesc writing = default;

            writing.DepthEnable = 1;
            writing.DepthWriteMask = DepthWriteMask.All;
            writing.DepthFunc = ComparisonFunc.Less;

            ComPtr<ID3D11DepthStencilState> writingState = default;

            SilkMarshal.ThrowHResult(
                device.CreateDepthStencilState(in writing, ref writingState));

            _depthWrite = writingState;
        }

        if (_decalDepth.Handle is null)
        {
            // Tested but never written, compared with DecalState.DepthFunc — see its remarks.
            DepthStencilDesc overlayDepth = default;

            overlayDepth.DepthEnable = 1;

            overlayDepth.DepthWriteMask =
                DecalState.WritesDepth ? DepthWriteMask.All : DepthWriteMask.Zero;

            overlayDepth.DepthFunc = DecalState.DepthFunc;

            ComPtr<ID3D11DepthStencilState> overlayState = default;

            SilkMarshal.ThrowHResult(
                device.CreateDepthStencilState(in overlayDepth, ref overlayState));

            _decalDepth = overlayState;
        }

        // **The engine's own convention: what is missing looks wrong on purpose.** Source draws an
        // unresolved material as a magenta and black chequer, and it is the right call - a surface
        // that quietly falls back to white or to nothing is a surface nobody investigates. Several
        // defects in this project hid for hours behind exactly that: a hole and a dark patch look
        // like art, while a magenta chequer looks like a bug and gets reported.
        _white = CreateTexture(device, context, MissingSize, MissingSize, TextureImage.Rgba(Missing()));

        // **An actually white texture, because `_white` is not one** (B62). The field above is
        // Valve's magenta-and-black chequer and has been since it was written; the name is the trap
        // `docs/memory/a-neutral-default-must-be-neutral.md` is about, and the comment beside
        // `mat_showlowresimage` already warns against binding it as a neutral.
        //
        // Needed once a material could legitimately have no base texture: a `Water` shader declares
        // none by design, and Valve's fallback for water it cannot shade is `Draw()` — plain lit
        // geometry, which is white times the lightmap. Binding `_white` there produced the chequer
        // the owner reported, and removing the material from `_chequered` did not help because the
        // fallback bind is what draws it.
        _flatWhite = CreateTexture(
            device, context, 1, 1, TextureImage.Rgba(new byte[] { 255, 255, 255, 255 }));

        if (assets.DevGrid is { } grid)
        {
            _devGrid = CreateTexture(device, context, grid.Width, grid.Height, grid.Image);
        }

        if (assets.LuxelGrid is { } luxels)
        {
            _luxelGrid = CreateTexture(device, context, luxels.Width, luxels.Height, luxels.Image);
        }

        // **Which materials will draw as the chequer, said once, by index.** A batch whose texture
        // handle is null silently binds the missing-material chequer at draw time — which is the
        // right thing to draw and the wrong thing to say nothing about. The asset census reports
        // "MISSING 0 with no base texture resolved", and that is a different question from "did the
        // upload produce a handle": every player in a capture came out magenta while that line read
        // zero. Cross-reference these indices against the `pairing` lines, which carry the names.
        List<int> chequered = [];

        // **The map's own cubemaps, uploaded before the materials that will ask for them.** These
        // are indexed by PLACEMENT rather than by material: a model's `$envmap "env_cubemap"` is
        // resolved per draw from where the model stands, so no material can own one.
        foreach (MapPlacedCubemap placed in assets.PlacedCubemaps)
        {
            ComPtr<ID3D11ShaderResourceView> uploadedCube = UploadCube(_assets, device, placed.Faces);

            if (uploadedCube.Handle is null)
            {
                // Dropped from BOTH lists together, so the positions stay parallel to the textures.
                // A placement kept with no texture would be chosen as nearest and then bind
                // nothing, which draws the previous model's reflection on this one.
                continue;
            }

            _placedCubemaps.Add(uploadedCube);
            _placements.Add(placed.Placement);
        }

        for (int index = 0; index < assets.Textures.Count; index++)
        {
            MapTexture? texture = assets.Textures[index];

            ComPtr<ID3D11ShaderResourceView> uploaded = Upload(device, context, texture);

            // **A `Water` material declares no `$basetexture` and is not missing** (B62). Water
            // refracts against `_rt_WaterRefraction` and takes its surface from a normal map;
            // `IsErrorMaterial` is false and the engine has never failed to draw one. Chequering it
            // was this project answering "nothing drawable here" for a material TF2 draws every
            // time — the owner found it on `cp_fulgur` and said the real game shows no chequer
            // anywhere on that map.
            //
            // **Valve's answer for a water material it cannot shade is `Draw()`** — its own comment
            // is *"draw something so that we won't go into wireframe-land"* (`water.cpp:578`). A
            // plain white bind is that: untextured geometry taking the lightmap, rather than a
            // marker saying the content is broken. `WaterShader.Pass` transcribes which pass the
            // engine would take, and none of its answers is "nothing".
            //
            // Shading it properly is still to come. What this removes is the false REPORT.
            bool water = index < assets.Shaders.Count &&
                assets.Shaders[index].Equals("Water", StringComparison.OrdinalIgnoreCase);

            if (uploaded.Handle is null)
            {
                if (water)
                {
                    _untextured.Add(index);
                }
                else
                {
                    chequered.Add(index);
                    _chequered.Add(index);
                }
            }

            _textures.Add(uploaded);


            // **The material's thumbnail, for `mat_showlowresimage`.** Uploaded beside the texture
            // rather than lazily when the mode is first switched on: the mode is a debug view and
            // building a few hundred 16x16 textures at that moment would stall the frame it was
            // asked for, which is the frame somebody is looking at.
            //
            // A default handle when the VTF carried none — every shipped texture measured has one,
            // but the format allows its absence, and the shader keeps the material's own texture
            // rather than blanking the surface.
            _thumbnails.Add(
                texture is { Thumbnail: { } thumbnail }
                    ? CreateTexture(
                        device, context, thumbnail.Width, thumbnail.Height, thumbnail.Image)
                    : default);

            if (texture is { IsNoCull: true })
            {
                _noCull.Add(index);
            }

            if (texture is { IsAdditive: true })
            {
                _additive.Add(index);
            }
            else if (texture is { IsModulate: true })
            {
                // **Its own kind, not translucency.** A Modulate material declares neither
                // $translucent nor $additive, so it fell through every test here and was drawn
                // opaque — a shader whose entire job is to darken what is behind it, painting over
                // it instead. It belongs in the blended pass with a blend state of its own.
                _modulate[index] = texture.Value.IsModulateTwice;
            }
            else if (texture is { IsTranslucent: true })
            {
                _translucent.Add(index);
            }

            // **Recorded separately from the blend kinds, because it decides DEPTH rather than
            // colour (B135).** A marking is drawn where its surface already is, so it must not write
            // depth — Valve's decal shaders say `EnableDepthWrites( false )` — and it needs a
            // slope-scaled offset to stop it fighting the surface it lies on. Whether it is also
            // translucent is a separate question its material answers separately.
            if (texture is { IsDecal: true })
            {
                _decalMaterials.Add(index);
            }
        }

        string chequeredAt = chequered.Count > 0
            ? " at " + string.Join(", ", chequered.Take(40))
            : string.Empty;

        _render.LogInformation(
            "textures: {Materials} materials, {Chequered} will draw as the missing-material chequer{At}",
            assets.Textures.Count,
            chequered.Count,
            chequeredAt);

        // **By INDEX, because that is what the draw actually tests.** The material ledger in
        // MapAssets names these by material NAME, which is the right form for reading and the
        // wrong form for matching: the prop log identifies a model's material as `mat 340`, and a
        // name cannot be joined to that. A surface drawn with the wrong blend state is invisible in
        // exactly the way missing geometry is, so the two lists have to be comparable.
        // **Guarded, because every one of these joins allocates.** Four `string.Join` calls over
        // material sets are real work, and CA1873 is right that doing it before the level is
        // consulted is work for nothing. The old static logger had no level to consult.
        if (_render.IsEnabled(LogLevel.Information))
        {
            _render.LogInformation(
                "blend classes by material index — additive [{Additive}] translucent [{Translucent}]" +
                " modulate [{Modulate}] decal [{Decal}]",
                string.Join(" ", _additive.Order()),
                string.Join(" ", _translucent.Order()),
                string.Join(" ", _modulate.Keys.Order()),
                string.Join(" ", _decalMaterials.Order()));
        }

        // **Kept rather than baked into the constants, because a proxy is a function of time.**
        // Everything else in the material buffer is decided once at load; these are the values that
        // have to be recomputed each time the material is bound.
        _proxies = assets.Proxies;

        foreach (MapTexture? texture in assets.BlendTextures)
        {
            _blendTextures.Add(Upload(device, context, texture));
        }

        // **Where the two whole-model override materials landed in the table** (B325). Nothing is
        // uploaded here: they were appended to `assets` alongside every other material, so the loops
        // above and below have already uploaded them. This is only the path-to-index map a draw
        // needs, since a corpse asks for gold by name and knows no index.
        _overrideMaterials = assets.OverrideMaterials;

        for (int index = 0; index < assets.Details.Count; index++)
        {
            MapDetail? detail = assets.Details[index];
            MapBump? bump = index < assets.Bumps.Count ? assets.Bumps[index] : null;

            _details.Add(detail is { } present ? Upload(device, context, present.Texture) : default);
            _bumps.Add(bump is { } mapped ? Upload(device, context, mapped.Texture) : default);

            // **Mode -1 is "no detail", and it has to be a value rather than an absence.** The
            // shader reads the same constant buffer for every draw, so a material without a detail
            // texture needs something in it that says so; leaving the previous material's numbers
            // there would apply one material's grain to the next one's surface.
            // Pulled out of the array initialisers: the analyser rejects a ternary inside one, and
            // the two values are the same whichever branch builds the rest.
            float hasBump = bump is null ? 0f : 1f;
            float isSelfShadowing = bump is { IsSelfShadowing: true } ? 1f : 0f;

            MapTexture? surface = index < assets.Textures.Count ? assets.Textures[index] : null;
            (float Red, float Green, float Blue)? glow = surface?.SelfIllum;
            float hasGlow = glow is null ? 0f : 1f;
            float glowRed = glow?.Red ?? 1f;
            float glowGreen = glow?.Green ?? 1f;
            float glowBlue = glow?.Blue ?? 1f;

            // **Alpha testing is a material property, which is what the engine treats it as.**
            // A material keeps its alpha channel when it is transparent or self-illuminated; only
            // an ALPHA-TESTED one wants that channel used as a cut-out. Translucent materials keep
            // alpha for blending and must not be clipped by it.
            // **The CUTOFF rather than a flag, which is what lets a material choose one.** This
            // used to be 1 or 0 and the shader clipped at a hardcoded half. Valve enables alpha
            // testing from MATERIAL_VAR_ALPHATEST and then overrides the reference only when the
            // material states one above zero (BaseVSShader.cpp:927), leaving the API default
            // otherwise — so zero here keeps its old meaning of "not alpha tested" and any other
            // value is the threshold to clip at.
            //
            // A material asking for 0.9 keeps only its most opaque texels; clipping everything at
            // half instead thickens every alpha-tested edge, which is visible on exactly the
            // surfaces that make a map read as a map — foliage, grates, chain-link, ladders.
            float alphaTested = 0f;

            if (surface is { IsTransparent: true, IsTranslucent: false })
            {
                alphaTested = surface.Value.AlphaTestReference > 0f
                    ? surface.Value.AlphaTestReference
                    : DefaultAlphaTestReference;
            }

            // **Which of the two combines the shader should use.** UnLitTwoTexture multiplies its
            // two textures; a WorldVertexTransition displacement mixes them by vertex alpha. Both
            // arrive in the same slot, so the material has to say which it is.
            float multiplies = surface is { MultipliesTextures: true } ? 1f : 0f;

            // **$halflambert, which 190 of this map's 1,034 model materials ask for.** It wraps the
            // direct term so a surface facing away from a light keeps a quarter of it rather than
            // going black, and it is the difference between a character reading as a solid shape in
            // shade and reading as a silhouette.
            float wrapsLight = surface is { IsHalfLambert: true } ? 1f : 0f;

            // **The material's own $color and $alpha, which is the rest value of the same slot a
            // proxy animates.** White and opaque for a material naming neither, which is the great
            // majority — so this changes nothing for them and is the whole effect for a tinted
            // haze or a coloured glow.
            (float Red, float Green, float Blue, float Alpha) tint =
                surface?.Modulation ?? (1f, 1f, 1f, 1f);

            // **The baked reflection's shading, resting at "no reflection".** The defaults matter
            // and point opposite ways: contrast is normal at ZERO and saturation at ONE, so a
            // resting value of zero for both would grey out every reflection the moment one was
            // bound.
            MapCubemap? reflection = index < assets.Cubemaps.Count ? assets.Cubemaps[index] : null;

            // **Two ways a material can reflect, and only one of them knows its cube at load.** A
            // brush face's was chosen by vbsp and baked into the material name, so it is uploaded
            // here alongside the material. A model's material says the literal `env_cubemap`, which
            // VertexLitGeneric keeps to runtime — so it carries the shading and takes its cube from
            // the map's placements, chosen per draw by where the model stands.
            MapEnvmapShading? local =
                index < assets.LocalReflections.Count ? assets.LocalReflections[index] : null;

            // A material asking for the map's own cubemap on a map that bakes none reflects
            // nothing, which is what the engine does: with no local cubemap bound the sample has
            // nothing to read. Guarded here rather than in the shader so the flag stays truthful.
            if (local is not null && assets.PlacedCubemaps.Count == 0)
            {
                local = null;
            }

            MapEnvmapShading? shading = reflection?.Shading ?? local;

            if (local is not null)
            {
                _usesLocalCubemap.Add(index);
            }

            (float Red, float Green, float Blue) envmapTint = shading?.Tint ?? (1f, 1f, 1f);
            float envmapContrast = shading?.Contrast ?? 0f;
            float envmapSaturation = shading?.Saturation ?? 1f;
            // **A mode rather than two flags**, because the two masks are mutually exclusive by
            // construction and pull opposite ways: 1 is the base texture's alpha INVERTED, 2 is the
            // bump map's alpha as-is. Encoding them as independent flags would admit a state the
            // shader's own SKIP directives forbid, and would leave open which wins.
            //
            // A material asking for the normal-map mask without a bump map gets NO mask rather than
            // the missing-texture chequer's alpha, which is what the slot holds when nothing was
            // bound.
            float envmapMask = 0f;

            if (shading is { MaskedByNormalMapAlpha: true } && bump is not null)
            {
                envmapMask = 2f;
            }
            else if (shading is { MaskedByBaseAlpha: true })
            {
                envmapMask = 1f;
            }

            float hasEnvmap = shading is null ? 0f : 1f;

            // **What each reflecting material actually got, said once** (B170). Fixing which CUBE a
            // skinned model reflects did not fix the wash, and the owner's manipulation is
            // unambiguous — `mat_specular 0` makes the weapon look right. So the next thing to read
            // is the strength the material is being given, which no offscreen measurement of a
            // material chosen BY THE TEST can answer: this reports the ones the map and its models
            // actually carry.
            if (hasEnvmap > 0.5f && index < assets.Materials.Count && _reportedEnvmap.Add(index))
            {
                _render.LogInformation(
                    "{Message}",
                    $"{assets.Materials[index].Name} reflects: tint " +
                    $"({envmapTint.Red:0.###}, {envmapTint.Green:0.###}, {envmapTint.Blue:0.###}), " +
                    $"mask {envmapMask:0}, contrast {envmapContrast:0.###}, " +
                    $"saturation {envmapSaturation:0.###}, " +
                    $"source {(reflection is null ? "local env_cubemap" : "BAKED")}");
            }

            // **The highlight's parameters, carrying their declared defaults when absent.** Zero is
            // the wrong resting value for two of these: an exponent of 0 makes `pow(x, 0)` return 1
            // for every pixel, and a boost of 0 erases the term the mask was authored against.
            MapPhong? phong = index < assets.Phong.Count ? assets.Phong[index] : null;

            // **Valve's own sentinel, kept as a sentinel** (B334). The engine does not carry a
            // separate "use the map" flag: it writes a NEGATIVE exponent and lets the shader choose,
            //
            //   fSpecExp = (g_EyePos_SpecExponent.w >= 0.0) ? g_EyePos_SpecExponent.w
            //                                               : (1.0f + 149.0f * vSpecExpMap.r);
            //
            // `skin_ps20b.fxc:268`. Encoding it the same way costs no constant-buffer slot, and
            // appending a float4 to this struct is the regression that has landed FOUR times here —
            // every one of them silent, because the per-batch write is addressed from the array's
            // end. A parity-faithful encoding that also cannot cause that is the whole argument.
            //
            // When `$phongexponentfactor` is stated it replaces the 149 rather than the branch, so
            // the negative constant carries the factor: -f, read back as `1 + f * r`.
            float phongExponent = phong switch
            {
                { ExponentFromMap: true, ExponentFactor: { } factor } => -factor,
                { ExponentFromMap: true } => -ExponentFromMapScale,
                { } stated => stated.Exponent,
                null => 5f,
            };

            float phongBoost = phong?.Boost ?? 1f;
            float hasPhong = phong is null ? 0f : 1f;

            // A material asking for the base-alpha mask, or one with phong and no bump map to read.
            // The second is not a fallback for convenience — the flag's own declaration says
            // $basemapalphaphongmask means "there is no normal map", so a material without one is
            // in that state whether or not it says so.
            float phongBaseAlphaMask =
                phong is { MaskedByBaseAlpha: true } || (phong is not null && bump is null) ? 1f : 0f;

            (float Low, float Mid, float High) phongFresnel = phong?.Fresnel ?? (1f, 0.5f, 1f);

            // **The tint's own sentinel, and it is the RED component that carries it.** Valve packs
            // the request to read the map's green channel as `vSpecularTint[0] = -1`
            // (`skin_dx9_helper.cpp:867`), tested as `g_SpecularTint.r >= 0.0`. Same reasoning as
            // the exponent above: the engine's encoding, and no new constant.
            (float Red, float Green, float Blue) phongTint =
                phong is { TintFromAlbedo: true }
                    ? (-1f, 1f, 1f)
                    : phong?.Tint ?? (1f, 1f, 1f);

            // **The exponent map, and WHITE when there is none** — which is not a convenience, it is
            // what the engine binds (`skin_dx9_helper.cpp:565`, `BindStandardTexture( SHADER_SAMPLER7,
            // TEXTURE_WHITE )`). The arithmetic runs either way: white gives `1 + 149 x 1 = 150` for
            // a material stating no exponent, an albedo tint of `lerp(white, albedo, 1)` for one
            // that asks, and a rim mask of 1.
            MapTexture? exponentMap =
                index < assets.PhongExponentMaps.Count ? assets.PhongExponentMaps[index] : null;

            _phongExponentMaps.Add(
                exponentMap is { } exponents ? Upload(device, context, exponents) : default);

            // The rim, which only exists inside phong. Its exponent defaults to 4 rather than the
            // highlight's 5, so the resting value is its own.
            // The authored lighting ramp. Independent of phong — plenty of materials warp their
            // diffuse and carry no highlight — and it rides in phongFresnel.w only because a
            // constant buffer is sized in whole float4s and that slot was spare.
            MapTexture? warp = index < assets.LightWarps.Count ? assets.LightWarps[index] : null;

            _lightWarps.Add(warp is { } ramp ? Upload(device, context, ramp) : default);

            float hasLightWarp = warp is null ? 0f : 1f;

            // **`$selfillummask`, which REPLACES the base map's alpha rather than adding anything**
            // (B327). The engine writes both cases as one lerp whose control is 1 exactly when a
            // mask is bound — `vSelfIllumMask = lerp( baseColor.aaa, vSelfIllumMask,
            // g_SelfIllumMaskControl )`, `vertexlit_and_unlit_generic_ps2x.fxc:442` — so this
            // uploads a texture for 53 of TF2's materials and changes nothing for the rest.
            MapTexture? illumMask =
                index < assets.SelfIllumMasks.Count ? assets.SelfIllumMasks[index] : null;

            _selfIllumMasks.Add(illumMask is { } mask ? Upload(device, context, mask) : default);

            float hasSelfIllumMask = illumMask is null ? 0f : 1f;

            // **Where the modulation lands** (B331). Off the BASE texture — `surface`, declared
            // above — because that is the only entry carrying a material's own flags: a bump map's
            // `MapTexture` has them at their defaults, and reading the wrong one would turn the
            // branch off for every painted item.
            // **The material's own coordinate transforms** (B332), identity where it states none —
            // which is Valve's fallback for a variable that is not a matrix
            // (`BaseVSShader.cpp:317-321`) and not a zeroed row, since a zeroed one collapses every
            // coordinate onto the first texel.
            TextureTransform baseTransform = surface?.BaseTransform ?? TextureTransform.Identity;
            TextureTransform secondTransform = surface?.SecondTransform ?? TextureTransform.Identity;

            float tintByBaseAlpha = surface is { TintsByBaseAlpha: true } ? 1f : 0f;
            float tintOverBase = surface?.TintOverBase ?? 0f;

            // **The two colours TF2's paint chain works on, kept apart from their product** (B330).
            // `$colortint_base` non-null is also what marks a material as tintable at all, so a
            // material without one never builds the proxy variable table.
            _tintBases.Add(surface?.TintBase);

            // Parallel to every other per-material list, and null wherever the material runs no
            // proxy — which is nearly all of them.
            _variables.Add(
                index < assets.Variables.Count ? assets.Variables[index] : null);

            // **The base texture's animation frames** (B341). Empty for all but the 152 shipped
            // materials that animate `$basetexture`, and those animate unconditionally.
            IReadOnlyList<MapTexture>? animation =
                index < assets.AnimationFrames.Count ? assets.AnimationFrames[index] : null;

            _animationFrames.Add(
                animation is null
                    ? []
                    : [.. animation.Select(frame => Upload(device, context, frame))]);

            // **The rate is per material, from its own proxy**, and the engine's default when a
            // material states none is 15 rather than the 30 TF2's own files almost all say.
            _animationRates.Add(RateOf(index < assets.Proxies.Count ? assets.Proxies[index] : []));
            _colourFactors.Add(surface?.ColourFactor ?? (1f, 1f, 1f));

            float rimExponent = phong?.Rim?.Exponent ?? 4f;
            float rimBoost = phong?.Rim?.Boost ?? 1f;
            float hasRim = phong?.Rim is null ? 0f : 1f;

            // **`g_RimMaskControl`, and it is a float rather than a flag** (B334). The helper writes
            // `$rimmask`'s VALUE when all three conditions hold and 0 otherwise
            // (`skin_dx9_helper.cpp:856`), and the shader lerps with it — so a material stating
            // `$rimmask 0.5` gets half the mask rather than all or none. 1,942 materials state one;
            // 1,643 of those say 1.
            float rimMaskControl = phong?.Rim?.MaskControl ?? 0f;

            // **One is the resting value and it means NO Fresnel falloff**, which is the opposite of
            // what a term called "fresnel" resting at zero would suggest. $fresnelreflection is
            // "1.0 == mirror, 0.0 == water" and defaults to 1; a model is always 1 because
            // VertexLitGeneric has no Fresnel term. Resting at zero would apply full Schlick to
            // every reflective surface on the map, which is the state this replaced.
            float envmapFresnel = shading?.Fresnel ?? 1f;

            _cubemaps.Add(reflection is { } cube ? UploadCube(_assets, device, cube.Faces) : default);

            _detailParameters.Add(detail is { } values
                ?
                [
                    values.Scale.U,
                    values.BlendFactor,
                    values.Mode,
                    values.Scale.V,
                    values.Tint.Red,
                    values.Tint.Green,
                    values.Tint.Blue,
                    1f,
                    hasBump,
                    isSelfShadowing,
                    hasGlow,
                    alphaTested,
                    glowRed, glowGreen, glowBlue, 1f,

                    // The two texture transforms and the modulation colour, at rest. A material
                    // whose proxies move them has them overwritten per frame by SetMaterial; these
                    // are the values for one that does not, and they have to be the identity rather
                    // than zero.
                    //
                    // **The modulation's rest value is the material's own $color and $alpha**, not
                    // a hardcoded white — that was the gap: a material declaring a tint and no
                    // proxy had it decoded and then overwritten with one here.
                    // **The material's OWN transform at rest, not the identity** (B332). A
                    // `TextureScroll` proxy overwrites these rows per frame, so they used to be
                    // identity and nothing noticed; a material stating a static
                    // `$basetexturetransform` runs no proxy and had its transform decoded and then
                    // replaced with one here — the same gap the modulation note below records.
                    baseTransform.Row0.X, baseTransform.Row0.Y, baseTransform.Row0.Z, baseTransform.Row0.W,
                    baseTransform.Row1.X, baseTransform.Row1.Y, baseTransform.Row1.Z, baseTransform.Row1.W,
                    secondTransform.Row0.X, secondTransform.Row0.Y, secondTransform.Row0.Z, secondTransform.Row0.W,
                    secondTransform.Row1.X, secondTransform.Row1.Y, secondTransform.Row1.Z, secondTransform.Row1.W,
                    tint.Red, tint.Green, tint.Blue, tint.Alpha,

                    multiplies, wrapsLight, 0f, 0f,

                    // The baked reflection's tint and contrast, then its saturation, mask and
                    // whether there is one at all. White, zero, one and zero is "reflect nothing",
                    // which is what the great majority of materials want.
                    envmapTint.Red, envmapTint.Green, envmapTint.Blue, envmapContrast,
                    envmapSaturation, envmapMask, hasEnvmap, envmapFresnel,

                    // The specular highlight. Exponent and boost carry their declared defaults even
                    // when there is no phong, so the resting state is a valid material rather than
                    // zeros — an exponent of 0 would raise every dot product to the power zero and
                    // return 1 everywhere the moment the flag was set.
                    phongExponent, phongBoost, hasPhong, phongBaseAlphaMask,
                    phongFresnel.Low, phongFresnel.Mid, phongFresnel.High, hasLightWarp,
                    // phongTint's spare w carries `$selfillummask`'s presence (B327), the same way
                    // phongFresnel's carries the light warp's and for the same reason: a constant
                    // buffer is sized in whole float4s and this slot was already there.
                    phongTint.Red, phongTint.Green, phongTint.Blue, hasSelfIllumMask,
                    // rimControl's spare w carries `g_RimMaskControl` (B334) — the same
                    // already-there-slot argument, and the reason no float4 was appended for the
                    // exponent texture's three controls: two of them ride Valve's own sentinels.
                    rimExponent, rimBoost, hasRim, rimMaskControl,

                    // categoryColour, a placeholder: it is per BATCH, so SetMaterial overwrites it
                    // in the mapped buffer after this array is copied in. Present so the array
                    // stays the length the shader's struct declares (B219).
                    1f, 1f, 1f, 0f,

                    // tintControl: `$blendtintbybasealpha` and `$blendtintcoloroverbase` (B331).
                    tintByBaseAlpha, tintOverBase, 0f, 0f,
                ]
                :
                [
                    0f, 0f, -1f, 0f,
                    1f, 1f, 1f, 1f,
                    hasBump,
                    isSelfShadowing,
                    hasGlow,
                    alphaTested,
                    glowRed, glowGreen, glowBlue, 1f,

                    // The two texture transforms and the modulation colour, at rest. A material
                    // whose proxies move them has them overwritten per frame by SetMaterial; these
                    // are the values for one that does not, and they have to be the identity rather
                    // than zero.
                    //
                    // **The modulation's rest value is the material's own $color and $alpha**, not
                    // a hardcoded white — that was the gap: a material declaring a tint and no
                    // proxy had it decoded and then overwritten with one here.
                    // **The material's OWN transform at rest, not the identity** (B332). A
                    // `TextureScroll` proxy overwrites these rows per frame, so they used to be
                    // identity and nothing noticed; a material stating a static
                    // `$basetexturetransform` runs no proxy and had its transform decoded and then
                    // replaced with one here — the same gap the modulation note below records.
                    baseTransform.Row0.X, baseTransform.Row0.Y, baseTransform.Row0.Z, baseTransform.Row0.W,
                    baseTransform.Row1.X, baseTransform.Row1.Y, baseTransform.Row1.Z, baseTransform.Row1.W,
                    secondTransform.Row0.X, secondTransform.Row0.Y, secondTransform.Row0.Z, secondTransform.Row0.W,
                    secondTransform.Row1.X, secondTransform.Row1.Y, secondTransform.Row1.Z, secondTransform.Row1.W,
                    tint.Red, tint.Green, tint.Blue, tint.Alpha,

                    multiplies, wrapsLight, 0f, 0f,

                    // The baked reflection's tint and contrast, then its saturation, mask and
                    // whether there is one at all. White, zero, one and zero is "reflect nothing",
                    // which is what the great majority of materials want.
                    envmapTint.Red, envmapTint.Green, envmapTint.Blue, envmapContrast,
                    envmapSaturation, envmapMask, hasEnvmap, envmapFresnel,

                    // The specular highlight. Exponent and boost carry their declared defaults even
                    // when there is no phong, so the resting state is a valid material rather than
                    // zeros — an exponent of 0 would raise every dot product to the power zero and
                    // return 1 everywhere the moment the flag was set.
                    phongExponent, phongBoost, hasPhong, phongBaseAlphaMask,
                    phongFresnel.Low, phongFresnel.Mid, phongFresnel.High, hasLightWarp,
                    // phongTint's spare w carries `$selfillummask`'s presence (B327), the same way
                    // phongFresnel's carries the light warp's and for the same reason: a constant
                    // buffer is sized in whole float4s and this slot was already there.
                    phongTint.Red, phongTint.Green, phongTint.Blue, hasSelfIllumMask,
                    // rimControl's spare w carries `g_RimMaskControl` (B334) — the same
                    // already-there-slot argument, and the reason no float4 was appended for the
                    // exponent texture's three controls: two of them ride Valve's own sentinels.
                    rimExponent, rimBoost, hasRim, rimMaskControl,

                    // categoryColour, a placeholder: it is per BATCH, so SetMaterial overwrites it
                    // in the mapped buffer after this array is copied in. Present so the array
                    // stays the length the shader's struct declares (B219).
                    1f, 1f, 1f, 0f,

                    // tintControl: `$blendtintbybasealpha` and `$blendtintcoloroverbase` (B331).
                    tintByBaseAlpha, tintOverBase, 0f, 0f,
                ]);
        }

        // **Linear, not sRGB.** A lightmap is light rather than a picture: linearising it on
        // sampling would apply the curve to values that never had it, darkening every shadow.
        _lightmap = CreateTexture(
            device,
            context,
            assets.Lightmaps.Width,
            assets.Lightmaps.Height,
            TextureImage.Rgba(assets.Lightmaps.Pixels),
            srgb: false);

        // Counted, because "we now skip additive materials" is a capability and this is the output.
        _render.LogInformation(
            "{Additive} of {Materials} materials are additive, drawn in a second pass",
            _additive.Count,
            assets.Textures.Count);

        _render.LogInformation(
            "{Translucent} of {Materials} materials are translucent, blended and sorted",
            _translucent.Count,
            assets.Textures.Count);

        // **The output, not the capability.** A detail chain that resolves nothing draws a map that
        // looks entirely reasonable, so the count of textures actually bound is the only thing that
        // distinguishes "implemented" from "working".
        //
        // Guarded because each of these counts walks a collection — cheap individually, and exactly
        // the kind of work CA1873 exists to keep out of a disabled log.
        if (_render.IsEnabled(LogLevel.Information))
        {
            _render.LogInformation(
                "{Details} materials draw with a detail texture",
                _details.Count(detail => detail.Handle is not null));

            _render.LogInformation(
                "{Bumps} materials draw with a bump map",
                _bumps.Count(bump => bump.Handle is not null));

            _render.LogInformation(
                "{SelfIllum} materials light themselves",
                assets.Textures.Count(texture => texture is { SelfIllum: not null }));
        }
    }

    /// <summary>Uploads a map's projected triangles, replacing anything already there.</summary>
    /// <param name="device">Device to create the vertex buffer on.</param>
    /// <param name="vertices">Every triangle corner, already in clip space.</param>
    /// <param name="batches">The runs, one per material.</param>
    /// <param name="decals">Overlay runs, drawn with the world and after its surfaces.</param>
    /// <param name="props">Static prop runs, drawn after the overlays as the engine does.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void UploadGeometry(
        ComPtr<ID3D11Device> device,
        IReadOnlyList<WorldVertex> vertices,
        IReadOnlyList<WorldBatch> batches,
        IReadOnlyList<WorldBatch>? decals = null,
        IReadOnlyList<WorldBatch>? props = null)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(batches);

        ReleaseGeometry();

        if (vertices.Count == 0)
        {
            return;
        }

        float[] data = Pack(vertices);

        CreateVertexBuffer(device, data);

        _batches = batches;

        // **Sorted once, at upload, because the order does not depend on the camera.** Looking
        // straight down, depth IS height, and height does not change when the view pans or zooms.
        // A perspective camera would have to re-sort per frame; this one never does.
        _decals = decals ?? [];
        _props = props ?? [];

        _sortedTranslucent =
        [
            .. batches
                .Where(batch => _translucent.Contains(batch.MaterialIndex))
                .OrderByDescending(batch => MeanDepth(vertices, batch)),
        ];
    }

    /// <summary>Whether to combine each material's detail texture, on by default.</summary>
    /// <remarks>
    /// **Turning it off is how the feature is measured.** A detail texture is a subtle multiply, and
    /// "the map has grain now" is not something a picture can be asserted against on its own. Two
    /// renders that differ only in this switch can be, and the same switch answers the question a
    /// person asks when they cannot tell whether it is working: what does it look like without.
    /// </remarks>
    public bool DrawDetail { get; set; } = true;

    /// <summary>Whether to light bumped surfaces from their three directional lightmaps.</summary>
    /// <remarks>
    /// Off falls back to the flat set, which is what the renderer drew before any of this and is
    /// what makes the feature measurable: two renders differing only in this switch.
    /// </remarks>
    public bool DrawBumped { get; set; } = true;

    /// <summary>Whether textures have been uploaded for a map.</summary>
    public bool HasTextures => _lightmap.Handle is not null;

    /// <summary>How many times a map's textures have been decoded and uploaded.</summary>
    /// <remarks>
    /// **Counted because the defect it guards against is invisible.** Re-uploading 208 textures on
    /// every viewport resize is correct in every respect except speed: the same picture comes out,
    /// and nothing but a clock or a counter says it happened more than once. A clock measures the
    /// machine; this measures the code.
    /// </remarks>
    public int TextureUploads { get; private set; }

    /// <summary>Binds the shaders, layout, samplers and camera every draw path needs.</summary>
    /// <param name="context">Context to bind on.</param>
    /// <remarks>
    /// **Extracted because <see cref="DrawModel"/> was relying on <see cref="Draw"/> having run,
    /// and nothing said so.** In an ordinary frame the world is drawn first and leaves the pipeline
    /// bound, so a model draw inherited it and worked. Two cases do not: a frame with no map, where
    /// <c>Draw</c> returns before binding anything, and an offscreen test that poses a model
    /// without a world. Both issue a draw with no vertex shader bound, which does not fail — it
    /// removes the device, and surfaces later as
    /// <c>"The GPU device instance has been suspended"</c> from whatever reads back next.
    ///
    /// Called by both paths rather than once per frame: the redundant binds are a handful of calls
    /// that D3D11 filters, against an ordering dependency that had no way to be discovered except
    /// by hitting it.
    /// </remarks>
    private void BindPipeline(ComPtr<ID3D11DeviceContext> context)
    {
        context.IASetInputLayout(_layout);
        context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context.VSSetShader(_vertexShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShader(_pixelShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetSamplers(0, 1, ref _wrapSampler);
        context.PSSetSamplers(1, 1, ref _clampSampler);

        // **Bound to BOTH stages, because both read it.** The vertex shader takes the matrix and
        // the pixel shader takes the category switch and the height cut. Binding it to the vertex
        // stage alone is not an error - D3D simply hands the pixel shader zeros - so the cut sat at
        // zero however hard it was pressed, and the category view drew nothing. Found by a test
        // that renders offscreen and reads a pixel, which is the only instrument that could see it.
        context.VSSetConstantBuffers(0, 1, ref _camera);
        context.PSSetConstantBuffers(0, 1, ref _camera);
        context.PSSetShaderResources(1, 1, ref _lightmap);

        // **Frame-constant, so it binds here with the lightmap rather than per batch.** The
        // category view replaces every material's texture with this one grid, so there is nothing
        // per-material to say about it — and binding it in the draw loop would be re-stating the
        // same resource thirteen thousand times to no effect.
        //
        // Falls back to the chequer when the game has no dev texture, which keeps the shader's
        // multiply well defined: white would be invisible in the tint, and the chequer at least
        // says out loud that the grid is missing.
        ComPtr<ID3D11ShaderResourceView> grid =
            _devGrid.Handle is not null ? _devGrid : _white;

        context.PSSetShaderResources(7, 1, ref grid);

        EnsureMaterialBuffer(context);
    }

    /// <summary>Draws the uploaded map.</summary>
    /// <param name="context">Context to issue the draws on.</param>
    /// <remarks>
    /// The caller binds the render target and viewport, as with every renderer here, so the same
    /// code serves the swap chain and an offscreen texture without knowing which it has.
    /// </remarks>
    public void Draw(ComPtr<ID3D11DeviceContext> context)
    {
        if (_batches.Count == 0)
        {
            return;
        }

        // Falls through to BindPipeline below. The early return above is deliberate and safe now
        // that DrawModel binds its own state: before, a frame with no map left the pipeline
        // entirely unbound, and any model drawn after it went to a context with no vertex shader.

        uint stride = VertexStride;
        uint offset = 0;

        BindPipeline(context);

        context.RSSetState(Raster(_bothSides));
        context.IASetVertexBuffers(0, 1, ref _vertices, in stride, in offset);

        // The map's own geometry is already in world space, so it draws with an identity model
        // matrix. Set every frame rather than once: an entity draw leaves its own matrix behind,
        // and inheriting it would move the whole map to wherever the last rocket was.
        SetModel(context, Identity);

        // **The engine's pass order, transcribed (B135).** `CBaseWorldView::DrawExecute` at
        // game/client/viewrender.cpp:5487:
        //
        //     DrawWorld( waterZAdjust );            // world surfaces AND their overlay fragments
        //     DrawOpaqueRenderables( DepthMode );   // static props, brush models, studio models
        //     DrawTranslucentRenderables( false, false );
        //
        // So overlays go with the world, and props come after them. They used to be batched into
        // _batches and drawn with the surfaces, which put a pipe in the depth buffer before the
        // overlay pass — and a biased overlay then painted over it. The pipes, the light fixtures
        // and the overlay seen through a wall were one symptom of this order, not of the bias.
        //
        // **Additive last**, which is unchanged: an additive fragment brightens whatever is behind
        // it, so anything drawn later would be added to nothing.
        // **No pass sets a depth state here any more; SetMaterial does, on bind.** That is the
        // engine's arrangement and the reason this ordering is safe to change at all — see the
        // remarks there. A first fix established the writing state at the top of the props pass,
        // which worked and left the next reordering to break the same way again.
        // **`r_drawworld` and `r_drawentities`, which are pass switches rather than shader ones.**
        // Both are engine cvars defaulting to 1 (`view.cpp:296` looks up `r_drawentities`), and what
        // they answer is "which pass owns this surface" — the question that comes up the moment
        // something is drawn twice, drawn in the wrong order, or drawn by code nobody expected.
        //
        // Overlays go with the world rather than with entities, because that is where the engine
        // draws them: `DrawWorld` renders world surfaces AND their overlay fragments before
        // `DrawOpaqueRenderables` runs at all.
        if (DrawWorld)
        {
            // **The visible runs when a cull produced some, else everything.** Decals are not
            // culled with them: an overlay fragment is clipped to the surface it marks and is not
            // named by any leaf's face list, so leaving them whole is the conservative direction.
            DrawOpaqueBatches(context, Drawn);
            DrawDecals(context);
        }

        if (DrawEntities)
        {
            DrawOpaqueBatches(context, _props);
        }

        DrawTranslucent(context);
        DrawAdditive(context);
    }

    /// <summary>The 3D skybox room's runs, drawn by <see cref="DrawSky"/> before the world.</summary>
    /// <remarks>
    /// **Set from the cull, which is the only thing that knows which leaves are the sky's** — see
    /// <c>WorldCulling.SkyBatches</c>. Empty for a map with no <c>sky_camera</c>, and empty when
    /// the room is outside the PVS from where the eye stands, which indoors is most of the time.
    /// </remarks>
    public IReadOnlyList<WorldBatch> SkyBatches { get; set; } = [];

    /// <summary>Draws the 3D skybox room, with whatever camera is currently set.</summary>
    /// <param name="context">The device context.</param>
    /// <remarks>
    /// **<c>CSkyboxView::DrawInternal</c> in the order it runs them** (<c>viewrender.cpp:4922</c>):
    /// <c>DrawWorld</c>, then <c>DrawOpaqueRenderables</c>, then the translucent passes. Only the
    /// world half is here, because nothing in this project puts an ENTITY in the sky area — a
    /// skybox room holds brushwork and static props, and a static prop comes through the ordinary
    /// prop path with its own transform rather than through this.
    ///
    /// **The caller owns the camera and the depth clear, not this.** The sky is drawn from a
    /// compressed view and the world is drawn over it with depth reset; both are frame-level
    /// decisions, and this draws the runs it is handed.
    /// </remarks>
    public void DrawSky(ComPtr<ID3D11DeviceContext> context)
    {
        if (SkyBatches.Count == 0)
        {
            return;
        }

        uint skyStride = VertexStride;
        uint skyOffset = 0;

        BindPipeline(context);

        context.RSSetState(Raster(_bothSides));
        context.IASetVertexBuffers(0, 1, ref _vertices, in skyStride, in skyOffset);

        // The room's geometry is in world space like the rest of the map, so it draws with an
        // identity model matrix for the same reason the world does.
        SetModel(context, Identity);

        DrawOpaqueBatches(context, SkyBatches);
    }

    /// <summary>Draws one run of opaque batches with the world shader.</summary>
    /// <param name="context">The device context.</param>
    /// <param name="batches">The batches to draw; translucent and additive materials are skipped.</param>
    /// <remarks>
    /// **Extracted so the world and the props share it rather than agreeing by copy.** They differ
    /// only in when they are drawn, which is the whole of B135 — and two loops that must stay
    /// identical are exactly how the material binding above would drift out of step.
    /// </remarks>
    private void DrawOpaqueBatches(
        ComPtr<ID3D11DeviceContext> context, IReadOnlyList<WorldBatch> batches)
    {
        // **An opaque pass establishes that it is opaque. It does not inherit it.**
        //
        // This is the bug B135's reordering created and nobody connected to it. `DrawDecals` turns
        // alpha blending ON and never turns it off; the next reset is inside `DrawTranslucent`,
        // two passes later. The old order was world → props → decals, so nothing ran between the
        // decals and the reset and the leak had nowhere to land. `e7b95cf` moved the props to
        // AFTER the overlays — correctly, because that is `CBaseWorldView::DrawExecute`'s order —
        // and every static prop in every map has been alpha-blended ever since.
        //
        // **What made it invisible rather than obviously wrong**: the alpha it blended against is
        // the base texture's alpha channel, and in a TF2 model material that channel is usually an
        // ENVMAP MASK rather than opacity ($basealphaenvmapmask). Shiny metal masks to near zero,
        // so pipes ghosted, the observatory dome went glassy, a sign showed the wall through it and
        // a silo's collar vanished outright — while props with an opaque alpha channel looked
        // perfect. It reads as four unrelated art faults, and it is one line of state.
        //
        // The owner found it by looking: the same triangles were present in the category view and
        // absent in the textured one, which is only possible after the fragment survives the clip.
        //
        // This project has already written down the rule this violates — "let a pass establish the
        // state it needs rather than trusting the previous pass to have restored it" — after
        // DrawTranslucent leaked a depth state onto models. Same failure, same file, other state.
        float[] factor = [1f, 1f, 1f, 1f];

        context.OMSetBlendState(default(ComPtr<ID3D11BlendState>), factor, 0xFFFFFFFF);

        foreach (WorldBatch batch in batches)
        {
            if (_additive.Contains(batch.MaterialIndex) || _translucent.Contains(batch.MaterialIndex))
            {
                continue;
            }

            // **The fallback is the chequer for a MISSING texture and plain white for a shader that
            // wants none** (B62). Those look identical from here — both are "the handle is null" —
            // and telling them apart is the whole fix: water drew Valve's broken-content marker on
            // a map the real game renders without one.
            ComPtr<ID3D11ShaderResourceView> absent =
                _untextured.Contains(batch.MaterialIndex) ? _flatWhite : _white;

            // **Says WHY a batch is about to draw as the chequer, once per index** (B62 follow-on).
            // The material inventory reports 1192 of 1193 resolved and the renderer reports 0
            // chequered, and yet pipe elbows and skybox panels draw magenta on cp_fulgur — so the
            // chequer is being reached by a route the counts cannot see. There are only two: a null
            // texture, which the inventory would have named, and an index outside the table, which
            // nothing reports at all. This separates them.
            //
            // Bounded by identity rather than by a count, as every diagnostic here is.
            if (_render.IsEnabled(LogLevel.Debug) &&
                (batch.MaterialIndex < 0 || batch.MaterialIndex >= _textures.Count) &&
                _unindexed.Add(batch.MaterialIndex))
            {
                _render.LogDebug(
                    "{Message}",
                    $"batch names material {batch.MaterialIndex}, outside the table of "
                    + $"{_textures.Count}; it will draw as the missing-material chequer");
            }

            ComPtr<ID3D11ShaderResourceView> still =
                batch.MaterialIndex >= 0 && batch.MaterialIndex < _textures.Count &&
                _textures[batch.MaterialIndex].Handle is not null
                    ? _textures[batch.MaterialIndex]
                    : absent;

            // **The animation frame outranks the still texture** (B341), because for a material
            // running `AnimatedTexture` on `$basetexture` the still one IS frame zero — binding it
            // is what left 152 shipped materials frozen.
            ComPtr<ID3D11ShaderResourceView> texture =
                AnimationFrame(batch.MaterialIndex, still);

            // The second layer, or the first again where a material has only one - so the
            // shader's mix becomes an identity rather than needing a branch.
            ComPtr<ID3D11ShaderResourceView> blend =
                batch.MaterialIndex >= 0 && batch.MaterialIndex < _blendTextures.Count &&
                _blendTextures[batch.MaterialIndex].Handle is not null
                    ? _blendTextures[batch.MaterialIndex]
                    : texture;

            // The detail pattern, or white with the mode set to "none" - the shader skips the
            // combine entirely rather than multiplying by an identity, because several of the modes
            // have no identity to multiply by.
            ComPtr<ID3D11ShaderResourceView> detail =
                batch.MaterialIndex >= 0 && batch.MaterialIndex < _details.Count &&
                _details[batch.MaterialIndex].Handle is not null
                    ? _details[batch.MaterialIndex]
                    : _white;

            ComPtr<ID3D11ShaderResourceView> bump =
                batch.MaterialIndex >= 0 && batch.MaterialIndex < _bumps.Count &&
                _bumps[batch.MaterialIndex].Handle is not null
                    ? _bumps[batch.MaterialIndex]
                    : _white;

            // **Bound for every draw, not only the reflecting ones.** A shader resource slot keeps
            // whatever was set last, so a material with no cubemap would sample the previous
            // material's — and the shader's own guard is what stops it being read, not the absence
            // of a binding. Setting a null view here is deliberate: it makes the slot empty rather
            // than stale.
            ComPtr<ID3D11ShaderResourceView> reflection =
                batch.MaterialIndex >= 0 && batch.MaterialIndex < _cubemaps.Count
                    ? _cubemaps[batch.MaterialIndex]
                    : default;

            SetMaterial(context, batch.MaterialIndex, batch.Category);

            context.PSSetShaderResources(0, 1, ref texture);
            context.PSSetShaderResources(2, 1, ref blend);
            context.PSSetShaderResources(3, 1, ref detail);
            context.PSSetShaderResources(4, 1, ref bump);
            context.PSSetShaderResources(5, 1, ref reflection);
            context.Draw((uint)batch.VertexCount, (uint)batch.FirstVertex);
        }
    }

    /// <summary>Sets the view for the frames that follow.</summary>
    /// <param name="device">Device to create the buffer on, the first time.</param>
    /// <param name="context">Context to upload through.</param>
    /// <param name="matrix">Sixteen floats, row major, from <c>ViewCamera.Matrix</c>.</param>
    /// <param name="surfaceColours">Whether to draw flat category colours instead of textures.</param>
    /// <param name="specular">
    /// Whether cubemap reflections are added — Valve's <c>mat_specular</c>, whose own comment for
    /// the same switch is "If mat_specular 0, then get rid of envmap".
    /// </param>
    /// <param name="fullbright">
    /// Which of Valve's <c>mat_fullbright</c> substitutions to apply; see <see cref="Fullbright"/>
    /// for why there are three of them rather than two.
    /// </param>
    /// <param name="debug">
    /// Valve's per-surface debug visualisations — <c>mat_drawflat</c>, <c>mat_luxels</c> and
    /// <c>mat_normalmaps</c>. Packed into one shader register, as Valve packs its own.
    /// </param>
    /// <param name="phong">
    /// Valve's <c>mat_phong</c>, default 1 — whether materials declaring <c>$phong</c> get their
    /// specular highlight at all. The same kind of switch as <paramref name="specular"/>: a
    /// material feature turned off wholesale, not a debug visualisation.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="matrix"/> is not sixteen floats.</exception>
    /// <remarks>
    /// **This is what a resize costs now.** The geometry is uploaded in world coordinates and never
    /// moves; changing the view rewrites one 64-byte buffer. Before this, the projection was baked
    /// into every vertex, so a new viewport meant rebuilding 2.9 million of them - 0.33 seconds,
    /// and the reason the free camera and per-player views could not exist.
    /// </remarks>
    public void SetCamera(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        float[] matrix,
        bool surfaceColours = false,
        bool specular = true,
        Fullbright fullbright = Fullbright.Off,
        DebugModes debug = default,
        bool phong = true)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        if (matrix.Length != 16)
        {
            throw new ArgumentException("A camera matrix is sixteen floats.", nameof(matrix));
        }

        if (_camera.Handle is null)
        {
            BufferDesc description = new()
            {
                ByteWidth = sizeof(float) * CameraConstants,
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.ConstantBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
            };

            ComPtr<ID3D11Buffer> buffer = default;

            SilkMarshal.ThrowHResult(device.CreateBuffer(in description, null, ref buffer));

            _camera = buffer;
        }

        // **Recovered rather than passed.** The matrix determines where the camera is, so asking
        // every caller for it as well would be two sources for one fact — and one of the two camera
        // types does not hold a position to give. A degenerate projection has no eye; the zero
        // below leaves reflections at map centre, which is why EyePosition returns null rather than
        // guessing and the shader is told there is no cubemap in that case.
        (float X, float Y, float Z) eye = EyePosition.From(matrix) ?? (0f, 0f, 0f);
        float hasEye = EyePosition.From(matrix) is null ? 0f : 1f;

        // The matrix, then a float4 whose first component is the category-view switch, then the
        // eye. Constant buffers are sized in whole sixteen-byte registers, so the padding is not
        // optional.
        float[] contents =
        [
            .. matrix,
            surfaceColours ? 1f : 0f,

            // **`mat_phong`, which took the slot the height cut left** (B213 reserved it, B170 used
            // it). It sits beside `mat_specular` because it is the same kind of switch — a material
            // feature turned off wholesale, not a debug view — and the register was already the one
            // holding those.
            phong ? 1f : 0f,
            specular ? 1f : 0f,
            (float)fullbright,
            eye.X, eye.Y, eye.Z, hasEye,
            debug.DrawFlat ? 1f : 0f,
            debug.Luxels ? 1f : 0f,
            debug.NormalMaps ? 1f : 0f,
            debug.BumpBasis ? 1f : 0f,

            // debugModes2: mat_showlowresimage, then three spare components. Written even when off
            // for the reason the comment below gives — the tail of a mapped buffer holds whatever
            // the last frame put there, so a component that is sometimes not written is a mode that
            // sometimes turns itself on.
            debug.ShowLowResImage ? 1f : 0f,
            0f,
            0f,
            0f,
        ];

        MappedSubresource mapped = default;

        SilkMarshal.ThrowHResult(
            context.Map(_camera, 0, Map.WriteDiscard, 0, ref mapped));

        fixed (float* source = contents)
        {
            // **Sized from the array, not from a literal.** This was a hardcoded twenty and the
            // buffer has now grown by a float4 — the same edit that gets forgotten and leaves the
            // tail holding whatever the previous frame wrote.
            System.Buffer.MemoryCopy(
                source,
                mapped.PData,
                sizeof(float) * CameraConstants,
                sizeof(float) * contents.Length);
        }

        context.Unmap(_camera, 0);
    }

    /// <summary>The per-material constants a material without a detail texture gets.</summary>
    /// <remarks>
    /// Mode -1, which is the value the shader tests to skip the combine entirely. The tint is white
    /// so that a stale sample can never darken anything even if the mode were somehow read.
    /// </remarks>
    /// <remarks>
    /// **The tail is not padding and cannot be zeroed.** After the detail, tint, bump and
    /// self-illum groups come the two texture transforms, the modulation colour and the combine
    /// mode — and each has a non-zero resting value. Identity rows are (1,0,0,0) and (0,1,0,0);
    /// zeroing them sends every coordinate to the texture's first texel, and zeroing the modulation
    /// multiplies every surface by black.
    /// </remarks>
    /// <summary>The material constant buffer's resting values, and its size.</summary>
    /// <remarks>
    /// Exposed so a test can hold this against the shader's own declaration. The two fell out of
    /// step once and nothing noticed, because the mismatch produced a correct picture on this
    /// driver.
    /// </remarks>
    internal static IReadOnlyList<float> MaterialRestingValues => NoDetail;

    /// <summary>The shader source, so a test can read what the pipeline actually declares.</summary>
    internal static string ShaderSourceText => ShaderText;

    private static readonly float[] NoDetail =
    [
        0f, 0f, -1f, 0f,
        1f, 1f, 1f, 1f,
        0f, 0f, 0f, 0f,
        1f, 1f, 1f, 1f,

        // baseTransform0, baseTransform1, secondTransform0, secondTransform1: identity.
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,

        // modulation: white, opaque.
        1f, 1f, 1f, 1f,

        // combine: mixed by vertex alpha rather than multiplied.
        0f, 0f, 0f, 0f,

        // **envmapTint and envmapControl, and their absence here was a real defect.** This array
        // sizes the constant buffer (see EnsureMaterialBuffer), whose comment claims it cannot
        // disagree with the shader because "NoDetail is that struct". It disagreed: the shader's
        // Material block grew two float4s when $envmap landed and this did not, so the buffer was
        // created 160 bytes wide while the shader declared 192 and SetMaterial copied 192 into it.
        //
        // An out-of-bounds write into a mapped constant buffer and an out-of-bounds read by the
        // shader, and it WORKED — reflections drew and their pixels measured correctly — because
        // this driver tolerated it. Nothing in the suite could see it; the symptom on a stricter
        // driver is reflections silently vanishing, because a read past a constant buffer returns
        // zero and `hasEnvmap` is in the part that fell off.
        //
        // Resting values, which are not all zero and that is the point: white tint, contrast 0,
        // saturation 1, no mask, no cubemap, and Fresnel 1 — which means NO falloff, the engine's
        // own default.
        1f, 1f, 1f, 0f,
        1f, 0f, 0f, 1f,

        // phongControl: exponent 5 (the declared default), boost 1, no phong, mask from the bump.
        // phongFresnel: [0 0.5 1] ENCODED, which is (1, 0.5, 1) and not the triple itself.
        // phongTint: white.
        5f, 1f, 0f, 0f,

        // phongFresnel: the encoded [0 0.5 1], and w = 0 for "no light warp".
        1f, 0.5f, 1f, 0f,

        // phongTint: white, and w = 0 for "no $selfillummask" — which means the base map's alpha
        // decides which parts light themselves, the engine's own fallback (B327).
        1f, 1f, 1f, 0f,

        // rimControl: exponent 4 (its own declared default, not phong's 5), boost 1, no rim.
        4f, 1f, 0f, 0f,

        // categoryColour: white, and w = 0 for "no category was supplied" (B219). Always written
        // rather than skipped, and this array must stay the same length as the shader's struct —
        // the comment above records what happened the last time it did not.
        1f, 1f, 1f, 0f,

        // tintControl: no `$blendtintbybasealpha`, so the modulation multiplies across the whole
        // surface — the branch every material but TF2's tintable items takes (B331).
        0f, 0f, 0f, 0f,
    ];

    /// <summary>The model matrix for geometry already in world space.</summary>
    private static readonly float[] Identity =
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];

    /// <summary>Sets the transform applied to the vertices before the camera sees them.</summary>
    /// <param name="context">The device context.</param>
    /// <param name="matrix">Sixteen floats, row major.</param>
    /// <param name="light">The ambient cube lighting this model, or null to leave it unlit.</param>
    /// <param name="blend">How far toward the next baked animation frame, from nought to one.</param>
    /// <param name="sun">The sun reaching this model, or null when it stands in shade.</param>
    /// <param name="bones">How many bones skin this draw, or zero for a baked model.</param>
    /// <param name="locals">
    /// The direct lights near this model, at most four. Null or empty where none reach it, which
    /// the shader reads as "no lamp" rather than as a black one at the origin.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="matrix"/> is not sixteen floats.</exception>
    /// <remarks>
    /// **Valve's arrangement, and the reason it matters here.**
    /// <c>IMaterialSystem::LoadBoneMatrix</c> hands bone matrices to the shader as constants and
    /// the GPU transforms model-space vertices — which is how the engine draws a great many
    /// animated models without noticing. Rebuilding vertices on the processor each frame is
    /// precisely what that path avoids, and a viewer that did it would feel slow where TF2 does
    /// not.
    ///
    /// A rigid entity is the one-bone case. Skinning adds more matrices and a weight per vertex;
    /// this arrangement does not change.
    /// </remarks>
    public void SetModel(
        ComPtr<ID3D11DeviceContext> context,
        float[] matrix,
        AmbientCube? light = null,
        SunLight? sun = null,
        float blend = 0f,
        int bones = 0,
        IReadOnlyList<LocalLight>? locals = null)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        if (matrix.Length != 16)
        {
            throw new ArgumentException("A model matrix is sixteen floats.", nameof(matrix));
        }

        EnsureModelBuffer(context);

        // **Written straight into the mapped buffer, because the engine does not build a managed
        // array per draw** (the outside audit's finding 3). This staged through a fresh
        // `new float[ModelConstants]` every call — one allocation per model per frame, in the
        // inner draw loop — and then memcpy'd the lot; Valve locks the constant buffer and writes
        // into it. WriteDiscard memory arrives UNDEFINED, so the clear below is load-bearing: the
        // layout relies on unwritten slots reading as zero — the cube's "is this real" flag, the
        // sun's, and every lamp slot past what was supplied.
        MappedSubresource mapped = default;

        SilkMarshal.ThrowHResult(context.Map(_model, 0, Map.WriteDiscard, 0, ref mapped));

        Span<float> contents = new(mapped.PData, ModelConstants);

        contents.Clear();

        matrix.AsSpan(0, 16).CopyTo(contents);

        if (light is { } cube)
        {
            WriteFace(contents, 16, cube.PositiveX);
            WriteFace(contents, 20, cube.NegativeX);
            WriteFace(contents, 24, cube.PositiveY);
            WriteFace(contents, 28, cube.NegativeY);
            WriteFace(contents, 32, cube.PositiveZ);
            WriteFace(contents, 36, cube.NegativeZ);

            contents[19] = 1f;
        }

        // The sun follows the cube: colour and "is it reaching this model", then the direction it
        // travels. Left at zero when the map has no sun or this model stands in shade, which the
        // shader reads as "no direct light" rather than as black.
        if (sun is { } direct)
        {
            contents[40] = direct.Red;
            contents[41] = direct.Green;
            contents[42] = direct.Blue;
            contents[43] = 1f;
            contents[44] = direct.DirectionX;
            contents[45] = direct.DirectionY;
            contents[46] = direct.DirectionZ;
        }

        // How far between this baked frame and the next, clamped because a cycle interpolated
        // between packets can overshoot and a blend past one extrapolates rather than smooths.
        contents[48] = Math.Clamp(blend, 0f, 1f);

        // How many bones skin this draw, or zero for a baked model. The shader reads this as the
        // switch between the two paths, so leaving it stale would skin a health pack by whatever
        // skeleton was last uploaded.
        contents[52] = bones;

        // **The lamps near this model.** Written into three parallel float4 arrays because that is
        // what the constant buffer holds; the slot's own w flag says whether it is live, and every
        // slot past what was supplied stays zero, which reads as "no light" rather than as a black
        // one at the origin.
        //
        // 56 is where the fixed part ends: sixteen for the matrix, twenty-four for the cube, and
        // four each for the sun, its direction, the frame blend and the skinning switch.
        const int LocalLightBase = 56;

        if (locals is { Count: > 0 })
        {
            int lamps = Math.Min(locals.Count, LocalLightSlots);

            // **Valve's `nNumLights`, in a spare channel.** The shader nests its ifs on this rather
            // than testing a flag per light, so it is what decides whether a slot is read at all —
            // and writing it AFTER the slots would be the kind of ordering nobody can see. Written
            // first, deliberately.
            contents[LocalLightBase + (LocalLightSlots * 8) + 3] = lamps;

            for (int slot = 0; slot < lamps; slot++)
            {
                LocalLight lamp = locals[slot];

                int position = LocalLightBase + (slot * 4);
                int colour = LocalLightBase + (LocalLightSlots * 4) + (slot * 4);
                int falloff = LocalLightBase + (LocalLightSlots * 8) + (slot * 4);

                contents[position] = lamp.X;
                contents[position + 1] = lamp.Y;
                contents[position + 2] = lamp.Z;

                contents[colour] = lamp.Red;
                contents[colour + 1] = lamp.Green;
                contents[colour + 2] = lamp.Blue;

                // Squared here rather than in the shader, which would do it per pixel to reach the
                // same number — and zero stays zero, which is the "no cutoff" every light on
                // cp_process actually carries.
                contents[colour + 3] = lamp.Range * lamp.Range;

                contents[falloff] = lamp.Constant;
                contents[falloff + 1] = lamp.Linear;
                contents[falloff + 2] = lamp.Quadratic;
            }
        }

        context.Unmap(_model, 0);

        // **Both stages, because both read it now.** The vertex shader takes the matrix and the
        // pixel shader takes the ambient cube. This was bound to the vertex stage alone, with a
        // comment saying the pixel shader had no use for it - true when the buffer held only a
        // matrix, and false the moment lighting arrived.
        //
        // The failure is silent in the worst way: D3D hands the pixel shader zeros, so the cube's
        // "is this real" flag reads false and every model draws exactly as it did before. Two
        // captures of the same view came back byte for byte identical, which is the only reason
        // it was noticed. The camera buffer made this same mistake once and cost a session.
        context.VSSetConstantBuffers(2, 1, ref _model);
        context.PSSetConstantBuffers(2, 1, ref _model);
    }

    /// <summary>Floats in the model constant buffer: a matrix, six cube faces, the sun, four lamps.</summary>
    /// <remarks>
    /// **This number and the <c>cbuffer Model</c> above must agree, and nothing at runtime can
    /// check it.** A buffer smaller than the declared struct leaves D3D reading past it; larger
    /// and the tail is ignored. `ModelConstantsMatchTheShader` counts the float4s in the HLSL and
    /// asserts they come to this, which is a generated denominator rather than a number somebody
    /// remembers to update — the same instrument the material buffer needed after a replace-all
    /// grew two of three arrays and turned the scene into a strobe.
    /// </remarks>
    private const int ModelConstants =
        16 + (6 * 4) + 4 + 4 + 4 + 4 + (LocalLightSlots * 4 * 3);

    /// <summary>How many local lights a model draw carries, matching the engine's four.</summary>
    private const int LocalLightSlots = 4;

    /// <summary>Floats in the camera buffer: the matrix, the view switches, and the eye.</summary>
    /// <remarks>
    /// **Named rather than written twice**, because it appears in the buffer's size and in the copy
    /// that fills it, and those two disagreeing is the exact failure the material buffer already
    /// hit: a short copy leaves the tail holding whatever was there before, which reads as one
    /// frame borrowing the previous one's state rather than as an error.
    /// </remarks>
    /// <summary>Floats in the camera buffer: the matrix, the view switches, the eye, the debug modes.</summary>
    /// <remarks>
    /// **Kept as arithmetic rather than a literal**, because the shader's struct and this number
    /// have to agree and a mismatch is not an error — D3D hands the shader zeros past the end of a
    /// short buffer, so a mode simply never turns on. That has happened here once already: the
    /// buffer was two float4s shorter than the shader (d561b14) and the constants it should have
    /// carried read as off.
    ///
    /// **Seven float4s, against Valve's budget of twelve for an entire shader.** `common_vs_fxc.h`
    /// reserves c0–c37 for the engine — fog, the view and model matrices, the ambient cube at
    /// c21–c26, four lights at c27–c36, the modulation colour at c37 — and gives the shader
    /// `SHADER_SPECIFIC_CONST_0..9` at c38–c47, with c14 and c15 borrowed for two more.
    ///
    /// That is the ceiling to design against, and it implies the shape as well as the size: Valve
    /// fits a whole shader's parameters into twelve registers by PACKING several values into the
    /// components of one, which is what `bump` and `envmapControl` below already do. So a new debug
    /// mode takes a component of the existing word rather than a register of its own — a register
    /// per feature would run out at a dozen features, and Valve never needed to.
    /// </remarks>
    private const int CameraConstants = 16 + 4 + 4 + 4 + 4;

    /// <summary>Floats in the bone buffer: three rows of four per bone.</summary>
    private const int BoneConstants = MaxBones * 3 * 4;

    /// <summary>Lays vertices out for the input layout.</summary>
    /// <remarks>
    /// **One writer, because there were two and they drifted.** The world buffer and the model
    /// buffer each had their own copy of this loop, and adding the next-frame fields to the vertex
    /// updated the stride and neither loop — so every vertex was written fifteen floats into a
    /// twenty-one float layout and the whole map sheared. Caught by the offscreen render tests
    /// rather than by anything reading this code.
    ///
    /// The order here IS the input layout above; the two are one decision written twice, and this
    /// is the half that can be checked by counting.
    /// </remarks>
    private static float[] Pack(IReadOnlyList<WorldVertex> vertices)
    {
        float[] data = new float[vertices.Count * (VertexStride / sizeof(float))];
        int at = 0;

        foreach (WorldVertex vertex in vertices)
        {
            data[at++] = vertex.X;
            data[at++] = vertex.Y;
            data[at++] = vertex.Depth;
            data[at++] = vertex.U;
            data[at++] = vertex.V;
            data[at++] = vertex.LightU;
            data[at++] = vertex.LightV;
            data[at++] = vertex.Alpha;
            data[at++] = vertex.Red;
            data[at++] = vertex.Green;
            data[at++] = vertex.Blue;
            data[at++] = vertex.LightStep;
            data[at++] = vertex.NormalX;
            data[at++] = vertex.NormalY;
            data[at++] = vertex.NormalZ;
            data[at++] = vertex.NextX;
            data[at++] = vertex.NextY;
            data[at++] = vertex.NextZ;
            data[at++] = vertex.NextNormalX;
            data[at++] = vertex.NextNormalY;
            data[at++] = vertex.NextNormalZ;
            data[at++] = vertex.BoneA;
            data[at++] = vertex.BoneB;
            data[at++] = vertex.BoneC;
            data[at++] = vertex.WeightA;
            data[at++] = vertex.WeightB;
            data[at++] = vertex.WeightC;
        }

        return data;
    }

    /// <summary>Writes one cube face, leaving its w alone.</summary>
    private static void WriteFace(Span<float> into, int at, (float Red, float Green, float Blue) face)
    {
        into[at] = face.Red;
        into[at + 1] = face.Green;
        into[at + 2] = face.Blue;
    }

    private void EnsureModelBuffer(ComPtr<ID3D11DeviceContext> context)
    {
        if (_model.Handle is not null)
        {
            return;
        }

        ComPtr<ID3D11Device> device = default;

        context.GetDevice(ref device);

        BufferDesc description = new()
        {
            ByteWidth = sizeof(float) * ModelConstants,
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.ConstantBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write,
        };

        ComPtr<ID3D11Buffer> buffer = default;

        SilkMarshal.ThrowHResult(device.CreateBuffer(in description, null, ref buffer));

        _model = buffer;
        device.Dispose();
    }

    /// <summary>Uploads the bone matrices for one skinned model.</summary>
    /// <param name="context">The device context.</param>
    /// <param name="matrices">Row-major 3x4 matrices, twelve floats each, one per bone.</param>
    /// <exception cref="ArgumentNullException"><paramref name="matrices"/> is null.</exception>
    /// <remarks>
    /// **This is the per-draw cost that baking avoids and skinning accepts.** A prop's frames are
    /// baked once and drawn by picking a vertex range; a player has too many animations for that,
    /// so its pose arrives as matrices instead. Roughly ninety bones is 4.3 kilobytes a draw,
    /// against the alternative of gigabytes of baked geometry.
    ///
    /// Bones past the buffer's room are dropped rather than allowed to overrun it. A model with
    /// more bones than this draws by the ones that fit, which is visibly wrong at an extremity and
    /// far better than a corrupt constant buffer - and TF2's models are well inside it.
    /// </remarks>
    public void SetBones(ComPtr<ID3D11DeviceContext> context, IReadOnlyList<float[]> matrices)
    {
        ArgumentNullException.ThrowIfNull(matrices);

        EnsureBoneBuffer(context);

        if (_bones.Handle is null)
        {
            return;
        }

        // **Straight into the mapped buffer** (the outside audit's finding 3). This staged through
        // `new float[BoneConstants]` — 1,536 floats, about six kilobytes, allocated once per
        // SKINNED draw — where the engine locks and writes. The clear is load-bearing twice over:
        // WriteDiscard memory is undefined, a model's unused bone slots must not carry the
        // previous model's skeleton, and the short-matrix `continue` below has always meant "this
        // slot stays zero".
        MappedSubresource mapped = default;

        SilkMarshal.ThrowHResult(context.Map(_bones, 0, Map.WriteDiscard, 0, ref mapped));

        Span<float> contents = new(mapped.PData, BoneConstants);

        contents.Clear();

        for (int bone = 0; bone < matrices.Count && bone < MaxBones; bone++)
        {
            float[] matrix = matrices[bone];

            if (matrix.Length < 12)
            {
                continue;
            }

            matrix.AsSpan(0, 12).CopyTo(contents.Slice(bone * 12, 12));
        }

        context.Unmap(_bones, 0);
        context.VSSetConstantBuffers(3, 1, ref _bones);
    }

    private void EnsureBoneBuffer(ComPtr<ID3D11DeviceContext> context)
    {
        if (_bones.Handle is not null)
        {
            return;
        }

        ComPtr<ID3D11Device> device = default;

        context.GetDevice(ref device);

        BufferDesc description = new()
        {
            ByteWidth = sizeof(float) * BoneConstants,
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.ConstantBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write,
        };

        ComPtr<ID3D11Buffer> buffer = default;

        SilkMarshal.ThrowHResult(device.CreateBuffer(in description, null, ref buffer));

        _bones = buffer;
        device.Dispose();
    }

    private void EnsureMaterialBuffer(ComPtr<ID3D11DeviceContext> context)
    {
        if (_material.Handle is not null)
        {
            return;
        }

        ComPtr<ID3D11Device> device = default;

        context.GetDevice(ref device);

        BufferDesc description = new()
        {
            // **Sized from the resting values, so the buffer and the shader cannot disagree** —
            // which is what this comment claimed while they disagreed by two float4s for as long
            // as $envmap has existed. NoDetail did not grow with the shader's Material block, so
            // the buffer was 160 bytes against a declared 192 and SetMaterial wrote past the end.
            // It drew correctly, because the driver tolerated it.
            //
            // The invariant is now CHECKED rather than asserted in prose:
            // `MaterialBufferTests` counts the float4s in the shader source and compares. A comment
            // stating an invariant is not an invariant.
            ByteWidth = (uint)(sizeof(float) * NoDetail.Length),
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.ConstantBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write,
        };

        ComPtr<ID3D11Buffer> buffer = default;

        SilkMarshal.ThrowHResult(device.CreateBuffer(in description, null, ref buffer));

        _material = buffer;
        device.Dispose();
    }

    /// <summary>Uploads one material's detail constants before its draw.</summary>
    /// <remarks>
    /// **Bound to the pixel stage only**, because only the pixel shader reads it - unlike the camera
    /// buffer, which both stages read and which spent a session bound to one of them.
    /// </remarks>
    /// <summary>Where each proxy-writable value sits in the material constant buffer.</summary>
    /// <remarks>
    /// Named rather than written as literals at the write sites, because these are offsets into an
    /// array whose layout is declared somewhere else entirely — the <c>cbuffer Material</c> in the
    /// shader — and a silent disagreement between the two is a material borrowing another's scroll.
    /// </remarks>
    private const int BaseTransformRow0 = 16;
    private const int BaseTransformRow1 = 20;
    private const int SecondTransformRow0 = 24;
    private const int SecondTransformRow1 = 28;
    private const int ModulationRed = 32;

    /// <summary>Where <c>categoryColour</c> starts, in floats from the struct's front.</summary>
    /// <remarks>
    /// **Named because addressing it from the END of the array was wrong the moment the struct
    /// grew** (B331). It was written as `contents.Length - 4` while `categoryColour` happened to be
    /// the last float4; appending `tintControl` after it sent the per-batch category colour into the
    /// tint controls, whose x is read as `$blendtintbybasealpha` — so every reflective model took
    /// the tint branch against a garbage mask and drew pure white.
    ///
    /// Seventeenth float4 of eighteen, counted from the shader's own struct: four uninterpreted,
    /// four transform rows, modulation, combine, two envmap, three phong, rim, category, tint.
    /// </remarks>
    private const int CategoryColourRed = 64;
    private const int ModulationAlpha = 35;

    /// <summary>Runs a material's proxies for the current playback time.</summary>
    /// <param name="contents">The material's constants, already copied.</param>
    /// <param name="proxies">What the VMT declared, in declaration order.</param>
    /// <param name="materialIndex">
    /// Which material, so a proxy can read the material's own variables — <c>$colortint_base</c> and
    /// the <c>$color</c> factor — rather than only the constants (B330).
    /// </param>
    /// <param name="paint">
    /// The colour the ENTITY being drawn is painted, or null for an unpainted one. TF2's
    /// <c>ItemTintColor</c> is a per-entity proxy, which is why it arrives at the bind rather than
    /// being folded into the material at load.
    /// </param>
    /// <param name="burn">
    /// How alight the entity is, 0 to 1, for TF2's <c>BurnLevel</c> proxy (B336). Per entity for
    /// the same reason the paint is.
    /// </param>
    /// <param name="urine">
    /// The jarate multiplier for this entity, for <c>YellowLevel</c> (B336). White where nobody is
    /// hit, which is a multiply by one.
    /// </param>
    /// <remarks>
    /// **In order, because last wins.** Two proxies writing the same variable is legal and the
    /// engine resolves it by running them in the order the file lists them.
    ///
    /// **Only the time-driven ones.** A proxy reading entity state — team colour, health, a
    /// player's item — needs the entity, which this layer does not have. An unrecognised proxy is
    /// skipped rather than guessed at, which leaves the material at its resting value: the same
    /// picture as before proxies existed, rather than a wrong one.
    /// </remarks>
    private void ApplyProxies(
        float[] contents,
        IReadOnlyList<MaterialProxy> proxies,
        int materialIndex,
        (float Red, float Green, float Blue)? paint,
        float burn,
        (float Red, float Green, float Blue) urine)
    {
        // **A material variable table, because TF2's paint chain writes one proxy's output into
        // another's input** (B330). `ItemTintColor` produces `$colortint_tmp` and
        // `SelectFirstIfNonZero` consumes it — neither is a shader constant, and running the second
        // without the first's result would be running half a mechanism.
        //
        // Seeded from the material rather than left empty: `$colortint_base` is what an UNPAINTED
        // item wears, and a `SelectFirstIfNonZero` reading a missing variable as zero would paint
        // every unpainted cosmetic black.
        // **Created for EVERY material that runs a proxy, not only tintable ones** (B337). It used
        // to exist only where `$colortint_base` did, because the paint chain was the only chain —
        // and `YellowLevel` writes `$yellow` on 7,570 materials, most of which carry no paint at
        // all. Gating the table on the paint would have left every one of those evaluating nothing
        // while looking implemented.
        // **Seeded from the material's OWN declared parameters** (B340), because that is what the
        // engine's lookup finds: `CFunctionProxy::Init` calls `pMaterial->FindVar( name, … )`,
        // which sees every parameter the VMT declares and not only what an earlier proxy wrote.
        //
        // **Seeding from proxy outputs alone drops whole operations**, and `dec18_dumb_bell.vmt` is
        // the worked example: it multiplies `$saturatedTint` by `$tintMulti`, and `$tintMulti` is
        // the declared constant `"10"`. With no seed the source is absent, the refusal below fires,
        // and the item's phong and envmap tints never get their multiplier.
        Dictionary<string, (float Red, float Green, float Blue)> variables =
            materialIndex >= 0 && materialIndex < _variables.Count &&
            _variables[materialIndex] is { } declared
                ? new(declared, StringComparer.OrdinalIgnoreCase)
                : new(StringComparer.OrdinalIgnoreCase);

        if (Tintable(materialIndex) is { } tintBase)
        {
            // `$colortint_base` is what an UNPAINTED item wears. It is normally among the declared
            // parameters above; this keeps the resolved value, which has been through the same
            // brace-versus-bracket rule and is what the rest of the renderer draws with.
            variables["$colortint_base"] = tintBase;
        }

        foreach (MaterialProxy proxy in proxies)
        {
            if (proxy.Name.Equals("Sine", StringComparison.OrdinalIgnoreCase))
            {
                ApplySine(contents, proxy);
            }
            else if (proxy.Name.Equals("TextureScroll", StringComparison.OrdinalIgnoreCase))
            {
                ApplyTextureScroll(contents, proxy);
            }
            else if (proxy.Name.Equals("ItemTintColor", StringComparison.OrdinalIgnoreCase))
            {
                ApplyItemTintColor(variables, proxy, paint);
            }
            else if (proxy.Name.Equals("SelectFirstIfNonZero", StringComparison.OrdinalIgnoreCase))
            {
                ApplySelectFirstIfNonZero(contents, variables, proxy, materialIndex);
            }
            else if (proxy.Name.Equals("BurnLevel", StringComparison.OrdinalIgnoreCase))
            {
                ApplyBurnLevel(contents, proxy, burn);
            }
            else if (proxy.Name.Equals("YellowLevel", StringComparison.OrdinalIgnoreCase))
            {
                // **It writes a VARIABLE and nothing else.** `$yellow` reaches the picture only
                // because two `Equals` proxies copy it into `$color2` and `$selfillumtint`, which
                // is why implementing this without the arithmetic below would be half a mechanism.
                if (proxy.Argument("resultVar") is { Length: > 0 } yellow)
                {
                    Publish(contents, variables, yellow, urine, materialIndex);
                }
            }
            else if (Arithmetic(proxy.Name) is { } operation)
            {
                ApplyArithmetic(contents, variables, proxy, operation, materialIndex);
            }
            else if (proxy.Name.Equals("Clamp", StringComparison.OrdinalIgnoreCase))
            {
                ApplyClamp(contents, variables, proxy, materialIndex);
            }
        }
    }

    /// <summary>Which arithmetic proxy a name is, or null for anything else.</summary>
    private static MaterialProxies.MathProxy? Arithmetic(string name) => name.ToUpperInvariant() switch
    {
        "EQUALS" => MaterialProxies.MathProxy.Equals,
        "ADD" => MaterialProxies.MathProxy.Add,
        "SUBTRACT" => MaterialProxies.MathProxy.Subtract,
        "MULTIPLY" => MaterialProxies.MathProxy.Multiply,
        "DIVIDE" => MaterialProxies.MathProxy.Divide,
        _ => null,
    };

    /// <summary>One of Valve's arithmetic proxies over the material's variables (B337).</summary>
    /// <param name="contents">The material constants for this draw.</param>
    /// <param name="variables">The table these proxies read and write.</param>
    /// <param name="proxy">The proxy, naming its sources and its result.</param>
    /// <param name="operation">Which arithmetic it is.</param>
    /// <param name="materialIndex">Which material, for the colour factor.</param>
    /// <remarks>
    /// **A missing source reads as zero, which is what an undefined material variable is.** The
    /// engine would refuse the proxy at `Init` for a variable the material does not declare —
    /// `FindVar( …, &amp;foundVar, false )` — and a proxy that never initialised never runs. Reading
    /// zero and running anyway differs only for a chain whose first link is absent, where the
    /// engine leaves the result alone and this writes a zero into it. Recorded rather than fixed:
    /// distinguishing them needs the material's declared variable list at bind, which this layer
    /// does not carry.
    /// </remarks>
    private void ApplyArithmetic(
        float[] contents,
        Dictionary<string, (float Red, float Green, float Blue)> variables,
        MaterialProxy proxy,
        MaterialProxies.MathProxy operation,
        int materialIndex)
    {
        if (proxy.Argument("srcVar1") is not { Length: > 0 } first ||
            proxy.Argument("resultVar") is not { Length: > 0 } result)
        {
            return;
        }

        // **A source no earlier proxy wrote is a variable the material does not declare**, and the
        // engine refuses such a proxy at `Init` rather than reading zero from it. See the longer
        // note in `ApplySelectFirstIfNonZero`, where relaxing exactly this reddened five pixel
        // tests.
        if (Read(variables, first) is not { } a)
        {
            return;
        }

        (float Red, float Green, float Blue) b = default;

        if (proxy.Argument("srcVar2") is { Length: > 0 } second)
        {
            b = Read(variables, second) ?? default;
        }
        else if (operation is not MaterialProxies.MathProxy.Equals)
        {
            // **Every one but `Equals` requires two arguments**, and the engine refuses the proxy
            // outright when the second is missing: `ok = ok && m_pSrc2` in each `Init`.
            return;
        }

        Publish(contents, variables, result, MaterialProxies.Apply(operation, a, b), materialIndex);
    }

    /// <summary><c>CClampProxy</c>, with its bounds (B337).</summary>
    /// <param name="contents">The material constants for this draw.</param>
    /// <param name="variables">The table it reads and writes.</param>
    /// <param name="proxy">The proxy, naming its source, its result and its bounds.</param>
    /// <param name="materialIndex">Which material, for the colour factor.</param>
    /// <remarks>
    /// Valve's defaults, from `CClampProxy::Init`: `min` 0 and `max` 1. Both are read through
    /// `CFloatInput`, so a material may state either as a number or omit it.
    /// </remarks>
    private void ApplyClamp(
        float[] contents,
        Dictionary<string, (float Red, float Green, float Blue)> variables,
        MaterialProxy proxy,
        int materialIndex)
    {
        if (proxy.Argument("srcVar1") is not { Length: > 0 } source ||
            proxy.Argument("resultVar") is not { Length: > 0 } result ||
            Read(variables, source) is not { } value)
        {
            return;
        }

        Publish(
            contents,
            variables,
            result,
            MaterialProxies.Clamp(
                value,
                MaterialProxies.Number(proxy.Argument("min"), 0f),
                MaterialProxies.Number(proxy.Argument("max"), 1f)),
            materialIndex);
    }

    /// <summary>One of a proxy's sources, or null when the material declares no such variable.</summary>
    /// <param name="variables">The table.</param>
    /// <param name="reference">What the VMT wrote, possibly naming one component.</param>
    /// <returns>The value, with a named component broadcast; null when there is no variable.</returns>
    /// <remarks>
    /// **Null and zero are different answers**, which is the whole reason this returns a nullable:
    /// a variable the material never declared means the engine refused the proxy at `Init`, and a
    /// variable holding zero means it ran on a zero. Collapsing them is what reddened five
    /// reflection tests in B337.
    /// </remarks>
    private static (float Red, float Green, float Blue)? Read(
        Dictionary<string, (float Red, float Green, float Blue)> variables, string reference)
    {
        (string name, int component) = MaterialProxies.Reference(reference);

        return variables.TryGetValue(name, out (float Red, float Green, float Blue) value)
            ? MaterialProxies.ReadComponent(value, component)
            : null;
    }

    /// <summary>Stores a proxy's result, and pushes it to the constants when it is drawn.</summary>
    /// <param name="contents">The material constants for this draw.</param>
    /// <param name="variables">The table.</param>
    /// <param name="reference">
    /// Which variable the proxy named, possibly naming one component — <c>$envmaptint[1]</c>.
    /// </param>
    /// <param name="value">What it computed.</param>
    /// <param name="materialIndex">Which material, for the colour factor.</param>
    /// <remarks>
    /// **Only <c>$color2</c> reaches a shader constant, and that is not a simplification** — it is
    /// the one material variable in this chain the renderer already draws with. Everything else a
    /// proxy writes is an intermediate that another proxy reads, which is exactly why the table
    /// exists.
    ///
    /// **The material's own <c>$color</c> multiplies it**, because `Modulation` is the resting
    /// product of the two and a proxy replaces only the second.
    /// </remarks>
    private void Publish(
        float[] contents,
        Dictionary<string, (float Red, float Green, float Blue)> variables,
        string reference,
        (float Red, float Green, float Blue) value,
        int materialIndex)
    {
        // **A proxy may name ONE component of a vector variable, and 150 shipped materials do**
        // (B339) — `$envmaptint[1]`, `$selfillumfresnelminmaxexp[2]`, `$temp[1]`. The engine writes
        // that component alone and leaves the rest (`functionproxy.cpp:141-160`); writing all three
        // turns a reflection tint or a self-illumination ramp into a grey of itself.
        (string result, int component) = MaterialProxies.Reference(reference);

        if (component >= 0)
        {
            // The operation is float-typed when a component is named, so the value arrives
            // broadcast and any of its components is the scalar the engine computed.
            _ = variables.TryGetValue(result, out (float Red, float Green, float Blue) current);

            value = MaterialProxies.WriteComponent(current, component, value.Red);
        }

        variables[result] = value;

        if (!result.Equals("$color2", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        (float Red, float Green, float Blue) factor =
            materialIndex >= 0 && materialIndex < _colourFactors.Count
                ? _colourFactors[materialIndex]
                : (1f, 1f, 1f);

        contents[ModulationRed] = factor.Red * value.Red;
        contents[ModulationRed + 1] = factor.Green * value.Green;
        contents[ModulationRed + 2] = factor.Blue * value.Blue;
    }

    /// <summary>How alight the entity is, written where the proxy says (B336).</summary>
    /// <param name="contents">The material constants for this draw.</param>
    /// <param name="proxy">The proxy, whose <c>resultVar</c> names its output.</param>
    /// <param name="burn">The value <c>CProxyBurnLevel</c> computed, 0 to 1.</param>
    /// <remarks>
    /// **`$detailblendfactor` and nothing else, because that is what the game asks for.** Measured
    /// with `vmt-proxy BurnLevel` over the 30,684 shipped materials: 6,715 of the 6,718 running
    /// this proxy name `$detailblendfactor` as their result. The other three name `$burnlevel` and
    /// the literal `1`, neither of which any shader here reads — so they are left alone rather than
    /// guessed at, which is what an unrecognised proxy already does.
    ///
    /// **The value REPLACES the material's own factor rather than scaling it**, and that is the
    /// engine: `m_pResult->SetFloatValue( flResult )`. A player material rests at
    /// `$detailblendfactor .01` — the fire is all but invisible — and the proxy writes over it, so
    /// multiplying instead would leave a burning player at a hundredth of the fire they should
    /// have.
    /// </remarks>
    private static void ApplyBurnLevel(float[] contents, MaterialProxy proxy, float burn)
    {
        if (!string.Equals(
            proxy.Argument("resultVar"), "$detailblendfactor", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        contents[DetailBlendFactor] = burn;
    }

    /// <summary>Where <c>$detailblendfactor</c> sits in the material constants.</summary>
    /// <remarks>
    /// **Named rather than written as 1**, because the four regressions this buffer has had were
    /// all a literal index that stopped meaning what it said. The guard on
    /// <see cref="CategoryColourRed"/> is the same idea from the other end.
    /// </remarks>
    private const int DetailBlendFactor = 1;

    /// <summary>The animation frame a material shows now, or a null handle (B341).</summary>
    /// <param name="materialIndex">Which material.</param>
    /// <param name="still">What to bind when the material animates nothing.</param>
    /// <returns>The frame's view, or <paramref name="still"/>.</returns>
    /// <remarks>
    /// **`CAnimatedTextureProxy` runs off ABSOLUTE time**, not per entity:
    /// `GetAnimationStartTime` returns 0 (`animatedtextureproxy.cpp:25-28`). So the frame is a
    /// function of the playback clock alone and every draw of a material shows the same one, which
    /// is why this needs no entity and belongs beside `Sine` rather than beside the paint.
    /// </remarks>
    private ComPtr<ID3D11ShaderResourceView> AnimationFrame(
        int materialIndex, ComPtr<ID3D11ShaderResourceView> still)
    {
        if (materialIndex < 0 || materialIndex >= _animationFrames.Count)
        {
            return still;
        }

        ComPtr<ID3D11ShaderResourceView>[] frames = _animationFrames[materialIndex];

        if (frames.Length == 0)
        {
            return still;
        }

        return frames[
            MaterialProxies.AnimationFrame(Seconds, _animationRates[materialIndex], frames.Length)];
    }

    /// <summary>A material's base-texture animation rate, or the engine's default (B341).</summary>
    /// <param name="proxies">The material's proxies.</param>
    /// <returns><c>animatedTextureFrameRate</c>, or 15.</returns>
    /// <remarks>
    /// **Only the proxy animating `$basetexture` counts.** A material may run several
    /// `AnimatedTexture` proxies at different rates — one for the base and one for the detail — and
    /// taking the first would give the base texture the detail's rate.
    /// </remarks>
    private static float RateOf(IReadOnlyList<MaterialProxy> proxies)
    {
        foreach (MaterialProxy proxy in proxies)
        {
            if (proxy.Name.Equals("AnimatedTexture", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    proxy.Argument("animatedTextureVar"),
                    "$basetexture",
                    StringComparison.OrdinalIgnoreCase))
            {
                return MaterialProxies.Number(
                    proxy.Argument("animatedTextureFrameRate"),
                    MaterialProxies.DefaultAnimationRate);
            }
        }

        return MaterialProxies.DefaultAnimationRate;
    }

    /// <summary>The colour an unpainted tintable item wears, or null for anything else.</summary>
    private (float Red, float Green, float Blue)? Tintable(int materialIndex) =>
        materialIndex >= 0 && materialIndex < _textures.Count &&
        materialIndex < _tintBases.Count
            ? _tintBases[materialIndex]
            : null;

    /// <summary>TF2's paint, written into whichever variable the proxy names (B330).</summary>
    /// <remarks>
    /// <c>CProxyItemTintColor::OnBind</c> (<c>econ_wearable.cpp:465-543</c>). **Zero when the item
    /// is unpainted, and that is the mechanism rather than a fallback** — the result starts at
    /// <c>Vector( 0, 0, 0 )</c> and is left there, which is precisely what makes the
    /// <c>SelectFirstIfNonZero</c> beside it choose the material's own colour instead. Writing
    /// white here, or skipping the write, would break the pair.
    ///
    /// The value itself — the attribute, the old-team sentinel, the alt fallback — is
    /// <c>ItemPaint</c>, computed where the item's econ attributes are.
    /// </remarks>
    private static void ApplyItemTintColor(
        Dictionary<string, (float Red, float Green, float Blue)> variables,
        MaterialProxy proxy,
        (float Red, float Green, float Blue)? paint)
    {
        if (proxy.Argument("resultVar") is not { Length: > 0 } result)
        {
            return;
        }

        variables[result] = paint ?? (0f, 0f, 0f);
    }

    /// <summary>Valve's <c>CSelectFirstIfNonZeroProxy</c>, over vectors (B330).</summary>
    /// <remarks>
    /// <code>
    /// if ( !a.IsZero() ) m_pResult->SetVecValue( a.Base(), vecSize );
    /// else               m_pResult->SetVecValue( b.Base(), vecSize );
    /// </code>
    ///
    /// <c>mathproxy.cpp:1050-1062</c>. **`IsZero` is all three channels**, so a paint of pure black
    /// would be indistinguishable from no paint — which is Valve's own behaviour and is reproduced
    /// rather than improved on.
    ///
    /// **The result reaches the shader only for `$color2`**, which is where every shipped tintable
    /// material sends it. The modulation constant holds `$color * $color2`, so the factor is
    /// multiplied back in here — writing the selected colour raw would drop a material's own
    /// `$color` on the floor.
    /// </remarks>
    private void ApplySelectFirstIfNonZero(
        float[] contents,
        Dictionary<string, (float Red, float Green, float Blue)> variables,
        MaterialProxy proxy,
        int materialIndex)
    {
        if (proxy.Argument("srcVar1") is not { Length: > 0 } first ||
            proxy.Argument("srcVar2") is not { Length: > 0 } second ||
            proxy.Argument("resultVar") is not { Length: > 0 } result)
        {
            return;
        }

        // **A proxy whose sources do not exist does not RUN**, which is the engine's own refusal:
        // `Init` calls `FindVar( name, &foundVar, false )` and returns false when the material does
        // not declare the variable, and a proxy that failed to initialise is never bound.
        //
        // **This was learned by breaking it** (B337). Widening the variable table to every material
        // — needed because `YellowLevel` writes `$yellow` on materials carrying no paint — made this
        // proxy run on materials where `$colortint_base` is absent, read it as zero, take the other
        // branch and OVERWRITE the modulation. Five reflection pixel tests went red, which is the
        // fourth time that family has caught a change to this buffer. The gate it replaced was
        // load-bearing, and the comment beside it said so: *"a SelectFirstIfNonZero reading a
        // missing variable as zero would paint every unpainted cosmetic black"*.
        if (Read(variables, first) is not { } a && Read(variables, second) is null)
        {
            return;
        }

        a = Read(variables, first) ?? default;

        (float Red, float Green, float Blue) b = Read(variables, second) ?? default;

        (float Red, float Green, float Blue) chosen = a is (0f, 0f, 0f) ? b : a;

        // **Through the shared publish**, which is where the `$color2` step lived when this was the
        // only chain. Extracted for B337 rather than copied: five more proxies now write variables,
        // and any of them may be the one a material ends its chain on.
        Publish(contents, variables, result, chosen, materialIndex);
    }

    /// <summary>Oscillates whichever variable the proxy names.</summary>
    /// <remarks>
    /// Valve's defaults, from <c>CSineProxy::Init</c>: period 1, max 1, min 0. A period of zero
    /// becomes one rather than holding still — see <see cref="MaterialProxies.Sine"/>.
    /// </remarks>
    private void ApplySine(float[] contents, MaterialProxy proxy)
    {
        float value = MaterialProxies.Sine(
            Seconds,
            MaterialProxies.Number(proxy.Argument("sinePeriod"), 1f),
            MaterialProxies.Number(proxy.Argument("sineMin"), 0f),
            MaterialProxies.Number(proxy.Argument("sineMax"), 1f));

        // A maths proxy names its destination with resultVar (CResultProxy::Init). The subscript
        // form -- $color[1] for green alone -- is not handled here, and a name carrying one falls
        // through to no write rather than being mistaken for the whole variable.
        switch (proxy.Argument("resultVar"))
        {
            case { } alpha when alpha.Equals("$alpha", StringComparison.OrdinalIgnoreCase):
                contents[ModulationAlpha] = value;
                break;

            case { } colour when colour.Equals("$color", StringComparison.OrdinalIgnoreCase):
                contents[ModulationRed] = value;
                contents[ModulationRed + 1] = value;
                contents[ModulationRed + 2] = value;
                break;

            default:
                break;
        }
    }

    /// <summary>Scrolls whichever texture transform the proxy names.</summary>
    /// <remarks>
    /// **A texture scroll does NOT use <c>resultVar</c>.** It reads <c>textureScrollVar</c>
    /// (<c>texturescrollmaterialproxy.cpp:54</c>), because what it writes is a matrix rather than a
    /// number and it is not a <c>CResultProxy</c> at all. Reading the wrong key silently disables
    /// it.
    /// </remarks>
    private void ApplyTextureScroll(float[] contents, MaterialProxy proxy)
    {
        TextureTransform transform = MaterialProxies.TextureScroll(
            Seconds,
            MaterialProxies.Number(proxy.Argument("textureScrollRate"), 1f),
            MaterialProxies.Number(proxy.Argument("textureScrollAngle"), 0f),
            MaterialProxies.Number(proxy.Argument("textureScale"), 1f));

        // Which of the material's two transforms this drives. The second texture's is named
        // differently by different shaders, so both spellings are accepted.
        int row0 = proxy.Argument("textureScrollVar") switch
        {
            { } name when name.Contains('2', StringComparison.Ordinal) => SecondTransformRow0,
            _ => BaseTransformRow0,
        };

        int row1 = row0 == SecondTransformRow0 ? SecondTransformRow1 : BaseTransformRow1;

        contents[row0] = transform.Row0.X;
        contents[row0 + 1] = transform.Row0.Y;
        contents[row0 + 2] = transform.Row0.Z;
        contents[row0 + 3] = transform.Row0.W;

        contents[row1] = transform.Row1.X;
        contents[row1 + 1] = transform.Row1.Y;
        contents[row1 + 2] = transform.Row1.Z;
        contents[row1 + 3] = transform.Row1.W;
    }

    /// <summary>Valve's dev-texture colour for a category, chosen at draw time (B219).</summary>
    /// <param name="category">What the batch is.</param>
    /// <returns>The colour the category view tints it.</returns>
    /// <remarks>
    /// **These numbers were baked into vertices until 2026-08-27**, which is why switching the view
    /// rebuilt every vertex in the map — and why `ClearWorld` then discarded the models with them.
    /// They are a bounded set, so nothing about them ever needed to be per-vertex: the owner,
    /// correcting the assumption that they might be arbitrary, *"the colors match valves dev texture
    /// colors"*.
    ///
    /// **White for Missing, so Valve's chequer shows in its own colours.** The renderer binds the
    /// magenta-and-black missing-material chequer under that category rather than the measurement
    /// grid, and a tint would only muddy the most recognisable "this is broken" signal in Source.
    /// Magenta belongs to Hammer's uncoloured entity; an unresolved material is a PATTERN, and the
    /// two are told apart by that rather than by hue.
    /// </remarks>
    private static (float Red, float Green, float Blue) CategoryColour(SurfaceCategory category) =>
        category switch
        {
            SurfaceCategory.Terrain => (0.25f, 0.85f, 0.35f),
            SurfaceCategory.Prop => (1f, 0.6f, 0.15f),

            // Violet, chosen to sit away from all four of the others rather than to look nice: an
            // overlay lies ON brushwork and next to props, so it has to be told from grey-blue and
            // orange at a glance and at a distance.
            SurfaceCategory.Overlay => (0.62f, 0.4f, 0.92f),
            SurfaceCategory.Missing => (1f, 1f, 1f),
            _ => (0.55f, 0.6f, 0.72f),
        };

    private void SetMaterial(
        ComPtr<ID3D11DeviceContext> context,
        int materialIndex,
        SurfaceCategory? category = null,
        (float Red, float Green, float Blue)? tint = null,
        (float Red, float Green, float Blue)? paint = null,
        float burn = 0f,
        (float Red, float Green, float Blue)? urine = null)
    {
        // **The category view's underlay, chosen per material because that is what decides it.**
        // A material that resolved to nothing draws Valve's magenta-and-black chequer; everything
        // else draws Valve's measurement grid. Bound to one slot either way, so the shader has no
        // branch to keep in step, and set HERE rather than at the three call sites for the reason
        // the depth state below is — three copies of one decision is how they drift apart.
        ComPtr<ID3D11ShaderResourceView> underlay =
            _chequered.Contains(materialIndex) || _devGrid.Handle is null ? _white : _devGrid;

        context.PSSetShaderResources(7, 1, ref underlay);

        // The luxel grid rides along on the same per-material bind: it is frame-constant, but it
        // costs nothing here and one binding site is easier to keep correct than two.
        ComPtr<ID3D11ShaderResourceView> luxels =
            _luxelGrid.Handle is not null ? _luxelGrid : _white;

        context.PSSetShaderResources(8, 1, ref luxels);

        // **The material's own thumbnail, which is why this one is per material at all.** devMap
        // and luxelMap are the same image for every surface; this differs per texture, so
        // `mat_showlowresimage` is the only debug substitution that has to be rebound here.
        //
        // **Falls back to an unbound slot, and NOT to `_white`.** `_white` in this renderer is
        // Valve's magenta-and-black chequer, so binding it here would chequer every material whose
        // VTF carried no thumbnail — a defect the debug view would have invented, and exactly the
        // trap `docs/memory/a-neutral-default-must-be-neutral.md` was written about.
        //
        // A null SRV samples as (0,0,0,0) in D3D11, so alpha is zero, and the shader's alpha test
        // keeps the material's own texture. "No thumbnail" therefore reads as "nothing to
        // substitute" rather than as any colour at all.
        ComPtr<ID3D11ShaderResourceView> thumbnail =
            materialIndex >= 0 && materialIndex < _thumbnails.Count
                ? _thumbnails[materialIndex]
                : default;

        context.PSSetShaderResources(9, 1, ref thumbnail);

        // **The material carries its own depth state, which is the engine's arrangement (B135).**
        // A shader in Source declares its render state in a SHADOW_STATE block and the material
        // system applies it when the material is bound — `EnableDepthWrites( false )` in
        // DecalModulate_dx9.cpp:66 is the plain case — so no pass inherits anything and the order
        // of passes is free.
        //
        // Setting it per PASS instead produced the same defect twice from opposite ends: a
        // translucent pass left a read-only state behind and models drew with no depth writes (B72),
        // and an overlay pass left the same state behind and static props did, so the rocks behind
        // a shipping container drew through it (B135). Both were fixed by making some pass tidy up
        // after another; this is the fix that stops the class.
        //
        // **Every clause below is Valve's, and B137 was filed saying otherwise (see its entry).**
        // The rule is one flag test per kind, each with a shader that states it:
        //
        //   $translucent  cable_dx9.cpp:55   if ( IS_FLAG_SET( MATERIAL_VAR_TRANSLUCENT ) )
        //                                    { EnableDepthWrites( false ); EnableBlending( true ); }
        //   $additive     cloud_dx9.cpp:52   EnableDepthWrites( false ); EnableBlending( true );
        //                                    then the flag picks ONE/ONE over SRC_ALPHA/INV
        //   a marking     DecalModulate_dx9.cpp:66   EnableDepthWrites( false )
        //   $alphatest    excluded, and that is the important one — EvaluateBlendRequirements
        //                 (BaseVSShader.cpp:1580) drops texture alpha from its translucency test
        //                 when MATERIAL_VAR_ALPHATEST is set, so foliage writes depth like any
        //                 opaque surface. VmtMaterial.IsTranslucent returns false for it.
        //
        // So blending and depth writing ARE one decision for these kinds in Valve's own shaders,
        // which is what this project does. Pinned by DepthWriteConformanceTests against the clauses
        // above rather than left as an assertion in a comment.
        //
        // The $decal key itself is no longer inferred: materialsystem.dll holds the flag-name table
        // as a `const char *` array INDEXED BY BIT POSITION, and $decal sits at index 16 — exactly
        // MATERIAL_VAR_DECAL = (1 << 16) from imaterial.h:372. Confirmed alongside $additive (7),
        // $alphatest (8) and $translucent (21), all from one base. See
        // DecalRenderStateConformanceTests.
        bool marks = _decalMaterials.Contains(materialIndex);

        bool blends = Blends(
            marks,
            _translucent.Contains(materialIndex),
            _additive.Contains(materialIndex),
            _modulate.ContainsKey(materialIndex));

        ComPtr<ID3D11DepthStencilState> depth = blends ? _decalDepth : _depthWrite;

        if (depth.Handle is not null)
        {
            context.OMSetDepthStencilState(depth, 0);
        }

        if (_material.Handle is null)
        {
            return;
        }

        float[] contents = materialIndex >= 0 && materialIndex < _detailParameters.Count
            ? _detailParameters[materialIndex]
            : NoDetail;

        if (!DrawDetail || !DrawBumped)
        {
            // Copied rather than mutated: the stored array is the material's, and turning a switch
            // off for one frame must not edit it for every frame after.
            contents = [.. contents];

            if (!DrawDetail)
            {
                contents[2] = -1f;
            }

            if (!DrawBumped)
            {
                contents[8] = 0f;
            }
        }

        // **This is the engine's OnBind.** IMaterialProxy has Init, OnBind and Release and no tick,
        // so a proxy runs when its material is bound for a draw — which is here. A material drawn
        // twice evaluates twice; one nothing draws evaluates never.
        if (materialIndex >= 0 && materialIndex < _proxies.Count && _proxies[materialIndex].Count > 0)
        {
            // Copied before writing, for the same reason as the switches above: the stored array is
            // the material's resting state, and a proxy must not bake this frame's value into it.
            contents = [.. contents];

            ApplyProxies(
                contents,
                _proxies[materialIndex],
                materialIndex,
                paint,
                burn,

                // **White, not the default triple.** An unset multiplier of (0,0,0) would draw
                // every player black — the neutral-default trap `_white` already sprang once.
                urine ?? (1f, 1f, 1f));
        }

        // **Every array feeding this buffer must be exactly the shader struct's length, and this is
        // the third time that has mattered.** The file already records a buffer created 160 bytes
        // wide against a declared 192 — written and read out of bounds, and it WORKED, because this
        // driver tolerated it.
        //
        // It happened again on 2026-08-27 adding `categoryColour`. Three arrays needed the extra
        // float4 and only two got it: `NoDetail` and the no-detail branch end with `]);` while the
        // WITH-detail branch ends with a bare `]`, so a replace-all matched two of three. Materials
        // carrying a detail texture then copied 64 floats into a 68-float buffer, leaving a tail of
        // whatever the last frame put there — and `Map.WriteDiscard` makes that different every
        // frame. The owner saw it immediately: *"the colors are kinda doing a disco now"*, and
        // *"it actually looks like it might be trying to do more than one debug view at once"*,
        // which is what a garbage float4 read as flags looks like.
        //
        // A comparison per batch is nothing next to a silent corruption that only some drivers
        // punish. Checked here rather than at load because this is the one place every array
        // arrives.
        if (contents.Length != NoDetail.Length)
        {
            throw new InvalidOperationException(
                $"material {materialIndex} carries {contents.Length} constants where the shader " +
                $"declares {NoDetail.Length}; a short array leaves the buffer's tail undefined");
        }

        // **And that the named offsets still point where they say** (B331). The length check above
        // catches an array that grew without the struct; this catches the struct growing without
        // the offsets — which is what happened when `tintControl` was appended after
        // `categoryColour` and the per-batch write, addressed from the END, landed in it.
        //
        // Cheap, and it is a comparison against the one array that IS the struct's shape rather
        // than a second hardcoded number that could drift the same way.
        if (CategoryColourRed + 4 != NoDetail.Length - 4)
        {
            throw new InvalidOperationException(
                $"categoryColour is declared at float {CategoryColourRed} of {NoDetail.Length}, "
                + "which is no longer the float4 before the last; the shader struct has changed "
                + "and the named offsets have not");
        }

        MappedSubresource mapped = default;

        SilkMarshal.ThrowHResult(context.Map(_material, 0, Map.WriteDiscard, 0, ref mapped));

        // **Sized from the array rather than from a literal.** It was a hardcoded sixteen floats,
        // which silently truncates the moment the buffer grows — and it grew, by five float4s of
        // texture transform and modulation. A short copy leaves the tail as whatever the previous
        // material wrote, which is the kind of fault that looks like one surface borrowing
        // another's scroll.
        fixed (float* source = contents)
        {
            System.Buffer.MemoryCopy(
                source,
                mapped.PData,
                sizeof(float) * contents.Length,
                sizeof(float) * contents.Length);
        }

        // **The category goes in after the copy, because it belongs to the BATCH** (B219). Two
        // batches of the same material can be different categories — a texture on a wall and on a
        // displacement — so it cannot live in the material's own array, and writing it here rather
        // than into a copy of that array keeps the per-batch cost at four floats instead of an
        // allocation.
        //
        // The last float4 of the struct, which is what `NoDetail` and both built arrays end with.
        if (category is { } which)
        {
            (float Red, float Green, float Blue) colour = CategoryColour(which);

            // **A brush entity's class colour goes on top of its category** (B219, B156). It is
            // brushwork, so it reads grey-blue like any other; the class colour is what says door,
            // lift, areaportal or trigger. Multiplied for the same reason the grid is: each says
            // something the other cannot, and replacing would throw one away.
            if (tint is { } entity)
            {
                colour = (colour.Red * entity.Red, colour.Green * entity.Green, colour.Blue * entity.Blue);
            }

            // **Addressed from a NAMED offset, not from the end of the array** (B331). This read
            // `contents.Length - 4` on the assumption that `categoryColour` is the struct's last
            // float4 — true when it was written, and false the moment `tintControl` was appended
            // after it. The category colour then landed in the tint controls, whose x is read as
            // `$blendtintbybasealpha`: every reflective model took the tint branch with a garbage
            // mask and drew pure white, which the two reflection render tests caught.
            //
            // The offset is derived from the struct rather than from the array's length, so
            // appending another float4 cannot move it again.
            float* target = (float*)mapped.PData;

            target[CategoryColourRed] = colour.Red;
            target[CategoryColourRed + 1] = colour.Green;
            target[CategoryColourRed + 2] = colour.Blue;
            target[CategoryColourRed + 3] = 1f;
        }

        context.Unmap(_material, 0);
        context.PSSetConstantBuffers(1, 1, ref _material);

        // **And to the vertex stage, which now reads it too.** The texture transforms are applied
        // to the coordinate in the vertex shader, as they are in the engine. The remark above this
        // method said this buffer was pixel-only and named the session that cost — so it is bound
        // to both rather than left to be discovered again.
        context.VSSetConstantBuffers(1, 1, ref _material);
    }

    /// <summary>Draws the additive materials over everything already painted.</summary>
    /// <summary>Draws the translucent materials over the opaque ones, back to front.</summary>
    /// <remarks>
    /// **Sorted per batch, and that is a real limitation rather than an oversight.** A batch is one
    /// material, so two translucent materials overlapping each other resolve by material order
    /// instead of by depth. Sorting per triangle would mean rebuilding the translucent geometry
    /// whenever the camera moves, which is exactly what the camera-matrix design removed - and the
    /// common case here is glass on a wall seen from above, which one ordering handles.
    ///
    /// Far to near means LARGEST depth first: height is inverted into depth, so the ground is far
    /// and a roof is near.
    /// </remarks>
    /// <summary>Does this material blend, and therefore write no depth?</summary>
    /// <param name="marks">It carries <c>$decal</c>.</param>
    /// <param name="translucent">
    /// <see cref="Content.Assets.VmtMaterial.IsTranslucent"/> — <c>$translucent</c>,
    /// <c>$vertexalpha</c> or a fractional <c>$alpha</c>, and NOT <c>$alphatest</c>.
    /// </param>
    /// <param name="additive"><c>$additive</c>.</param>
    /// <param name="modulate">The shader is <c>Modulate</c>.</param>
    /// <remarks>
    /// **Extracted so a conformance test can compare it against Valve's clauses without a GPU.**
    /// The four kinds and their citations are listed at the call site in <c>SetMaterial</c>; the
    /// reason it is one decision rather than two is that Valve's own shaders make it one decision —
    /// <c>cable_dx9.cpp:55</c> sets <c>EnableDepthWrites( false )</c> and <c>EnableBlending( true )</c>
    /// inside a single <c>IS_FLAG_SET( MATERIAL_VAR_TRANSLUCENT )</c>.
    ///
    /// **The clause that carries the weight is the one that is absent**: alpha-tested materials are
    /// not here, so foliage and grates write depth like any opaque surface. Getting that wrong is
    /// invisible on a screenshot and wrecks everything drawn behind a fence.
    /// </remarks>
    internal static bool Blends(bool marks, bool translucent, bool additive, bool modulate) =>
        marks || translucent || additive || modulate;

    /// <summary>A batch's average depth, for ordering.</summary>
    private static float MeanDepth(IReadOnlyList<WorldVertex> vertices, WorldBatch batch)
    {
        if (batch.VertexCount <= 0)
        {
            return 0f;
        }

        double total = 0;

        for (int at = batch.FirstVertex; at < batch.FirstVertex + batch.VertexCount; at++)
        {
            total += vertices[at].Depth;
        }

        return (float)(total / batch.VertexCount);
    }

    /// <summary>Draws the map's decals over the surfaces they lie on.</summary>
    /// <remarks>
    /// Before the translucent pass, because a decal is part of the surface it sits on rather than
    /// something in front of it: a window should blend over a sign painted on the wall behind it,
    /// not the other way round.
    /// </remarks>
    private void DrawDecals(ComPtr<ID3D11DeviceContext> context)
    {
        if (_decals.Count == 0 || _decalOffset.Handle is null)
        {
            return;
        }

        context.RSSetState(Raster(_decalOffset));

        // **Tested, never written (B135).** Set here rather than left to the opaque pass's state,
        // which writes: an overlay that writes depth makes everything drawn afterwards test against
        // a surface that is not there. Nothing after this needs restoring — DrawTranslucent
        // establishes its own state and so does the model pass (B72).
        if (_decalDepth.Handle is null)
        {
            // **Reported, not skipped.** A missing state here silently inherits the opaque pass's
            // — writing depth, comparing with Less — which is the exact defect this pass exists to
            // avoid, and it would look like an overlay bug rather than a wiring one. The first
            // version of this guard returned quietly and cost an evening of reading screenshots.
            DecodeLog.Lost("render", "the overlay depth state was never created; overlays will "
                + "inherit the opaque pass's writing state and occlude what stands in front of them");
        }
        else
        {
            context.OMSetDepthStencilState(_decalDepth, 0);
        }

        // **Blended, because a decal is a stain on a surface rather than a surface of its own.**
        // This pass set the depth bias and no blend state, so every overlay drew fully opaque: a
        // flat coloured square painted over the ground instead of tinting it. The patch under a
        // health pack looked like a placeholder marker, and was read as one for an evening.
        //
        // The engine blends them too - a decal material is translucent, and its alpha is the shape
        // of the stain. Drawn opaque, the transparent surround is painted as solid colour, which
        // is why the squares had hard edges no decal in the game has.
        float* factor = stackalloc float[4] { 1f, 1f, 1f, 1f };

        if (_alphaBlend.Handle is not null)
        {
            context.OMSetBlendState(_alphaBlend, factor, 0xFFFFFFFF);
        }

        foreach (WorldBatch batch in _decals)
        {
            if (batch.MaterialIndex < 0 || batch.MaterialIndex >= _textures.Count)
            {
                continue;
            }

            ComPtr<ID3D11ShaderResourceView> still =
                _textures[batch.MaterialIndex].Handle is not null
                    ? _textures[batch.MaterialIndex]
                    : _white;

            ComPtr<ID3D11ShaderResourceView> texture =
                AnimationFrame(batch.MaterialIndex, still);

            SetMaterial(context, batch.MaterialIndex, batch.Category);

            // A decal's second texture, on the same rule as everything else: the real one when the
            // material names it, and the base otherwise so a mix stays an identity.
            ComPtr<ID3D11ShaderResourceView> second =
                batch.MaterialIndex < _blendTextures.Count &&
                _blendTextures[batch.MaterialIndex].Handle is not null
                    ? _blendTextures[batch.MaterialIndex]
                    : texture;

            // The same lookup as every other path, for the same reason models needed it: `_white`
            // is the missing-material chequer, so binding it as a detail paints magenta squares
            // onto any material whose combine mode is not −1. No decal in the corpus has been seen
            // to declare one — this is the second instance of one fault, fixed with it rather than
            // left to be found again from a screenshot.
            ComPtr<ID3D11ShaderResourceView> detail =
                batch.MaterialIndex < _details.Count &&
                _details[batch.MaterialIndex].Handle is not null
                    ? _details[batch.MaterialIndex]
                    : _white;

            ComPtr<ID3D11ShaderResourceView> bump =
                batch.MaterialIndex < _bumps.Count &&
                _bumps[batch.MaterialIndex].Handle is not null
                    ? _bumps[batch.MaterialIndex]
                    : _white;

            context.PSSetShaderResources(0, 1, ref texture);
            context.PSSetShaderResources(2, 1, ref second);
            context.PSSetShaderResources(3, 1, ref detail);
            context.PSSetShaderResources(4, 1, ref bump);
            context.Draw((uint)batch.VertexCount, (uint)batch.FirstVertex);
        }

        // Back to the ordinary rasteriser, or everything after this is pulled forward too.
        context.RSSetState(Raster(_bothSides));
    }

    private void DrawTranslucent(ComPtr<ID3D11DeviceContext> context)
    {
        if (_translucent.Count == 0 || _alphaBlend.Handle is null)
        {
            return;
        }

        float* factor = stackalloc float[4] { 1f, 1f, 1f, 1f };

        context.OMSetBlendState(_alphaBlend, factor, 0xFFFFFFFF);

        if (_depthReadOnly.Handle is not null)
        {
            context.OMSetDepthStencilState(_depthReadOnly, 0);
        }

        foreach (WorldBatch batch in _sortedTranslucent)
        {
            if (batch.MaterialIndex >= _textures.Count ||
                _textures[batch.MaterialIndex].Handle is null)
            {
                continue;
            }

            ComPtr<ID3D11ShaderResourceView> texture =
                AnimationFrame(batch.MaterialIndex, _textures[batch.MaterialIndex]);

            ComPtr<ID3D11ShaderResourceView> detail =
                batch.MaterialIndex < _details.Count &&
                _details[batch.MaterialIndex].Handle is not null
                    ? _details[batch.MaterialIndex]
                    : _white;

            ComPtr<ID3D11ShaderResourceView> bump =
                batch.MaterialIndex < _bumps.Count &&
                _bumps[batch.MaterialIndex].Handle is not null
                    ? _bumps[batch.MaterialIndex]
                    : _white;

            SetMaterial(context, batch.MaterialIndex, batch.Category);

            context.PSSetShaderResources(0, 1, ref texture);
            context.PSSetShaderResources(2, 1, ref texture);
            context.PSSetShaderResources(3, 1, ref detail);
            context.PSSetShaderResources(4, 1, ref bump);
            context.Draw((uint)batch.VertexCount, (uint)batch.FirstVertex);
        }

        context.OMSetBlendState(default(ComPtr<ID3D11BlendState>), factor, 0xFFFFFFFF);
    }

    private void DrawAdditive(ComPtr<ID3D11DeviceContext> context)
    {
        if (_additive.Count == 0 || _addBlend.Handle is null)
        {
            return;
        }

        float* factor = stackalloc float[4] { 1f, 1f, 1f, 1f };

        context.OMSetBlendState(_addBlend, factor, 0xFFFFFFFF);

        foreach (WorldBatch batch in _batches)
        {
            if (!_additive.Contains(batch.MaterialIndex) ||
                batch.MaterialIndex >= _textures.Count ||
                _textures[batch.MaterialIndex].Handle is null)
            {
                continue;
            }

            ComPtr<ID3D11ShaderResourceView> texture =
                AnimationFrame(batch.MaterialIndex, _textures[batch.MaterialIndex]);

            ComPtr<ID3D11ShaderResourceView> detail =
                batch.MaterialIndex < _details.Count &&
                _details[batch.MaterialIndex].Handle is not null
                    ? _details[batch.MaterialIndex]
                    : _white;

            SetMaterial(context, batch.MaterialIndex, batch.Category);

            context.PSSetShaderResources(0, 1, ref texture);
            context.PSSetShaderResources(2, 1, ref texture);
            context.PSSetShaderResources(3, 1, ref detail);
            context.Draw((uint)batch.VertexCount, (uint)batch.FirstVertex);
        }

        // Back to ordinary painting, or every later frame keeps adding.
        context.OMSetBlendState(default(ComPtr<ID3D11BlendState>), factor, 0xFFFFFFFF);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _addBlend.Dispose();
        _alphaBlend.Dispose();
        _depthReadOnly.Dispose();
        _decalDepth.Dispose();
        _depthWrite.Dispose();
        ReleaseMap();
        _material.Dispose();
        _camera.Dispose();
        _model.Dispose();
        ReleaseModelBuffers();
        _decalOffset.Dispose();
        _bothSides.Dispose();
        _modelCull.Dispose();

        // The wireframe twins are states like any other and leak exactly as loudly if forgotten.
        foreach (ComPtr<ID3D11RasterizerState> wire in _wireframeFor.Values)
        {
            wire.Dispose();
        }

        _wireframeFor.Clear();
        _viewmodelCull.Dispose();
        _clampSampler.Dispose();
        _wrapSampler.Dispose();
        _layout.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
    }

    private void ReleaseMap()
    {
        ReleaseTextures();
        ReleaseGeometry();
    }

    private void ReleaseTextures()
    {
        foreach (ComPtr<ID3D11ShaderResourceView> texture in
                 _textures.Concat(_blendTextures).Concat(_details).Concat(_bumps).Concat(_cubemaps)
                     .Concat(_placedCubemaps).Concat(_lightWarps).Concat(_selfIllumMasks)
                     .Concat(_phongExponentMaps).Concat(_animationFrames.SelectMany(frames => frames))
                     .Concat(_thumbnails)
                     .Where(texture => texture.Handle is not null))
        {
            texture.Dispose();
        }

        _textures.Clear();
        _thumbnails.Clear();
        _blendTextures.Clear();
        _details.Clear();
        _bumps.Clear();
        _cubemaps.Clear();
        _placedCubemaps.Clear();
        _placements.Clear();
        _usesLocalCubemap.Clear();
        _lightWarps.Clear();
        _selfIllumMasks.Clear();
        _phongExponentMaps.Clear();
        _tintBases.Clear();
        _variables.Clear();
        _animationFrames.Clear();
        _animationRates.Clear();
        _colourFactors.Clear();
        _sortedTranslucent = [];
        _decals = [];
        _detailParameters.Clear();
        _additive.Clear();
        _translucent.Clear();

        if (_lightmap.Handle is not null)
        {
            _lightmap.Dispose();
            _lightmap = default;
        }

        if (_white.Handle is not null)
        {
            _white.Dispose();
            _devGrid.Dispose();
            _luxelGrid.Dispose();
            _white = default;
        }
    }

    private void ReleaseGeometry()
    {
        if (_vertices.Handle is not null)
        {
            _vertices.Dispose();
            _vertices = default;
        }

        _batches = [];
    }

    /// <summary>One static vertex buffer per model, as the engine keeps one mesh per model.</summary>
    /// <remarks>
    /// **Never rebuilt once created.** That is the whole fix, and the reason the buffers are
    /// `Immutable`: a model's geometry in model space genuinely does not change after it is packed,
    /// so the strongest usage flag is also the correct one.
    /// </remarks>
    private readonly Dictionary<string, ComPtr<ID3D11Buffer>> _modelBuffers =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Uploads any entity model that has not been uploaded yet, in model space.</summary>
    /// <param name="device">The device.</param>
    /// <param name="models">Each model's own vertices and its runs, keyed by path.</param>
    /// <exception cref="ArgumentNullException"><paramref name="models"/> is null.</exception>
    /// <remarks>
    /// **This used to pack and upload EVERY model on every addition, and the comment here said that
    /// was "rare and bounded — every one of them is known within a few seconds of playback". Both
    /// halves were false, and the log said so the first time anyone looked.** Instrumented
    /// 2026-08-24 against a real match: 25 full rebuilds spread over 1 minute 43 seconds, five of
    /// them inside two seconds, each 193 to 231 ms. At 27 floats a vertex, 2,067,354 vertices is
    /// 223 MB — packed into a fresh array and pushed to the GPU to add ONE model.
    ///
    /// The owner saw it as *"everything freezes for a half a second to maybe a second"* while the
    /// frame rate never dropped, and no counter named it: this sits outside both `_posingTicks` and
    /// `_drawTicks`, so every performance investigation was reading numbers structurally incapable
    /// of seeing it.
    ///
    /// **It was not wrong when it was written.** The commit that introduced it (`a54e61e`,
    /// 2026-08-13) says "uploaded once", and one buffer uploaded once is a good design. Lazy
    /// on-sight model loading grew in around it afterwards, turning "once" into "on every addition",
    /// and the buffer was never revisited to match — instead a comment was written asserting the new
    /// behaviour was cheap. Nobody measured it, and because the assertion was confident and specific
    /// it read as though somebody had, which is what stopped the question being asked again. Same
    /// family as `docs/memory/per-item-apis-hide-quadratic-reads.md`, and the same shape as any
    /// unfalsifiable comment: it outlives the bug it excuses.
    ///
    /// **Now done the way the engine does it**, on the owner's direction — *"so we switch to valves,
    /// which is what we should have been using in the first place, becasue valves imp is blazingly
    /// fast"*. `IMaterialSystem` publishes the shape: `CreateStaticMesh` / `DestroyStaticMesh` give
    /// every model its own mesh, with a separate shared `GetDynamicMesh` for transient geometry. So
    /// adding a model allocates one small buffer and touches no other model's, and the cost is
    /// O(the model added) rather than O(the entire set).
    ///
    /// **An assistant proposal to keep the single packed buffer and only append to it was
    /// overruled, and rightly.** It would have fixed the cost while preserving the architecture that
    /// produced it — and would have left behind a second confident comment explaining why the
    /// arrangement was fine.
    ///
    /// Valve also loads models at level load rather than on sight: `CBaseEntity::PrecacheModel` sits
    /// behind `IsPrecacheAllowed()` and warns on an out-of-order precache. Matching that timing —
    /// packing from the demo's own `modelprecache` table before playback starts — is the remaining
    /// half and is filed rather than done here.
    /// </remarks>
    public void UploadModels(
        ComPtr<ID3D11Device> device,
        IReadOnlyDictionary<string, PackedModel> models)
    {
        ArgumentNullException.ThrowIfNull(models);

        Dictionary<string, IReadOnlyList<IReadOnlyList<WorldBatch>>> batches =
            new(StringComparer.OrdinalIgnoreCase);

        foreach ((string path, PackedModel model) in models)
        {
            batches[path] = model.Frames;

            // **Already uploaded means already correct**, because a model's geometry in model space
            // never changes. This one line is the difference between O(added) and O(total).
            if (_modelBuffers.ContainsKey(path))
            {
                continue;
            }

            if (model.Vertices.Count == 0)
            {
                continue;
            }

            float[] data = Pack(model.Vertices);

            BufferDesc description = new()
            {
                ByteWidth = (uint)((long)data.Length * sizeof(float)),

                // Immutable is now the ACCURATE flag rather than an obstacle. It promises the
                // contents never change after creation, which is true of one model's geometry and
                // was never true of a buffer holding every model.
                Usage = Usage.Immutable,
                BindFlags = (uint)BindFlag.VertexBuffer,
            };

            ComPtr<ID3D11Buffer> buffer = default;

            fixed (float* first = data)
            {
                SubresourceData initial = new() { PSysMem = first };

                SilkMarshal.ThrowHResult(
                    device.CreateBuffer(in description, in initial, ref buffer));
            }

            _modelBuffers[path] = buffer;
        }

        _modelBatches = batches;
    }

    /// <summary>Forgets every uploaded model, so the next upload rebuilds them.</summary>
    /// <remarks>
    /// **For a caller that reuses a model NAME for different geometry**, which the production path
    /// never does — a model path maps to fixed vertices for the life of a map, and that is exactly
    /// what lets <see cref="UploadModels"/> skip anything it already holds.
    ///
    /// The offscreen target is the exception and needs this: it renders one posed model at a time
    /// under a fixed name, with different geometry each call. Three rendering tests failed the
    /// moment per-model buffers arrived, because the second render drew the first one's model —
    /// which is the assumption failing loudly rather than silently, and worth keeping as the reason
    /// this method exists.
    /// </remarks>
    public void ClearModels() => ReleaseModelBuffers();

    /// <summary>Drops every model buffer. Called when the world goes.</summary>
    private void ReleaseModelBuffers()
    {
        foreach (ComPtr<ID3D11Buffer> buffer in _modelBuffers.Values)
        {
            buffer.Dispose();
        }

        _modelBuffers.Clear();
    }

    /// <summary>The packed batches for one model, or empty when it is not loaded.</summary>
    /// <param name="modelPath">The model's path.</param>
    /// <returns>Its runs, indexing into the model buffer.</returns>
    public IReadOnlyList<WorldBatch> ModelBatches(string modelPath) => ModelBatches(modelPath, 0);

    /// <summary>The runs for one model at one baked animation frame.</summary>
    /// <param name="modelPath">Which model.</param>
    /// <param name="index">Which baked frame; clamped into what was uploaded.</param>
    /// <returns>Its runs, or empty when the model is not packed.</returns>
    /// <remarks>
    /// **A frame is a different range of the same buffer.** Every frame of an animated model was
    /// skinned at load and packed end to end, so choosing one costs an index rather than any
    /// transform work at draw time.
    /// </remarks>
    public IReadOnlyList<WorldBatch> ModelBatches(string modelPath, int index)
    {
        if (!_modelBatches.TryGetValue(
                modelPath, out IReadOnlyList<IReadOnlyList<WorldBatch>>? frames) ||
            frames.Count == 0)
        {
            return [];
        }

        return frames[Math.Clamp(index, 0, frames.Count - 1)];
    }

    /// <summary>Draws one posed model.</summary>
    /// <param name="context">The device context.</param>
    /// <param name="modelPath">Which model, so its own static mesh can be bound (D86).</param>
    /// <param name="matrix">Where it stands: sixteen floats, row major.</param>
    /// <param name="batches">Its runs, indexing into that model's own vertices.</param>
    /// <param name="light">The ambient cube of the leaf it stands in, or null.</param>
    /// <param name="sun">The sun reaching it, or null when it traced to solid rather than sky.</param>
    /// <param name="blend">How far toward the next baked animation frame, from nought to one.</param>
    /// <param name="bones">How many bones skin this draw, or zero for a baked model.</param>
    /// <param name="skin">Which material replaces which for a team colour; null for the model's own.</param>
    /// <param name="pass">
    /// Which half of the model to draw, or all of it — <c>STUDIORENDER_DRAW_*</c>. The default is
    /// Valve's: a model is drawn whole unless something says it is two-pass.
    /// </param>
    /// <param name="bodyParts">The model's body parts, for reading the body number.</param>
    /// <param name="body">Which alternative each part shows, packed as m_nBody.</param>
    /// <param name="mirrored">
    /// Whether the model is drawn mirrored, as a viewmodel is. That reverses its winding, so the
    /// faces needing culling are the opposite ones — <c>C_BaseViewModel::InternalDrawModel</c>
    /// sets <c>MATERIAL_CULLMODE_CW</c> around exactly this and puts it back afterwards.
    /// </param>
    /// <param name="bothSides">
    /// Draw every face regardless of winding, as <c>$nocull</c> does per material. A diagnostic
    /// lever: it separates "this model is culled away" from "this model is not where it seems".
    /// </param>
    /// <param name="origin">
    /// Where the model actually stands, for choosing which of the map's cubemaps it reflects.
    /// Defaults to the translation of <paramref name="matrix"/>, which is right for a BAKED model
    /// and wrong for a skinned one — a skinned model's placement travels in its bones and leaves
    /// the matrix at identity, so the translation reads as the map origin (B170).
    /// </param>
    /// <param name="tint">
    /// Valve's colour for a brush entity's class, applied in the category view only (B219, B156).
    /// Null for anything that is not a brush entity.
    /// </param>
    /// <param name="locals">
    /// The direct lights near this model, at most four (B170). Empty where none reach it.
    /// </param>
    /// <param name="overrideMaterial">
    /// One material's VMT path replacing EVERY one of the model's own, or null for the ordinary
    /// case (B325) — the engine's <c>ForcedMaterialOverride</c>, which a corpse uses to turn gold
    /// or to ice. Not a skin: a skin picks another entry from the model's own table, and this
    /// ignores the table. An unresolved path falls back to the model's own materials.
    /// </param>
    /// <param name="paint">
    /// The colour this ENTITY's item is painted, or null for an unpainted one (B330). Feeds TF2's
    /// <c>ItemTintColor</c> proxy at the bind, which is where the engine runs it — a proxy is
    /// per entity per draw, so this cannot be folded into the material at load.
    /// </param>
    /// <param name="burn">
    /// How alight this ENTITY is, 0 to 1 (B336). Feeds TF2's <c>BurnLevel</c> proxy, which writes
    /// <c>$detailblendfactor</c> and so blends in the fire overlay the material already carries.
    /// Zero for everything not on fire, which is the proxy's own resting value.
    /// </param>
    /// <param name="urine">
    /// The jarate multiplier for this ENTITY, or null for white (B336). Feeds <c>YellowLevel</c>,
    /// whose result two <c>Equals</c> proxies copy into <c>$color2</c> and <c>$selfillumtint</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **One matrix and one draw per entity, which is the engine's shape.** The vertices were
    /// uploaded once and never move; only this constant changes between instances. Callers set the
    /// map's identity matrix back afterwards — see <see cref="Draw"/>, which does so every frame
    /// precisely because an entity draw leaves its own matrix behind.
    /// </remarks>
    public void DrawModel(
        ComPtr<ID3D11DeviceContext> context,
        string modelPath,
        float[] matrix,
        IReadOnlyList<WorldBatch> batches,
        AmbientCube? light = null,
        SunLight? sun = null,
        float blend = 0f,
        int bones = 0,
        IReadOnlyDictionary<int, int>? skin = null,
        ModelPass pass = ModelPass.EntireModel,
        // See the bind site: one material replacing every one of the model's own, by VMT path.
        IReadOnlyList<(int Base, int Count)>? bodyParts = null,
        int body = 0,
        bool mirrored = false,
        bool bothSides = false,
        (float X, float Y, float Z)? origin = null,
        (float Red, float Green, float Blue)? tint = null,
        IReadOnlyList<LocalLight>? locals = null,
        string? overrideMaterial = null,
        (float Red, float Green, float Blue)? paint = null,
        float burn = 0f,
        (float Red, float Green, float Blue)? urine = null)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(batches);

        ArgumentNullException.ThrowIfNull(modelPath);

        // **Named now that each model owns its buffer.** "Nothing was uploaded" used to be one
        // question about one shared buffer; it is now a question about THIS model, and saying which
        // one is the difference between a lead and a shrug.
        if (!_modelBuffers.TryGetValue(modelPath, out ComPtr<ID3D11Buffer> vertexBuffer) ||
            vertexBuffer.Handle is null)
        {
            // Not silent: a caller asking to draw a model when nothing was uploaded is a wiring
            // fault, and it looks exactly like a model that is correctly invisible.
            DecodeLog.Lost(
                "render", $"{modelPath} was posed before its geometry was uploaded");
            return;
        }

        if (batches.Count == 0)
        {
            // Not silent either. A posed model with no batches means the renderer's copy of the
            // packed set is older than the caller's, which draws nothing and reports nothing.
            DecodeLog.Lost("render", "a model was posed but the renderer has no geometry for it");
            return;
        }

        uint stride = VertexStride;
        uint offset = 0;

        // Hoisted out of the batch loop: one blend factor serves every batch, and allocating it
        // per iteration grows the stack frame without bound (CA2014).
        float* blendFactor = stackalloc float[4] { 1f, 1f, 1f, 1f };

        // **Its own state, rather than whatever the world left behind.** See BindPipeline: this
        // path used to inherit the bindings from Draw, which works in an ordinary frame and does
        // not when the map is absent or when a test poses a model on its own.
        BindPipeline(context);

        // One bind per model instance, which is what per-model meshes cost and what the engine pays
        // too. It buys the buffer never being rebuilt, which was 200 ms a time.
        context.IASetVertexBuffers(0, 1, ref vertexBuffer, in stride, in offset);

        SetModel(context, matrix, light, sun, blend, bones, locals);

        // **Which of the map's cubemaps this model reflects, chosen once for the whole model.** A
        // model's material says the literal `env_cubemap`, which VertexLitGeneric keeps to runtime
        // and resolves against whatever the engine has bound as local; the choice is by position,
        // so it belongs to the model rather than to any of its materials or batches. Valve's rule
        // is `Cubemap_FindClosestCubemap`, vbsp/cubemap.cpp:835 — see BspCubemaps.Closest for which
        // half of it applies to something with no surface plane, and for the evidence class.
        //
        // The translation of a row-vector model matrix is row three, indices 12 to 14 — see
        // MatrixConvention, which is the one place that crosses between the two conventions here.
        // **Where the model IS, which is not always what its matrix says** (B170). A baked model is
        // put in the world by its matrix, so the translation is its position. A SKINNED model is put
        // there by its bones and its matrix stays at identity — so reading the translation asks for
        // the cubemap nearest the map ORIGIN, and every skinned model on the map reflects the same
        // wrong cube.
        //
        // Measured in the viewer with the eye at (-4816, -1280, 648): `c_scattergun at (0, 0, 0)`,
        // `scout at (0, 0, 0)`, `soldier at (0, 0, 0)`, all reflecting cubemap 39 at (0, 0, 608).
        // It shows on weapons and not on arms or player skins because TF2's `c_` weapon materials
        // declare `$envmap` and those do not.
        //
        // The same mistake, in the same shape, as the one `EntityModels` already records for
        // LIGHTING: "a merged item's own pose is (0,0,0) by construction, so sampling the ambient
        // cube from it asks the leaf at the map origin". That was fixed by sampling at the wearer's
        // illumination point; this is the cubemap half of it, and it never got the same treatment.
        (float X, float Y, float Z) where = origin ?? (matrix[12], matrix[13], matrix[14]);

        ComPtr<ID3D11ShaderResourceView> local = default;

        if (_placements.Count > 0 &&
            BspCubemaps.Closest(_placements, where.X, where.Y, where.Z) is >= 0 and var nearest)
        {
            local = _placedCubemaps[nearest];

            // **Which cube a model reflects, said once per model path** (B170). Every offscreen
            // measurement of the reflection came back within its material's tint, so what is left is
            // the state the VIEWMODEL PASS supplies — and the cube it picks is half of that. Said
            // once rather than per draw: the answer is about a model, and this runs for every model
            // every frame.
            //
            // Information rather than Debug deliberately. The question this answers cannot be asked
            // of a normal run otherwise, and a `developer 1` requirement is exactly why the
            // viewmodel position line was unreadable when it was needed.
            if (_reportedCubemap.Add(modelPath))
            {
                BspCubemap placement = _placements[nearest];

                _render.LogInformation(
                    "{Message}",
                    $"{System.IO.Path.GetFileNameWithoutExtension(modelPath)} at " +
                    $"({where.X:0.#}, {where.Y:0.#}, {where.Z:0.#}) reflects cubemap " +
                    $"{nearest} of {_placements.Count} at " +
                    $"({placement.X:0.#}, {placement.Y:0.#}, {placement.Z:0.#})");
            }
        }

        // **A ledger of why each batch did or did not draw, counted on the way OUT** (B?). The
        // symptom this exists for is "the weapon sometimes is not there", and the three candidate
        // causes are indistinguishable from the picture: the pass filter kept nothing, the body
        // number selected nothing, or there was nothing offered in the first place. Counting only
        // the survivors would report "0 drawn" for all three.
        int kept = 0;
        int drawn = 0;
        int filteredByPass = 0;
        int filteredByBody = 0;

        foreach (WorldBatch batch in batches)
        {
            // **A skin is one lookup at draw time, which is how the engine does it.** Valve resolve
            // a mesh's material through the skin table - pSkinref(skin * numskinref + material) -
            // rather than keeping a second copy of anything. A RED player and a BLU one share their
            // geometry, their batching and their vertex ranges exactly; only which material paints
            // each run differs, so duplicating batches per team would be memory spent for nothing.
            //
            // Resolving here also means a player who switches teams is right on the very next
            // frame, with nothing repacked.
            // **Looked up by the batch's SKINREF, not by the material it already resolved to**
            // (B229). `MaterialIndex` is family zero's answer; keying on it asks what one family's
            // material becomes in another, which has two answers as soon as two meshes share it and
            // no answer at all when family zero's texture is the one the map does not ship. The
            // engine indexes by the mesh's own reference, which is what `MaterialSlot` carries.
            int material = skin is not null && skin.TryGetValue(batch.MaterialSlot, out int swapped)
                ? swapped
                : batch.MaterialIndex;

            // **A whole-model override replaces the resolved material outright, which is what makes
            // it different from a skin** (B325). A skin picks another entry from the model's own
            // table — the lookup directly above — and this ignores the table:
            // `modelrender->ForcedMaterialOverride( pOverrideMaterial )` (`c_baseanimating.cpp:3438`)
            // binds ONE material for the whole draw, so a gold corpse's hands, coat and boots all
            // paint from it.
            //
            // **Substituted here, before anything reads `material`, so the override is a material
            // rather than a texture.** Gold's look is mostly `$envmap cubemaps/cubemap_gold001` with
            // `$envmaptint [1.5 1.2 .2]` and a rim term, and ice adds a bump, a phong warp and a
            // light warp — swapping slot 0 alone would have kept the PLAYER material's cubemap,
            // phong, detail and blend state under a flat swatch, which draws something and is not
            // the engine's answer.
            //
            // **An override that did not resolve leaves the model's own**, rather than falling to
            // the white chequer. A machine without TF2 has neither material, and a corpse in its own
            // skin is far closer to right than a magenta one — and is what the engine shows when
            // `m_MaterialOverride.Init` fails.
            if (overrideMaterial is not null &&
                _overrideMaterials.TryGetValue(overrideMaterial, out int forced))
            {
                material = forced;
            }

            // **Culling per material, because that is where the engine keeps it.** $nocull sets
            // MATERIAL_VAR_NOCULL and shaders test it per material (imaterial.h:369,
            // depthwrite.cpp:93); everything else culls back faces, front wound clockwise
            // (imaterialsystem.h:180). Set inside the loop rather than once per model because two
            // batches of one model can disagree — a sign that culls and a flag that does not.
            context.RSSetState(Raster(CullFor(mirrored, bothSides || _noCull.Contains(material)) switch
            {
                ModelCull.None => _bothSides,
                ModelCull.Front => _viewmodelCull,
                _ => _modelCull,
            }));

            // **A model's materials are sorted into the same two passes the world's are.** Until
            // now every model batch drew opaque, whatever its material said, which is why a capture
            // point's hologram came out as a solid ribbed slab rather than something to see
            // through. The classification is already done, at upload, for the map's textures — a
            // model's materials live in the same table, so it is the same lookup.
            bool wantsBlending = _additive.Contains(material) ||
                _translucent.Contains(material) ||
                _modulate.ContainsKey(material);

            // **And which HALF is drawn is now the caller's to say, because most models have no
            // halves.** This used to filter unconditionally, which is `STUDIORENDER_DRAW_OPAQUE_ONLY`
            // and `_TRANSLUCENT_ONLY` applied to every model in the scene — correct machinery
            // pointed at everything. `EntireModel` is Valve's default and what a model without
            // `$mostlyopaque` gets; see `RenderGroups` for who decides.
            if (pass is ModelPass.OpaqueOnly && wantsBlending)
            {
                filteredByPass++;
                continue;
            }

            if (pass is ModelPass.TranslucentOnly && !wantsBlending)
            {
                filteredByPass++;
                continue;
            }

            // **The body part's chosen alternative, per entity.** Every alternative is packed once
            // and the choice is made here, which is how three capture points sharing one model show
            // three different signs. Batches never span two alternatives, so skipping is whole runs
            // rather than triangles.
            if (bodyParts is { Count: > 0 } &&
                !Shows(bodyParts, batch.BodyPart, batch.BodyModel, body))
            {
                filteredByBody++;
                continue;
            }

            kept++;

            // **Chosen per MATERIAL rather than per pass, which is the engine's arrangement and the
            // other half of B135.** A shader in Source declares `EnableBlending` in its own
            // SHADOW_STATE block, so the material system sets it on bind and no pass inherits
            // anything — `SetMaterial` below already does exactly this for the DEPTH state, for
            // reasons its comment spells out at length. Blending was still per pass, which worked
            // only because each pass happened to hold materials of one kind.
            //
            // `EntireModel` is what broke that: one draw now carries a model's solid and blended
            // meshes together, so "the pass knows" is no longer true of anything. Set unconditionally
            // — including the null state that means ordinary painting — so an opaque batch cannot
            // inherit the blend of whatever drew before it.
            //
            // Per batch, because a model can carry every kind at once: additive ADDS light to what
            // is behind it, which is what a hologram does; modulate multiplies it; alpha blends.
            ComPtr<ID3D11BlendState> blending = default;

            if (_modulate.TryGetValue(material, out bool twice))
            {
                blending = twice ? _modulateTwiceBlend : _modulateBlend;
            }
            else if (_additive.Contains(material))
            {
                blending = _addBlend;
            }
            else if (_translucent.Contains(material))
            {
                blending = _alphaBlend;
            }

            context.OMSetBlendState(blending, blendFactor, 0xFFFFFFFF);


            ComPtr<ID3D11ShaderResourceView> texture =
                material >= 0 && material < _textures.Count &&
                _textures[material].Handle is not null
                    ? _textures[material]
                    : _white;

            // **The material's second texture, where it has one.** Binding the base to both slots
            // was right while the only combine was a vertex-alpha mix, since mixing a texture with
            // itself is an identity — but UnLitTwoTexture MULTIPLIES, and multiplying a texture by
            // itself squares it. A model with a real $texture2 needs the real one.
            ComPtr<ID3D11ShaderResourceView> second =
                material >= 0 && material < _blendTextures.Count &&
                _blendTextures[material].Handle is not null
                    ? _blendTextures[material]
                    : texture;

            // **The detail and the bump, looked up the way every other draw path looks them up.**
            // This bound `_white` unconditionally, and `_white` is not white — it is the
            // missing-material chequer, magenta and black. The shader combines a detail whenever
            // the material's mode is not −1, so every model material declaring `$detail` had a
            // magenta chequer multiplied into its albedo: a medic's coat came out in purple and
            // grey squares while the texture itself decodes perfectly.
            //
            // The three paths that draw the world, the translucent pass and the blended pass all do
            // the lookup below. This one did not, which is why the fault was confined to models —
            // and why the map, the props and the world looked right in the same frame.
            ComPtr<ID3D11ShaderResourceView> detail =
                material >= 0 && material < _details.Count &&
                _details[material].Handle is not null
                    ? _details[material]
                    : _white;

            ComPtr<ID3D11ShaderResourceView> bump =
                material >= 0 && material < _bumps.Count &&
                _bumps[material].Handle is not null
                    ? _bumps[material]
                    : _white;

            // **The reflection, which this path never bound at all.** t5 was set only by the world
            // pass, so a model material with a cubemap raised the shader's "there is one" flag and
            // then sampled whatever texture the last brush face happened to leave in the slot.
            // Inert until now only because no model material ever resolved a cubemap: vbsp patches
            // brush faces and cannot patch a prop, so every one of them arrived as `env_cubemap`
            // and was discarded on the way in.
            //
            // Bound unconditionally, including the null handle for a material that reflects
            // nothing, so no draw can inherit its predecessor's cube.
            ComPtr<ID3D11ShaderResourceView> reflection = default;

            if (material >= 0 && _usesLocalCubemap.Contains(material))
            {
                reflection = local;
            }
            else if (material >= 0 && material < _cubemaps.Count)
            {
                reflection = _cubemaps[material];
            }

            // **Which material each batch of a model actually DRAWS with, said once per model**
            // (B170).
            //
            // **Keyed by MaterialSlot, and it was keyed by MaterialIndex until 2026-08-31** (B243).
            // A skin row is `skinref -> resolved material`, which is what the DRAW uses forty lines
            // up (`skin.TryGetValue( batch.MaterialSlot, … )`); looking it up by the resolved index
            // instead misses every time and reports family zero's material. So this line said
            // `soldier_sleeves_red` while the draw was correctly binding `soldier_sleeves_blue`,
            // and an hour went into chasing a fix that was already working. Third lying diagnostic
            // of the night, after the illumination point and the cull census. Everything read so far was the material table at BUILD time, where
            // `c_shotgun` carries a tint of 0.05 — and the screenshots show the weapon changing by
            // far more than that tint can produce. The gap between those two facts is which
            // material index the draw resolves to, after the skin swap, which nothing has reported.
            if (_reportedBatchMaterials.Add(modelPath))
            {
                // **The matrix this draw actually uses, said once per model** (B241). The line
                // above it — `<model> at (x, y, z) reflects cubemap N` — is the ILLUMINATION point,
                // sampled from the entity's local pose to choose a cubemap, and for a PARENTED prop
                // that is its offset from its parent rather than where it is drawn. It reads
                // (0, 0, 0) whether the placement works or not, and an evening of the gates was
                // argued from it as though it were the position.
                //
                // So the one number that cannot lie about where a model is drawn is the one handed
                // to the draw call. Translation lives in the last row of the shader's row-vector
                // matrix; the diagonal is printed with it because a matrix with a correct
                // translation and a zero rotation collapses every vertex onto that point, which is
                // a model that is placed, bounded, batched and invisible.
                _render.LogInformation(
                    "{Message}",
                    string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"{System.IO.Path.GetFileNameWithoutExtension(modelPath)} draws at " +
                        $"({matrix[12]:0.#}, {matrix[13]:0.#}, {matrix[14]:0.#}) " +
                        $"diag ({matrix[0]:0.###}, {matrix[5]:0.###}, {matrix[10]:0.###}) " +
                        $"bones {bones.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +

                        // **Whether a skin row arrived at all, and how big it is.** A model drawn in
                        // the wrong team's colours has three possible causes and the picture cannot
                        // tell them apart: no row (the entity's skin never reached here), family
                        // zero's row (the skin was zero), or a row whose keys do not match what the
                        // batch carries. The first two are distinguishable here; the third shows as
                        // a row present and the materials unchanged.
                        $"skinrow {(skin is null
                            ? "none"
                            : string.Join(
                                ",",
                                skin.Select(pair => $"{pair.Key}->{pair.Value}")))}"));

                _render.LogInformation(
                    "{Message}",
                    $"{System.IO.Path.GetFileNameWithoutExtension(modelPath)} draws materials: " +
                    string.Join(
                        ", ",
                        batches
                            .Select(each =>
                            {
                                int drawn = skin is not null &&
                                    skin.TryGetValue(each.MaterialSlot, out int swap)
                                    ? swap
                                    : each.MaterialIndex;

                                // **The classification, because its absence made an ASSUMPTION
                                // load-bearing.** This line named the material indices and nothing
                                // else, so "are the arms opaque?" was answered from the material's
                                // NAME — `demoman_hands` sounds opaque — rather than from the table
                                // that decides which pass it draws in. That is the difference
                                // between a measurement and a guess, and the guess was steering a
                                // regression hunt.
                                return $"{drawn}:{DescribeMaterial(drawn)}" +
                                    $"{(drawn != each.MaterialIndex ? $"(from {each.MaterialIndex})" : string.Empty)}" +
                                    $"{(drawn >= 0 && _usesLocalCubemap.Contains(drawn) ? " REFLECTS-LOCAL" : string.Empty)}";
                            })
                            .Distinct()));
            }

            context.PSSetShaderResources(0, 1, ref texture);
            context.PSSetShaderResources(2, 1, ref second);
            context.PSSetShaderResources(3, 1, ref detail);
            context.PSSetShaderResources(4, 1, ref bump);
            context.PSSetShaderResources(5, 1, ref reflection);

            // Bound unconditionally, including the null handle, so a material with no ramp cannot
            // inherit the previous draw's. The shader reads it only when phongFresnel.w says so.
            ComPtr<ID3D11ShaderResourceView> ramp =
                material >= 0 && material < _lightWarps.Count ? _lightWarps[material] : default;

            context.PSSetShaderResources(6, 1, ref ramp);

            // **`$selfillummask`, bound the same way and for the same reason** (B327): every draw
            // sets it, including the null handle, so a material without one cannot sample whatever
            // the previous draw left in the slot. The shader reads it only when phongTint.w says
            // so, and takes the base map's alpha otherwise — which is the engine's own fallback,
            // written as one lerp rather than a branch.
            ComPtr<ID3D11ShaderResourceView> illumMask =
                material >= 0 && material < _selfIllumMasks.Count
                    ? _selfIllumMasks[material]
                    : default;

            context.PSSetShaderResources(10, 1, ref illumMask);

            // **The exponent map, or flat white — never nothing** (B334). The engine's own
            // substitution is `BindStandardTexture( SHADER_SAMPLER7, TEXTURE_WHITE )`
            // (`skin_dx9_helper.cpp:565`), and it matters that it is WHITE rather than an unbound
            // slot: D3D11 reads zero from an unbound view, which would give `1 + 149 x 0 = 1` and
            // put a full-strength highlight on every unlit face of every material with no exponent
            // texture. Every draw sets it, for the same reason the mask above does.
            ComPtr<ID3D11ShaderResourceView> exponents =
                material >= 0 && material < _phongExponentMaps.Count &&
                _phongExponentMaps[material].Handle is not null
                    ? _phongExponentMaps[material]
                    : _flatWhite;

            context.PSSetShaderResources(11, 1, ref exponents);

            // A model's own batches carry their category too — `Prop`, or `Missing` where the
            // material did not resolve. A brush entity adds its class colour on top (B219).
            SetMaterial(context, material, batch.Category, tint, paint, burn, urine);

            drawn += batch.VertexCount;

            context.Draw((uint)batch.VertexCount, (uint)batch.FirstVertex);
        }

        // **What was actually SUBMITTED, reported when it changes** (B222). `kept` counts batches
        // that survived the filters and says nothing about their size — a batch of zero vertices is
        // "kept" and draws nothing at all, so every claim tonight that "the batches really are being
        // drawn" rested on a counter that could not tell those apart. This is the vertex total, and
        // it is the last thing between the scene's numbers, which all measure healthy, and the
        // picture, which is missing.
        //
        // Signature rather than a count, because the interesting change may be WHICH materials are
        // bound rather than how many corners went in.
        // **Capped PER MODEL, not globally, and the first version got that wrong in a way that
        // wasted a reproduction.** A global budget of 200 was consumed entirely by
        // `cappoint_hologram` (120) and `demo_scotchbonnet` (80) — two animating props that change
        // their submission constantly — before the viewmodel, the only subject this was built for,
        // reported anything at all. This method runs for every one of 250 props a frame, so a
        // shared budget is a budget the noisiest model spends.
        //
        // Same shape as every other blind instrument tonight: the resolution was chosen without
        // asking what had to remain visible through it.
        // **Keyed by model AND PASS, because one model is drawn in both** (B264). A weapon is a
        // world model for other players and a viewmodel for the followed one, with entirely
        // different geometry — `c_proto_medigun` submits 1,008 vertices in one pass and 14,976 in
        // the other. Sharing one slot made each pass overwrite the other's, so the change guard
        // below saw a change on EVERY frame and this line fired forever: measured on the UI
        // suite's own log, 21,745 lines for that one model out of 37,897, and two models between
        // them 80% of the whole file.
        //
        // The rule was already written down one file over, on `DrawTally.Report`: *"A change guard
        // against a value that oscillates is not a guard."* Same trap, same week, same shape — a
        // per-pass quantity keyed as though it were per-model.
        (string Model, ModelPass Pass) subject = (modelPath, pass);

        _reportedDraw.TryGetValue(subject, out (int Kept, int Drawn, int Reports) last);

        // **Guarded on the work.** The message below joins and de-duplicates a description of every
        // batch, which is real allocation on a path that runs for 250 props a frame. A diagnostic
        // must cost nothing when nobody is reading it.
        if (last.Reports > 0 && (last.Kept != kept || last.Drawn != drawn) &&
            _render.IsEnabled(LogLevel.Debug))
        {
            // **Skin and body are printed because they are what is LEFT.** During a sticky charge
            // the weapon is built, submitted, drawn, correctly sized, and merged onto the same
            // bones as the arms — so it cannot be somewhere the arms are not, or they would vanish
            // together. The arms are visible and the weapon is not, and the only thing that differs
            // between the two models at that point is which materials each binds. Skin family, body
            // number, and the resolved material indices are the whole of that.
            _render.LogDebug(
                "{Message}",
                $"{System.IO.Path.GetFileNameWithoutExtension(modelPath)} submitted: " +
                $"{last.Kept} batches/{last.Drawn} vertices -> {kept} batches/{drawn} vertices " +
                $"in the {pass} pass, body {body}, " +
                $"skin {(skin is null ? "own" : $"{skin.Count} swaps")}, " +
                $"materials [{string.Join(", ", batches.Select(each =>
                    skin is not null && skin.TryGetValue(each.MaterialSlot, out int swap)
                        ? $"{swap}<-{each.MaterialIndex}:{DescribeMaterial(swap)}"
                        : $"{each.MaterialIndex}:{DescribeMaterial(each.MaterialIndex)}").Distinct())}]");
        }

        _reportedDraw[subject] = (
            kept,
            drawn,
            last.Kept != kept || last.Drawn != drawn || last.Reports == 0
                ? last.Reports + 1
                : last.Reports);

        // **A model asked to draw that drew nothing, said once per model and pass.** This is the
        // instrument for "the weapon is sometimes not there" — a symptom that looks identical
        // whether the pass filter kept nothing, the body number selected nothing, or the frame
        // offered nothing, and which no component test can see, because every component did exactly
        // what it was told.
        //
        // **Warning, not Debug.** A model the renderer was told to draw and did not draw is a defect
        // in something; only which thing is open. The offered count is included because "nothing
        // offered" is a different fault from "all filtered", and the guard at the top of this method
        // already returns early for a model with no batches at all — so reaching here with zero kept
        // means the filtering did it.
        //
        // Once per (model, pass), because the condition is frame-dependent: unguarded it would
        // repeat sixty times a second, and guarding on the model alone would hide whichever pass is
        // the one that matters.
        if (kept == 0 && _reportedEmptyDraw.Add((modelPath, pass)))
        {
            _render.LogDebug(
                "{Message}",
                $"{System.IO.Path.GetFileNameWithoutExtension(modelPath)} drew NOTHING in the " +
                $"{pass} pass: {batches.Count} batches offered, {filteredByPass} filtered by the " +
                $"pass, {filteredByBody} by body {body}");
        }
    }

    /// <summary>Which models have already reported drawing nothing, per pass.</summary>
    private readonly HashSet<(string Model, ModelPass Pass)> _reportedEmptyDraw = [];

    /// <summary>What each model last submitted, and how many changes it has reported.</summary>
    private readonly Dictionary<(string Model, ModelPass Pass), (int Kept, int Drawn, int Reports)>
        _reportedDraw = [];


    /// <summary>Whether a batch is the alternative its body part shows.</summary>
    /// <remarks>GetBodygroup, shared/animation.cpp:876, applied to a packed run.</remarks>
    /// <summary>Whether any material this model currently shows is blended.</summary>
    /// <param name="batches">
    /// The model's runs, as <see cref="ModelBatches(string, int)"/> returns them.
    /// </param>
    /// <param name="skin">Which material replaces which for its team, or null for its own.</param>
    /// <param name="bodyParts">The model's body parts, for reading the body number.</param>
    /// <param name="body">The entity's <c>m_nBody</c>.</param>
    /// <returns>Whether the engine would call this model translucent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="batches"/> is null.</exception>
    /// <remarks>
    /// **<c>IVModelInfo::IsTranslucent</c>, and the answer is ANY rather than ALL.** The evidence is
    /// <c>STUDIOHDR_FLAGS_FORCE_OPAQUE</c>, whose whole job is to override this answer for a model
    /// that *"[has] translucent parts … but we're not going to sort it"*. A flag that suppresses an
    /// answer would be pointless if the answer were "every material".
    ///
    /// **Skin and body are parameters because Valve's are.**
    /// <c>RecomputeTranslucency( model, nSkin, nBody, pClientRenderable, … )</c>
    /// (<c>ivmodelinfo.h:125</c>) takes both, so translucency is a property of the materials a model
    /// is CURRENTLY showing, not of every material in its file. A hidden bodygroup with a glass
    /// visor must not drag the whole model into the translucent pass while it is hidden.
    ///
    /// **Walked per model per frame rather than cached, deliberately.** Valve caches and recomputes
    /// on change; the equivalent here would key on the model, the skin table's identity and the body
    /// number, which is a dictionary probe to save a walk of a few dozen hash lookups. Measure
    /// before adding it — a per-frame recompute in `SetCamera` was the last real cost in this
    /// renderer, and it was found by the UI suite's duration rather than by reasoning about it.
    /// </remarks>
    public bool IsTranslucent(
        IReadOnlyList<WorldBatch> batches,
        IReadOnlyDictionary<int, int>? skin = null,
        IReadOnlyList<(int Base, int Count)>? bodyParts = null,
        int body = 0)
    {
        ArgumentNullException.ThrowIfNull(batches);

        foreach (WorldBatch batch in batches)
        {
            if (bodyParts is { Count: > 0 } &&
                !Shows(bodyParts, batch.BodyPart, batch.BodyModel, body))
            {
                continue;
            }

            // **Looked up by the batch's SKINREF, not by the material it already resolved to**
            // (B229). `MaterialIndex` is family zero's answer; keying on it asks what one family's
            // material becomes in another, which has two answers as soon as two meshes share it and
            // no answer at all when family zero's texture is the one the map does not ship. The
            // engine indexes by the mesh's own reference, which is what `MaterialSlot` carries.
            int material = skin is not null && skin.TryGetValue(batch.MaterialSlot, out int swapped)
                ? swapped
                : batch.MaterialIndex;

            if (_additive.Contains(material) ||
                _translucent.Contains(material) ||
                _modulate.ContainsKey(material))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How a material was classified, for the log.</summary>
    /// <param name="material">Its index in the uploaded table.</param>
    /// <returns>"additive", "translucent" or "opaque".</returns>
    /// <remarks>
    /// **Because which PASS a batch lands in decides whether it is seen.** The capture point shows
    /// the right sign for RED and neutral and every beam for BLU, while the selection was measured
    /// as keeping exactly three of nine batches for every team — so the difference is not which
    /// meshes are drawn but how the three that are drawn are shaded, and that is this table.
    /// </remarks>
    internal string DescribeMaterial(int material)
    {
        if (_additive.Contains(material))
        {
            return "additive";
        }

        if (_modulate.TryGetValue(material, out bool twice))
        {
            return twice ? "modulate2x" : "modulate";
        }

        return _translucent.Contains(material) ? "translucent" : "opaque";
    }

    /// <summary>Whether a material has a real texture, or falls back to the chequer.</summary>
    /// <param name="material">The material index a batch binds.</param>
    /// <returns>A short description of what will actually be sampled.</returns>
    /// <remarks>
    /// **The fact the per-model census was missing, and its absence cost three wrong diagnoses.**
    /// That census reported which material index a model binds and whether the material is opaque —
    /// both of which were correct for a door drawing as grey rock. What it never said is whether the
    /// index resolves to a TEXTURE, and `_white` is not white: it is the grey chequer this project
    /// uses for a missing one. A model falling back to it looks like untextured stone, which is
    /// exactly what the owner reported and what "material 480, opaque" cannot distinguish.
    /// </remarks>
    internal string DescribeTexture(int material)
    {
        if (material < 0)
        {
            return "no-material";
        }

        if (material >= _textures.Count)
        {
            return $"past-the-table({_textures.Count})";
        }

        return _textures[material].Handle is null ? "CHEQUER" : "textured";
    }

    /// <summary>Which faces to cull for one batch of a model.</summary>
    /// <param name="mirrored">Whether the model is drawn mirrored, as a viewmodel is.</param>
    /// <param name="noCull">Whether the material set <c>$nocull</c>.</param>
    /// <returns>The cull mode.</returns>
    /// <remarks>
    /// **Pulled out as a function so the three-way choice can be tested**, because it is written
    /// from two booleans and that is the shape that loses a case. The case it loses draws a weapon
    /// inside out rather than failing.
    ///
    /// <c>$nocull</c> is checked first and outranks the flip. The flag says the material's faces
    /// are meant to be visible from behind — a chain-link fence, a flat blade — which is true
    /// whichever way the model carrying it is wound, so culling its front faces would hide exactly
    /// what it asked to keep.
    /// </remarks>
    internal static ModelCull CullFor(bool mirrored, bool noCull)
    {
        if (noCull)
        {
            return ModelCull.None;
        }

        return mirrored ? ModelCull.Front : ModelCull.Back;
    }

    internal static bool Shows(
        IReadOnlyList<(int Base, int Count)> parts, int part, int model, int body)
    {
        if (part < 0 || part >= parts.Count)
        {
            return model == 0;
        }

        (int place, int count) = parts[part];

        return place <= 0 || count <= 0 ? model == 0 : model == (body / place) % count;
    }

    /// <summary>Puts blending back to opaque after a blended pass.</summary>
    /// <param name="context">The device context.</param>
    /// <remarks>
    /// **Because a leaked state is what caused the last defect.** DrawTranslucent left a read-only
    /// depth state set and every model after it drew without depth writes, which put a medkit over
    /// a medic and a player's eyes through the back of his head. A pass that changes a state hands
    /// it back rather than leaving the next one to discover it.
    /// </remarks>
    public static void ResetBlend(ComPtr<ID3D11DeviceContext> context)
    {
        float* factor = stackalloc float[4] { 1f, 1f, 1f, 1f };

        context.OMSetBlendState(default(ComPtr<ID3D11BlendState>), factor, 0xFFFFFFFF);
    }

    private void CreateVertexBuffer(ComPtr<ID3D11Device> device, float[] data)
    {
        BufferDesc description = new()
        {
            ByteWidth = (uint)(data.Length * sizeof(float)),
            Usage = Usage.Immutable,
            BindFlags = (uint)BindFlag.VertexBuffer,
        };

        fixed (float* first = data)
        {
            SubresourceData initial = new() { PSysMem = first };

            SilkMarshal.ThrowHResult(device.CreateBuffer(in description, in initial, ref _vertices));
        }

    }

    /// <summary>Creates a texture with a full mip chain and a view onto it.</summary>
    /// <remarks>
    /// **The mip chain is the whole point, and its absence was visible.** Uploaded with a single
    /// level, a 512-pixel texture is sampled at one texel per pixel however small the triangle is —
    /// and an overhead view of a whole map draws terrain triangles a few pixels across. Each pixel
    /// then takes an arbitrary texel, which aliases into dark noise rather than into an average.
    /// It got conspicuously worse the moment displacements were subdivided into real terrain,
    /// because that turned a handful of large quads into thousands of tiny ones.
    ///
    /// Generated by the GPU rather than uploaded from the VTF's own chain. Valve's chain is right
    /// there in the file and using it would save the generation, but it needs every level uploaded
    /// separately and the decoder currently returns one; this is the smaller change and the
    /// difference is a few milliseconds at load.
    ///
    /// The cost is that the texture can no longer be immutable — generating mips writes to it — so
    /// it is Default usage with a render-target bind, which is what GenerateMips requires.
    /// </remarks>
    /// <remarks>
    /// **The sRGB flag decides whether the hardware linearises on sampling**, which is what makes
    /// the shader's arithmetic linear (B54). A texture is a picture and wants it; a lightmap is
    /// light and does not, since linearising values that never carried the curve darkens every
    /// shadow in the map.
    /// </remarks>
    /// <remarks>
    /// **Two shapes, because block-compressed textures cannot be treated like pixels (B149).**
    ///
    /// A DXT image goes up exactly as it sits in the file: `BC1`, `BC2` or `BC3` is what Direct3D
    /// samples, so there is nothing to convert. Its mips come from the file too — `GenerateMips`
    /// needs a render target and a BC format cannot be one, and Valve's chain is already there and
    /// already filtered.
    ///
    /// Anything else is RGBA with one level, and keeps the old arrangement: an empty texture whose
    /// mips the driver generates from level zero.
    ///
    /// **The pitch is the detail that decides whether this works.** For a block format Direct3D
    /// wants bytes per row of BLOCKS, not of pixels — `ceil(width / 4) * blockBytes`. Give it a
    /// pixel pitch and the image skews rather than failing.
    /// </remarks>
    private static ComPtr<ID3D11ShaderResourceView> CreateTexture(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        int width,
        int height,
        TextureImage image,
        bool srgb = true)
    {
        return image.IsBlockCompressed
            ? CreateBlockTexture(device, width, height, image, srgb)
            : CreatePixelTexture(device, context, width, height, image.Top.Span, srgb);
    }

    /// <summary>Uploads DXT blocks untouched, with the file's own mip chain.</summary>
    private static ComPtr<ID3D11ShaderResourceView> CreateBlockTexture(
        ComPtr<ID3D11Device> device, int width, int height, TextureImage image, bool srgb)
    {
        int levels = image.Levels.Count;
        Texture2DDesc description = new()
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = (uint)levels,
            ArraySize = 1,
            Format = BlockFormat(image.Format, srgb),
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Usage = Usage.Immutable,
            BindFlags = (uint)BindFlag.ShaderResource,
        };

        // **Pinned together and supplied at creation**, because an immutable texture takes its data
        // once. One entry per mip, in the order Direct3D numbers them: subresource zero is the top.
        GCHandle[] pins = new GCHandle[levels];
        SubresourceData[] data = new SubresourceData[levels];

        try
        {
            for (int level = 0; level < levels; level++)
            {
                byte[] bytes = image.Levels[level].ToArray();
                pins[level] = GCHandle.Alloc(bytes, GCHandleType.Pinned);

                data[level] = new SubresourceData
                {
                    PSysMem = (void*)pins[level].AddrOfPinnedObject(),
                    SysMemPitch = (uint)BlockPitch(image.Format, width, level),
                };
            }

            ComPtr<ID3D11Texture2D> texture = default;

            fixed (SubresourceData* first = data)
            {
                SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, first, ref texture));
            }

            ComPtr<ID3D11ShaderResourceView> view = default;
            SilkMarshal.ThrowHResult(device.CreateShaderResourceView(
                texture, ref Unsafe.NullRef<ShaderResourceViewDesc>(), ref view));

            texture.Dispose();
            return view;
        }
        finally
        {
            foreach (GCHandle pin in pins)
            {
                if (pin.IsAllocated)
                {
                    pin.Free();
                }
            }
        }
    }

    /// <summary>Uploads a baked reflection's six faces as a compressed cube texture.</summary>
    /// <remarks>
    /// **The same treatment every other texture gets, which is what Valve does (B149).** A baked
    /// cubemap is a DXT VTF, and the engine samples it with `texCUBE` off a hardware cube texture —
    /// so the blocks belong on the device untouched.
    ///
    /// **Subresources are indexed face-major**: `face * mips + mip`. Getting that order backwards
    /// assembles a reflection out of the wrong images, which is the same failure mode as reading six
    /// faces where the file has seven — a picture rather than an error.
    ///
    /// **The file's mip chain is used rather than one level.** The RGBA path above takes only the
    /// top, on the grounds that a 32-pixel cube gains little from a chain; here the chain costs
    /// nothing to carry — it is already in the file, and a BC texture cannot have mips generated
    /// for it anyway.
    /// </remarks>
    private static ComPtr<ID3D11ShaderResourceView> UploadCompressedCube(
        ComPtr<ID3D11Device> device, int size, IReadOnlyList<MapTexture> cubeFaces)
    {
        TextureImage first = cubeFaces[0].Image;

        // Every face of a cube is the same size and format, so the shortest chain governs — a face
        // that somehow carried fewer levels would otherwise leave a subresource unwritten.
        int mips = first.Levels.Count;

        for (int face = 1; face < 6; face++)
        {
            mips = Math.Min(mips, cubeFaces[face].Image.Levels.Count);
        }

        Texture2DDesc description = new()
        {
            Width = (uint)size,
            Height = (uint)size,
            MipLevels = (uint)mips,
            ArraySize = 6,

            // **Linear, not sRGB, exactly as the uncompressed path is.** A cubemap is light rather
            // than a picture: it is added to the lit result, so it belongs in the same space as the
            // lightmap. Treating it as sRGB darkens every reflection by the gamma curve.
            Format = BlockFormat(first.Format, srgb: false),
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Usage = Usage.Immutable,
            BindFlags = (uint)BindFlag.ShaderResource,
            MiscFlags = (uint)ResourceMiscFlag.Texturecube,
        };

        GCHandle[] pins = new GCHandle[6 * mips];
        SubresourceData[] data = new SubresourceData[6 * mips];

        try
        {
            for (int face = 0; face < 6; face++)
            {
                IReadOnlyList<ReadOnlyMemory<byte>> levels = cubeFaces[face].Image.Levels;

                for (int mip = 0; mip < mips; mip++)
                {
                    int at = (face * mips) + mip;

                    byte[] bytes = levels[mip].ToArray();
                    pins[at] = GCHandle.Alloc(bytes, GCHandleType.Pinned);

                    data[at] = new SubresourceData
                    {
                        PSysMem = (void*)pins[at].AddrOfPinnedObject(),
                        SysMemPitch = (uint)BlockPitch(first.Format, size, mip),
                    };
                }
            }

            ComPtr<ID3D11Texture2D> texture = default;

            fixed (SubresourceData* firstData = data)
            {
                SilkMarshal.ThrowHResult(
                    device.CreateTexture2D(in description, firstData, ref texture));
            }

            ShaderResourceViewDesc view = new()
            {
                Format = description.Format,
                ViewDimension = Silk.NET.Core.Native.D3DSrvDimension.D3D11SrvDimensionTexturecube,
            };

            view.TextureCube.MipLevels = (uint)mips;
            view.TextureCube.MostDetailedMip = 0;

            ComPtr<ID3D11ShaderResourceView> resource = default;
            SilkMarshal.ThrowHResult(device.CreateShaderResourceView(texture, in view, ref resource));

            texture.Dispose();
            return resource;
        }
        finally
        {
            foreach (GCHandle pin in pins)
            {
                if (pin.IsAllocated)
                {
                    pin.Free();
                }
            }
        }
    }

    /// <summary>The DXGI format a VTF's block format maps onto.</summary>
    /// <remarks>
    /// **DXT1, DXT3 and DXT5 ARE BC1, BC2 and BC3** — the same bits under two names, which is the
    /// whole reason none of this needs decoding.
    ///
    /// **sRGB matters and gets no error if it is wrong.** A colour texture uploaded as `BC1_UNORM`
    /// rather than `BC1_UNORM_SRGB` samples too bright everywhere, uniformly, and looks like a
    /// lighting choice rather than a mistake.
    /// </remarks>
    internal static Silk.NET.DXGI.Format BlockFormat(VtfFormat format, bool srgb) => format switch
    {
        VtfFormat.Dxt1 or VtfFormat.Dxt1OneBitAlpha => srgb
            ? Silk.NET.DXGI.Format.FormatBC1UnormSrgb
            : Silk.NET.DXGI.Format.FormatBC1Unorm,

        VtfFormat.Dxt3 => srgb
            ? Silk.NET.DXGI.Format.FormatBC2UnormSrgb
            : Silk.NET.DXGI.Format.FormatBC2Unorm,

        _ => srgb
            ? Silk.NET.DXGI.Format.FormatBC3UnormSrgb
            : Silk.NET.DXGI.Format.FormatBC3Unorm,
    };

    /// <summary>Bytes per row of BLOCKS for one mip level of a block-compressed texture.</summary>
    /// <param name="format">Which block format.</param>
    /// <param name="width">The full texture width.</param>
    /// <param name="level">Which mip; zero is the top.</param>
    /// <returns>The pitch Direct3D wants.</returns>
    /// <remarks>
    /// **Separated out so it can be tested without a device.** A wrong pitch does not fail — it
    /// skews the image — and CI has no GPU, so this arithmetic is the only part of the upload that
    /// can be checked where the suite actually runs.
    /// </remarks>
    internal static int BlockPitch(VtfFormat format, int width, int level)
    {
        int blockBytes = format is VtfFormat.Dxt1 or VtfFormat.Dxt1OneBitAlpha ? 8 : 16;
        int levelWidth = Math.Max(1, width >> level);

        return Math.Max(1, (levelWidth + 3) / 4) * blockBytes;
    }

    /// <summary>Uploads RGBA and lets the driver build the mips.</summary>
    private static ComPtr<ID3D11ShaderResourceView> CreatePixelTexture(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        int width,
        int height,
        ReadOnlySpan<byte> pixels,
        bool srgb)
    {
        Texture2DDesc description = new()
        {
            Width = (uint)width,
            Height = (uint)height,

            // Zero means "every level down to 1x1", which the driver fills in.
            MipLevels = 0,
            ArraySize = 1,
            Format = srgb
                ? Silk.NET.DXGI.Format.FormatR8G8B8A8UnormSrgb
                : Silk.NET.DXGI.Format.FormatR8G8B8A8Unorm,
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)(BindFlag.ShaderResource | BindFlag.RenderTarget),
            MiscFlags = (uint)ResourceMiscFlag.GenerateMips,
        };

        ComPtr<ID3D11Texture2D> texture = default;
        SilkMarshal.ThrowHResult(device.CreateTexture2D(
            in description, ref Unsafe.NullRef<SubresourceData>(), ref texture));

        // Level zero is uploaded; the rest are generated from it.
        fixed (byte* first = pixels)
        {
            context.UpdateSubresource(texture, 0, (Box*)null, first, (uint)(width * 4), 0u);
        }

        ComPtr<ID3D11ShaderResourceView> view = default;
        SilkMarshal.ThrowHResult(device.CreateShaderResourceView(
            texture, ref Unsafe.NullRef<ShaderResourceViewDesc>(), ref view));

        context.GenerateMips(view);

        texture.Dispose();
        return view;
    }

    /// <summary>Uploads a baked reflection's six faces as a cube texture.</summary>
    /// <remarks>
    /// **The faces go up in file order because that order is already D3D's.** Valve's names read
    /// RIGHT, LEFT, BACK, FRONT, UP, DOWN and are misleading — <c>LookDir_t</c>, declared beside
    /// them in the same header, gives the real order as <c>+X, −X, +Y, −Y, +Z, −Z</c>, which is
    /// what a <c>TextureCube</c> wants. The seventh face in the file is a fallback spheremap and
    /// was dropped when the cubemap was decoded.
    ///
    /// **Not sRGB, unlike every other texture here.** A cubemap is light rather than a picture: it
    /// is added to the lit result, so it belongs in the same space as the lightmap, which is also
    /// uploaded linear. Treating it as sRGB darkens every reflection by the gamma curve — a
    /// plausible-looking result rather than an obviously wrong one.
    /// </remarks>
    // The `assets` logger is threaded in because this is STATIC (D83): a static method has no
    // injected logger, and the alternatives were making it an instance method for the sake of one
    // warning, or reaching for a static logger — which is the thing being removed.
    private static ComPtr<ID3D11ShaderResourceView> UploadCube(
        ILogger assets, ComPtr<ID3D11Device> device, IReadOnlyList<MapTexture> cubeFaces)
    {
        if (cubeFaces.Count != 6)
        {
            // A cube has six faces and nothing else can be uploaded as one. Reported rather than
            // padded: a short list means the decode changed shape, and inventing a face would draw
            // a seam that looks like a texture bug.
            assets.LogWarning(
                "a cubemap carries {Faces} faces rather than six and was not uploaded",
                cubeFaces.Count);

            return default;
        }

        int size = cubeFaces[0].Width;

        // **A cubemap goes up compressed like everything else (B149).** Valve's shaders sample one
        // with `texCUBE( envmapSampler, reflect )` — a hardware cube texture — and the material
        // system hands it to the device in whatever format the VTF stored, which for a baked
        // reflection is DXT. Expanding it first was this viewer's own invention.
        //
        // **The owner asked for these specifically**: *"dont skip the cubemaps, cubemap bugs are
        // some of the most common map bugs in tf2, there may not be a lot of them but they are
        // heavy, and they break easily, so they need to be on the gpu like valve has them."*
        TextureImage first = cubeFaces[0].Image;

        if (first.IsBlockCompressed)
        {
            return UploadCompressedCube(device, size, cubeFaces);
        }

        Texture2DDesc description = new()
        {
            Width = (uint)size,
            Height = (uint)size,

            // One level. A 32-pixel cube reflected on a wall does not benefit from a mip chain,
            // and generating one would need the render-target binding every face.
            MipLevels = 1,
            ArraySize = 6,
            Format = Silk.NET.DXGI.Format.FormatR8G8B8A8Unorm,
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.ShaderResource,
            MiscFlags = (uint)ResourceMiscFlag.Texturecube,
        };

        // **One contiguous buffer, pinned once.** D3D wants six pointers that stay put for the
        // duration of the call; six separate pins would need six nested `fixed` blocks, and
        // copying into one array makes it a single pin over memory that is already a copy.
        int faceBytes = size * size * 4;
        byte[] all = new byte[faceBytes * 6];

        for (int face = 0; face < 6; face++)
        {
            cubeFaces[face].Image.Top.Span.CopyTo(all.AsSpan(face * faceBytes));
        }

        SubresourceData[] faces = new SubresourceData[6];

        fixed (byte* pixels = all)
        {
            for (int face = 0; face < 6; face++)
            {
                faces[face] = new SubresourceData
                {
                    PSysMem = pixels + (face * faceBytes),
                    SysMemPitch = (uint)(size * 4),
                };
            }

            ComPtr<ID3D11Texture2D> texture = default;

            fixed (SubresourceData* data = faces)
            {
                SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, data, ref texture));
            }

            ShaderResourceViewDesc view = new()
            {
                Format = description.Format,
                ViewDimension = Silk.NET.Core.Native.D3DSrvDimension.D3D11SrvDimensionTexturecube,
            };

            view.TextureCube.MipLevels = 1;
            view.TextureCube.MostDetailedMip = 0;

            ComPtr<ID3D11ShaderResourceView> resource = default;
            SilkMarshal.ThrowHResult(
                device.CreateShaderResourceView(texture, in view, ref resource));

            texture.Dispose();

            return resource;
        }
    }

    /// <summary>Anisotropy asked of the sampler, matching the reference capture config.</summary>
    /// <remarks>
    /// <c>mat_forceaniso 16</c> in the owner's ultra profile, which is what the screenshots this
    /// project compares against were taken with. D3D11's maximum is 16, so this is both the game's
    /// setting and the hardware ceiling.
    /// </remarks>
    private const uint MaxAnisotropy = 16;

    private static ComPtr<ID3D11SamplerState> Sampler(
        ComPtr<ID3D11Device> device, TextureAddressMode address)
    {
        // **Anisotropic, because the reference captures are.** The owner's ultra config sets
        // `mat_forceaniso 16`, and this was `MinMagMipLinear` with no anisotropy — so every surface
        // seen at an angle, which is most of a floor or a wall from a free camera, was blurrier here
        // than in the screenshots this project compares itself against. A parity gap in our own
        // disfavour, and cheap.
        //
        // **Sixteen is the ceiling worth asking for**, and matching the config exactly is the point:
        // a comparison is only meaningful when the two sides were told to do the same thing.
        //
        // No `MipLODBias`. A negative bias sharpens distant surfaces and aliases them, and TF2 does
        // not apply one — sharper than the game is as wrong as blurrier when the goal is parity.
        //
        // The related question of going BELOW `mat_picmip -1` has an answer and it is no: a VTF's
        // LOD resource caps size at `(1 << m_ResolutionClamp)` and picmip subtracts from that
        // exponent, while the texture's own mip 0 is a hard ceiling. There is no detail past mip 0
        // to ask for.
        SamplerDesc description = new()
        {
            Filter = Filter.Anisotropic,
            MaxAnisotropy = MaxAnisotropy,
            AddressU = address,
            AddressV = address,
            AddressW = address,
            MaxLOD = float.MaxValue,
        };

        ComPtr<ID3D11SamplerState> sampler = default;
        SilkMarshal.ThrowHResult(device.CreateSamplerState(in description, ref sampler));

        return sampler;
    }

    /// <summary>Compiles one entry point of the world shader.</summary>
    /// <remarks>
    /// **The source is converted to bytes first, and that is not incidental.** Passing a C# string
    /// through <c>GetPinnableReference</c> hands the compiler UTF-16: it reads 's', then a zero
    /// byte, and stops. The message is
    /// <c>(1,1): error X3000: unrecognized identifier 's'</c> — a complaint about the first letter
    /// of the first word, which reads like a broken shader rather than a broken encoding.
    /// </remarks>
    private static ComPtr<ID3D10Blob> Compile(D3DCompiler compiler, string entry, string profile)
    {
        ComPtr<ID3D10Blob> bytecode = default;
        ComPtr<ID3D10Blob> errors = default;

        byte[] source = System.Text.Encoding.ASCII.GetBytes(ShaderSource);

        fixed (byte* text = source)
        {
            int result = compiler.Compile(
                text,
                (nuint)source.Length,
                (byte*)null,
                null,
                ref Unsafe.NullRef<ID3DInclude>(),
                entry,
                profile,
                0,
                0,
                ref bytecode,
                ref errors);

            if (result < 0)
            {
                // The compiler's own message, not just an HRESULT: it names the line and the
                // reason, and losing that turns a typo into a hex code.
                string message = errors.Handle is not null
                    ? SilkMarshal.PtrToString((nint)errors.GetBufferPointer()) ?? "no detail"
                    : "no detail";

                errors.Dispose();

                throw new InvalidOperationException(
                    $"Compiling {entry} ({profile}) failed: {message}");
            }
        }

        errors.Dispose();
        return bytecode;
    }
}
