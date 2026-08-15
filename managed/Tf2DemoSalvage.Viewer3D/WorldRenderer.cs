using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Diagnostics;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>One corner of a world triangle, ready for the GPU.</summary>
/// <param name="X">Clip-space horizontal position, -1 to 1.</param>
/// <param name="Y">Clip-space vertical position, -1 to 1.</param>
/// <param name="Depth">Clip-space depth, 0 nearest; derived from the surface's height.</param>
/// <param name="U">Texture coordinate across.</param>
/// <param name="V">Texture coordinate down.</param>
/// <param name="LightU">Lightmap atlas coordinate across.</param>
/// <param name="LightV">Lightmap atlas coordinate down.</param>
/// <param name="Alpha">Blend between the material's two textures; 0 draws only the first.</param>
/// <param name="Red">Per-vertex light, one for anything that takes its light from the lightmap.</param>
/// <param name="Green">Per-vertex light.</param>
/// <param name="Blue">Per-vertex light.</param>
/// <param name="NormalX">World surface normal, east-west; lights models from the ambient cube.</param>
/// <param name="NormalY">World surface normal, north-south.</param>
/// <param name="NormalZ">World surface normal, vertically.</param>
/// <param name="LightStep">How far along the atlas each directional lightmap sits, or zero.</param>
/// <remarks>
/// **The per-vertex colour exists for static props and nothing else.** A brush face takes its light
/// from the lightmap atlas; a model cannot, because the same model stands in many places under
/// different light, so the compiler bakes a colour per vertex per placement. Brush faces carry
/// white here, which multiplies to no change, so one shader serves both.
/// </remarks>
/// <param name="NextX">Where this vertex sits one animation frame later.</param>
/// <param name="NextY">Where this vertex sits one animation frame later.</param>
/// <param name="NextZ">Where this vertex sits one animation frame later.</param>
/// <param name="NextNormalX">Its normal one animation frame later.</param>
/// <param name="NextNormalY">Its normal one animation frame later.</param>
/// <param name="NextNormalZ">Its normal one animation frame later.</param>
/// <param name="BoneA">Which bone moves this vertex most, for a model skinned on the GPU.</param>
/// <param name="BoneB">The second bone moving it.</param>
/// <param name="BoneC">The third bone moving it.</param>
/// <param name="WeightA">How much the first bone moves it.</param>
/// <param name="WeightB">How much the second moves it.</param>
/// <param name="WeightC">How much the third moves it.</param>
internal readonly record struct WorldVertex(
    float X, float Y, float Depth, float U, float V, float LightU, float LightV, float Alpha,
    float Red = 1f, float Green = 1f, float Blue = 1f, float LightStep = 0f,
    float NormalX = 0f, float NormalY = 0f, float NormalZ = 1f,
    float NextX = 0f, float NextY = 0f, float NextZ = 0f,
    float NextNormalX = 0f, float NextNormalY = 0f, float NextNormalZ = 1f,
    float BoneA = 0f, float BoneB = 0f, float BoneC = 0f,
    float WeightA = 0f, float WeightB = 0f, float WeightC = 0f);

/// <summary>A run of triangles sharing one texture.</summary>
/// <param name="MaterialIndex">Which material, indexed into the map's table.</param>
/// <param name="FirstVertex">Where the run starts.</param>
/// <param name="VertexCount">How many vertices it covers.</param>
/// <param name="BodyPart">Which body part this run belongs to, for a model batch.</param>
/// <param name="BodyModel">Which of that part's alternatives, so one can be chosen per entity.</param>
/// <remarks>
/// **A batch never spans two body parts**, which is what makes the choice possible at draw time. The
/// grouping key is the material AND the part and alternative it came from, so a run can be skipped
/// whole when the entity's <c>m_nBody</c> did not select it. Merging on material alone would put a
/// capture point's three signs in one run, and then no per-entity decision could separate them.
/// </remarks>
internal readonly record struct WorldBatch(
    int MaterialIndex,
    int FirstVertex,
    int VertexCount,
    int BodyPart = 0,
    int BodyModel = 0);

