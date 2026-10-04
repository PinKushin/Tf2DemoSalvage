using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Render;

/// <summary>One frame's water: what <see cref="WorldRenderer.DrawWaterViews"/> renders before the main view (B62).</summary>
/// <param name="Views">The engine's views in draw order, <see cref="WaterViews.Plan"/>.</param>
/// <param name="Material">The visible fog volume's material, whose fog the refraction draws under; −1 for none.</param>
/// <param name="WaterHeight">The volume's surface, <c>m_flWaterHeight</c>.</param>
/// <param name="Camera">The main view's camera matrix.</param>
/// <param name="ReflectedCamera">The same view mirrored by <see cref="WaterViews.Reflect"/>, for the reflection.</param>
/// <param name="Fog">The world's fog, which the reflection and the under-water refraction draw under.</param>
/// <param name="DrawSkybox">Draws the 2D skybox through a camera matrix — the reflection's DF_DRAWSKYBOX — or null.</param>
/// <param name="DrawEntities">Draws a view's opaque then translucent renderables, for a view with DF_DRAW_ENTITITES.</param>
internal sealed record WaterDraw(
    IReadOnlyList<WaterView> Views,
    int Material,
    float WaterHeight,
    float[] Camera,
    float[] ReflectedCamera,
    Core.Scene.SceneFog? Fog,
    Action<float[]>? DrawSkybox,
    Action<WaterView>? DrawEntities = null)
{
    /// <summary>The volume's SURFACE material, which <c>SetFogVolumeState( ..., true )</c> takes its fog from; −1 means <see cref="Material"/>.</summary>
    public int SurfaceMaterial { get; init; } = -1;

    /// <summary>The frame's own target, which the under-water refraction renders into before it is copied out.</summary>
    public WaterFrameTarget? Frame { get; init; }

    /// <summary>The surface material, resolved.</summary>
    public int Surface => SurfaceMaterial >= 0 ? SurfaceMaterial : Material;
}

/// <summary>The frame's render target and depth, and its size.</summary>
/// <param name="Target">The colour target.</param>
/// <param name="Depth">The depth target.</param>
/// <param name="Width">Its width.</param>
/// <param name="Height">Its height.</param>
internal readonly record struct WaterFrameTarget(
    ComPtr<ID3D11RenderTargetView> Target, ComPtr<ID3D11DepthStencilView> Depth, int Width, int Height);

/// <summary>The engine's water: its views, its render targets and the <c>Water</c> shader's two passes (B62).</summary>
internal sealed unsafe partial class WorldRenderer
{
    /// <summary><c>_rt_WaterReflection</c> and <c>_rt_WaterRefraction</c> are 1024 square, <c>RT_SIZE_PICMIP</c>.</summary>
    /// <remarks>engine.dll <c>0x1800f6cd5</c>-<c>0x1800f6d54</c>: <c>CreateNamedRenderTargetTextureEx2( name, 0x400, 0x400,
    /// 2, ... )</c>, clamped in s and t (texture flags 0xc). <c>mat_picmip</c> 0 leaves them at 1024.</remarks>
    internal const int WaterTargetSize = 1024;

    /// <summary>Floats in <c>cbuffer WaterView</c>.</summary>
    private const int WaterConstants = 16 * 4;

