using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Render;

/// <summary>One frame's water: what <see cref="WorldRenderer.DrawWaterViews"/> renders before the main view (B62).</summary>
/// <param name="Views">The engine's views in draw order, <see cref="WaterViews.Plan"/>.</param>
/// <param name="Material">The visible fog volume's material, whose fog the refraction draws under; −1 for none.</param>
/// <param name="WaterHeight">The volume's surface, <c>m_flWaterHeight</c>.</param>
/// <param name="Camera">The main view's camera matrix.</param>
/// <param name="ReflectedCamera">The same view mirrored by <see cref="WaterViews.Reflect"/>, for the reflection.</param>
/// <param name="Fog">The world's fog, which the reflection and the under-water refraction draw under.</param>
/// <param name="DrawSkybox">Draws the 2D skybox through a camera matrix — the reflection's DF_DRAWSKYBOX — or null.</param>
/// <param name="DrawEntities">
/// For a view with DF_DRAW_ENTITITES, its opaque renderables then its translucent pass, the translucent world interleaved by
/// leaf with its translucent renderables (<c>viewrender.cpp:5524-5538</c>, B711). True when it drew the translucent world;
/// false leaves it to the view.
/// </param>
internal sealed record WaterDraw(
    IReadOnlyList<WaterView> Views,
    int Material,
    float WaterHeight,
    float[] Camera,
    float[] ReflectedCamera,
    Core.Scene.SceneFog? Fog,
    Action<float[]>? DrawSkybox,
    Func<WaterView, bool>? DrawEntities = null)
{
    /// <summary>
    /// A view's pruned list as <c>m_pActualLeafIndex</c>, the places of its world list its translucent pass walks (B711);
    /// null, or a null answer, for every leaf in buffer order.
    /// </summary>
    public Func<WaterView, IReadOnlyList<int>?>? PrunedLeaves { get; init; }

    /// <summary>The volume's SURFACE material, which <c>SetFogVolumeState( ..., true )</c> takes its fog from; −1 means <see cref="Material"/>.</summary>
    public int SurfaceMaterial { get; init; } = -1;

    /// <summary>The frame's own target, which the under-water refraction renders into before it is copied out.</summary>
    public WaterFrameTarget? Frame { get; init; }

    /// <summary>
    /// The reflection's own world list (<see cref="ReflectionCull"/>, B711), which the reflection view draws in place of the
    /// main view's runs. Null draws every batch, as a map that cannot be culled does — never the main view's runs. Every
    /// other water view draws the main view's list.
    /// </summary>
    public ReflectionWorld? Reflection { get; init; }

    /// <summary>
    /// The monitor's water views (B711): every view but the reflection draws the monitor's world list, so its shadows; its
    /// reflection has no shadows cut for it, so draws none.
    /// </summary>
    public bool Monitor { get; init; }

    /// <summary>The surface material, resolved.</summary>
    public int Surface => SurfaceMaterial >= 0 ? SurfaceMaterial : Material;
}

/// <summary>What a Water face binds for one water target while a view draws (B663, <see cref="WorldRenderer.WaterSampleWhileDrawing"/>).</summary>
internal enum WaterViewSample
{
    /// <summary>The target itself.</summary>
    Live,

    /// <summary>The view's snapshot of the target being drawn into (D244's pick, not parity).</summary>
    Snapshot,

    /// <summary>Nothing: an unbound input, zeros (option (a)).</summary>
    Nothing,
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

            return float4(PixelFog(result.rgb, input.wpos, tintControl.z, tintControl.w), 1.0f);
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

            return float4(PixelFog(specularLighting, input.wpos, tintControl.z, tintControl.w), flAlpha);
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

    /// <summary>The HDR type the map runs under, which the <c>Water</c> shader and the reflection view branch on (B62).</summary>
    public HdrType HdrType { get; set; }

    /// <summary>Whether the map blooms, which <c>mat_hdr_level 1</c> keeps under no HDR (B537).</summary>
    public bool Bloom { get; set; }

    /// <summary>The tone-map scale every shader's output is multiplied by — <c>LINEAR_LIGHT_SCALE</c> (B62).</summary>
    public float LinearLightScale { get; set; } = 1f;

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

    /// <summary>The water target a view is drawing into right now, or <see cref="WaterViewTarget.BackBuffer"/> for none (B663).</summary>
    private WaterViewTarget _drawingInto = WaterViewTarget.BackBuffer;

    /// <summary>What the last <see cref="SetCamera"/> was told besides the matrix, so a water view keeps the main view's switches.</summary>
    private (bool Colours, bool Specular, Fullbright Fullbright, DebugModes Debug, bool Phong) _cameraSwitches = (false, true, Fullbright.Off, default, true);

    /// <summary>The view's cheap-water distances, which the <c>WaterLOD</c> proxy hands each water material it binds.</summary>
    public (float Start, float End) WaterLod { get; set; } = (0f, 0.1f);

    /// <summary><c>r_waterforceexpensive</c>, which the shader reads as well as the view (<c>water.cpp:526</c>).</summary>
    public bool ForceExpensiveWater { get; set; }

    /// <summary>How many water batches drew each pass this frame — reset by <see cref="DrawWaterViews"/>.</summary>
    public (int Expensive, int Cheap, int Plain) WaterDraws { get; private set; }

    /// <summary>
    /// materialsystem's HDR-enabled state, which only a map load changes: false before any map (B662). Kept apart from
    /// <see cref="HdrType"/>, which a caller may set for one draw without loading a map.
    /// </summary>
    private bool _hdrEnabled;

    private void UploadWaters(ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, MapAssets assets)
    {
        ReleaseWaters();

        // engine.dll Mod_LoadMapFlags (0x1800ffa10, disassembly): when the map's HDR-enabled state differs from
        // materialsystem's (+0x228), it sets it (+0x230) and shuts down and re-creates the well-known render targets
        // (FUN_1800f6920), cleared opaque black by CTexture::ReconstructTexture. An equal state keeps them and what they hold.
        bool hdrEnabled = assets.Hdr != HdrType.None;

        if (hdrEnabled != _hdrEnabled)
        {
            ReleaseWaterTargets();
            _hdrEnabled = hdrEnabled;
        }

        _waters = assets.Waters;
        HdrType = assets.Hdr;
        Bloom = assets.Bloom;

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
        DisposeMonitor();
        _waterShader.Dispose();
        _cheapWaterShader.Dispose();
        _waterBuffer.Dispose();
        ReleaseWaterTargets();

        // Anything left is a twin registered by an owner outside this renderer (the back buffer's, an offscreen target's):
        // forgotten, not released, since it is not ours.
        _gammaTwins.Clear();
        _gammaOutputOn.Dispose();
        _gammaOutputOff.Dispose();
        _powerOfTwoView.Dispose();
        _refractModelShader.Dispose();
        _refractModelConstants.Dispose();
        _cloakShader.Dispose();
        _cloakDepth.Dispose();
        ReleaseScreenOverlays();
        DisposeShadows();
        _powerOfTwoTarget.Dispose();
        _powerOfTwoTexture.Dispose();
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

        PackHeightFog(_waterView, heightFog, ShaderSrgb);
        WriteWater(context, null, -1);
    }

