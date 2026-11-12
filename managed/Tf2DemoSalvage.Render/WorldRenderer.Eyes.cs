using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Render;

/// <summary>The <c>EyeRefract</c>, <c>Eyes</c> and <c>Teeth</c> shaders (B725).</summary>
/// <remarks>
/// **Their own vertex shader, as in the engine**: all three light per vertex, each with a normal of its own — the eye's from
/// its centre, the teeth's darkened by the mouth — and EyeRefract hands four lights' attenuation and cosine to the pixel
/// shader. The derivation and citations are <c>docs/RISKS.md</c> B725.
/// </remarks>
internal sealed unsafe partial class WorldRenderer
{
    /// <summary>The <c>cbuffer Eye</c> register.</summary>
    private const int EyeConstantsSlot = 8;

    /// <summary>Its size in floats: eight float4s.</summary>
    private const int EyeConstantFloats = 32;

    /// <summary>The cornea, occlusion and Eyes' iris slots.</summary>
    private const int EyeCorneaSlot = 17;

    private const int EyeOcclusionSlot = 18;

    private const int EyeIrisSlot = 19;

    private const string EyeShaderText = """

        // **The face shaders' constants** (B725): what the studio render writes into the material before the mesh draws,
        // and the material's own parameters.
        cbuffer Eye : register(b8)
        {
            float4 eyeOrigin;          // xyz `$eyeorigin`; w the shader: 1 EyeRefract, 2 Eyes, 3 Teeth
            float4 eyeUp;              // xyz `$eyeup` (Eyes); w 1 for the HALFLAMBERT combo
            float4 eyeIrisU;           // `$irisu`
            float4 eyeIrisV;           // `$irisv`
            float4 eyeScalars;         // `$dilation`, `$glossiness`, z 1 for an 8-bit cube (read through sRGB),
                                       // `$corneabumpstrength`
            float4 eyeScalars2;        // x 1 under a `$lightwarptexture`, y `$eyeballradius`, z 1 `$raytracesphere`, 2 with
                                       // `$spheretexkillcombo`, w `$parallaxstrength`
            float4 eyeOcclusionColour; // `$ambientocclcolor`, raw (c5)
            float4 teethLighting;      // `$forward`, `$illumfactor` (teeth_vs20.fxc:22)
        };

        Texture2D eyeCornea    : register(t17);
        Texture2D eyeOcclusion : register(t18);
        Texture2D eyeIris      : register(t19);

        struct EyeVsOut
        {
            float4 pos             : SV_POSITION;
            float2 uv              : TEXCOORD0;
            float3 vertexLight     : TEXCOORD1;
            float3 tangentView     : TEXCOORD2;
            float3 wpos            : TEXCOORD3;
            float3 nrm             : TEXCOORD4;
            float3 tangent         : TEXCOORD5;
            float4 falloffCosine01 : TEXCOORD6;
            float4 falloffCosine23 : TEXCOORD7;
            float3 binormal        : TEXCOORD8;
            float2 irisUv          : TEXCOORD9;
            float  clip            : SV_ClipDistance0;
        };

        // **The light list these shaders walk: the sun first where the model sees it, then the lamps, four at most** — the
        // engine's four slots (`g_nLightCount`), which the sun takes one of (B170's remainder; its rank among them is not
        // read).
        int EyeLightCount()
        {
            return min((sunColour.w > 0.5f ? 1 : 0) + (int)(localLightFalloff[0].w + 0.5f), 4);
        }

        int EyeLamp(int light)
        {
            return sunColour.w > 0.5f ? light - 1 : light;
        }

        // `PixelShaderGetLightColor`, the light's colour.
        float3 EyeLightColour(int light)
        {
            int lamp = EyeLamp(light);
            return lamp < 0 ? sunColour.rgb : localLightColour[lamp].rgb;
        }

        // `VertexAttenInternal`: a directional light unattenuated.
        float EyeLightAtten(int light, float3 wpos)
        {
            int lamp = EyeLamp(light);
            return lamp < 0 ? 1.0f : LampAttenuation(lamp, wpos);
        }

        // `CosineTermInternal` (common_vs_fxc.h:811-832): toward a point, `-dir` for a directional light, clamped or wrapped.
        float EyeCosineTerm(int light, float3 wpos, float3 normal, bool halfLambert)
        {
            int lamp = EyeLamp(light);
            float3 toward = lamp < 0
                ? -normalize(sunDirection.xyz)
                : (localLightPosition[lamp].w > 0.5f
                    ? -normalize(localLightDirection[lamp].xyz)
                    : normalize(localLightPosition[lamp].xyz - wpos));
            float nDotL = dot(normal, toward);
            float wrapped = nDotL * 0.5f + 0.5f;
            return halfLambert ? wrapped * wrapped : max(0.0f, nDotL);
        }

        // `PixelShaderGetLightVector`: `normalize( pos - worldPos )`, a directional light's pos 10000 back (B721).
        float3 EyePixelToward(int light, float3 wpos)
        {
            int lamp = EyeLamp(light);
            return lamp < 0 ? DirectionalToward(sunDirection.xyz, wpos) : LampToward(lamp, wpos);
        }

        // `AmbientLight` (common_vs_fxc.h:749-758): the cube by the squared normal.
        float3 EyeAmbient(float3 normal)
        {
            float3 nSquared = normal * normal;
            int3 isNegative = (int3)(normal < 0.0f);

            return nSquared.x * ambientCube[isNegative.x].rgb +
                   nSquared.y * ambientCube[isNegative.y + 2].rgb +
                   nSquared.z * ambientCube[isNegative.z + 4].rgb;
        }

        // `DoLighting`'s dynamic half (common_vs_fxc.h:862-892), over this list.
        float3 EyeDoLighting(float3 wpos, float3 normal, bool halfLambert)
        {
            float3 light = EyeAmbient(normal);
            int count = EyeLightCount();

            [unroll]
            for (int i = 0; i < 4; i++)
            {
                if (i < count)
                {
                    light += EyeLightColour(i) * EyeCosineTerm(i, wpos, normal, halfLambert) * EyeLightAtten(i, wpos);
                }
            }

            return light;
        }

        // `GetAmbientLightCubeLuminance` (shaderapidx9.dll 0x18001afe0), saturated (eye_refract_helper.cpp:314).
        float EyeAverageAmbient()
        {
            float sum = 0.0f;

            [unroll]
            for (int face = 0; face < 6; face++)
            {
                sum += dot(ambientCube[face].rgb, float3(0.3f, 0.59f, 0.11f));
            }

            return saturate(sum / 6.0f);
        }

        EyeVsOut VsEye(VsIn input)
        {
            EyeVsOut output;

            float3 posed = lerp(input.pos, input.nextPos, frameBlend.x);

            // **The normal normalised BEFORE the bones and never after** (`teeth_vs20.fxc:84-89`): `SkinPositionAndNormal`
            // rotates it by the blended matrix only (`common_vs_fxc.h:661-697`), so a scaling bone or a blend of bones
            // leaves it unnormalised, and `DoLighting` takes it so. Only the teeth read it.
            float3 posedNormal = normalize(lerp(input.nrm, input.nextNrm, frameBlend.x));

            if (skinning.x >= 1.0f)
            {
                // `ApplyMorph` then the skinning, as VsMain.
                float3 flexedPos = input.pos + input.flexPos;
                float3 flexedNrm = normalize(input.nrm + input.flexNrm);
                posed = SkinPosition(flexedPos, input.bones, input.weights, skinning.x);
                float3 boneOrigin = SkinPosition(float3(0.0f, 0.0f, 0.0f), input.bones, input.weights, skinning.x);
                posedNormal = SkinPosition(flexedNrm, input.bones, input.weights, skinning.x) - boneOrigin;
            }

            float4 world = mul(float4(posed, 1.0f), model);
            float3 wpos = world.xyz;
            output.pos = mul(world, viewProjection);
            output.wpos = wpos;
            output.clip = dot(float4(wpos, 1.0f), clipPlane);

            // The artist's coordinate, untransformed (eye_refract_vs20.fxc:189, eyes_vs20.fxc:139, teeth_vs20.fxc:122).
            output.uv = input.uv;
            output.tangentView = float3(0.0f, 0.0f, 1.0f);
            output.nrm = float3(0.0f, 0.0f, 1.0f);
            output.tangent = float3(1.0f, 0.0f, 0.0f);
            output.binormal = float3(0.0f, 1.0f, 0.0f);
            output.falloffCosine01 = float4(0.0f, 0.0f, 0.0f, 0.0f);
            output.falloffCosine23 = float4(0.0f, 0.0f, 0.0f, 0.0f);
            output.irisUv = float2(0.0f, 0.0f);

            bool halfLambert = eyeUp.w > 0.5f;
            float shader = eyeOrigin.w;

            if (shader > 2.5f)
            {
                // Teeth (teeth_vs20.fxc:84-121): the skinned normal as it is, DoLighting without half-Lambert, darkened.
                float3 normal = mul(float4(posedNormal, 0.0f), model).xyz;
                output.vertexLight = EyeDoLighting(wpos, normal, false) * teethLighting.w * saturate(dot(normal, teethLighting.xyz));
            }
            else if (shader > 1.5f)
            {
                // Eyes (eyes_vs20.fxc): `n = pos - origin`, less half its part along `$eyeup`, normalised.
                float3 normal = wpos - eyeOrigin.xyz;
                float normalDotUp = -dot(normal, eyeUp.xyz) * 0.5f;
                normal = normalize(normalDotUp * eyeUp.xyz + normal);
                output.vertexLight = EyeDoLighting(wpos, normal, halfLambert);
                output.irisUv = float2(dot(eyeIrisU, float4(wpos, 1.0f)), dot(eyeIrisV, float4(wpos, 1.0f)));
            }
            else
            {
                // EyeRefract (eye_refract_vs20.fxc:100-186).
                float3 socketUp = normalize(-eyeIrisV.xyz);
                float3 socketLeft = normalize(-eyeIrisU.xyz);
                float3 normal = normalize(wpos - eyeOrigin.xyz);
                float3 tangent = normalize(cross(socketUp, normal));
                float3 binormal = normalize(cross(normal, tangent));
                float3 view = normalize(wpos - eyePosition.xyz);

                output.nrm = normal;
                output.tangent = tangent;
                output.binormal = binormal;
                output.tangentView = normalize(float3(dot(view, tangent), dot(view, binormal), dot(view, normal)));

                float side = -dot(normal, socketLeft) * 0.5f;
                float3 bent = normalize(side * socketLeft + normal);

                // Under a warp only the ambient: the lights come per pixel through it (:159-171).
                output.vertexLight = eyeScalars2.x > 0.5f ? EyeAmbient(bent) : EyeDoLighting(wpos, bent, halfLambert);

                int count = EyeLightCount();
                output.falloffCosine01 = float4(
                    count > 0 ? EyeLightAtten(0, wpos) : 0.0f,
                    count > 1 ? EyeLightAtten(1, wpos) : 0.0f,
                    count > 0 ? EyeCosineTerm(0, wpos, normal, halfLambert) : 0.0f,
                    count > 1 ? EyeCosineTerm(1, wpos, normal, halfLambert) : 0.0f);
                output.falloffCosine23 = float4(
                    count > 2 ? EyeLightAtten(2, wpos) : 0.0f,
                    count > 3 ? EyeLightAtten(3, wpos) : 0.0f,
                    count > 2 ? EyeCosineTerm(2, wpos, normal, halfLambert) : 0.0f,
                    count > 3 ? EyeCosineTerm(3, wpos, normal, halfLambert) : 0.0f);
            }

            return output;
        }

        float EyeFalloff(EyeVsOut input, int light)
        {
            return light == 0 ? input.falloffCosine01.x
                : (light == 1 ? input.falloffCosine01.y : (light == 2 ? input.falloffCosine23.x : input.falloffCosine23.y));
        }

        float EyeCosine(EyeVsOut input, int light)
        {
            return light == 0 ? input.falloffCosine01.z
                : (light == 1 ? input.falloffCosine01.w : (light == 2 ? input.falloffCosine23.z : input.falloffCosine23.w));
        }

        // The sRGB curve's decode, which the D3D9 sampler applies to an 8-bit texture read with sRGB on.
        float3 EyeSrgbToLinear(float3 stored)
        {
            float3 low = stored / 12.92f;
            float3 high = pow(abs((stored + 0.055f) / 1.055f), 2.4f);
            return float3(
                stored.r <= 0.04045f ? low.r : high.r,
                stored.g <= 0.04045f ? low.g : high.g,
                stored.b <= 0.04045f ? low.b : high.b);
        }

        // `IntersectRaySphere` (eye_refract_ps2x.fxc:100-107).
        float EyeIntersectRaySphere(float3 cameraPos, float3 ray, float3 sphereCenter, float sphereRadius)
        {
            float3 dst = cameraPos - sphereCenter;
            float b = dot(dst, ray);
            float c = dot(dst, dst) - sphereRadius * sphereRadius;
            float d = b * b - c;
            return d > 0.0f ? (-b - sqrt(d)) : 0.0f;
        }

        // **`eye_refract_ps2x.fxc`, ps_2_b and later, without the flashlight** (this viewer has none).
        float4 PsEyeRefract(EyeVsOut input) : SV_TARGET
        {
            float3 vertexLight = input.vertexLight;
            float3 normal = input.nrm;
            float3 tangent = input.tangent;
            float3 binormal = input.binormal;
            float3 tangentView = input.tangentView;
            float3 wpos = input.wpos;
            float3 viewVector = normalize(wpos - eyePosition.xyz);
            int count = EyeLightCount();

            // TF NPR lighting (:146-179).
            if (eyeScalars2.x > 0.5f)
            {
                [unroll]
                for (int light = 0; light < 4; light++)
                {
                    if (light < count)
                    {
                        float3 warped = 2.0f * lightWarp.Sample(clampSampler, float2(EyeCosine(input, light), 0.5f)).rgb;
                        vertexLight += EyeFalloff(input, light) * EyeLightColour(light) * warped;
                    }
                }
            }

            // The sphere ray cast (:184-196): off in every TF2 eye.
            if (eyeScalars2.z > 0.5f)
            {
                float distanceAlong = EyeIntersectRaySphere(eyePosition.xyz, viewVector, eyeOrigin.xyz, eyeScalars2.y);
                wpos = eyePosition.xyz + viewVector * distanceAlong;

                if (distanceAlong == 0.0f)
                {
                    if (eyeScalars2.z > 1.5f)
                    {
                        clip(-1.0f);
                    }

                    wpos = eyeOrigin.xyz + normal * eyeScalars2.y;
                }
            }

            // Sphere and cornea coordinates, the parallax and the cornea's normal (:201-252).
            float2 corneaUv = float2(dot(eyeIrisU, float4(wpos, 1.0f)), dot(eyeIrisV, float4(wpos, 1.0f)));
            float2 sphereUv = corneaUv * 0.5f + 0.25f;
            float4 corneaSample = eyeCornea.Sample(clampSampler, corneaUv);
            float irisOffset = corneaSample.b;

            float2 parallax = (tangentView.xy * irisOffset * eyeScalars2.w) / (1.0f - tangentView.z);
            parallax.x = -parallax.x;

            float2 irisUv = sphereUv - parallax;
            float corneaNoise = albedoMap.Sample(clampSampler, sphereUv + parallax * 0.5f).a;

            float3 corneaTangentNormal = float3(corneaSample.rg - 0.5f, 1.0f);
            corneaTangentNormal.xy *= eyeScalars.w;
            corneaTangentNormal.xy += corneaNoise * 0.1f;
            corneaTangentNormal = normalize(corneaTangentNormal);

            float3 corneaWorldNormal = normalize(
                corneaTangentNormal.x * tangent + corneaTangentNormal.y * binormal + corneaTangentNormal.z * normal);

            // Pupil dilation (:306-311).
            irisUv -= 0.5f;
            float centreToBorder = saturate(length(irisUv) / 0.2f);
            irisUv *= lerp(1.0f, centreToBorder, saturate(eyeScalars.x) * 2.5f - 1.25f);
            irisUv += 0.5f;

            float4 irisColour = albedoMap.Sample(clampSampler, irisUv);

            // The iris's highlights per light, and its view-dependent ambient term (:324-377).
            float highlightMask = corneaSample.a;
            float3 irisTangentNormal = corneaTangentNormal;
            irisTangentNormal.xy *= -2.5f;
            float3 irisLighting = float3(0.0f, 0.0f, 0.0f);
            float3 highlights = float3(0.0f, 0.0f, 0.0f);
            float3 reflected = reflect(viewVector, corneaWorldNormal);

            [unroll]
            for (int j = 0; j < 4; j++)
            {
                if (j < count)
                {
                    float3 worldLight = EyePixelToward(j, input.wpos);
                    float3 tangentLight = float3(dot(worldLight, tangent), dot(worldLight, binormal), dot(worldLight, normal));
                    float3 flattened = -tangentLight;
                    flattened.xy *= -0.5f;
                    flattened.z = max(flattened.z, 0.5f);
                    flattened = normalize(flattened);

                    float facing = pow(abs(dot(irisTangentNormal, flattened)), 6.0f) * 0.5f;
                    float darkness = pow(1.0f - saturate((-tangentLight.z - 0.25f) / 0.75f), 4.0f);
                    float3 colour = EyeFalloff(input, j) * EyeLightColour(j);

                    irisLighting += facing * highlightMask * darkness * colour;

                    // The glint (:414-425).
                    highlights += pow(saturate(dot(reflected, worldLight)), 128.0f) * colour;
                }
            }

            irisLighting += saturate(dot(irisTangentNormal, -tangentView)) * EyeAverageAmbient() * highlightMask * 0.5f;

            // The occlusion colours the light (:386-388).
            float3 occlusion = eyeOcclusion.Sample(wrapSampler, input.uv).rgb;
            vertexLight *= lerp(eyeOcclusionColour.rgb, float3(1.0f, 1.0f, 1.0f), occlusion);

            // **No `ENV_MAP_SCALE`** (:395-396 — the scaled line is the commented one), and **read through sRGB**
            // (`EnableSRGBRead( SHADER_SAMPLER2, true )`, eye_refract_helper.cpp:217). This renderer uploads every cube
            // linear (`UploadCube`, for the generic paths' bakes), so an 8-bit one is decoded here; TF2's eye cube is 8-bit
            // only (`eye-reflection-cubemap-` ships no `.hdr`), and sRGB reading does not apply to a float one.
            float3 cubeSample = envMap.Sample(wrapSampler, reflected).rgb;
            float3 reflection = eyeScalars.y * (eyeScalars.z > 0.5f ? EyeSrgbToLinear(cubeSample) : cubeSample);

            // Combine (:430-445).
            float3 result = irisColour.rgb;
            result += corneaNoise * 0.1f;
            result *= vertexLight + irisLighting;
            result += reflection * vertexLight;
            result += highlights;

            return float4(PixelFog(result, input.wpos, tintControl.z, tintControl.w), 1.0f);
        }

        // **`eyes_ps2x.fxc:36-58`**: the iris over the base by its alpha, times the light. The glint render target is not
        // drawn here (B725 open), so its term is zero.
        float4 PsEyes(EyeVsOut input) : SV_TARGET
        {
            float4 baseSample = albedoMap.Sample(wrapSampler, input.uv);
            float4 irisSample = eyeIris.Sample(clampSampler, input.irisUv);
            float3 result = lerp(baseSample.rgb, irisSample.rgb, irisSample.a) * input.vertexLight;

            return float4(PixelFog(result, input.wpos, tintControl.z, tintControl.w), baseSample.a);
        }

        // **`teeth_ps2x.fxc:37-48`**: the base times the vertex's light.
        float4 PsTeeth(EyeVsOut input) : SV_TARGET
        {
            float4 baseSample = albedoMap.Sample(wrapSampler, input.uv);

            return float4(PixelFog(baseSample.rgb * input.vertexLight, input.wpos, tintControl.z, tintControl.w), baseSample.a);
        }
        """;