    /// <summary>The <c>Water</c> shader's expensive and cheap passes, appended to the world shader's source.</summary>
    /// <remarks>
    /// **`water_ps2x.fxc` with `water_ps2x_helper.h`'s <c>DrawWater</c>, and <c>WaterCheap_ps2x.fxc</c>**, transcribed;
    /// the vertex shaders' work (<c>water_vs20.fxc</c>, <c>WaterCheap_vs20.fxc</c>) is done here per pixel from the
    /// world vertex shader's outputs. The <c>BASETEXTURE</c> combo is not ported: no shipped TF2 water declares one.
    ///
    /// **The tangent frame is built from screen derivatives** — the world vertex carries no tangents, and a water
    /// surface is a plane, so the frame the derivatives give is the texinfo's S and T. <i>Interpolated</i>: T's sign
    /// is taken as "increasing v".
    /// </remarks>
    private const string WaterShaderText = """

        Texture2D refractMap : register(t12);
        Texture2D reflectMap : register(t13);

        void WaterTangents(float3 wpos, float2 uv, float3 n, out float3 s, out float3 t)
        {
            float3 dp1 = ddx(wpos);
            float3 dp2 = ddy(wpos);
            float2 duv1 = ddx(uv);
            float2 duv2 = ddy(uv);
            float3 dp2perp = cross(dp2, n);
            float3 dp1perp = cross(n, dp1);
            float3 tangent = dp2perp * duv1.x + dp1perp * duv2.x;
            float3 binormal = dp2perp * duv1.y + dp1perp * duv2.y;
            float scale = rsqrt(max(max(dot(tangent, tangent), dot(binormal, binormal)), 1e-20f));
            s = tangent * scale;
            t = binormal * scale;
        }

        // DecompressNormal( NORMAL_DECODE_NONE ), or MULTITEXTURE's three samples averaged first (water_ps2x_helper.h).
        // The extra coordinates are water_vs20.fxc's: the RAW texcoord rotated 45 degrees and scaled, plus TexOffsets.
        float4 WaterNormal(float2 bumpUv, float2 raw)
        {
            float4 normal = bumpMap.Sample(wrapSampler, bumpUv);

            if (waterFlags.w > 0.5f)
            {
                float f45x = raw.x + raw.y;
                float f45y = raw.y - raw.x;
                float4 extra = float4(
                    f45x * 0.1f + texOffsets.x, f45y * 0.1f + texOffsets.y,
                    raw.y * 0.45f + texOffsets.z, raw.x * 0.45f + texOffsets.w);

                normal = 0.33f * (normal + bumpMap.Sample(wrapSampler, extra.xy) + bumpMap.Sample(wrapSampler, extra.zw));
            }

            normal.xyz = 2.0f * normal.xyz - 1.0f;
            return normal;
        }

        float4 PsWater(VsOut input) : SV_TARGET
        {
            float4 coordinate = float4(input.uv, 0.0f, 1.0f);
            float2 bumpUv = float2(dot(coordinate, bumpTransform0), dot(coordinate, bumpTransform1));
            float4 vNormal = WaterNormal(bumpUv, input.uv);

            float3 n = normalize(input.nrm);
            float3 s;
            float3 t;
            WaterTangents(input.wpos, input.uv, n, s, t);
            float3 toEye = eyePosition.xyz - input.wpos;
            float3 vTangentEyeVect = float3(dot(toEye, s), dot(toEye, t), dot(toEye, n));

            // water_vs20.fxc: the projected position mapped to both textures, refraction with y inverted.
            float4 proj = mul(float4(input.wpos, 1.0f), viewProjection);
            float2 reflectPos = (proj.xy + proj.w) * 0.5f;
            float2 refractPos = (float2(proj.x, -proj.y) + proj.w) * 0.5f;
            float4 reflectXY_refractYX = float4(reflectPos.x, reflectPos.y, refractPos.y, refractPos.x);

            float ooW = 1.0f / proj.w;
            float2 unwarpedRefractTexCoord = reflectXY_refractYX.wz * ooW;

            // "We don't actually have valid depth values in alpha when we are underwater looking out"
            float waterFogDepthValue = waterFlags.z > 0.5f
                ? refractMap.Sample(clampSampler, unwarpedRefractTexCoord).a
                : 1.0f;

            float4 reflectRefractScale2 = reflectRefractScale;

            if (waterFlags2.x < 0.5f)
            {
                reflectRefractScale2 *= waterFogDepthValue;
            }

            float4 vN = float4(vNormal.x, vNormal.y, vNormal.y, vNormal.x);
            float4 vDependentTexCoords = vN * vNormal.a * reflectRefractScale2;
            vDependentTexCoords += reflectXY_refractYX * ooW;
            float2 vReflectTexCoord = vDependentTexCoords.xy;
            float2 vRefractTexCoord = vDependentTexCoords.wz;

            float4 vReflectColor = reflectMap.Sample(clampSampler, vReflectTexCoord);
            float4 vRefractColor;

            if (waterFlags2.x > 0.5f)
            {
                // BLURRY_REFRACT: genwaterloop.pl's 5x5 box, then the overbright and BOTH tints.
                vRefractColor = float4(0.0f, 0.0f, 0.0f, 0.0f);

                [unroll]
                for (int ix = -2; ix <= 2; ix++)
                {
                    [unroll]
                    for (int iy = -2; iy <= 2; iy++)
                    {
                        vRefractColor += refractMap.Sample(clampSampler, vRefractTexCoord + float2(ix * 0.005f, iy * 0.005f));
                    }
                }

                vRefractColor *= (1.0f / 25.0f);
                vReflectColor *= waterFogParams.z;
                vReflectColor *= reflectTint;
                vRefractColor *= refractTint;

                if (waterFlags.z > 0.5f)
                {
                    waterFogDepthValue = vRefractColor.a;
                }
            }
            else
            {
                // **No refraction tint on this path**, as written: only the blurred one applies $refracttint.
                vReflectColor *= reflectTint;
                vRefractColor = refractMap.Sample(clampSampler, vRefractTexCoord);

                if (waterFlags.z > 0.5f)
                {
                    waterFogDepthValue = vRefractColor.a;
                }
            }

            float3 vEyeVect = normalize(vTangentEyeVect);
            float fNdotV = saturate(dot(vEyeVect, vNormal.xyz));
            float fFresnel = pow(1.0f - fNdotV, 5.0f);

            fFresnel *= saturate((waterFogDepthValue - 0.05f) * 20.0f);

            if (waterFlags.z > 0.5f)
            {
                vRefractColor = lerp(vRefractColor, waterFogColour, saturate(waterFogDepthValue - 0.05f));
            }
            else
            {
                float waterFogFactor = saturate((proj.z - waterFogParams.x) / waterFogParams.y);
                vRefractColor = lerp(vRefractColor, waterFogColour, waterFogFactor);
            }

            float4 result;

            if (waterFlags.x > 0.5f && waterFlags.y > 0.5f)
            {
                result = lerp(vRefractColor, vReflectColor, fFresnel);
            }
            else if (waterFlags.x > 0.5f)
            {
                result = vReflectColor;
            }
            else if (waterFlags.y > 0.5f)
            {
                result = vRefractColor;
            }
            else
            {
                result = float4(0.0f, 0.0f, 0.0f, 0.0f);
            }

            return float4(PixelFog(result.rgb, input.wpos, tintControl.z), 1.0f);
        }

        float4 PsWaterCheap(VsOut input) : SV_TARGET
        {
            // WaterCheap_vs20.fxc hands the normal map its coordinate UNTRANSFORMED.
            float4 vNormal = WaterNormal(input.uv, input.uv);

            float3 n = normalize(input.nrm);
            float3 s;
            float3 t;
            WaterTangents(input.wpos, input.uv, n, s, t);
            float3 worldSpaceNormal = vNormal.x * s + vNormal.y * t + vNormal.z * n;

            float3 worldSpaceEye = eyePosition.xyz - input.wpos;
            float flWorldSpaceDist = length(worldSpaceEye);
            worldSpaceEye /= flWorldSpaceDist;

            // CalcReflectionVectorUnnormalized: 2 * N * dot(N, E) - E * dot(N, N).
            float3 reflectVect = 2.0f * worldSpaceNormal * dot(worldSpaceNormal, worldSpaceEye) -
                worldSpaceEye * dot(worldSpaceNormal, worldSpaceNormal);
            float3 specularLighting = envMap.Sample(wrapSampler, reflectVect).rgb * cheapReflectTint.rgb;

            float flFresnelFactor;

            if (waterFlags2.y > 0.5f)
            {
                float flDotResult = 1.0f - max(0.0f, dot(worldSpaceEye, worldSpaceNormal));
                flFresnelFactor = flDotResult * flDotResult;
                flFresnelFactor *= flFresnelFactor;
                flFresnelFactor *= flDotResult;
            }
            else
            {
                flFresnelFactor = cheapReflectTint.w;
            }

            float flAlpha;

            if (waterFlags2.z > 0.5f)
            {
                // The LOD fade: the cheap pass covers the expensive one as the distance runs from start to end.
                float flReflectAmount = saturate(flWorldSpaceDist * cheapParams.z - cheapParams.w);
                flAlpha = saturate(flFresnelFactor + flReflectAmount);

                if (waterFlags2.w > 0.5f)
                {
                    float4 proj = mul(float4(input.wpos, 1.0f), viewProjection);
                    float2 unwarped = (float2(proj.x, -proj.y) + proj.w) * 0.5f / proj.w;
                    float fogDepthValue = refractMap.Sample(clampSampler, unwarped).a;
                    flAlpha *= saturate((fogDepthValue - 0.05f) * 20.0f);
                }
            }
            else
            {
                flAlpha = 1.0f;
                specularLighting = lerp(cheapFogColour.rgb, specularLighting, flFresnelFactor);
            }

            return float4(PixelFog(specularLighting, input.wpos, tintControl.z), flAlpha);
        }
        """;