    /// <summary>
    /// <c>r_shader_srgb</c>, default 0 ("-1 = use hardware caps. 0 = use hardware srgb. 1 = use shader srgb", the ConVar
    /// at <c>shaderapidx9.dll 0x1800a16e0</c>). Only the height-fog colour reads it here (<see cref="FogConstants.HeightFogChannel"/>).
    /// </summary>
    public int ShaderSrgb { get; set; }

    /// <summary>
    /// **The volume's height fog, as R_SetFogVolumeState sets it** (engine.dll 0x1800e0cd0): SetFogZ, the colour, start and
    /// end, into the water view's constants 4-11. CalcWaterFogAlpha reads the fog z from g_FogParams.y and one over the
    /// range from .w, packed as the range fog's own registers are (FogConstants, B139). The colour, and in .w the grey of
    /// 128 a modulating material fogs toward (B637; shader fog mode 1 or 3 hands c29 the bytes 0x80, 0x180014200), take
    /// shaderapi's height-fog conversion: the sRGB decode at <c>r_shader_srgb 0</c>, <c>pow 2.2</c> at 1 (0x180025b1f).
    /// </summary>
    /// <param name="view">The water view's twelve floats; 4-11 are written, all zero without height fog.</param>
    /// <param name="heightFog">The volume's fog, or null.</param>
    /// <param name="shaderSrgb"><c>r_shader_srgb</c>.</param>
    internal static void PackHeightFog(Span<float> view, VolumeFog? heightFog, int shaderSrgb)
    {
        if (heightFog is not { HeightFog: true } fog)
        {
            view[4..12].Clear();

            return;
        }

        float range = fog.End - fog.Start;

        view[4] = 1f;
        view[5] = fog.FogZ;
        view[6] = 1f;
        view[7] = range != 0f ? 1f / range : 0f;
        view[8] = FogConstants.HeightFogChannel(fog.Color.Red, shaderSrgb);
        view[9] = FogConstants.HeightFogChannel(fog.Color.Green, shaderSrgb);
        view[10] = FogConstants.HeightFogChannel(fog.Color.Blue, shaderSrgb);
        view[11] = FogConstants.HeightFogChannelOfByte(ModulatingGreyByte, shaderSrgb);
    }

    /// <summary>The literal byte shader fog modes 1 and 3 hand c29 (<c>shaderapidx9.dll</c> <c>0x180014200</c>).</summary>
    private const int ModulatingGreyByte = 0x80;

    /// <summary>
    /// <c>CSimpleWorldView</c>'s own start (<c>viewrender.cpp:5705-5781</c>): its draw flags, and from inside a volume its
    /// colour clear to the volume's fog (<see cref="MainViewFogClear"/>). Called where the main view begins, after the 3D
    /// sky, which the clear covers.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="target">The main view's target, its sRGB view.</param>
    /// <param name="draws">The view's draw flags.</param>
    /// <param name="fogClear">The fog clear, or null for none.</param>
    public void BeginSimpleView(
        ComPtr<ID3D11DeviceContext> context, ComPtr<ID3D11RenderTargetView> target, ViewDraws draws,
        (float Red, float Green, float Blue)? fogClear)
    {
        ViewDraws = draws;

        if (fogClear is { } colour)
        {
            float* clear = stackalloc float[4] { colour.Red, colour.Green, colour.Blue, 1f };

            context.ClearRenderTargetView(target, clear);
        }
    }

    /// <summary>
    /// A material colour parameter as <c>CBaseVSShader::SetPixelShaderConstantGammaToLinear</c> converts it
    /// (<c>BaseVSShader.cpp:146-165</c>): <c>x &gt; 1 ? x : GammaToLinear( x )</c>, mathlib's byte table of
    /// <c>pow( i / 255, 2.2 )</c> with 1 from 0.95. Not shaderapi's fog upload, which is the sRGB decode (B653 review).
    /// </summary>
    internal static float Linear(float gamma) => ModelMaterialShading.ModulationToLinear(gamma);

    /// <summary><c>$reflecttint</c>'s conversion: mathlib's <c>GammaToLinear</c> under integer HDR (<c>water.cpp:300-306</c>), else <see cref="Linear"/>.</summary>
    internal static float ReflectTint(float gamma, bool integerHdr) =>
        integerHdr ? ModelMaterialShading.GammaToLinear(gamma) : Linear(gamma);

    /// <summary>
    /// c16, the reflect tint (<c>water.cpp:296-312</c>): under integer HDR water.cpp converts it itself, <c>GammaToLinear( x )
    /// · 4</c> with no pass-through above 1 (:300-306); otherwise <c>SetPixelShaderConstantGammaToLinear</c> (:311), which
    /// passes values above 1 through. Times the HDR type's reflect scale.
    /// </summary>
    /// <param name="tint"><c>$reflecttint</c>, gamma.</param>
    /// <param name="hdr">The map's HDR type.</param>
    /// <returns>The three channels c16 carries.</returns>
    internal static (float Red, float Green, float Blue) ReflectTintConstant((float Red, float Green, float Blue) tint, HdrType hdr)
    {
        bool integerHdr = hdr == HdrType.IntegerHdr;
        float scale = BspHdr.WaterScales(hdr).ReflectTint;

        return (ReflectTint(tint.Red, integerHdr) * scale, ReflectTint(tint.Green, integerHdr) * scale,
            ReflectTint(tint.Blue, integerHdr) * scale);
    }

    /// <summary>
    /// <c>LinearToGammaFullRange</c> as the client calls it (<c>client.dll</c> <c>0x18085bcf0</c>): <c>pow</c> in double,
    /// with the exponent the binary stores at <c>0x180b34560</c>, <c>1.0 / (double)2.2f</c>, cast back to float.
    /// </summary>
    private static readonly double LinearToGammaExponent = BitConverter.Int64BitsToDouble(0x3FDD1745C6E043B8);