/// <summary>The sun as it reaches one model.</summary>
/// <param name="Red">Intensity, linear, from the map's own emit_skylight.</param>
/// <param name="Green">Intensity, linear.</param>
/// <param name="Blue">Intensity, linear.</param>
/// <param name="DirectionX">The direction the light travels, as the map stores it.</param>
/// <param name="DirectionY">The direction the light travels.</param>
/// <param name="DirectionZ">The direction the light travels.</param>
/// <remarks>
/// Present only for a model that traced to sky. A sky light is defined with that condition in
/// Valve's own description — "surface must trace to SKY texture" — so a model in shade carries no
/// sun rather than a dimmed one.
/// </remarks>
internal readonly record struct SunLight(
    float Red, float Green, float Blue,
    float DirectionX, float DirectionY, float DirectionZ);

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
/// **Positions arrive already in clip space**, as they do for the point renderer, because the
/// projection is <see cref="TopDownCamera"/>'s job and is tested as ordinary arithmetic rather than
/// through a GPU.
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
            float2 luv : TEXCOORD1;
            float  a   : TEXCOORD2;
            float3 vc  : TEXCOORD3;
            float3 nrm : TEXCOORD5;
            float  ls  : TEXCOORD4;
        };

        cbuffer Camera : register(b0)
        {
            row_major float4x4 viewProjection;

            // x: a debug view that replaces the texture with a flat category colour. Turning the
            //    map into "this is world, this is terrain, this is a prop, this is missing" answers
            //    in one glance what a textured picture hides.
            // y: a cutting plane in DEPTH, which is world height inverted. Everything nearer than
            //    this is discarded, so a roof can be taken off to show the room under it - the
            //    hallways into last on cp_process being the case that asked for it. Zero draws
            //    everything.
            float4 surfaceColours;
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
            output.uv = input.uv;
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

        float4 PsMain(VsOut input) : SV_TARGET
        {
            // **Two textures mixed by the vertex's alpha, which is what terrain is.** A
            // WorldVertexTransition material carries dirt and grass, and a displacement's vertices
            // say how much of each. Where a material has only one texture the second is bound to
            // the same image, so the mix is an identity and costs a sample.
            float4 first = albedoMap.Sample(wrapSampler, input.uv);
            float4 second = blendMap.Sample(wrapSampler, input.uv);
            // **The cut is on depth, which is height.** Discarding here rather than dropping the
            // geometry means the slice moves without rebuilding anything - the camera matrix work
            // is what makes that free.
            clip(input.pos.z - surfaceColours.y);

            float4 albedo = lerp(first, second, saturate(input.a));

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
            if (bump.w > 0.5f)
            {
                clip(albedo.a - 0.5f);
            }

            // In the category view the vertex colour IS the answer, so the texture is dropped.
            if (surfaceColours.x > 0.5f)
            {
                return float4(input.vc, 1.0f);
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
                    light += sunColour.rgb * saturate(dot(input.nrm, -sunDirection.xyz));
                }
            }

            float3 lit = albedo.rgb * light * input.vc;

            if (mode >= 0)
            {
                lit = CombineDetailAfterLighting(lit, detailColour, mode, detail.y);
            }

            // **The base texture's alpha decides which parts light themselves**, one being fully
            // unlit and zero normally lit. Applied after the lightmap, because the whole point is
            // that these parts ignore it.
            if (bump.z > 0.5f)
            {
                lit = lerp(lit, selfIllumTint.rgb * albedo.rgb, albedo.a);
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

    /// <summary>Entity model geometry, in model space, uploaded once.</summary>
    private ComPtr<ID3D11Buffer> _modelVertices;

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
        return CreateTexture(device, context, present.Width, present.Height, present.Pixels.Span);
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

    private readonly List<ComPtr<ID3D11ShaderResourceView>> _textures = [];

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

    /// <summary>The translucent batches, farthest first.</summary>
    private IReadOnlyList<WorldBatch> _sortedTranslucent = [];

    /// <summary>The decal batches, drawn over the world with a depth bias.</summary>
    private IReadOnlyList<WorldBatch> _decals = [];

    /// <summary>Rasteriser state that pulls a decal toward the camera.</summary>
    /// <remarks>
    /// **Valve's own numbers, from materialsystem_config.h**, which is published even though the
    /// overlay renderer is not:
    ///
    /// <code>
    /// m_SlopeScaleDepthBias_Decal = -0.5f;
    /// m_DepthBias_Decal = -262144;
    /// </code>
    ///
    /// Against a 24-bit depth buffer, -262144 is a push of 262144 / 2^24, about **1.6% of the
    /// range** — and that is the trap. Valve's projection is perspective, where most of the depth
    /// range sits close to the camera, so 1.6% near the surface being decalled is a fraction of a
    /// unit. This projection is orthographic over the whole map's height: 1.6% of a 1,600-unit
    /// range is **twenty-five world units**, which is taller than a health pack.
    ///
    /// The visible result was a decal painted over the pickup standing on it, with the pack's
    /// shape faintly showing through — reported as "the health packs are not drawing" and chased
    /// through the model pipeline for an evening. Comparing against TF2 itself is what settled it:
    /// in game the pack sits clearly on top of a much smaller patch.
    ///
    /// So the bias is computed from the map's own height range to be worth about one world unit,
    /// which is what Valve's constant achieves in Valve's projection. Copying the number without
    /// matching the projection copies the intent and inverts the effect.
    /// </remarks>
    private ComPtr<ID3D11RasterizerState> _decalOffset;

    /// <summary>Depth bias used until the map's height range is known.</summary>
    /// <remarks>
    /// Sized for a range of about 1,600 units, which is a typical TF2 map: one unit of a 24-bit
    /// range is 2^24 / 1600, near enough ten thousand. <see cref="SetDecalBias"/> replaces it with
    /// the real arithmetic once the map has been read.
    /// </remarks>
    private const int DefaultDecalBias = -10000;

    /// <summary>Blend state that ADDS a fragment to what is already there.</summary>
    private ComPtr<ID3D11BlendState> _addBlend;
    private readonly List<ComPtr<ID3D11ShaderResourceView>> _blendTextures = [];
    private ComPtr<ID3D11ShaderResourceView> _lightmap;
    private ComPtr<ID3D11ShaderResourceView> _white;

    /// <summary>The detail pattern for each material, empty where it has none.</summary>
    private readonly List<ComPtr<ID3D11ShaderResourceView>> _details = [];

    /// <summary>The bump map for each material, empty where it has none.</summary>
    private readonly List<ComPtr<ID3D11ShaderResourceView>> _bumps = [];

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

    /// <summary>Depth tested but not written, so a blended surface does not occlude.</summary>
    private ComPtr<ID3D11DepthStencilState> _depthReadOnly;

    private IReadOnlyList<WorldBatch> _batches = [];

    private WorldRenderer(
        ComPtr<ID3D11VertexShader> vertexShader,
        ComPtr<ID3D11PixelShader> pixelShader,
        ComPtr<ID3D11InputLayout> layout,
        ComPtr<ID3D11SamplerState> wrapSampler,
        ComPtr<ID3D11SamplerState> clampSampler)
    {
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
    /// <returns>The renderer.</returns>
    public static WorldRenderer Create(ComPtr<ID3D11Device> device)
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

        // The same state pulled toward the camera, by an amount worth about a world unit rather
        // than by Valve's raw constant - see the remarks on _decalOffset.
        RasterizerDesc biased = rasterizer;

        biased.DepthBias = DefaultDecalBias;
        biased.SlopeScaledDepthBias = -0.5f;

        ComPtr<ID3D11RasterizerState> decalOffset = default;
        SilkMarshal.ThrowHResult(device.CreateRasterizerState(in biased, ref decalOffset));

        return new WorldRenderer(
            vertexShader,
            pixelShader,
            layout,
            Sampler(device, TextureAddressMode.Wrap),
            Sampler(device, TextureAddressMode.Clamp))
        {
            _bothSides = bothSides,
            _decalOffset = decalOffset,
        };
    }

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
            BlendDesc description = default;

            description.RenderTarget[0].BlendEnable = 1;
            description.RenderTarget[0].SrcBlend = Blend.One;
            description.RenderTarget[0].DestBlend = Blend.One;
            description.RenderTarget[0].BlendOp = BlendOp.Add;
            description.RenderTarget[0].SrcBlendAlpha = Blend.One;
            description.RenderTarget[0].DestBlendAlpha = Blend.One;
            description.RenderTarget[0].BlendOpAlpha = BlendOp.Add;
            description.RenderTarget[0].RenderTargetWriteMask = (byte)ColorWriteEnable.All;

            ComPtr<ID3D11BlendState> state = default;

            SilkMarshal.ThrowHResult(device.CreateBlendState(in description, ref state));

            _addBlend = state;
        }

        if (_alphaBlend.Handle is null)
        {
            // Source-alpha over one-minus-source-alpha, which is what BT_BLEND means. The factors
            // themselves are NOT in source-sdk-2013 - SetDefaultBlendingShadowState is defined in
            // the closed materialsystem - so this is interpolated from the name and from what the
            // surrounding code assumes. Flagged in docs/findings/17-translucency.md.
            BlendDesc description = default;

            description.RenderTarget[0].BlendEnable = 1;
            description.RenderTarget[0].SrcBlend = Blend.SrcAlpha;
            description.RenderTarget[0].DestBlend = Blend.InvSrcAlpha;
            description.RenderTarget[0].BlendOp = BlendOp.Add;
            description.RenderTarget[0].SrcBlendAlpha = Blend.One;
            description.RenderTarget[0].DestBlendAlpha = Blend.InvSrcAlpha;
            description.RenderTarget[0].BlendOpAlpha = BlendOp.Add;
            description.RenderTarget[0].RenderTargetWriteMask = (byte)ColorWriteEnable.All;

            ComPtr<ID3D11BlendState> blend = default;

            SilkMarshal.ThrowHResult(device.CreateBlendState(in description, ref blend));

            _alphaBlend = blend;
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

        // **The engine's own convention: what is missing looks wrong on purpose.** Source draws an
        // unresolved material as a magenta and black chequer, and it is the right call - a surface
        // that quietly falls back to white or to nothing is a surface nobody investigates. Several
        // defects in this project hid for hours behind exactly that: a hole and a dark patch look
        // like art, while a magenta chequer looks like a bug and gets reported.
        _white = CreateTexture(device, context, MissingSize, MissingSize, Missing());

        for (int index = 0; index < assets.Textures.Count; index++)
        {
            MapTexture? texture = assets.Textures[index];

            _textures.Add(Upload(device, context, texture));

            if (texture is { IsAdditive: true })
            {
                _additive.Add(index);
            }
            else if (texture is { IsTranslucent: true })
            {
                _translucent.Add(index);
            }
        }

        foreach (MapTexture? texture in assets.BlendTextures)
        {
            _blendTextures.Add(Upload(device, context, texture));
        }

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
            float alphaTested = surface is { IsTransparent: true, IsTranslucent: false } ? 1f : 0f;

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
                ]);
        }

        // **Linear, not sRGB.** A lightmap is light rather than a picture: linearising it on
        // sampling would apply the curve to values that never had it, darkening every shadow.
        _lightmap = CreateTexture(
            device,
            context,
            assets.Lightmaps.Width,
            assets.Lightmaps.Height,
            assets.Lightmaps.Pixels,
            srgb: false);

        // Counted, because "we now skip additive materials" is a capability and this is the output.
        ViewerLog.Write(
            "render",
            $"{_additive.Count} of {assets.Textures.Count} materials are additive, drawn in a second pass");

        ViewerLog.Write(
            "render",
            $"{_translucent.Count} of {assets.Textures.Count} materials are translucent, blended and sorted");

        // **The output, not the capability.** A detail chain that resolves nothing draws a map that
        // looks entirely reasonable, so the count of textures actually bound is the only thing that
        // distinguishes "implemented" from "working".
        ViewerLog.Write(
            "render",
            $"{_details.Count(detail => detail.Handle is not null)} materials draw with a detail texture");

        ViewerLog.Write(
            "render",
            $"{_bumps.Count(bump => bump.Handle is not null)} materials draw with a bump map");

        ViewerLog.Write(
            "render",
            $"{assets.Textures.Count(texture => texture is { SelfIllum: not null })} materials light themselves");
    }

    /// <summary>Uploads a map's projected triangles, replacing anything already there.</summary>
    /// <param name="device">Device to create the vertex buffer on.</param>
    /// <param name="vertices">Every triangle corner, already in clip space.</param>
    /// <param name="batches">The runs, one per material.</param>
    /// <param name="decals">Overlay runs, drawn afterwards with a depth bias.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void UploadGeometry(
        ComPtr<ID3D11Device> device,
        IReadOnlyList<WorldVertex> vertices,
        IReadOnlyList<WorldBatch> batches,
        IReadOnlyList<WorldBatch>? decals = null)
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

        uint stride = VertexStride;
        uint offset = 0;

        context.RSSetState(_bothSides);
        context.IASetInputLayout(_layout);
        context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context.IASetVertexBuffers(0, 1, ref _vertices, in stride, in offset);
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

        EnsureMaterialBuffer(context);

        // The map's own geometry is already in world space, so it draws with an identity model
        // matrix. Set every frame rather than once: an entity draw leaves its own matrix behind,
        // and inheriting it would move the whole map to wherever the last rocket was.
        SetModel(context, Identity);

        // **Opaque first, additive after.** An additive fragment brightens whatever is behind it,
        // so anything drawn later would be added to nothing.
        foreach (WorldBatch batch in _batches)
        {
            if (_additive.Contains(batch.MaterialIndex) || _translucent.Contains(batch.MaterialIndex))
            {
                continue;
            }

            ComPtr<ID3D11ShaderResourceView> texture =
                batch.MaterialIndex >= 0 && batch.MaterialIndex < _textures.Count &&
                _textures[batch.MaterialIndex].Handle is not null
                    ? _textures[batch.MaterialIndex]
                    : _white;

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

            SetMaterial(context, batch.MaterialIndex);

            context.PSSetShaderResources(0, 1, ref texture);
            context.PSSetShaderResources(2, 1, ref blend);
            context.PSSetShaderResources(3, 1, ref detail);
            context.PSSetShaderResources(4, 1, ref bump);
            context.Draw((uint)batch.VertexCount, (uint)batch.FirstVertex);
        }

        DrawDecals(context);
        DrawTranslucent(context);
        DrawAdditive(context);
    }

    /// <summary>Sets the view for the frames that follow.</summary>
    /// <param name="device">Device to create the buffer on, the first time.</param>
    /// <param name="context">Context to upload through.</param>
    /// <param name="matrix">Sixteen floats, row major, from <see cref="TopDownCamera.ToMatrix"/>.</param>
    /// <param name="surfaceColours">Whether to draw flat category colours instead of textures.</param>
    /// <param name="heightCut">Discard anything above this height, from 0 (all) to 1 (nothing).</param>
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
        float heightCut = 0f)
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
                ByteWidth = sizeof(float) * 20,
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.ConstantBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
            };

            ComPtr<ID3D11Buffer> buffer = default;

            SilkMarshal.ThrowHResult(device.CreateBuffer(in description, null, ref buffer));

            _camera = buffer;
        }

        // The matrix, then a float4 whose first component is the category-view switch. Constant
        // buffers are sized in whole sixteen-byte registers, so the padding is not optional.
        float[] contents =
        [
            .. matrix,
            surfaceColours ? 1f : 0f, Math.Clamp(heightCut, 0f, 1f), 0f, 0f,
        ];

        MappedSubresource mapped = default;

        SilkMarshal.ThrowHResult(
            context.Map(_camera, 0, Map.WriteDiscard, 0, ref mapped));

        fixed (float* source = contents)
        {
            System.Buffer.MemoryCopy(
                source, mapped.PData, sizeof(float) * 20, sizeof(float) * 20);
        }

        context.Unmap(_camera, 0);
    }

    /// <summary>The per-material constants a material without a detail texture gets.</summary>
    /// <remarks>
    /// Mode -1, which is the value the shader tests to skip the combine entirely. The tint is white
    /// so that a stale sample can never darken anything even if the mode were somehow read.
    /// </remarks>
    private static readonly float[] NoDetail =
        [0f, 0f, -1f, 0f, 1f, 1f, 1f, 1f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f];

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
        int bones = 0)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        if (matrix.Length != 16)
        {
            throw new ArgumentException("A model matrix is sixteen floats.", nameof(matrix));
        }

        EnsureModelBuffer(context);

        // Sixteen for the matrix, then six float4s for the cube: the faces in the shader's own
        // order, with w on the first saying whether a cube was supplied at all.
        float[] contents = new float[ModelConstants];

        Array.Copy(matrix, contents, 16);

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

        MappedSubresource mapped = default;

        SilkMarshal.ThrowHResult(context.Map(_model, 0, Map.WriteDiscard, 0, ref mapped));

        fixed (float* source = contents)
        {
            System.Buffer.MemoryCopy(
                source, mapped.PData, sizeof(float) * ModelConstants, sizeof(float) * ModelConstants);
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

    /// <summary>Floats in the model constant buffer: a matrix, six cube faces, and the sun.</summary>
    private const int ModelConstants = 16 + (6 * 4) + 4 + 4 + 4 + 4;

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
    private static void WriteFace(float[] into, int at, (float Red, float Green, float Blue) face)
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

        float[] contents = new float[BoneConstants];

        for (int bone = 0; bone < matrices.Count && bone < MaxBones; bone++)
        {
            float[] matrix = matrices[bone];

            if (matrix.Length < 12)
            {
                continue;
            }

            Array.Copy(matrix, 0, contents, bone * 12, 12);
        }

        MappedSubresource mapped = default;

        SilkMarshal.ThrowHResult(context.Map(_bones, 0, Map.WriteDiscard, 0, ref mapped));

        fixed (float* source = contents)
        {
            System.Buffer.MemoryCopy(
                source, mapped.PData, sizeof(float) * BoneConstants, sizeof(float) * BoneConstants);
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
            ByteWidth = sizeof(float) * 16,
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
    private void SetMaterial(ComPtr<ID3D11DeviceContext> context, int materialIndex)
    {
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

        MappedSubresource mapped = default;

        SilkMarshal.ThrowHResult(context.Map(_material, 0, Map.WriteDiscard, 0, ref mapped));

        fixed (float* source = contents)
        {
            System.Buffer.MemoryCopy(source, mapped.PData, sizeof(float) * 16, sizeof(float) * 16);
        }

        context.Unmap(_material, 0);
        context.PSSetConstantBuffers(1, 1, ref _material);
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

        context.RSSetState(_decalOffset);

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

            ComPtr<ID3D11ShaderResourceView> texture =
                _textures[batch.MaterialIndex].Handle is not null
                    ? _textures[batch.MaterialIndex]
                    : _white;

            SetMaterial(context, batch.MaterialIndex);

            context.PSSetShaderResources(0, 1, ref texture);
            context.PSSetShaderResources(2, 1, ref texture);
            context.PSSetShaderResources(3, 1, ref _white);
            context.PSSetShaderResources(4, 1, ref _white);
            context.Draw((uint)batch.VertexCount, (uint)batch.FirstVertex);
        }

        // Back to the ordinary rasteriser, or everything after this is pulled forward too.
        context.RSSetState(_bothSides);
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

            ComPtr<ID3D11ShaderResourceView> texture = _textures[batch.MaterialIndex];

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

            SetMaterial(context, batch.MaterialIndex);

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

            ComPtr<ID3D11ShaderResourceView> texture = _textures[batch.MaterialIndex];

            ComPtr<ID3D11ShaderResourceView> detail =
                batch.MaterialIndex < _details.Count &&
                _details[batch.MaterialIndex].Handle is not null
                    ? _details[batch.MaterialIndex]
                    : _white;

            SetMaterial(context, batch.MaterialIndex);

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
        ReleaseMap();
        _material.Dispose();
        _camera.Dispose();
        _model.Dispose();
        _modelVertices.Dispose();
        _decalOffset.Dispose();
        _bothSides.Dispose();
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
                 _textures.Concat(_blendTextures).Concat(_details).Concat(_bumps)
                     .Where(texture => texture.Handle is not null))
        {
            texture.Dispose();
        }

        _textures.Clear();
        _blendTextures.Clear();
        _details.Clear();
        _bumps.Clear();
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

    /// <summary>Uploads every entity model's triangles, in model space.</summary>
    /// <param name="device">The device.</param>
    /// <param name="vertices">Packed model geometry; may be empty.</param>
    /// <param name="batches">Each model's runs, keyed by its path.</param>
    /// <exception cref="ArgumentNullException"><paramref name="vertices"/> is null.</exception>
    /// <remarks>
    /// **A second buffer rather than a bigger one**, because the two have entirely different
    /// lifetimes: the map's geometry is rebuilt when the world is, and this grows only when a
    /// model the demo has not shown before appears. Merging them would rebuild the map every time
    /// a new rocket type turned up.
    ///
    /// Uploaded whole each time a model is added, which is rare and bounded — a match uses a few
    /// hundred distinct models and every one of them is known within a few seconds of playback.
    /// </remarks>
    public void UploadModels(
        ComPtr<ID3D11Device> device,
        IReadOnlyList<WorldVertex> vertices,
        Dictionary<string, IReadOnlyList<IReadOnlyList<WorldBatch>>> batches)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(batches);

        _modelBatches = batches;

        _modelVertices.Dispose();
        _modelVertices = default;

        if (vertices.Count == 0)
        {
            return;
        }

        float[] data = Pack(vertices);

        BufferDesc description = new()
        {
            ByteWidth = (uint)(data.Length * sizeof(float)),
            Usage = Usage.Immutable,
            BindFlags = (uint)BindFlag.VertexBuffer,
        };

        fixed (float* first = data)
        {
            SubresourceData initial = new() { PSysMem = first };

            SilkMarshal.ThrowHResult(
                device.CreateBuffer(in description, in initial, ref _modelVertices));
        }
    }

    /// <summary>Sizes the decal bias for the map's own height range.</summary>
    /// <param name="device">The device.</param>
    /// <param name="worldRange">Highest world height minus lowest, in units.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="worldRange"/> is not positive.</exception>
    /// <remarks>
    /// **A world unit, whatever the map.** The depth buffer spans the map's whole height, so the
    /// same bias means different distances on different maps — a tall map would push its decals
    /// further through whatever stands on them. One unit is enough to stop a decal fighting the
    /// surface it lies on and far less than the smallest thing that can stand on one.
    /// </remarks>
    public void SetDecalBias(ComPtr<ID3D11Device> device, float worldRange)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(worldRange);

        RasterizerDesc description = new()
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            DepthClipEnable = 1,
            DepthBias = -(int)(16777216.0 / worldRange),
            SlopeScaledDepthBias = -0.5f,
        };

        ComPtr<ID3D11RasterizerState> replacement = default;

        SilkMarshal.ThrowHResult(device.CreateRasterizerState(in description, ref replacement));

        _decalOffset.Dispose();
        _decalOffset = replacement;
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
    /// <param name="matrix">Where it stands: sixteen floats, row major.</param>
    /// <param name="batches">Its runs, indexing into the model buffer.</param>
    /// <param name="light">The ambient cube of the leaf it stands in, or null.</param>
    /// <param name="sun">The sun reaching it, or null when it traced to solid rather than sky.</param>
    /// <param name="blend">How far toward the next baked animation frame, from nought to one.</param>
    /// <param name="bones">How many bones skin this draw, or zero for a baked model.</param>
    /// <param name="skin">Which material replaces which for a team colour; null for the model's own.</param>
    /// <param name="blended">Draw the blended materials rather than the opaque ones.</param>
    /// <param name="bodyParts">The model's body parts, for reading the body number.</param>
    /// <param name="body">Which alternative each part shows, packed as m_nBody.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **One matrix and one draw per entity, which is the engine's shape.** The vertices were
    /// uploaded once and never move; only this constant changes between instances. Callers set the
    /// map's identity matrix back afterwards — see <see cref="Draw"/>, which does so every frame
    /// precisely because an entity draw leaves its own matrix behind.
    /// </remarks>
    public void DrawModel(
        ComPtr<ID3D11DeviceContext> context,
        float[] matrix,
        IReadOnlyList<WorldBatch> batches,
        AmbientCube? light = null,
        SunLight? sun = null,
        float blend = 0f,
        int bones = 0,
        IReadOnlyDictionary<int, int>? skin = null,
        bool blended = false,
        IReadOnlyList<(int Base, int Count)>? bodyParts = null,
        int body = 0)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(batches);

        if (_modelVertices.Handle is null)
        {
            // Not silent: a caller asking to draw a model when nothing was uploaded is a wiring
            // fault, and it looks exactly like a model that is correctly invisible.
            DecodeLog.Lost("render", "a model was posed before any model geometry was uploaded");
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

        context.IASetVertexBuffers(0, 1, ref _modelVertices, in stride, in offset);

        SetModel(context, matrix, light, sun, blend, bones);

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
            int material = skin is not null && skin.TryGetValue(batch.MaterialIndex, out int swapped)
                ? swapped
                : batch.MaterialIndex;

            // **A model's materials are sorted into the same two passes the world's are.** Until
            // now every model batch drew opaque, whatever its material said, which is why a capture
            // point's hologram came out as a solid ribbed slab rather than something to see
            // through. The classification is already done, at upload, for the map's textures — a
            // model's materials live in the same table, so it is the same lookup.
            bool wantsBlending = _additive.Contains(material) || _translucent.Contains(material);

            if (wantsBlending != blended)
            {
                continue;
            }

            // **The body part's chosen alternative, per entity.** Every alternative is packed once
            // and the choice is made here, which is how three capture points sharing one model show
            // three different signs. Batches never span two alternatives, so skipping is whole runs
            // rather than triangles.
            if (bodyParts is { Count: > 0 } &&
                !Shows(bodyParts, batch.BodyPart, batch.BodyModel, body))
            {
                continue;
            }

            if (blended)
            {
                // Per batch, because a model can carry both kinds: additive ADDS light to what is
                // behind it, which is what a hologram does, and alpha blends against it.
                context.OMSetBlendState(
                    _additive.Contains(material) ? _addBlend : _alphaBlend,
                    blendFactor,
                    0xFFFFFFFF);
            }


            ComPtr<ID3D11ShaderResourceView> texture =
                material >= 0 && material < _textures.Count &&
                _textures[material].Handle is not null
                    ? _textures[material]
                    : _white;

            context.PSSetShaderResources(0, 1, ref texture);
            context.PSSetShaderResources(2, 1, ref texture);
            context.PSSetShaderResources(3, 1, ref _white);
            context.PSSetShaderResources(4, 1, ref _white);

            SetMaterial(context, material);

            context.Draw((uint)batch.VertexCount, (uint)batch.FirstVertex);
        }
    }

    /// <summary>Whether a batch is the alternative its body part shows.</summary>
    /// <remarks>GetBodygroup, shared/animation.cpp:876, applied to a packed run.</remarks>
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
    private static ComPtr<ID3D11ShaderResourceView> CreateTexture(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        int width,
        int height,
        ReadOnlySpan<byte> pixels,
        bool srgb = true)
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

    private static ComPtr<ID3D11SamplerState> Sampler(
        ComPtr<ID3D11Device> device, TextureAddressMode address)
    {
        SamplerDesc description = new()
        {
            Filter = Filter.MinMagMipLinear,
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