    private ComPtr<ID3D11PixelShader> _waterShader;
    private ComPtr<ID3D11PixelShader> _cheapWaterShader;
    private ComPtr<ID3D11Buffer> _waterBuffer;

    /// <summary>The view's rows of <c>cbuffer WaterView</c>: clip plane, height fog, height fog colour.</summary>
    private readonly float[] _waterView = new float[12];

    private readonly float[] _waterContents = new float[WaterConstants];

    private IReadOnlyList<MapWater?> _waters = [];
    /// <summary>Each water material's normal-map frames, uploaded.</summary>
    private readonly Dictionary<int, List<ComPtr<ID3D11ShaderResourceView>>> _waterNormals = [];

    /// <summary>The frame copy the under-water refraction is stretched from, and the pass that stretches it.</summary>
    private ComPtr<ID3D11Texture2D> _frameCopy;
    private ComPtr<ID3D11ShaderResourceView> _frameCopyView;
    private (int Width, int Height, Silk.NET.DXGI.Format Format) _frameCopyShape;
    private ComPtr<ID3D11VertexShader> _blitVertex;
    private ComPtr<ID3D11PixelShader> _blitPixel;

    /// <summary>A full-screen triangle sampling t0 — <c>CopyRenderTargetToTextureEx</c>'s stretch, as a pass.</summary>
    private const string BlitShaderText = """
        Texture2D source : register(t0);
        SamplerState linearClamp : register(s0);

        struct BlitOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

        BlitOut VsBlit(uint id : SV_VertexID)
        {
            BlitOut output;
            output.uv = float2((id << 1) & 2, id & 2);
            output.pos = float4(output.uv * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
            return output;
        }

        float4 PsBlit(BlitOut input) : SV_TARGET
        {
            return source.Sample(linearClamp, input.uv);
        }
        """;

    /// <summary>Every side of the water and its surface: a view that draws the whole world.</summary>
    public static ViewDraws AllWaterDraws => ViewDraws.RenderAboveWater | ViewDraws.RenderUnderWater | ViewDraws.RenderWater;

    /// <summary>The view's draw flags — which sort groups and whether the water surface draw (B62).</summary>
    public ViewDraws ViewDraws { get; set; } = AllWaterDraws;

    private ComPtr<ID3D11Texture2D> _reflectionTexture;
    private ComPtr<ID3D11RenderTargetView> _reflectionTarget;
    private ComPtr<ID3D11ShaderResourceView> _reflectionView;
    private ComPtr<ID3D11Texture2D> _refractionTexture;
    private ComPtr<ID3D11RenderTargetView> _refractionTarget;
    private ComPtr<ID3D11ShaderResourceView> _refractionView;
    private ComPtr<ID3D11Texture2D> _waterDepthTexture;
    private ComPtr<ID3D11DepthStencilView> _waterDepth;

    /// <summary>What the last <see cref="SetCamera"/> was told besides the matrix, so a water view keeps the main view's switches.</summary>
    private (bool Colours, bool Specular, Fullbright Fullbright, DebugModes Debug, bool Phong) _cameraSwitches = (false, true, Fullbright.Off, default, true);

    /// <summary>The view's cheap-water distances, which the <c>WaterLOD</c> proxy hands each water material it binds.</summary>
    public (float Start, float End) WaterLod { get; set; } = (0f, 0.1f);

    /// <summary><c>r_waterforceexpensive</c>, which the shader reads as well as the view (<c>water.cpp:526</c>).</summary>
    public bool ForceExpensiveWater { get; set; }

    /// <summary>How many water batches drew each pass this frame — reset by <see cref="DrawWaterViews"/>.</summary>
    public (int Expensive, int Cheap, int Plain) WaterDraws { get; private set; }