    /// <summary>Each face material by index, with its cornea, occlusion map and Eyes' iris uploaded.</summary>
    private readonly Dictionary<int, (Scene.EyeMaterial Material, ComPtr<ID3D11ShaderResourceView> Cornea, ComPtr<ID3D11ShaderResourceView> Occlusion, ComPtr<ID3D11ShaderResourceView> Iris)> _eyes = [];

    private ComPtr<ID3D11VertexShader> _eyeVertex;
    private ComPtr<ID3D11PixelShader> _eyeRefractPixel;
    private ComPtr<ID3D11PixelShader> _eyesPixel;
    private ComPtr<ID3D11PixelShader> _teethPixel;
    private ComPtr<ID3D11Buffer> _eyeConstants;

    /// <summary>The face materials whose own cube is 8-bit, so the eye shader reads it through sRGB.</summary>
    private readonly HashSet<int> _eyeByteCubes = [];

    /// <summary>Uploads a face material's extra textures, and compiles the face shaders the first time one loads (B725).</summary>
    /// <param name="device">The device.</param>
    /// <param name="context">The context.</param>
    /// <param name="index">The material's index.</param>
    /// <param name="eye">Its parameters.</param>
    /// <param name="cube">Its own cube, or null.</param>
    /// <remarks>
    /// **At load, not at the first face drawn**: the engine's shaders are compiled offline and bound by the material's
    /// snapshot when the material is precached; compiling three here at the first eye was a stall mid-playback.
    /// </remarks>
    private void UploadEye(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, int index, Scene.EyeMaterial eye, Scene.MapCubemap? cube)
    {
        EnsureEyeShaders();

        _eyes[index] = (
            eye,
            eye.Cornea is { } cornea ? Upload(device, context, cornea, srgb: false) : default,
            eye.AmbientOcclusion is { } occlusion ? Upload(device, context, occlusion) : default,
            eye.Iris is { } iris ? Upload(device, context, iris) : default);

        if (cube is { Faces: [{ Image.Format: not VtfFormat.Rgba16161616F }, ..] })
        {
            _eyeByteCubes.Add(index);
        }
    }