    /// <summary>
    /// One channel of a water view's fog clear, as the linear value the sRGB view needs to store the engine's byte.
    /// </summary>
    /// <remarks>
    /// <c>GetFogColor</c>'s byte is <c>FogColor3fv</c>'s truncation (<see cref="FogConstants.StoredByte"/>). A scaled clear is
    /// <c>SetClearColorToFogColor</c> (<c>client.dll</c> <c>0x18034d560</c>, <c>viewrender.cpp:754-768</c>), only under
    /// <c>HDR_TYPE_INTEGER</c>: <c>(float)byte · LinearToGammaFullRange( scale )</c>, CVTTSS2SI, and the low byte stored, so a
    /// product above 255 WRAPS (200 at scale 4 stores 119). An unscaled one is <c>GetFogColor</c> straight into
    /// <c>ClearColor4ub</c>.
    ///
    /// **shaderapi stores the clear's bytes as they are** (<c>shaderapidx9.dll</c>): <c>ClearColor4ub</c> (<c>0x180014f90</c>)
    /// packs them with no conversion, and <c>ClearBuffers</c> (<c>0x180014bd0</c>) turns <c>D3DRS_SRGBWRITEENABLE</c> off
    /// around <c>Clear</c> when the current shadow state has it on, restoring it after (<c>0x180031db0</c>). So the decode
    /// here, through our sRGB view, stores exactly the byte; the B641 gamma twin reads the same texels.
    /// </remarks>
    /// <param name="gamma">The fog colour channel, gamma 0..1.</param>
    /// <param name="scale">The frame's tone-map scale, before any water view quartered it.</param>
    /// <param name="scaled">The clear is <see cref="ClearScalesWithToneMap"/>'s and the map is integer HDR.</param>
    /// <returns>The value to clear with.</returns>
    internal static float WaterClearColour(float gamma, float scale, bool scaled)
    {
        int stored = FogConstants.StoredByte(gamma);

        if (scaled)
        {
            float factor = (float)Math.Pow(scale, LinearToGammaExponent);

            stored = FogConstants.Cvttss2si(stored * factor) & 0xFF;
        }

        return FogConstants.SrgbDecode(stored / 255f);
    }

    /// <summary>
    /// Whether a view's fog clear is <c>SetClearColorToFogColor</c>'s, scaled: the above-water refraction view (<c>:6061</c>)
    /// and the intersection view (<c>:6097</c>), as the radial-vis main view (<c>:1423</c>, not modelled) would be. The
    /// eye-in-volume simple view (<c>:5780</c>), the under-water main view (<c>:6178</c>) and its refraction view
    /// (<c>:6228</c>) put <c>GetFogColor</c> into <c>ClearColor4ub</c> unscaled.
    /// </summary>
    /// <param name="kind">The view.</param>
    /// <returns>True for the scaled clears.</returns>
    internal static bool ClearScalesWithToneMap(WaterViewKind kind) =>
        kind is WaterViewKind.Refraction or WaterViewKind.Intersection;

    /// <summary>
    /// The main view's own fog clear: <c>CSimpleWorldView::DrawExecute</c> from inside a volume sets
    /// <c>VIEW_CLEAR_COLOR</c>, <c>SetFogVolumeState( m_fogInfo, false )</c>, then <c>GetFogColor</c> into
    /// <c>ClearColor4ub</c> unscaled (<c>viewrender.cpp:5772-5780</c>), after the 3D sky drew, which it covers. The
    /// under-water main view sets the clear colour too (<c>:6178</c>) but clears depth only (<c>:6134</c>), so nothing.
    /// </summary>
    /// <remarks>
    /// **A volume whose material has <c>$fogenable 0</c> is not cleared here, where Valve still clears.** The clear is
    /// unconditional (<c>:5772</c>), but <c>R_SetFogVolumeState</c> then sets <c>MATERIAL_FOG_NONE</c> and no colour
    /// (engine.dll <c>0x1800e0cd0</c>), so <c>GetFogColor</c> returns whatever byte the material system last stored: the
    /// previous <c>FogColor3fv</c> of this frame or an earlier one (the 3D sky's fog, the world's, another volume's). That
    /// depends on the call history, not on the frame, and is not modelled; the frame keeps its earlier clear (RISKS B655).
    /// </remarks>
    /// <param name="main">The frame's back-buffer view.</param>
    /// <param name="inside">The volume's fog as the view sets it (the bottom material's, not height fog).</param>
    /// <returns>The sRGB-view values that store the fog bytes, or null when the view clears no colour to fog.</returns>
    public static (float Red, float Green, float Blue)? MainViewFogClear(WaterView main, VolumeFog inside)
    {
        if (!main.ClearToFogColor || (main.Clear & ViewClears.Color) == 0)
        {
            return null;
        }

        return (WaterClearColour(inside.Color.Red, 1f, scaled: false), WaterClearColour(inside.Color.Green, 1f, scaled: false),
            WaterClearColour(inside.Color.Blue, 1f, scaled: false));
    }

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
            // colour, linear; c7 start, end − start and the reflect overbright. Under HDR_TYPE_INTEGER the reflect tint
            // AND the overbright are 4, undoing the reflection view's ×0.25 (water.cpp:296-312, :351-355) — so the
            // BLURRY_REFRACT path, which applies both, carries 16.
            float overbright = BspHdr.WaterScales(HdrType).Overbright;

            Put(c, 12, Linear(water.RefractTint.Red), Linear(water.RefractTint.Green), Linear(water.RefractTint.Blue), 1f);
            (float tintRed, float tintGreen, float tintBlue) = ReflectTintConstant(water.ReflectTint, HdrType);

            Put(c, 16, tintRed, tintGreen, tintBlue, 1f);
            Put(c, 20, water.ReflectAmount, water.ReflectAmount, water.RefractAmount, water.RefractAmount);
            Put(c, 24, Linear(water.FogColor.Red), Linear(water.FogColor.Green), Linear(water.FogColor.Blue), 1f);
            Put(c, 28, water.FogStart, water.FogEnd - water.FogStart, overbright, 0f);

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

        SubmitCounters.Upload(); SilkMarshal.ThrowHResult(context.Map(_waterBuffer, 0, Map.WriteDiscard, 0, ref mapped));

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

    /// <summary>The raster a water material draws with: back faces culled, as the <c>Water</c> shader leaves them (B657).</summary>
    /// <param name="material">The material.</param>
    /// <returns>The water cull, or both sides for a <c>$nocull</c> material; the wireframe twin under <c>mat_wireframe</c>.</returns>
    /// <remarks>
    /// The shader's shadow state turns culling off only in the editor (<c>water.cpp:393-397</c>, "In edit mode, use
    /// nocull"; that the default is on is read from that line and measured against TF2, shaderlib being unpublished). A
    /// volume's surface is two coplanar faces, the downward one with <c>$bottommaterial</c> (vbsp <c>faces.cpp:1255-1289</c>),
    /// and the world walk keeps both (a water-leaf surface skips the plane-side test, <c>VisibleWorld.Surfaces</c>), so this
    /// cull alone decides which one the eye sees. Without it 2fort's surface drew <c>water_2fort_beneath</c> from above,
    /// 20-50 levels grey-green over TF2's brown. A <c>$nocull</c> water material keeps both sides, as
    /// <c>MATERIAL_VAR_NOCULL</c> does for every shader (interpolated; TF2 ships no such water). Its own state, not the
    /// model cull, so <c>TF2VIEW_MODEL_CULL</c> leaves water alone.
    /// </remarks>
    private ComPtr<ID3D11RasterizerState> WaterRaster(int material) =>
        Raster(_noCull.Contains(material) ? _bothSides : _waterCull);

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