    private void UploadWaters(ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, MapAssets assets)
    {
        ReleaseWaters();
        _waters = assets.Waters;

        for (int index = 0; index < _waters.Count; index++)
        {
            if (_waters[index] is { NormalFrames.Count: > 0 } water)
            {
                // A normal map is a direction, not a colour, so it is never read through the sRGB curve.
                _waterNormals[index] = [.. water.NormalFrames.Select(frame => Upload(device, context, frame, srgb: false))];
            }
        }

        if (_waterShader.Handle is null)
        {
            using D3DCompiler compiler = D3DCompiler.GetApi();

            _waterShader = PixelShader(device, compiler, "PsWater");
            _cheapWaterShader = PixelShader(device, compiler, "PsWaterCheap");
        }
    }

    private static ComPtr<ID3D11PixelShader> PixelShader(ComPtr<ID3D11Device> device, D3DCompiler compiler, string entry)
    {
        ComPtr<ID3D10Blob> bytecode = Compile(compiler, entry, "ps_5_0");
        ComPtr<ID3D11PixelShader> shader = default;

        SilkMarshal.ThrowHResult(device.CreatePixelShader(
            bytecode.GetBufferPointer(), bytecode.GetBufferSize(), ref Unsafe.NullRef<ID3D11ClassLinkage>(), ref shader));

        bytecode.Dispose();
        return shader;
    }

    private void ReleaseWaters()
    {
        foreach (ComPtr<ID3D11ShaderResourceView> normal in _waterNormals.Values.SelectMany(frames => frames))
        {
            normal.Dispose();
        }

        _waterNormals.Clear();
        _waters = [];
    }

    private void DisposeWater()
    {
        ReleaseWaters();
        _waterShader.Dispose();
        _cheapWaterShader.Dispose();
        _waterBuffer.Dispose();
        _reflectionView.Dispose();
        _reflectionTarget.Dispose();
        _reflectionTexture.Dispose();
        _refractionView.Dispose();
        _refractionTarget.Dispose();
        _refractionTexture.Dispose();
        _waterDepth.Dispose();
        _waterDepthTexture.Dispose();
        _frameCopyView.Dispose();
        _frameCopy.Dispose();
        _blitVertex.Dispose();
        _blitPixel.Dispose();
    }

    /// <summary>Whether a material is a <c>Water</c> material.</summary>
    /// <param name="material">The material index.</param>
    /// <returns>True for water.</returns>
    public bool IsWater(int material) => material >= 0 && material < _waters.Count && _waters[material] is not null;

    /// <summary>Sets the view's height clip and, for a refraction view, the volume's height fog.</summary>
    /// <param name="context">The context.</param>
    /// <param name="clip">The clip, <see cref="HeightClip.None"/> for none.</param>
    /// <param name="heightFog">The volume's height fog — <see cref="WaterViews.VolumeFogFor"/> — or null.</param>
    public void SetWaterView(ComPtr<ID3D11DeviceContext> context, HeightClip clip, VolumeFog? heightFog = null)
    {
        Array.Clear(_waterView);

        // RENDER_ABOVE keeps z ≥ height: the plane (0, 0, 1, −h). RENDER_BELOW keeps z ≤ height: (0, 0, −1, h).
        if (clip.Mode == HeightClipMode.RenderAbove)
        {
            _waterView[2] = 1f;
            _waterView[3] = -clip.Z;
        }
        else if (clip.Mode == HeightClipMode.RenderBelow)
        {
            _waterView[2] = -1f;
            _waterView[3] = clip.Z;
        }

        // **The volume's height fog, as R_SetFogVolumeState sets it** (engine.dll 0x1800e0cd0): SetFogZ, the colour,
        // start and end. CalcWaterFogAlpha reads the fog z from g_FogParams.y and one over the range from .w — packed
        // here as the range fog's own registers are (FogConstants, B139).
        if (heightFog is { HeightFog: true } fog)
        {
            float range = fog.End - fog.Start;

            _waterView[4] = 1f;
            _waterView[5] = fog.FogZ;
            _waterView[6] = 1f;
            _waterView[7] = range != 0f ? 1f / range : 0f;
            _waterView[8] = Linear(fog.Color.Red);
            _waterView[9] = Linear(fog.Color.Green);
            _waterView[10] = Linear(fog.Color.Blue);
        }

        WriteWater(context, null, -1);
    }

    private static float Linear(float gamma) => MathF.Pow(gamma, 2.2f);

    /// <summary>Whether the draws that follow write the height fog factor to alpha — only fully opaque ones do.</summary>
    /// <param name="context">The context.</param>
    /// <param name="opaque">True before the opaque draws, false before the blended ones.</param>
    public void WriteWaterFogToAlpha(ComPtr<ID3D11DeviceContext> context, bool opaque)
    {
        if (_waterView[4] > 0.5f)
        {
            _waterView[6] = opaque ? 1f : 0f;
            WriteWater(context, null, -1);
        }
    }

    private void BindWaterView(ComPtr<ID3D11DeviceContext> context)
    {
        if (_waterBuffer.Handle is null)
        {
            WriteWater(context, null, -1);
        }

        context.VSSetConstantBuffers(4, 1, ref _waterBuffer);
        context.PSSetConstantBuffers(4, 1, ref _waterBuffer);
    }