    /// <summary>Whether the face shaders are compiled, for the test that they are at load.</summary>
    internal bool EyeShadersCompiled => _eyeVertex.Handle is not null;

    private void ReleaseEyes()
    {
        foreach ((Scene.EyeMaterial _, ComPtr<ID3D11ShaderResourceView> cornea, ComPtr<ID3D11ShaderResourceView> occlusion, ComPtr<ID3D11ShaderResourceView> iris) in _eyes.Values)
        {
            cornea.Dispose();
            occlusion.Dispose();
            iris.Dispose();
        }

        _eyes.Clear();
        _eyeByteCubes.Clear();
    }

    private void DisposeEyes()
    {
        ReleaseEyes();
        _eyeVertex.Dispose();
        _eyeRefractPixel.Dispose();
        _eyesPixel.Dispose();
        _teethPixel.Dispose();
        _eyeConstants.Dispose();
    }

    /// <summary>
    /// Binds a face material's shaders and constants over the batch's ordinary binds, or answers false where it draws the
    /// ordinary way: an eye with no state from the draw (a caller that sets none).
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="material">The batch's material.</param>
    /// <param name="eyeball">The batch's eyeball, or −1.</param>
    /// <param name="eyes">The draw's eyeball states.</param>
    /// <param name="mouth">The draw's mouth, or null for the material's own <c>$forward</c> and <c>$illumfactor</c>.</param>
    /// <returns>Whether the face shaders are bound; the caller then calls <see cref="UnbindEye"/> after the draw.</returns>
    private bool BindEye(
        ComPtr<ID3D11DeviceContext> context,
        int material,
        int eyeball,
        IReadOnlyList<Scene.EyeShading>? eyes,
        Scene.MouthShading? mouth)
    {
        if (!_eyes.TryGetValue(material, out (Scene.EyeMaterial Material, ComPtr<ID3D11ShaderResourceView> Cornea, ComPtr<ID3D11ShaderResourceView> Occlusion, ComPtr<ID3D11ShaderResourceView> Iris) eye))
        {
            return false;
        }

        Scene.EyeShading state = default;

        if (eye.Material.Shader != Scene.EyeShader.Teeth)
        {
            if (eyes is null || eyeball < 0 || eyeball >= eyes.Count)
            {
                return false;
            }

            state = eyes[eyeball];
        }

        EnsureEyeShaders();

        MappedSubresource mapped = default;

        SubmitCounters.Upload(); SilkMarshal.ThrowHResult(context.Map(_eyeConstants, 0, Map.WriteDiscard, 0, ref mapped));

        float* into = (float*)mapped.PData;
        Scene.EyeMaterial parameters = eye.Material;
        (float X, float Y, float Z) forward = mouth?.Forward ?? parameters.Forward;

        Put(into, 0, state.Origin.X, state.Origin.Y, state.Origin.Z, (float)parameters.Shader);
        Put(into, 4, state.Up.X, state.Up.Y, state.Up.Z, parameters.HalfLambert ? 1f : 0f);
        Put(into, 8, state.IrisU.X, state.IrisU.Y, state.IrisU.Z, state.IrisU.W);
        Put(into, 12, state.IrisV.X, state.IrisV.Y, state.IrisV.Z, state.IrisV.W);
        Put(into, 16, parameters.Dilation, parameters.Glossiness, _eyeByteCubes.Contains(material) ? 1f : 0f, parameters.CorneaBumpStrength);
        float rayCast = parameters.SphereTexKill ? 2f : 1f;

        Put(
            into, 20,
            material < _lightWarps.Count && _lightWarps[material].Handle is not null ? 1f : 0f,
            parameters.EyeballRadius,
            parameters.RaytraceSphere ? rayCast : 0f,
            parameters.ParallaxStrength);
        Put(
            into, 24,
            parameters.AmbientOcclusionColour.Red, parameters.AmbientOcclusionColour.Green, parameters.AmbientOcclusionColour.Blue, 0f);
        Put(into, 28, forward.X, forward.Y, forward.Z, mouth?.IllumFactor ?? parameters.IllumFactor);

        context.Unmap(_eyeConstants, 0);

        ComPtr<ID3D11PixelShader> pixel = parameters.Shader switch
        {
            Scene.EyeShader.Eyes => _eyesPixel,
            Scene.EyeShader.Teeth => _teethPixel,
            _ => _eyeRefractPixel,
        };

        context.VSSetShader(_eyeVertex, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShader(pixel, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.VSSetConstantBuffers(EyeConstantsSlot, 1, ref _eyeConstants);
        context.PSSetConstantBuffers(EyeConstantsSlot, 1, ref _eyeConstants);
        context.PSSetShaderResources(EyeCorneaSlot, 1, ref eye.Cornea);
        context.PSSetShaderResources(EyeOcclusionSlot, 1, ref eye.Occlusion);
        context.PSSetShaderResources(EyeIrisSlot, 1, ref eye.Iris);

        return true;
    }

    /// <summary>The world's own shaders back after a face batch.</summary>
    private void UnbindEye(ComPtr<ID3D11DeviceContext> context)
    {
        context.VSSetShader(_vertexShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShader(_pixelShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
    }

    private static void Put(float* into, int at, float x, float y, float z, float w)
    {
        into[at] = x;
        into[at + 1] = y;
        into[at + 2] = z;
        into[at + 3] = w;
    }

    private void EnsureEyeShaders()
    {
        if (_eyeVertex.Handle is not null)
        {
            return;
        }

        using D3DCompiler compiler = D3DCompiler.GetApi();
        ComPtr<ID3D10Blob> vertex = Compile(compiler, "VsEye", "vs_5_0");

        SilkMarshal.ThrowHResult(_device.CreateVertexShader(
            vertex.GetBufferPointer(), vertex.GetBufferSize(), ref Unsafe.NullRef<ID3D11ClassLinkage>(), ref _eyeVertex));
        vertex.Dispose();

        _eyeRefractPixel = PixelShader(_device, compiler, "PsEyeRefract");
        _eyesPixel = PixelShader(_device, compiler, "PsEyes");
        _teethPixel = PixelShader(_device, compiler, "PsTeeth");

        BufferDesc buffer = new()
        {
            ByteWidth = EyeConstantFloats * sizeof(float),
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.ConstantBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write,
        };

        SilkMarshal.ThrowHResult(_device.CreateBuffer(in buffer, null, ref _eyeConstants));
    }
}