        if (!DrawWaterPasses(context, water, batch.MaterialIndex, batch.Category, batch.VertexCount, batch.FirstVertex))
        {
            // The opaque loop's draw takes the water raster and hands the two-sided one back after it (B657 review).
            return false;
        }

        // Hand the world's two-sided raster back to the next batch.
        context.RSSetState(Raster(_bothSides));

        return true;
    }

    /// <summary>
    /// **The one place the B663 choice lives:** what a Water face samples for one water target while a view draws.
    /// A target that is not being drawn into is sampled live. The target being drawn into is the engine's undefined case
    /// (a feedback bind, D3D9). NOT PARITY: the lead's pick (D244) is <see cref="WaterViewSample.Snapshot"/>. Option (a)
    /// would answer <see cref="WaterViewSample.Nothing"/> here.
    /// </summary>
    /// <param name="target">The water target the slot binds.</param>
    /// <param name="drawingInto">The water target being drawn into, or <see cref="WaterViewTarget.BackBuffer"/> for none.</param>
    /// <returns>What to bind for that slot.</returns>
    internal static WaterViewSample WaterSampleWhileDrawing(WaterViewTarget target, WaterViewTarget drawingInto) =>
        target == drawingInto ? WaterViewSample.Snapshot : WaterViewSample.Live;

    /// <summary>What one water slot binds, as <see cref="WaterSampleWhileDrawing"/> decides it.</summary>
    /// <param name="context">The context.</param>
    /// <param name="sample">The decision.</param>
    /// <param name="live">The target's own view.</param>
    /// <returns>The view to bind.</returns>
    private ComPtr<ID3D11ShaderResourceView> WaterSlot(
        ComPtr<ID3D11DeviceContext> context, WaterViewSample sample, ComPtr<ID3D11ShaderResourceView> live) => sample switch
    {
        WaterViewSample.Live => live,
        WaterViewSample.Snapshot => ViewSnapshot(context),
        _ => default,
    };

    /// <summary>
    /// The snapshot a Water face inside a water view samples in place of the target being drawn into (B663): copied
    /// once per view, at the view's first water bind that reads a target, so a view with no such face copies nothing.
    /// NOT PARITY (D244, RISKS B663).
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>The snapshot's view; nothing when no water target is being drawn into.</returns>
    private ComPtr<ID3D11ShaderResourceView> ViewSnapshot(ComPtr<ID3D11DeviceContext> context)
    {
        ComPtr<ID3D11Texture2D> source = _drawingInto switch
        {
            WaterViewTarget.Refraction => _refractionTexture,
            WaterViewTarget.Reflection => _reflectionTexture,
            _ => default,
        };

        if (source.Handle is null)
        {
            return default;
        }

        if (_viewSnapshotTexture.Handle is null)
        {
            CreateViewSnapshot();
        }

        if (!_viewSnapshotTaken)
        {
            context.CopyResource(_viewSnapshotTexture, source);
            _viewSnapshotTaken = true;
            ViewSnapshots++;
        }

        return _viewSnapshotView;
    }

    /// <summary>The snapshot texture: a copy destination and a shader input only, the water targets' size and format.</summary>
    private void CreateViewSnapshot()
    {
        Texture2DDesc description = new()
        {
            Width = WaterTargetSize,
            Height = WaterTargetSize,
            MipLevels = 1,
            ArraySize = 1,
            Format = Silk.NET.DXGI.Format.FormatR8G8B8A8Typeless,
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.ShaderResource,
        };

        ShaderResourceViewDesc view = new()
        {
            Format = Silk.NET.DXGI.Format.FormatR8G8B8A8UnormSrgb,
            ViewDimension = D3DSrvDimension.D3DSrvDimensionTexture2D,
        };

        view.Texture2D.MipLevels = 1;

        SilkMarshal.ThrowHResult(_device.CreateTexture2D(in description, ref Unsafe.NullRef<SubresourceData>(), ref _viewSnapshotTexture));
        SilkMarshal.ThrowHResult(_device.CreateShaderResourceView(_viewSnapshotTexture, in view, ref _viewSnapshotView));

        // A new texture holds nothing yet: this view's copy is still to take.
        _viewSnapshotTaken = false;
    }

    private ComPtr<ID3D11Texture2D> _viewSnapshotTexture;
    private ComPtr<ID3D11ShaderResourceView> _viewSnapshotView;

    /// <summary>Whether this view's snapshot is taken; reset as each water view starts.</summary>
    private bool _viewSnapshotTaken;

    /// <summary>How many view snapshots were copied since the renderer started, for a test.</summary>
    internal int ViewSnapshots { get; private set; }

    /// <summary>The water target a view's draws actually land in, as <see cref="DrawWaterViews"/> chose its output (B663).</summary>
    /// <param name="reflection">The view is the reflection.</param>
    /// <param name="viaFrame">The view draws into the frame and is copied out after.</param>
    /// <returns>Reflection, the frame (no water target), or the refraction target.</returns>
    internal static WaterViewTarget DrawnInto(bool reflection, bool viaFrame)
    {
        if (reflection)
        {
            return WaterViewTarget.Reflection;
        }

        return viaFrame ? WaterViewTarget.BackBuffer : WaterViewTarget.Refraction;
    }

    /// <summary>
    /// The <c>Water</c> shader's <c>SHADER_DRAW</c> on one run (<c>water.cpp:534-584</c>), for the world's water surface
    /// and a brush entity's water face alike: the shader has no input saying which it is drawing (B659).
    /// </summary>
    /// <param name="context">The context, with the run's vertices bound.</param>
    /// <param name="water">The material's water parameters.</param>
    /// <param name="material">The material index.</param>
    /// <param name="category">The run's category.</param>
    /// <param name="vertexCount">Vertices in the run.</param>
    /// <param name="firstVertex">Where it starts.</param>
    /// <returns>
    /// True when a pass drew, the pixel shader and the view's water constants handed back; false for the Plain fallback,
    /// with only the water raster set, for the caller's own untextured draw (Valve's bare <c>Draw()</c>).
    /// </returns>
    private bool DrawWaterPasses(
        ComPtr<ID3D11DeviceContext> context, MapWater water, int material, SurfaceCategory category, int vertexCount,
        int firstVertex)
    {
        (bool reflection, bool refraction, bool forceCheap) = Combos(water);
        ComPtr<ID3D11ShaderResourceView> cube = material < _cubemaps.Count ? _cubemaps[material] : default;

        WaterPass passes = WaterShader.Pass(
            refraction, water.View.ReflectTexture, cube.Handle is not null, forceCheap,
            !forceCheap && (ForceExpensiveWater || water.View.ForceExpensive), isDecal: false);

        (int expensive, int cheap, int plain) = WaterDraws;

        if (passes == WaterPass.Plain)
        {
            // Valve's bare Draw() still runs under the Water shader's shadow state, culling included (water.cpp:579-584).
            WaterDraws = (expensive, cheap, plain + 1);
            context.RSSetState(WaterRaster(material));
            return false;
        }

        SetMaterial(context, material, category);
        WriteWater(context, water, material);

        // BindTexture( NORMALMAP, BUMPFRAME ), BUMPFRAME being the AnimatedTexture proxy's output.
        ComPtr<ID3D11ShaderResourceView> normal =
            _waterNormals.TryGetValue(material, out List<ComPtr<ID3D11ShaderResourceView>>? frames)
                ? frames[water.NormalFrameAt(Seconds, frames.Count)]
                : _flatWhite;

        // **A target no view has written still exists, cleared opaque black** (B662): materialsystem creates a render
        // target through CTexture::ReconstructTexture and clears it with ClearColor4ub( 0, 0, 0, 255 ) (materialsystem.dll
        // 0x18003cd60, "%s - Clearing"), so the shader samples alpha 1, a full fog, never an unbound zero.
        EnsureWaterTargets();

        // **The target a water view is drawing into is sampled through a snapshot** (B663): a brush entity's water face
        // inside the refraction or reflection view binds its own render target in the engine (shaderapidx9 BindTexture
        // 0x1800149b0 to SetTextureState 0x180023840, no guard), which D3D9 leaves undefined. NOT PARITY: an interpolation,
        // the lead's pick of option (b) in RISKS B663. The live target, copied at this view's first water bind, without
        // the in-flight writes. Alternatives kept there: (a) zeros, (c) last frame's copy, (d) skip the face.
        // Only the expensive pass reads the targets, and only those its combos bind (water.cpp:270-278): a cheap-only face
        // is forceCheap or reads neither (no REFRACTALPHA without bBlend, :407-411), so it binds nothing and copies nothing.
        bool expensivePass = (passes & WaterPass.ReflectionRefraction) != 0;
        ComPtr<ID3D11ShaderResourceView> refract = expensivePass && refraction
            ? WaterSlot(context, WaterSampleWhileDrawing(WaterViewTarget.Refraction, _drawingInto), _refractionView)
            : default;
        ComPtr<ID3D11ShaderResourceView> reflect = expensivePass && reflection
            ? WaterSlot(context, WaterSampleWhileDrawing(WaterViewTarget.Reflection, _drawingInto), _reflectionView)
            : default;

        context.PSSetShaderResources(4, 1, ref normal);
        context.PSSetShaderResources(5, 1, ref cube);
        context.PSSetShaderResources(12, 1, ref refract);
        context.PSSetShaderResources(13, 1, ref reflect);

        context.RSSetState(WaterRaster(material));

        // Neither DrawReflectionRefraction's shadow state (water.cpp:182-266) nor the cheap pass's without bBlend
        // (:399-402) enables blending, and a material's shadow state is set whole on bind: both passes start opaque,
        // whatever the previous run left (a model's additive batch included). The blended cheap pass sets its own below.
        ResetBlend(context);

        if ((passes & WaterPass.ReflectionRefraction) != 0)
        {
            context.PSSetShader(_waterShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
            SubmitCounters.Draw(); context.Draw((uint)vertexCount, (uint)firstVertex);
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
            SubmitCounters.Draw(); context.Draw((uint)vertexCount, (uint)firstVertex);
            ResetBlend(context);
            cheap++;
        }

        WaterDraws = (expensive, cheap, plain);

        // Hand the shader and the view's constants back to the next run.
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

            EnsureWaterDepth();

            // **The under-water refraction renders into the FRAME and is copied out** (viewrender.cpp:6203-6248):
            // "Refraction renders into the back buffer ... It is then blitted out into the refraction target."
            bool reflection = target == WaterViewTarget.Reflection;
            bool viaFrame = target == WaterViewTarget.BackBufferCopiedToRefraction && frame.Frame is not null;
            ComPtr<ID3D11RenderTargetView> into = reflection ? _reflectionTarget : _refractionTarget;

            if (viaFrame)
            {
                into = frame.Frame!.Value.Target;
            }

            // **Under HDR_TYPE_INTEGER the reflection view draws at a quarter of the tone-map scale** (PushView's
            // SetLightmapScaleForWater, viewrender.cpp:5351 and :2726-2736), straight into the 8-bit target through every
            // shader's FinalOutput, so light up to 4 survives it; PopView puts the scale back (:5399-5403).
            float savedScale = LinearLightScale;

            if (reflection)
            {
                LinearLightScale = savedScale * BspHdr.WaterScales(HdrType).ReflectionViewScale;
            }
            ComPtr<ID3D11DepthStencilView> depth = viaFrame ? frame.Frame!.Value.Depth : _waterDepth;
            Viewport viewport = viaFrame
                ? new(0f, 0f, frame.Frame!.Value.Width, frame.Frame.Value.Height, 0f, 1f)
                : new(0f, 0f, WaterTargetSize, WaterTargetSize, 0f, 1f);

            // **Unbound before it is drawn into** — a texture cannot be a shader input and an output at once.
            ComPtr<ID3D11ShaderResourceView> none = default;

            context.PSSetShaderResources(12, 1, ref none);
            context.PSSetShaderResources(13, 1, ref none);
            context.OMSetRenderTarget(into, depth);
            context.RSSetViewports(1, in viewport);

            // VIEW_CLEAR_COLOR to the fog colour where the view sets it — SetFogVolumeState( fogInfo, true ) then
            // GetFogColor, so the SURFACE material's (engine.dll 0x1800e0cd0) — else black.
            colour[0] = 0f;
            colour[1] = 0f;
            colour[2] = 0f;
            colour[3] = 1f;

            if (view.ClearToFogColor && WaterViews.VolumeFogFor(Water(frame.Surface), frame.WaterHeight, true) is { } clearFog)
            {
                // The colour is taken before PushView quarters a reflection view's scale (SetLightmapScaleForWater runs in
                // DrawExecute's PushView, after SetClearColorToFogColor), so from the frame's scale; and only the
                // SetClearColorToFogColor views scale it, only under HDR_TYPE_INTEGER (WaterClearColour).
                bool scaled = HdrType == HdrType.IntegerHdr && ClearScalesWithToneMap(view.Kind);

                colour[0] = WaterClearColour(clearFog.Color.Red, savedScale, scaled);
                colour[1] = WaterClearColour(clearFog.Color.Green, savedScale, scaled);
                colour[2] = WaterClearColour(clearFog.Color.Blue, savedScale, scaled);
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

            // The target this view draws into, which a water face inside it must not sample (B663). Through the frame,
            // the refraction target is not the output, so it stays bound: last frame's copy, as the engine's would be.
            _drawingInto = DrawnInto(reflection, viaFrame);
            _viewSnapshotTaken = false;

            try
            {
                ExecuteView(context, frame, view);
            }
            finally
            {
                _drawingInto = WaterViewTarget.BackBuffer;
            }

            if (viaFrame)
            {
                StretchFrameToRefraction(context, frame.Frame!.Value);
            }

            LinearLightScale = savedScale;
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
    /// <remarks>
    /// **The reflection draws its own world list** (<see cref="WaterDraw.Reflection"/>, B711) — its runs, its blended
    /// leaves, its overlays and its shadow decals — and puts the main view's back after; every other water view's list is
    /// the main view's (<see cref="ReflectionCull"/>).
    /// </remarks>
    /// <summary>The shadow decals a water view draws: those cut on the world list it draws (B702, B711).</summary>
    /// <param name="monitor">Whether it is one of the monitor's water views.</param>
    /// <param name="reflection">Whether it is the reflection, which draws its own cull's list.</param>
    /// <returns>The list: each reflection its own cull's, every other water view its camera's.</returns>
    internal static ShadowList WaterViewShadows(bool monitor, bool reflection)
    {
        if (reflection)
        {
            return monitor ? ShadowList.MonitorReflection : ShadowList.Reflection;
        }

        return monitor ? ShadowList.Monitor : ShadowList.World;
    }

    private void ExecuteView(ComPtr<ID3D11DeviceContext> context, WaterDraw frame, WaterView view)
    {
        SetWaterView(
            context, view.Clip,
            view.Fog == WaterViewFog.VolumeHeight ? WaterViews.VolumeFogFor(Water(frame.Surface), frame.WaterHeight, true) : null);

        ViewDraws drawn = ViewDraws;
        bool own = view.Kind == WaterViewKind.Reflection;
        (IReadOnlyList<WorldBatch>? Batches, TranslucentLeafRuns? Translucent, IReadOnlyList<WorldBatch>? Overlays) main =
            (VisibleBatches, TranslucentLeaves, Overlays);
        ShadowList shadows = _shadowWorldList;

        _shadowWorldList = WaterViewShadows(frame.Monitor, own);

        if (own)
        {
            (VisibleBatches, TranslucentLeaves, Overlays) = frame.Reflection is { } list
                ? (list.Batches, list.Translucent, list.Overlays)
                : (null, null, null);
        }

        try
        {
            ViewDraws = view.Draw;
            Draw(context);

            // DF_DRAW_ENTITITES: DrawOpaqueRenderables, DrawTranslucentRenderables (with the translucent world); without
            // it, DrawTranslucentWorldInLeaves (viewrender.cpp:5524-5553), both over the view's pruned list (B711).
            bool drewTranslucent = (view.Draw & ViewDraws.DrawEntities) != 0 && frame.DrawEntities?.Invoke(view) == true;

            if (!drewTranslucent)
            {
                WriteWaterFogToAlpha(context, opaque: false);

                if (TranslucentLeaves is not null && frame.PrunedLeaves?.Invoke(view) is { } pruned)
                {
                    DrawTranslucentWorldInLeaves(context, pruned);
                }
                else
                {
                    DrawTranslucentWorld(context);
                }

                WriteWaterFogToAlpha(context, opaque: true);
            }
        }
        finally
        {
            ViewDraws = drawn;
            _shadowWorldList = shadows;

            if (own)
            {
                (VisibleBatches, TranslucentLeaves, Overlays) = main;
            }
        }
    }

    /// <summary>
    /// <c>DrawTranslucentWorldInLeaves</c> (<c>viewrender.cpp:4278-4292</c>): the translucent world of a pruned list's leaves,
    /// back to front, each by its <c>m_pActualLeafIndex</c> into the installed world list (B711).
    /// </summary>
    /// <param name="context">Context to issue the draws on.</param>
    /// <param name="actual">The pruned list's places in the world list, nearest first.</param>
    internal void DrawTranslucentWorldInLeaves(ComPtr<ID3D11DeviceContext> context, IReadOnlyList<int> actual)
    {
        for (int at = actual.Count; --at >= 0;)
        {
            DrawTranslucentLeaf(context, actual[at]);
        }
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
        EnsureWaterTargets();

        // A water face inside the view may have left the refraction bound as an input; it is the output now.
        ComPtr<ID3D11ShaderResourceView> none = default;

        context.PSSetShaderResources(12, 1, ref none);
        context.PSSetShaderResources(13, 1, ref none);
        StretchFrameInto(context, frame, _refractionTarget);
    }

    /// <summary>
    /// <c>UpdateRefractTexture()</c> (`view_scene.h:41-74`): the whole frame, stretched into <c>_rt_PowerOfTwoFB</c>, then
    /// the frame bound again — what a <c>Refract</c> material warps (B476).
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="frame">The frame being drawn.</param>
    /// <returns>The copy, to bind as the refract shader's frame.</returns>
    /// <remarks>
    /// **Its own target, not the water's**: <c>_rt_PowerOfTwoFB</c> is created beside the water targets, 1024 square
    /// (engine.dll <c>0x1800f69b3</c>-<c>0x1800f69ef</c>: <c>CreateNamedRenderTargetTextureEx2( "_rt_PowerOfTwoFB",
    /// 0x400, 0x400, 1, … )</c>). On the PC every call copies — <c>if ( IsPC() || … )</c> — so a second refracting
    /// renderable sees the first.
    /// </remarks>
    internal ComPtr<ID3D11ShaderResourceView> UpdateRefractTexture(ComPtr<ID3D11DeviceContext> context, WaterFrameTarget frame)
    {
        if (_powerOfTwoTarget.Handle is null)
        {
            (_powerOfTwoTexture, _powerOfTwoTarget, _powerOfTwoView) = WaterTarget();
        }

        StretchFrameInto(context, frame, _powerOfTwoTarget);

        return _powerOfTwoView;
    }

    /// <summary>The copy a model's refracting batch warps, of whatever target the model is being drawn into (B506).</summary>
    private ComPtr<ID3D11ShaderResourceView> _refractModelFrame;

    /// <summary>The viewport the model was drawn under, so the refract shader can place each pixel in the frame.</summary>
    private Viewport _refractModelViewport;

    /// <summary>
    /// <c>UpdateRefractTexture()</c> for a model batch: the bound target, copied, then bound again with its viewport (B506).
    /// </summary>
    /// <param name="context">The context.</param>
    private void CopyFrameForRefract(ComPtr<ID3D11DeviceContext> context)
    {
        ComPtr<ID3D11RenderTargetView> target = default;
        ComPtr<ID3D11DepthStencilView> depth = default;
        ComPtr<ID3D11Resource> resource = default;
        ComPtr<ID3D11Texture2D> texture = default;
        uint viewports = 1;
        Viewport viewport = default;

        context.RSGetViewports(ref viewports, ref viewport);
        context.OMGetRenderTargets(1u, ref target, ref depth);
        target.GetResource(ref resource);
        SilkMarshal.ThrowHResult(resource.QueryInterface(out texture));

        Texture2DDesc description = default;

        texture.GetDesc(ref description);
        _refractModelFrame = UpdateRefractTexture(
            context, new WaterFrameTarget(target, depth, (int)description.Width, (int)description.Height));
        context.RSSetViewports(1, in viewport);
        _refractModelViewport = viewport;

        texture.Dispose();
        resource.Dispose();
        target.Dispose();
        depth.Dispose();
    }

    /// <summary>Binds <c>PsRefractModel</c> and its inputs over the batch's ordinary material binds (B506).</summary>
    /// <param name="context">The context.</param>
    /// <param name="refract">The material, its normal map and its tint texture.</param>
    private void BindRefractModel(
        ComPtr<ID3D11DeviceContext> context,
        (RefractMaterial Material, ComPtr<ID3D11ShaderResourceView> Normal, ComPtr<ID3D11ShaderResourceView> Tint) refract)
    {
        if (_refractModelShader.Handle is null)
        {
            using D3DCompiler compiler = D3DCompiler.GetApi();

            _refractModelShader = PixelShader(_device, compiler, "PsRefractModel");
        }

        EnsureRefractModelConstants();

        MappedSubresource mapped = default;

        SubmitCounters.Upload(); SilkMarshal.ThrowHResult(context.Map(_refractModelConstants, 0, Map.WriteDiscard, 0, ref mapped));

        float* into = (float*)mapped.PData;
        RefractMaterial material = refract.Material;

        // SetPixelShaderConstantGammaToLinear( 1, REFRACTTINT ), c5.x $refractamount (refract_dx9_helper.cpp:282-291).
        into[0] = Linear(material.RefractTint.Red);
        into[1] = Linear(material.RefractTint.Green);
        into[2] = Linear(material.RefractTint.Blue);
        into[3] = material.RefractAmount;
        into[4] = material.BlurAmount;
        into[5] = refract.Tint.Handle is not null ? 1f : 0f;
        into[6] = material.Fogged ? 1f : 0f;
        into[7] = 0f;
        into[8] = _refractModelViewport.Width;
        into[9] = _refractModelViewport.Height;
        into[10] = _refractModelViewport.TopLeftX;
        into[11] = _refractModelViewport.TopLeftY;

        context.Unmap(_refractModelConstants, 0);

        ComPtr<ID3D11ShaderResourceView> normal = refract.Normal;
        ComPtr<ID3D11ShaderResourceView> tint = refract.Tint;

        context.PSSetShader(_refractModelShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetConstantBuffers(5, 1, ref _refractModelConstants);
        context.PSSetShaderResources(12, 1, ref normal);
        context.PSSetShaderResources(13, 1, ref _refractModelFrame);
        context.PSSetShaderResources(14, 1, ref tint);
    }

    /// <summary>The constant buffer the model refract and cloak shaders share (register b5).</summary>
    private void EnsureRefractModelConstants()
    {
        if (_refractModelConstants.Handle is not null)
        {
            return;
        }

        BufferDesc buffer = new()
        {
            ByteWidth = 12 * sizeof(float),
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.ConstantBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write,
        };

        SilkMarshal.ThrowHResult(_device.CreateBuffer(in buffer, null, ref _refractModelConstants));
    }

    /// <summary><c>DrawCloakBlendedPass</c> for one batch, drawn again over its standard pass.</summary>
    /// <param name="context">The context.</param>
    /// <param name="pass">The material's cloak pass.</param>
    /// <param name="bound">The <c>$cloakfactor</c> and <c>$cloakColorTint</c> the proxies left.</param>
    /// <param name="bumped">The <c>BUMPMAP</c> combo: the material's <c>$bumpmap</c> is a texture (bound at t4).</param>
    /// <param name="batch">The batch.</param>
    /// <remarks>
    /// **Its own state, as the helper resets it** ("since we're drawing from two materials"): blended
    /// <c>SRC_ALPHA, ONE_MINUS_SRC_ALPHA</c>, depth tested and written. The frame is the copy made before the
    /// renderable, bound as <c>TEXTURE_FRAME_BUFFER_FULL_TEXTURE_0</c> — which <c>UpdateRefractTexture</c>'s
    /// <c>SetFrameBufferCopyTexture( pTexture )</c> points at the power-of-two copy (`view_scene.h:62`).
    /// </remarks>
    private void DrawCloakPass(
        ComPtr<ID3D11DeviceContext> context,
        CloakPass pass,
        (float Factor, (float Red, float Green, float Blue) Tint) bound,
        bool bumped,
        WorldBatch batch)
    {
        if (_cloakShader.Handle is null)
        {
            using D3DCompiler compiler = D3DCompiler.GetApi();

            _cloakShader = PixelShader(_device, compiler, "PsCloakModel");

            DepthStencilDesc depth = default;

            depth.DepthEnable = 1;
            depth.DepthWriteMask = DepthWriteMask.All;
            depth.DepthFunc = ComparisonFunc.LessEqual;

            SilkMarshal.ThrowHResult(_device.CreateDepthStencilState(in depth, ref _cloakDepth));
        }

        EnsureRefractModelConstants();

        MappedSubresource mapped = default;

        SubmitCounters.Upload(); SilkMarshal.ThrowHResult(context.Map(_refractModelConstants, 0, Map.WriteDiscard, 0, ref mapped));

        float* into = (float*)mapped.PData;

        // c7 $cloakcolortint and c6.y $refractamount, both as written; c6.x $cloakfactor.
        into[0] = bound.Tint.Red;
        into[1] = bound.Tint.Green;
        into[2] = bound.Tint.Blue;
        into[3] = pass.RefractAmount;
        into[4] = bound.Factor;
        into[5] = bumped ? 1f : 0f;
        into[6] = 0f;
        into[7] = 0f;
        into[8] = _refractModelViewport.Width;
        into[9] = _refractModelViewport.Height;
        into[10] = _refractModelViewport.TopLeftX;
        into[11] = _refractModelViewport.TopLeftY;

        context.Unmap(_refractModelConstants, 0);

        ComPtr<ID3D11DepthStencilState> depthBefore = default;
        uint stencilBefore = 0;

        context.OMGetDepthStencilState(ref depthBefore, ref stencilBefore);

        float* blendFactor = stackalloc float[4] { 1f, 1f, 1f, 1f };

        context.OMSetBlendState(_alphaBlend, blendFactor, 0xFFFFFFFF);
        context.OMSetDepthStencilState(_cloakDepth, 0);
        context.PSSetShader(_cloakShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetConstantBuffers(5, 1, ref _refractModelConstants);
        context.PSSetShaderResources(13, 1, ref _refractModelFrame);

        // The B597 cap, as the standard pass takes it: inside the model pass, so a cloaked model is capped too.
        if (!ModelVertexClamp.Suppresses(ModelVertexCap))
        {
            SubmitCounters.Draw(); context.Draw((uint)ModelVertexClamp.Apply(batch.VertexCount, ModelVertexCap), (uint)batch.FirstVertex);
        }

        // A bound input cannot be an output, and the next batch draws with the material's own state and shader.
        ComPtr<ID3D11ShaderResourceView> none = default;

        context.PSSetShaderResources(13, 1, ref none);
        context.PSSetShader(_pixelShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.OMSetDepthStencilState(depthBefore, stencilBefore);
        depthBefore.Dispose();
    }

    private ComPtr<ID3D11Texture2D> _powerOfTwoTexture;
    private ComPtr<ID3D11RenderTargetView> _powerOfTwoTarget;
    private ComPtr<ID3D11ShaderResourceView> _powerOfTwoView;

    private void StretchFrameInto(
        ComPtr<ID3D11DeviceContext> context, WaterFrameTarget frame, ComPtr<ID3D11RenderTargetView> into)
    {
        ComPtr<ID3D11Resource> source = default;

        frame.Target.GetResource(ref source);

        RenderTargetViewDesc viewDescription = default;

        frame.Target.GetDesc(ref viewDescription);
        EnsureFrameCopy(frame.Width, frame.Height, viewDescription.Format);
        context.CopyResource(_frameCopy, source);
        source.Dispose();

        Blit(context, _frameCopyView, into);

        Viewport frameViewport = new(0f, 0f, frame.Width, frame.Height, 0f, 1f);

        context.OMSetRenderTarget(frame.Target, frame.Depth);
        context.RSSetViewports(1, in frameViewport);
    }

    /// <summary>Draws <paramref name="source"/> over a whole water target — <c>CopyRenderTargetToTextureEx</c>'s stretch.</summary>
    /// <remarks>Leaves the world's pipeline bound and <paramref name="target"/> as the output; the caller rebinds its own.</remarks>
    private void Blit(
        ComPtr<ID3D11DeviceContext> context, ComPtr<ID3D11ShaderResourceView> source, ComPtr<ID3D11RenderTargetView> target)
    {
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

        context.OMSetRenderTarget(target, default);
        context.RSSetViewports(1, in viewport);
        context.IASetInputLayout(default(ComPtr<ID3D11InputLayout>));
        context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context.VSSetShader(_blitVertex, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShader(_blitPixel, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShaderResources(0, 1, ref source);
        context.PSSetSamplers(0, 1, ref _clampSampler);
        ResetBlend(context);
        SubmitCounters.Draw(); context.Draw(3, 0);

        // The world's pipeline back: its shaders and samplers.
        ComPtr<ID3D11ShaderResourceView> none = default;

        context.PSSetShaderResources(0, 1, ref none);
        BindPipeline(context);
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

    /// <summary>
    /// The reflection and refraction targets and their depth gone, each target's gamma twin released and unmapped with it
    /// (B641 review); <see cref="EnsureWaterTargets"/> makes them afresh on the next ask.
    /// </summary>
    internal void ReleaseWaterTargets()
    {
        ReleaseOwnedGammaTwin(_reflectionTarget);
        ReleaseOwnedGammaTwin(_refractionTarget);
        _reflectionView.Dispose();
        _reflectionView = default;
        _reflectionTarget.Dispose();
        _reflectionTarget = default;
        _reflectionTexture.Dispose();
        _reflectionTexture = default;
        _refractionView.Dispose();
        _refractionView = default;
        _refractionTarget.Dispose();
        _refractionTarget = default;
        _refractionTexture.Dispose();
        _refractionTexture = default;
        _waterDepth.Dispose();
        _waterDepth = default;
        _waterDepthTexture.Dispose();
        _waterDepthTexture = default;
        _viewSnapshotView.Dispose();
        _viewSnapshotView = default;
        _viewSnapshotTexture.Dispose();
        _viewSnapshotTexture = default;
        _viewSnapshotTaken = false;
    }

    /// <summary>The two colour targets, the engine's own, created cleared on first ask (B662).</summary>
    private void EnsureWaterTargets()
    {
        if (_reflectionTarget.Handle is not null)
        {
            return;
        }

        (_reflectionTexture, _reflectionTarget, _reflectionView) = WaterTarget();
        (_refractionTexture, _refractionTarget, _refractionView) = WaterTarget();
    }

    /// <summary>The depth the water views draw with, which only a view needs, so a cheap-only frame never makes it.</summary>
    private void EnsureWaterDepth()
    {
        EnsureWaterTargets();

        if (_waterDepth.Handle is not null)
        {
            return;
        }

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
            // Typeless, viewed through the sRGB curve as before, so a modulating run can draw through a UNORM twin (B641).
            Format = Silk.NET.DXGI.Format.FormatR8G8B8A8Typeless,
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)(BindFlag.RenderTarget | BindFlag.ShaderResource),
        };

        ComPtr<ID3D11Texture2D> texture = default;
        ComPtr<ID3D11RenderTargetView> target = default;
        ComPtr<ID3D11RenderTargetView> twin = default;
        ComPtr<ID3D11ShaderResourceView> view = default;

        RenderTargetViewDesc srgbTarget = new()
        {
            Format = Silk.NET.DXGI.Format.FormatR8G8B8A8UnormSrgb,
            ViewDimension = RtvDimension.Texture2D,
        };

        RenderTargetViewDesc gammaTarget = srgbTarget with { Format = Silk.NET.DXGI.Format.FormatR8G8B8A8Unorm };
        ShaderResourceViewDesc srgbView = new()
        {
            Format = Silk.NET.DXGI.Format.FormatR8G8B8A8UnormSrgb,
            ViewDimension = D3DSrvDimension.D3DSrvDimensionTexture2D,
        };

        srgbView.Texture2D.MipLevels = 1;

        SilkMarshal.ThrowHResult(_device.CreateTexture2D(in description, ref Unsafe.NullRef<SubresourceData>(), ref texture));
        SilkMarshal.ThrowHResult(_device.CreateRenderTargetView(texture, in srgbTarget, ref target));
        SilkMarshal.ThrowHResult(_device.CreateRenderTargetView(texture, in gammaTarget, ref twin));
        SilkMarshal.ThrowHResult(_device.CreateShaderResourceView(texture, in srgbView, ref view));

        _ownedGammaTwins.Add(twin);
        SetGammaTwin(target, twin);

        // **A target no view has drawn yet reads as opaque black with alpha one**, as the engine clears every new colour
        // render target: CTexture::ReconstructTexture's ClearColor4ub( 0, 0, 0, 255 ) and ClearBuffers (materialsystem.dll
        // 0x18003cd60, "%s - Clearing"; disassembly, B662). Cleared on the immediate context, which is the only one: this
        // renderer creates no deferred context, so a call mid-draw lands in order with the draw that asked for it.
        float* black = stackalloc float[4] { 0f, 0f, 0f, 1f };

        ComPtr<ID3D11DeviceContext> immediate = default;

        _device.GetImmediateContext(ref immediate);
        immediate.ClearRenderTargetView(target, black);
        immediate.Dispose();

        return (texture, target, view);
    }
}