    private void WriteWater(ComPtr<ID3D11DeviceContext> context, MapWater? water, int material)
    {
        if (_waterBuffer.Handle is null)
        {
            BufferDesc description = new()
            {
                ByteWidth = sizeof(float) * WaterConstants,
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.ConstantBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
            };

            SilkMarshal.ThrowHResult(_device.CreateBuffer(in description, null, ref _waterBuffer));
        }

        Span<float> c = _waterContents;

        c.Clear();
        _waterView.CopyTo(c);

        if (water is not null)
        {
            // SetPixelShaderConstantGammaToLinear( 1, REFRACTTINT ) and ( 4, REFLECTTINT ); c5 the amounts; c6 the fog
            // colour, linear; c7 start, end − start and the reflect overbright, 1 off HDR_TYPE_INTEGER (water.cpp:300-350).
            Put(c, 12, Linear(water.RefractTint.Red), Linear(water.RefractTint.Green), Linear(water.RefractTint.Blue), 1f);
            Put(c, 16, Linear(water.ReflectTint.Red), Linear(water.ReflectTint.Green), Linear(water.ReflectTint.Blue), 1f);
            Put(c, 20, water.ReflectAmount, water.ReflectAmount, water.RefractAmount, water.RefractAmount);
            Put(c, 24, Linear(water.FogColor.Red), Linear(water.FogColor.Green), Linear(water.FogColor.Blue), 1f);
            Put(c, 28, water.FogStart, water.FogEnd - water.FogStart, 1f, 0f);

            (bool reflection, bool refraction, bool forceCheap) = Combos(water);

            Put(c, 32, reflection ? 1f : 0f, refraction ? 1f : 0f, water.AboveWater ? 1f : 0f, MathF.Abs(water.Scroll1.X) > 0f ? 1f : 0f);

            // The cheap pass's FRESNEL, BLEND ( !bForceCheap ) and REFRACTALPHA ( bRefraction ) combos (water.cpp:394-430).
            Put(c, 36, water.BlurRefract ? 1f : 0f, water.NoFresnel ? 0f : 1f, forceCheap ? 0f : 1f, refraction ? 1f : 0f);

            // The WaterLOD proxy overwrites the distances with the view's before the shader reads them.
            (float start, float end) = water.FollowsViewLod ? WaterLod : (water.CheapWaterStart, water.CheapWaterEnd);
            float delta = end - start;

            Put(c, 40, start, end, delta != 0f ? 1f / delta : 0f, delta != 0f ? start / delta : 0f);
            // $bumptransform after the material's proxy chain has run for this bind.
            TextureTransform bump = water.BumpTransformAt(Seconds);

            Put(c, 44, bump.Row0.X, bump.Row0.Y, bump.Row0.Z, bump.Row0.W);
            Put(c, 48, bump.Row1.X, bump.Row1.Y, bump.Row1.Z, bump.Row1.W);

            float time = (float)Seconds;

            Put(c, 52, time * water.Scroll1.X, time * water.Scroll1.Y, time * water.Scroll2.X, time * water.Scroll2.Y);

            // SetPixelShaderConstant( 2, REFLECTTINT, REFLECTBLENDFACTOR ) — NOT gamma converted — and ( 0, FOGCOLOR ).
            Put(c, 56, water.ReflectTint.Red, water.ReflectTint.Green, water.ReflectTint.Blue, water.ReflectBlendFactor);
            Put(c, 60, water.FogColor.Red, water.FogColor.Green, water.FogColor.Blue, 0f);
        }

        _ = material;

        MappedSubresource mapped = default;

        SilkMarshal.ThrowHResult(context.Map(_waterBuffer, 0, Map.WriteDiscard, 0, ref mapped));

        fixed (float* source = _waterContents)
        {
            System.Buffer.MemoryCopy(source, mapped.PData, sizeof(float) * WaterConstants, sizeof(float) * WaterConstants);
        }

        context.Unmap(_waterBuffer, 0);

        static void Put(Span<float> into, int at, float x, float y, float z, float w)
        {
            into[at] = x;
            into[at + 1] = y;
            into[at + 2] = z;
            into[at + 3] = w;
        }
    }

    /// <summary><c>Water_DX90::SHADER_DRAW</c>'s combo decisions, water.cpp:521-535.</summary>
    private (bool Reflection, bool Refraction, bool ForceCheap) Combos(MapWater water)
    {
        bool forceCheap = water.View.ForceCheap;
        bool forceExpensive = !forceCheap && (ForceExpensiveWater || water.View.ForceExpensive);

        return (forceExpensive && water.View.ReflectTexture, water.View.RefractTexture, forceCheap);
    }

    /// <summary>Draws one water batch with the <c>Water</c> shader's passes.</summary>
    /// <returns>True when handled here; false for the Plain fallback, which the opaque loop draws white.</returns>
    private bool WaterBatch(ComPtr<ID3D11DeviceContext> context, WorldBatch batch)
    {
        if (!IsWater(batch.MaterialIndex) || _waters[batch.MaterialIndex] is not { } water)
        {
            return false;
        }

        // DF_RENDER_WATER: the surface draws only in a view that asks for MAT_SORT_GROUP_WATERSURFACE.
        if ((ViewDraws & ViewDraws.RenderWater) == 0)
        {
            return true;
        }

        (_, bool refraction, bool forceCheap) = Combos(water);
        ComPtr<ID3D11ShaderResourceView> cube =
            batch.MaterialIndex < _cubemaps.Count ? _cubemaps[batch.MaterialIndex] : default;

        WaterPass passes = WaterShader.Pass(
            refraction, water.View.ReflectTexture, cube.Handle is not null, forceCheap,
            !forceCheap && (ForceExpensiveWater || water.View.ForceExpensive), isDecal: false);

        (int expensive, int cheap, int plain) = WaterDraws;

        if (passes == WaterPass.Plain)
        {
            WaterDraws = (expensive, cheap, plain + 1);
            return false;
        }

        SetMaterial(context, batch.MaterialIndex, batch.Category);
        WriteWater(context, water, batch.MaterialIndex);

        // BindTexture( NORMALMAP, BUMPFRAME ), BUMPFRAME being the AnimatedTexture proxy's output.
        ComPtr<ID3D11ShaderResourceView> normal =
            _waterNormals.TryGetValue(batch.MaterialIndex, out List<ComPtr<ID3D11ShaderResourceView>>? frames)
                ? frames[water.NormalFrameAt(Seconds, frames.Count)]
                : _flatWhite;

        context.PSSetShaderResources(4, 1, ref normal);
        context.PSSetShaderResources(5, 1, ref cube);
        context.PSSetShaderResources(12, 1, ref _refractionView);
        context.PSSetShaderResources(13, 1, ref _reflectionView);

        if ((passes & WaterPass.ReflectionRefraction) != 0)
        {
            context.PSSetShader(_waterShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
            context.Draw((uint)batch.VertexCount, (uint)batch.FirstVertex);
            expensive++;
        }

        if ((passes & WaterPass.Cheap) != 0)
        {
            // EnableAlphaBlending( SRC_ALPHA, ONE_MINUS_SRC_ALPHA ) when bBlend (water.cpp:384).
            if (!forceCheap)
            {
                float* factor = stackalloc float[4] { 1f, 1f, 1f, 1f };

                context.OMSetBlendState(_alphaBlend, factor, 0xFFFFFFFF);
            }

            context.PSSetShader(_cheapWaterShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
            context.Draw((uint)batch.VertexCount, (uint)batch.FirstVertex);
            ResetBlend(context);
            cheap++;
        }

        WaterDraws = (expensive, cheap, plain);

        // Hand the world shader and the view's constants back to the next batch.
        context.PSSetShader(_pixelShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        WriteWater(context, null, -1);

        return true;
    }

    /// <summary>Renders the frame's reflection and refraction views into their targets (B62).</summary>
    /// <param name="context">The context.</param>
    /// <param name="frame">The frame's water.</param>
    /// <param name="restore">Rebinds the main view's target, viewport and camera afterwards.</param>
    /// <returns>The main view's own entry — Main, UnderMain or Simple — for the caller to draw, or null.</returns>
    /// <remarks>
    /// Each view runs <c>DrawExecute</c>'s order (<c>viewrender.cpp:5524-5553</c>): the world (its sort groups by the
    /// view's DF_ flags), then with DF_DRAW_ENTITITES the opaque and translucent renderables, and the translucent world.
    /// The main view's flags and height clip are left set for the caller's draw.
    /// </remarks>
    public WaterView? DrawWaterViews(ComPtr<ID3D11DeviceContext> context, WaterDraw frame, Action restore)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(restore);

        WaterDraws = (0, 0, 0);
        WaterView? main = null;
        float* colour = stackalloc float[4];

        foreach (WaterView view in frame.Views)
        {
            WaterViewTarget target = view.Target;

            if (target == WaterViewTarget.BackBuffer)
            {
                main ??= view;
                continue;
            }

            EnsureWaterTargets();

            // **The under-water refraction renders into the FRAME and is copied out** (viewrender.cpp:6203-6248):
            // "Refraction renders into the back buffer ... It is then blitted out into the refraction target."
            bool reflection = target == WaterViewTarget.Reflection;
            bool viaFrame = target == WaterViewTarget.BackBufferCopiedToRefraction && frame.Frame is not null;
            ComPtr<ID3D11RenderTargetView> into = reflection ? _reflectionTarget : _refractionTarget;

            if (viaFrame)
            {
                into = frame.Frame!.Value.Target;
            }
            ComPtr<ID3D11DepthStencilView> depth = viaFrame ? frame.Frame!.Value.Depth : _waterDepth;
            Viewport viewport = viaFrame
                ? new(0f, 0f, frame.Frame!.Value.Width, frame.Frame.Value.Height, 0f, 1f)
                : new(0f, 0f, WaterTargetSize, WaterTargetSize, 0f, 1f);

            // **Unbound before it is drawn into** — a texture cannot be a shader input and an output at once.
            ComPtr<ID3D11ShaderResourceView> none = default;

            context.PSSetShaderResources(12, 1, ref none);
            context.PSSetShaderResources(13, 1, ref none);
            context.OMSetRenderTargets(1u, into.GetAddressOf(), depth);
            context.RSSetViewports(1, in viewport);

            // VIEW_CLEAR_COLOR to the fog colour where the view sets it — SetFogVolumeState( fogInfo, true ) then
            // GetFogColor, so the SURFACE material's (engine.dll 0x1800e0cd0) — else black.
            colour[0] = 0f;
            colour[1] = 0f;
            colour[2] = 0f;
            colour[3] = 1f;

            if (view.ClearToFogColor && WaterViews.VolumeFogFor(Water(frame.Surface), frame.WaterHeight, true) is { } clearFog)
            {
                colour[0] = Linear(clearFog.Color.Red);
                colour[1] = Linear(clearFog.Color.Green);
                colour[2] = Linear(clearFog.Color.Blue);
            }

            if ((view.Clear & ViewClears.Color) != 0)
            {
                context.ClearRenderTargetView(into, colour);
            }

            context.ClearDepthStencilView(depth, (uint)ClearFlag.Depth, 1f, 0);

            float[] camera = reflection ? frame.ReflectedCamera : frame.Camera;

            SetCamera(
                _device, context, camera, _cameraSwitches.Colours, _cameraSwitches.Specular, _cameraSwitches.Fullbright,
                _cameraSwitches.Debug, _cameraSwitches.Phong, view.Fog == WaterViewFog.World ? frame.Fog : null);

            if ((view.Draw & ViewDraws.DrawSkybox) != 0)
            {
                frame.DrawSkybox?.Invoke(camera);
            }

            ExecuteView(context, frame, view);

            if (viaFrame)
            {
                StretchFrameToRefraction(context, frame.Frame!.Value);
            }
        }

        if (main is { } chosen)
        {
            ViewDraws = chosen.Draw;
        }

        SetWaterView(context, main is { } clipped ? clipped.Clip : HeightClip.None);
        restore();

        return main;
    }

    /// <summary>The views drawn into the frame after the main one: <c>CIntersectionView</c> (<c>viewrender.cpp:5958-5961</c>).</summary>
    /// <param name="context">The context, with the main view's target and camera bound.</param>
    /// <param name="frame">The frame's water.</param>
    /// <returns>True when a view drew, so the caller restores its own state.</returns>
    public bool DrawWaterIntersection(ComPtr<ID3D11DeviceContext> context, WaterDraw frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        bool drew = false;

        foreach (WaterView view in frame.Views)
        {
            if (view.Kind == WaterViewKind.Intersection)
            {
                ExecuteView(context, frame, view);
                drew = true;
            }
        }

        // The main view's flags back for whatever draws next in it.
        if (drew && frame.Views.FirstOrDefault(view => view.Kind is WaterViewKind.Main or WaterViewKind.UnderMain or WaterViewKind.Simple) is { } main)
        {
            ViewDraws = main.Draw;
            SetWaterView(context, main.Clip);
        }

        return drew;
    }

    /// <summary><c>CBaseWorldView::DrawExecute</c> for a water view, its camera already bound.</summary>
    private void ExecuteView(ComPtr<ID3D11DeviceContext> context, WaterDraw frame, WaterView view)
    {
        SetWaterView(
            context, view.Clip,
            view.Fog == WaterViewFog.VolumeHeight ? WaterViews.VolumeFogFor(Water(frame.Surface), frame.WaterHeight, true) : null);

        ViewDraws drawn = ViewDraws;

        ViewDraws = view.Draw;
        Draw(context);

        // DF_DRAW_ENTITITES: DrawOpaqueRenderables, DrawTranslucentRenderables (with the translucent world); without
        // it, DrawTranslucentWorldInLeaves (viewrender.cpp:5524-5553).
        if ((view.Draw & ViewDraws.DrawEntities) != 0)
        {
            frame.DrawEntities?.Invoke(view);
        }

        WriteWaterFogToAlpha(context, opaque: false);
        DrawTranslucentWorld(context);
        WriteWaterFogToAlpha(context, opaque: true);
        ViewDraws = drawn;
    }

    private MapWater? Water(int material) => material >= 0 && material < _waters.Count ? _waters[material] : null;

    /// <summary><c>CopyRenderTargetToTextureEx( refraction, 0, &amp;srcRect )</c>: the frame, stretched into the 1024² target.</summary>
    /// <remarks>
    /// **A pass rather than a copy, because a D3D11 copy cannot stretch** and the engine's (shaderapi's
    /// <c>StretchRect</c>) does. The frame is first copied whole into a texture that can be sampled, then a full-screen
    /// triangle draws it into the target with linear filtering — what a stretch does.
    /// </remarks>
    private void StretchFrameToRefraction(ComPtr<ID3D11DeviceContext> context, WaterFrameTarget frame)
    {
        ComPtr<ID3D11Resource> source = default;

        frame.Target.GetResource(ref source);

        RenderTargetViewDesc viewDescription = default;

        frame.Target.GetDesc(ref viewDescription);
        EnsureFrameCopy(frame.Width, frame.Height, viewDescription.Format);
        context.CopyResource(_frameCopy, source);
        source.Dispose();

        if (_blitPixel.Handle is null)
        {
            using D3DCompiler compiler = D3DCompiler.GetApi();

            ComPtr<ID3D10Blob> vertex = CompileSource(compiler, BlitShaderText, "VsBlit", "vs_5_0");
            ComPtr<ID3D10Blob> pixel = CompileSource(compiler, BlitShaderText, "PsBlit", "ps_5_0");

            SilkMarshal.ThrowHResult(_device.CreateVertexShader(
                vertex.GetBufferPointer(), vertex.GetBufferSize(), ref Unsafe.NullRef<ID3D11ClassLinkage>(), ref _blitVertex));
            SilkMarshal.ThrowHResult(_device.CreatePixelShader(
                pixel.GetBufferPointer(), pixel.GetBufferSize(), ref Unsafe.NullRef<ID3D11ClassLinkage>(), ref _blitPixel));
            vertex.Dispose();
            pixel.Dispose();
        }

        Viewport viewport = new(0f, 0f, WaterTargetSize, WaterTargetSize, 0f, 1f);
        ComPtr<ID3D11DepthStencilView> noDepth = default;

        context.OMSetRenderTargets(1u, _refractionTarget.GetAddressOf(), noDepth);
        context.RSSetViewports(1, in viewport);
        context.IASetInputLayout(default(ComPtr<ID3D11InputLayout>));
        context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context.VSSetShader(_blitVertex, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShader(_blitPixel, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShaderResources(0, 1, ref _frameCopyView);
        context.PSSetSamplers(0, 1, ref _clampSampler);
        ResetBlend(context);
        context.Draw(3, 0);

        // The world's pipeline back: its shaders, samplers and the frame target.
        ComPtr<ID3D11ShaderResourceView> none = default;

        context.PSSetShaderResources(0, 1, ref none);
        BindPipeline(context);

        Viewport frameViewport = new(0f, 0f, frame.Width, frame.Height, 0f, 1f);

        context.OMSetRenderTargets(1u, frame.Target.GetAddressOf(), frame.Depth);
        context.RSSetViewports(1, in frameViewport);
    }

    private void EnsureFrameCopy(int width, int height, Silk.NET.DXGI.Format viewFormat)
    {
        if (_frameCopy.Handle is not null && _frameCopyShape == (width, height, viewFormat))
        {
            return;
        }

        _frameCopyView.Dispose();
        _frameCopy.Dispose();

        // Typeless, so the frame's bytes copy across whichever of UNORM and SRGB its target view reads them as.
        Silk.NET.DXGI.Format typeless = viewFormat switch
        {
            Silk.NET.DXGI.Format.FormatB8G8R8A8Unorm or Silk.NET.DXGI.Format.FormatB8G8R8A8UnormSrgb => Silk.NET.DXGI.Format.FormatB8G8R8A8Typeless,
            _ => Silk.NET.DXGI.Format.FormatR8G8B8A8Typeless,
        };

        Texture2DDesc description = new()
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = typeless,
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.ShaderResource,
        };

        SilkMarshal.ThrowHResult(_device.CreateTexture2D(in description, ref Unsafe.NullRef<SubresourceData>(), ref _frameCopy));

        ShaderResourceViewDesc view = new() { Format = viewFormat, ViewDimension = D3DSrvDimension.D3DSrvDimensionTexture2D };

        view.Texture2D.MipLevels = 1;
        SilkMarshal.ThrowHResult(_device.CreateShaderResourceView(_frameCopy, in view, ref _frameCopyView));
        _frameCopyShape = (width, height, viewFormat);
    }

    /// <summary>One of the refraction or reflection target's pixels, for a test.</summary>
    /// <param name="context">The context.</param>
    /// <param name="refraction">The refraction target, else the reflection.</param>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <returns>Red, green and blue as stored.</returns>
    internal (int Red, int Green, int Blue) WaterTargetPixel(ComPtr<ID3D11DeviceContext> context, bool refraction, int x, int y)
    {
        EnsureWaterTargets();

        Texture2DDesc description = new()
        {
            Width = 1,
            Height = 1,
            MipLevels = 1,
            ArraySize = 1,
            Format = Silk.NET.DXGI.Format.FormatR8G8B8A8UnormSrgb,
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Usage = Usage.Staging,
            CPUAccessFlags = (uint)CpuAccessFlag.Read,
        };

        ComPtr<ID3D11Texture2D> staging = default;

        SilkMarshal.ThrowHResult(_device.CreateTexture2D(in description, ref Unsafe.NullRef<SubresourceData>(), ref staging));

        Box box = new((uint)x, (uint)y, 0, (uint)x + 1, (uint)y + 1, 1);

        context.CopySubresourceRegion(staging, 0, 0, 0, 0, refraction ? _refractionTexture : _reflectionTexture, 0, in box);

        MappedSubresource mapped = default;

        SilkMarshal.ThrowHResult(context.Map(staging, 0, Map.Read, 0, ref mapped));

        byte* texel = (byte*)mapped.PData;
        (int, int, int) result = (texel[0], texel[1], texel[2]);

        context.Unmap(staging, 0);
        staging.Dispose();

        return result;
    }

    private void EnsureWaterTargets()
    {
        if (_reflectionTarget.Handle is not null)
        {
            return;
        }

        (_reflectionTexture, _reflectionTarget, _reflectionView) = WaterTarget();
        (_refractionTexture, _refractionTarget, _refractionView) = WaterTarget();

        Texture2DDesc depth = new()
        {
            Width = WaterTargetSize,
            Height = WaterTargetSize,
            MipLevels = 1,
            ArraySize = 1,
            Format = Device3D.DepthFormat,
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.DepthStencil,
        };

        SilkMarshal.ThrowHResult(_device.CreateTexture2D(in depth, ref Unsafe.NullRef<SubresourceData>(), ref _waterDepthTexture));
        SilkMarshal.ThrowHResult(_device.CreateDepthStencilView(
            _waterDepthTexture, ref Unsafe.NullRef<DepthStencilViewDesc>(), ref _waterDepth));
    }

    /// <summary>One water target: RGBA8 read and written through the sRGB curve, as the shader's EnableSRGBRead asks.</summary>
    private (ComPtr<ID3D11Texture2D>, ComPtr<ID3D11RenderTargetView>, ComPtr<ID3D11ShaderResourceView>) WaterTarget()
    {
        Texture2DDesc description = new()
        {
            Width = WaterTargetSize,
            Height = WaterTargetSize,
            MipLevels = 1,
            ArraySize = 1,
            Format = Silk.NET.DXGI.Format.FormatR8G8B8A8UnormSrgb,
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)(BindFlag.RenderTarget | BindFlag.ShaderResource),
        };

        ComPtr<ID3D11Texture2D> texture = default;
        ComPtr<ID3D11RenderTargetView> target = default;
        ComPtr<ID3D11ShaderResourceView> view = default;

        SilkMarshal.ThrowHResult(_device.CreateTexture2D(in description, ref Unsafe.NullRef<SubresourceData>(), ref texture));
        SilkMarshal.ThrowHResult(_device.CreateRenderTargetView(texture, ref Unsafe.NullRef<RenderTargetViewDesc>(), ref target));
        SilkMarshal.ThrowHResult(_device.CreateShaderResourceView(texture, ref Unsafe.NullRef<ShaderResourceViewDesc>(), ref view));

        // **A target no view has drawn yet reads as opaque black with alpha one** — the engine's contents before
        // its first render are not in the SDK; alpha one is "deep", which lets the cheap pass cover it fully.
        float* black = stackalloc float[4] { 0f, 0f, 0f, 1f };

        ComPtr<ID3D11DeviceContext> immediate = default;

        _device.GetImmediateContext(ref immediate);
        immediate.ClearRenderTargetView(target, black);
        immediate.Dispose();

        return (texture, target, view);
    }
}
