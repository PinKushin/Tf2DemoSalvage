using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// Draws the map's detail sprites — the grass — from geometry rebuilt for each view (B361).
/// </summary>
/// <remarks>
/// **Its own pass, because the engine has its own system.** `CDetailObjectSystem` owns one
/// material, one mesh and one draw; detail sprites are not surfaces, not props and not entities,
/// and they are not in any leaf's face list. Routing them through the world's static buffer is what
/// the first version did, and it could not fade them or turn them.
///
/// **A dynamic buffer, because a detail sprite's geometry depends on the eye.** Its alpha comes
/// from the distance to the view (`cl_detaildist` / `cl_detailfade`) and two of the three
/// orientations have their angles recomputed from the view origin every frame
/// (`CDetailModel::ComputeAngles`, `detailobjectsystem.cpp:950`). Nothing about that can be baked.
///
/// **The material is `UnlitGeneric` with `$translucent`, `$nocull`, `$vertexcolor` and
/// `$vertexalpha`**, read from the shipped `detail/detailsprites.vmt`, and the shader below is that
/// material and nothing else: the texture multiplied by the vertex colour, with the alphas
/// multiplied together. `$AlphaTestReference 0.4` is declared beside a **commented-out**
/// `$AlphaTest`, so there is no cut-out — the sheet blends.
/// </remarks>
public sealed unsafe class DetailSpriteRenderer : IDisposable
{
    /// <summary>
    /// Position, texture coordinate, colour with alpha, and the frame being animated toward.
    /// </summary>
    private const int Floats = 12;

    /// <summary>How many bytes one corner takes, which the layout offsets must agree with.</summary>
    private const int VertexStride = sizeof(float) * Floats;

    /// <summary>Sixteen floats of camera, in the slot the other renderers use.</summary>
    private const int CameraConstants = 16;

    /// <summary>The constant buffer slot, matching <c>WorldRenderer</c>'s.</summary>
    private const uint CameraSlot = 4;

    /// <summary>
    /// **`row_major`, multiplied from the LEFT**, matching every other renderer here and
    /// `FreeCamera.ToMatrix`.
    /// </summary>
    private const string ShaderSource = """
        cbuffer Camera : register(b4)
        {
            row_major float4x4 viewProjection;
        };

        Texture2D sheet : register(t0);
        SamplerState linearWrap : register(s0);

        struct VsIn
        {
            float3 pos : POSITION;
            float2 uv : TEXCOORD0;
            float4 col : COLOR;
            float3 next : TEXCOORD1;
        };

        struct VsOut
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
            float4 col : COLOR;
            float3 next : TEXCOORD1;
            float3 refract : TEXCOORD2;
            float4 wpos : TEXCOORD3;
        };

        VsOut VsMain(VsIn input)
        {
            VsOut output;
            output.pos = mul(float4(input.pos, 1.0f), viewProjection);
            output.uv = input.uv;
            output.col = input.col;
            output.next = input.next;

            // Refract_vs20.fxc: "Map projected position to the refraction texture", y inverted, divided per pixel.
            output.refract = float3(
                (output.pos.x + output.pos.w) * 0.5f, (-output.pos.y + output.pos.w) * 0.5f, output.pos.w);
            output.wpos = float4(input.pos, output.pos.z);
            return output;
        }

        // refract_ps2x.fxc for the combos a sprite trail selects: no CUBEMAP, FADEOUTONSILHOUETTE, MASKED or
        // SECONDARY_NORMAL; BLUR 0 or 1; REFRACTTINTTEXTURE and COLORMODULATE either way (B476). The frame and the tint
        // texture are read through the sRGB curve and the result written through it, as EnableSRGBRead/Write ask.
        cbuffer Refract : register(b5)
        {
            float4 refractTintAmount;   // linear $refracttint, $refractamount
            float4 refractFlags;        // BLUR, COLORMODULATE, REFRACTTINTTEXTURE, PIXELFOGTYPE on
            float4 refractFogColour;    // FogConstants: linear colour, w the fog type (0 off, 1 range, 2 radial)
            float4 refractFogParams;    // FogConstants: start/range, -, max density, 1/range
            float4 refractEye;
        };

        Texture2D normalMap : register(t1);
        Texture2D frameCopy : register(t2);
        Texture2D tintMap : register(t3);
        SamplerState clampLinear : register(s1);

        static const float g_BlurFraction = 1.0f / 512.0f;
        static const float g_HalfBlurFraction = 0.5f * g_BlurFraction;

        float4 PsRefract(VsOut input) : SV_TARGET
        {
            // DecompressNormal( NORMAL_DECODE_NONE ).
            float4 vNormal = normalMap.Sample(linearWrap, input.uv);
            vNormal.xyz = vNormal.xyz * 2.0f - 1.0f;

            float3 refractTintColor = refractFlags.z > 0.5f
                ? 2.0f * refractTintAmount.rgb * tintMap.Sample(linearWrap, input.uv).rgb
                : refractTintAmount.rgb;

            float4 colorModulate = refractFlags.y > 0.5f ? input.col : float4(1.0f, 1.0f, 1.0f, 1.0f);
            refractTintColor *= colorModulate.rgb;

            float ooW = 1.0f / input.refract.z;
            float2 vRefractTexCoordNoWarp = input.refract.xy * ooW;
            float2 vRefractTexCoord = vNormal.xy;
            float scale = vNormal.a * refractTintAmount.w * colorModulate.a;
            vRefractTexCoord *= scale;
            vRefractTexCoord += vRefractTexCoordNoWarp;

            // FADEOUTONSILHOUETTE is 0, so blend is 1.
            float3 result;

            if (refractFlags.x > 0.5f)
            {
                // "use polyphase magic to convert 9 lookups into 4"
                float2 upper_2x2_loc = vRefractTexCoord - float2(g_HalfBlurFraction, g_HalfBlurFraction);
                float2 right_1x2_loc = vRefractTexCoord + float2(g_BlurFraction, -g_HalfBlurFraction);
                float2 lower_2x1_loc = vRefractTexCoord + float2(-g_HalfBlurFraction, g_BlurFraction);
                float2 singleton_loc = vRefractTexCoord + float2(g_BlurFraction, g_BlurFraction);
                result  = frameCopy.Sample(clampLinear, upper_2x2_loc).rgb * 0.4444444f;
                result += frameCopy.Sample(clampLinear, right_1x2_loc).rgb * 0.2222222f;
                result += frameCopy.Sample(clampLinear, lower_2x1_loc).rgb * 0.2222222f;
                result += frameCopy.Sample(clampLinear, singleton_loc).rgb * 0.1111111f;

                float3 unblurredColor = frameCopy.Sample(clampLinear, vRefractTexCoordNoWarp).rgb;
                result = lerp(unblurredColor, result * refractTintColor, 1.0f);
            }
            else
            {
                float3 colorWarp = frameCopy.Sample(clampLinear, vRefractTexCoord).rgb;
                float3 colorNoWarp = frameCopy.Sample(clampLinear, vRefractTexCoordNoWarp).rgb;
                colorWarp *= refractTintColor;
                result = lerp(colorNoWarp, colorWarp, 1.0f);
            }

            // FinalOutput( …, TONEMAP_SCALE_NONE ) with CalcPixelFogFactor: the range or radial fog the world's PixelFog
            // runs, toward the fog colour by the squared factor. Interpolated: the fog colour is unscaled, as
            // TONEMAP_SCALE_NONE scales nothing else.
            if (refractFlags.w > 0.5f && refractFogColour.w > 0.5f)
            {
                // Refract_vs20.fxc's worldPos_projPosZ: the projected z, carried from the vertex shader (the camera
                // buffer is bound to the vertex stage only).
                float projZ = refractFogColour.w > 1.5f
                    ? distance(refractEye.xyz, input.wpos.xyz)
                    : input.wpos.w;
                float fogFactor = saturate(min(refractFogParams.z, (projZ * refractFogParams.w) - refractFogParams.x));
                result = lerp(result, refractFogColour.rgb, fogFactor * fogFactor);
            }

            return float4(result, colorModulate.a * vNormal.a);
        }

        float4 PsMain(VsOut input) : SV_TARGET
        {
            float4 first = sheet.Sample(linearWrap, input.uv);
            float4 second = sheet.Sample(linearWrap, input.next.xy);
            float4 texel = lerp(first, second, input.next.z);
            return float4(texel.rgb * input.col.rgb, texel.a * input.col.a);
        }
        """;

    private ComPtr<ID3D11VertexShader> _vertexShader;
    private ComPtr<ID3D11PixelShader> _pixelShader;
    private ComPtr<ID3D11InputLayout> _layout;
    private ComPtr<ID3D11Buffer> _vertices;
    private ComPtr<ID3D11Buffer> _camera;
    private ComPtr<ID3D11SamplerState> _sampler;
    private ComPtr<ID3D11BlendState> _blend;
    private ComPtr<ID3D11BlendState> _additiveBlend;
    private ComPtr<ID3D11BlendState> _addOverBlend;
    private ComPtr<ID3D11BlendState> _inverseAlphaAddBlend;

    /// <summary>Which blend this pass draws with.</summary>
    private SpriteBlend _mode = SpriteBlend.Translucent;

    /// <summary>Which depth state this pass draws with.</summary>
    private SpriteDepth _depth = SpriteDepth.TestNoWrite;
    private ComPtr<ID3D11DepthStencilState> _testNoWrite;
    private ComPtr<ID3D11DepthStencilState> _testAndWrite;
    private ComPtr<ID3D11DepthStencilState> _depthOff;
    private ComPtr<ID3D11RasterizerState> _noCull;

    private ComPtr<ID3D11ShaderResourceView> _sheet;

    /// <summary>How many corners the buffer can hold, so it is grown rather than remade.</summary>
    private int _capacity;

    /// <summary>How many corners the last upload put in it.</summary>
    private int _corners;

    /// <summary>One blend state, since the three differ only in two fields.</summary>
    private static ComPtr<ID3D11BlendState> MakeBlend(
        ComPtr<ID3D11Device> device, Blend source, Blend destination)
    {
        BlendDesc description = default;

        description.RenderTarget[0].BlendEnable = 1;
        description.RenderTarget[0].SrcBlend = source;
        description.RenderTarget[0].DestBlend = destination;
        description.RenderTarget[0].BlendOp = BlendOp.Add;
        description.RenderTarget[0].SrcBlendAlpha = Blend.One;
        description.RenderTarget[0].DestBlendAlpha = Blend.InvSrcAlpha;
        description.RenderTarget[0].BlendOpAlpha = BlendOp.Add;
        description.RenderTarget[0].RenderTargetWriteMask = (byte)ColorWriteEnable.All;

        ComPtr<ID3D11BlendState> blend = default;
        SilkMarshal.ThrowHResult(device.CreateBlendState(in description, ref blend));

        return blend;
    }

    /// <summary>One depth state; the three differ in whether the test runs and whether depth is written.</summary>
    private static ComPtr<ID3D11DepthStencilState> MakeDepth(
        ComPtr<ID3D11Device> device, bool enable, DepthWriteMask write)
    {
        DepthStencilDesc description = new()
        {
            DepthEnable = enable,
            DepthWriteMask = write,
            DepthFunc = ComparisonFunc.Less,
        };

        ComPtr<ID3D11DepthStencilState> depth = default;
        SilkMarshal.ThrowHResult(device.CreateDepthStencilState(in description, ref depth));

        return depth;
    }

    private DetailSpriteRenderer(
        ComPtr<ID3D11VertexShader> vertexShader,
        ComPtr<ID3D11PixelShader> pixelShader,
        ComPtr<ID3D11PixelShader> refractShader,
        ComPtr<ID3D11InputLayout> layout)
    {
        _vertexShader = vertexShader;
        _pixelShader = pixelShader;
        _refractShader = refractShader;
        _layout = layout;
    }

    private ComPtr<ID3D11PixelShader> _refractShader;
    private ComPtr<ID3D11Buffer> _refractConstants;
    private ComPtr<ID3D11SamplerState> _clampSampler;
    private ComPtr<ID3D11ShaderResourceView> _refractNormal;
    private ComPtr<ID3D11ShaderResourceView> _refractFrame;
    private ComPtr<ID3D11ShaderResourceView> _refractTint;
    private Tf2DemoSalvage.Scene.RefractMaterial? _refract;

    /// <summary>
    /// Draws the next batches through the <c>Refract</c> shader instead of the sheet — or, with null, the sheet again
    /// (B476).
    /// </summary>
    /// <param name="refract">The material's parameters, or null.</param>
    /// <param name="normal">Its normal map, uploaded raw.</param>
    /// <param name="frame">The copy of the frame it warps, <c>_rt_PowerOfTwoFB</c>.</param>
    /// <param name="tint">Its tint texture, uploaded through the sRGB curve; a null handle when it has none.</param>
    /// <param name="fog">The fog in force, or null for none.</param>
    /// <param name="eye">Where the camera is, for radial fog.</param>
    public void SetRefract(
        Tf2DemoSalvage.Scene.RefractMaterial? refract,
        ComPtr<ID3D11ShaderResourceView> normal = default,
        ComPtr<ID3D11ShaderResourceView> frame = default,
        ComPtr<ID3D11ShaderResourceView> tint = default,
        Tf2DemoSalvage.Core.Scene.SceneFog? fog = null,
        System.Numerics.Vector3 eye = default)
    {
        _refract = refract;
        _refractNormal = normal;
        _refractFrame = frame;
        _refractTint = tint;
        _refractFog = FogConstants.For(fog);
        _refractEye = eye;
    }

    private float[] _refractFog = new float[8];
    private System.Numerics.Vector3 _refractEye;

    /// <summary>Binds the refract pass's inputs: <c>c1</c> linear, <c>c5.x</c>, and the three combos as flags.</summary>
    private void BindRefract(ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, Tf2DemoSalvage.Scene.RefractMaterial refract)
    {
        if (_refractConstants.Handle is null)
        {
            BufferDesc description = new()
            {
                ByteWidth = 20 * sizeof(float),
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.ConstantBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
            };

            SilkMarshal.ThrowHResult(device.CreateBuffer(in description, ref Unsafe.NullRef<SubresourceData>(), ref _refractConstants));

            // _rt_PowerOfTwoFB is created clamped in s and t (texture flags 0xc, engine.dll 0x1800f69cb).
            SamplerDesc clamp = new()
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                ComparisonFunc = ComparisonFunc.Never,
                MaxLOD = float.MaxValue,
            };

            SilkMarshal.ThrowHResult(device.CreateSamplerState(in clamp, ref _clampSampler));
        }

        MappedSubresource mapped = default;

        SilkMarshal.ThrowHResult(context.Map(_refractConstants, 0, Map.WriteDiscard, 0, ref mapped));

        float* into = (float*)mapped.PData;

        // SetPixelShaderConstantGammaToLinear( 1, REFRACTTINT ) (refract_dx9_helper.cpp:282).
        into[0] = WorldRenderer.Linear(refract.RefractTint.Red);
        into[1] = WorldRenderer.Linear(refract.RefractTint.Green);
        into[2] = WorldRenderer.Linear(refract.RefractTint.Blue);
        into[3] = refract.RefractAmount;
        into[4] = refract.BlurAmount;
        into[5] = refract.VertexColorModulate ? 1f : 0f;
        into[6] = _refractTint.Handle is not null ? 1f : 0f;
        into[7] = refract.Fogged ? 1f : 0f;

        for (int at = 0; at < 8; at++)
        {
            into[8 + at] = _refractFog[at];
        }

        into[16] = _refractEye.X;
        into[17] = _refractEye.Y;
        into[18] = _refractEye.Z;
        into[19] = 0f;

        context.Unmap(_refractConstants, 0);

        context.PSSetShader(_refractShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetConstantBuffers(5, 1, ref _refractConstants);
        context.PSSetSamplers(1, 1, ref _clampSampler);
        context.PSSetShaderResources(1, 1, ref _refractNormal);
        context.PSSetShaderResources(2, 1, ref _refractFrame);
        context.PSSetShaderResources(3, 1, ref _refractTint);
    }

    /// <summary>Compiles the shaders and builds the input layout.</summary>
    /// <param name="device">The device to create against.</param>
    /// <returns>A renderer ready to be given geometry.</returns>
    public static DetailSpriteRenderer Create(ComPtr<ID3D11Device> device)
    {
        using D3DCompiler compiler = D3DCompiler.GetApi();

        ComPtr<ID3D10Blob> vertexBytecode = Compile(compiler, "VsMain", "vs_5_0");
        ComPtr<ID3D10Blob> pixelBytecode = Compile(compiler, "PsMain", "ps_5_0");
        ComPtr<ID3D10Blob> refractBytecode = Compile(compiler, "PsRefract", "ps_5_0");

        ComPtr<ID3D11PixelShader> refractShader = default;
        SilkMarshal.ThrowHResult(device.CreatePixelShader(
            refractBytecode.GetBufferPointer(),
            refractBytecode.GetBufferSize(),
            ref Unsafe.NullRef<ID3D11ClassLinkage>(),
            ref refractShader));
        refractBytecode.Dispose();

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
        byte* texture = (byte*)SilkMarshal.StringToPtr("TEXCOORD");
        byte* colour = (byte*)SilkMarshal.StringToPtr("COLOR");

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
                SemanticName = texture,
                Format = Silk.NET.DXGI.Format.FormatR32G32Float,
                AlignedByteOffset = sizeof(float) * 3,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = colour,
                Format = Silk.NET.DXGI.Format.FormatR32G32B32A32Float,
                AlignedByteOffset = sizeof(float) * 5,
                InputSlotClass = InputClassification.PerVertexData,
            },

            // **`SemanticIndex = 1` is what makes this `TEXCOORD1` rather than a second
            // `TEXCOORD0`.** It defaults to zero, and two elements claiming the same semantic and
            // index is a layout the runtime refuses — which shows up as a failed `CreateInputLayout`
            // and nothing drawn, not as a wrong picture.
            new()
            {
                SemanticName = texture,
                SemanticIndex = 1,
                Format = Silk.NET.DXGI.Format.FormatR32G32B32Float,
                AlignedByteOffset = sizeof(float) * 9,
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
        SilkMarshal.Free((nint)texture);
        SilkMarshal.Free((nint)colour);

        vertexBytecode.Dispose();
        pixelBytecode.Dispose();

        return new DetailSpriteRenderer(vertexShader, pixelShader, refractShader, layout);
    }

    /// <summary>The one sheet every detail sprite is drawn from.</summary>
    /// <param name="sheet">Its texture, or a null handle for a map with no detail props.</param>
    /// <remarks>
    /// **One material for all of them**, because the engine has exactly one:
    /// <c>#define DETAIL_SPRITE_MATERIAL "detail/detailsprites"</c>
    /// (<c>detailobjectsystem.cpp:44</c>). The dictionary's entries are sub-rectangles of it.
    /// </remarks>
    public void SetSheet(ComPtr<ID3D11ShaderResourceView> sheet) => _sheet = sheet;

    /// <summary>Which blend this pass draws with — the material's, not a fixed one.</summary>
    /// <param name="mode">The blend the material selects.</param>
    /// <remarks>
    /// **Read from published source**, `spritecard.cpp:255-270`, and it is a three-way choice rather
    /// than a flag:
    ///
    /// <code>
    /// if ( bAdditive2ndTexture || bAddOverBlend || bAddSelf )
    ///     EnableAlphaBlending( SHADER_BLEND_ONE, SHADER_BLEND_ONE_MINUS_SRC_ALPHA );
    /// else if ( IS_FLAG_SET(MATERIAL_VAR_ADDITIVE) )
    ///     EnableAlphaBlending( SHADER_BLEND_SRC_ALPHA, SHADER_BLEND_ONE );
    /// else
    ///     EnableAlphaBlending( SHADER_BLEND_SRC_ALPHA, SHADER_BLEND_ONE_MINUS_SRC_ALPHA );
    /// </code>
    ///
    /// **It is not a rare case: 304 of TF2's 697 `SpriteCard` materials set `$additive`**, measured
    /// with `particles materials`. Drawing all of them translucent makes every spark, glow and muzzle
    /// flash in the game darker than the engine draws it and lets them occlude what is behind them
    /// instead of adding to it.
    ///
    /// **A detail sprite never calls this and keeps the translucent default**, which is what
    /// `detailsprites` is: the pass is shared, the material's choice is not.
    /// </remarks>
    public void SetBlend(SpriteBlend mode) => _mode = mode;

    /// <summary>Which depth state this pass draws with — an entity sprite's render mode's (B391).</summary>
    /// <param name="depth">The depth state; particles and detail sprites keep the default, tested and unwritten.</param>
    public void SetDepth(SpriteDepth depth) => _depth = depth;

    /// <summary>Whether there is anything to draw.</summary>
    public bool HasSprites => _sheet.Handle is not null && _corners > 0;

    /// <summary>Uploads one view's quads, replacing the last view's.</summary>
    /// <param name="device">The device to create the buffer on.</param>
    /// <param name="context">The device context.</param>
    /// <param name="corners">The quads, farthest first — three corners per triangle.</param>
    /// <exception cref="ArgumentNullException"><paramref name="corners"/> is null.</exception>
    /// <remarks>
    /// **The order is load-bearing and is not re-established here.** These blend against whatever
    /// is already in the frame buffer, so they must arrive sorted back to front;
    /// <see cref="DetailSprites.Build"/> does that and this writes them in the order it is given.
    ///
    /// **The buffer grows and is never shrunk.** A view looking across a field holds far more
    /// sprites than one facing a wall, and a buffer resized on every camera move would create and
    /// destroy a megabyte of GPU memory several times a second.
    /// </remarks>
    public void Upload(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        IReadOnlyList<DetailSpriteVertex> corners)
    {
        ArgumentNullException.ThrowIfNull(corners);

        _corners = corners.Count;

        if (_corners == 0)
        {
            return;
        }

        EnsureBuffers(device, _corners);

        MappedSubresource mapped = default;

        SilkMarshal.ThrowHResult(
            context.Map(_vertices, 0, Map.WriteDiscard, 0, ref mapped));

        float* into = (float*)mapped.PData;

        for (int at = 0; at < _corners; at++)
        {
            DetailSpriteVertex corner = corners[at];

            into[0] = corner.X;
            into[1] = corner.Y;
            into[2] = corner.Z;
            into[3] = corner.U;
            into[4] = corner.V;
            into[5] = corner.Red;
            into[6] = corner.Green;
            into[7] = corner.Blue;
            into[8] = corner.Alpha;
            into[9] = corner.NextU;
            into[10] = corner.NextV;
            into[11] = corner.Blend;

            into += Floats;
        }

        context.Unmap(_vertices, 0);
    }

    /// <summary>Draws the uploaded quads.</summary>
    /// <param name="device">The device.</param>
    /// <param name="context">The device context.</param>
    /// <param name="viewProjection">The camera, row major, sixteen floats.</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewProjection"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="viewProjection"/> is not sixteen floats.</exception>
    /// <remarks>
    /// **Every piece of state this pass needs is set here rather than inherited.** That rule is
    /// written in this project's own comments twice over — a translucent pass once left a depth
    /// state on the model pass (B72) and a decal pass once left alpha blending on the props (B135)
    /// — and a pass that inherits is a pass that breaks when its neighbour is reordered.
    ///
    /// **Depth tested, never written**, which is what `$translucent` means in Valve's own shaders:
    /// `cable_dx9.cpp:55` sets `EnableDepthWrites( false )` and `EnableBlending( true )` inside one
    /// `IS_FLAG_SET( MATERIAL_VAR_TRANSLUCENT )`. Writing depth would make each blade of grass
    /// occlude the ones behind it in the buffer order rather than blending with them.
    ///
    /// **No culling, from `$nocull 1` in the shipped material.** A sprite is a flat quad seen from
    /// both sides, and half of them face away from any given eye.
    /// </remarks>
    public void Draw(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        float[] viewProjection) =>
        Draw(device, context, viewProjection, 0, _corners);

    /// <summary>Draws a run of the uploaded corners — one leaf's sprites, or part of them (B434).</summary>
    /// <param name="device">The device.</param>
    /// <param name="context">The device context.</param>
    /// <param name="viewProjection">The camera, row major, sixteen floats.</param>
    /// <param name="firstCorner">The first corner to draw.</param>
    /// <param name="cornerCount">How many; clamped to what was uploaded.</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewProjection"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="viewProjection"/> is not sixteen floats.</exception>
    public void Draw(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        float[] viewProjection,
        int firstCorner,
        int cornerCount)
    {
        ArgumentNullException.ThrowIfNull(viewProjection);

        cornerCount = Math.Min(cornerCount, _corners - firstCorner);

        if (viewProjection.Length != CameraConstants)
        {
            throw new ArgumentException(
                "A camera matrix is sixteen floats.", nameof(viewProjection));
        }

        if (!HasSprites || firstCorner < 0 || cornerCount <= 0)
        {
            return;
        }

        UploadCamera(device, context, viewProjection);

        uint stride = VertexStride;
        uint offset = 0;

        float[] factor = [1f, 1f, 1f, 1f];

        context.IASetInputLayout(_layout);
        context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context.IASetVertexBuffers(0, 1, ref _vertices, in stride, in offset);
        context.VSSetShader(_vertexShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.VSSetConstantBuffers(CameraSlot, 1, ref _camera);
        context.PSSetShader(_pixelShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetSamplers(0, 1, ref _sampler);
        context.PSSetShaderResources(0, 1, ref _sheet);

        if (_refract is { } refract)
        {
            BindRefract(device, context, refract);
        }
        context.OMSetBlendState(
            _mode switch
            {
                SpriteBlend.Additive => _additiveBlend,
                SpriteBlend.AddOver => _addOverBlend,
                SpriteBlend.InverseAlphaAdd => _inverseAlphaAddBlend,

                // **`kRenderNormal` enables no blending at all** (`sprite_dx9.cpp:229-241`, B391).
                SpriteBlend.Opaque => default,
                _ => _blend,
            },
            factor,
            0xFFFFFFFF);
        context.OMSetDepthStencilState(
            _depth switch
            {
                SpriteDepth.TestAndWrite => _testAndWrite,
                SpriteDepth.Off => _depthOff,
                _ => _testNoWrite,
            },
            0);
        context.RSSetState(_noCull);

        context.Draw((uint)cornerCount, (uint)firstCorner);

        if (_refract is not null)
        {
            // The frame copy is drawn into again before the next refracting draw; a bound input cannot be an output.
            ComPtr<ID3D11ShaderResourceView> none = default;

            context.PSSetShaderResources(2, 1, ref none);
        }

        // **The depth state goes back to what every pass before B391 left**, for the same reason the
        // blend is turned off below: a pass that follows may inherit it, and a glow's depth-off state
        // inherited by the next pass would draw it through the world.
        context.OMSetDepthStencilState(_testNoWrite, 0);

        // **Blending is turned back OFF here, and this is not tidiness.** The model pass that
        // follows binds its own shaders, layout and camera through `BindPipeline` — but it does
        // NOT bind a blend state, so it inherits whatever the last pass left. Leaving alpha
        // blending on makes every model blend against its base texture's alpha channel, which in a
        // TF2 model material is usually an envmap mask rather than opacity: shiny metal masks to
        // near zero, so pipes ghost, a dome goes glassy and a silo's collar vanishes.
        //
        // That is B135 exactly, and it is written up in `DrawOpaqueBatches` as a defect that took
        // the owner looking at it to find, because it reads as four unrelated art faults. This pass
        // is newer than that note and re-created it within the hour.
        context.OMSetBlendState(default(ComPtr<ID3D11BlendState>), factor, 0xFFFFFFFF);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _vertices.Dispose();
        _camera.Dispose();
        _sampler.Dispose();
        _blend.Dispose();
        _additiveBlend.Dispose();
        _addOverBlend.Dispose();
        _inverseAlphaAddBlend.Dispose();
        _testNoWrite.Dispose();
        _testAndWrite.Dispose();
        _depthOff.Dispose();
        _noCull.Dispose();
        _layout.Dispose();
        _refractConstants.Dispose();
        _clampSampler.Dispose();
        _refractShader.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();

        GC.SuppressFinalize(this);
    }

    private void EnsureBuffers(ComPtr<ID3D11Device> device, int corners)
    {
        if (_capacity < corners)
        {
            _vertices.Dispose();
            _vertices = default;

            // **Doubled rather than fitted, so a camera panning across a field does not reallocate
            // every frame.** The sprite count changes continuously as the view moves, and a buffer
            // sized exactly to each view would be created and destroyed sixty times a second.
            _capacity = Math.Max(corners, _capacity * 2);

            BufferDesc description = new()
            {
                ByteWidth = (uint)(_capacity * VertexStride),
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.VertexBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
            };

            ComPtr<ID3D11Buffer> buffer = default;
            SilkMarshal.ThrowHResult(device.CreateBuffer(
                in description, ref Unsafe.NullRef<SubresourceData>(), ref buffer));

            _vertices = buffer;
        }

        if (_camera.Handle is null)
        {
            BufferDesc description = new()
            {
                ByteWidth = CameraConstants * sizeof(float),
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.ConstantBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
            };

            ComPtr<ID3D11Buffer> buffer = default;
            SilkMarshal.ThrowHResult(device.CreateBuffer(
                in description, ref Unsafe.NullRef<SubresourceData>(), ref buffer));

            _camera = buffer;
        }

        if (_sampler.Handle is null)
        {
            // **Wrapped, which is the material system's default and therefore the sheet's.** Every
            // rectangle in the dictionary lies inside the sheet, so no sample reaches the edge in
            // practice; the mode matters only for what a coordinate outside [0, 1] would do, and
            // inventing a clamp here would be a departure with no reason behind it.
            SamplerDesc description = new()
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Wrap,
                AddressV = TextureAddressMode.Wrap,
                AddressW = TextureAddressMode.Wrap,
                ComparisonFunc = ComparisonFunc.Never,
                MaxLOD = float.MaxValue,
            };

            ComPtr<ID3D11SamplerState> sampler = default;
            SilkMarshal.ThrowHResult(device.CreateSamplerState(in description, ref sampler));

            _sampler = sampler;
        }

        if (_blend.Handle is null)
        {
            _blend = MakeBlend(device, Blend.SrcAlpha, Blend.InvSrcAlpha);
        }

        if (_additiveBlend.Handle is null)
        {
            _additiveBlend = MakeBlend(device, Blend.SrcAlpha, Blend.One);
        }

        if (_addOverBlend.Handle is null)
        {
            _addOverBlend = MakeBlend(device, Blend.One, Blend.InvSrcAlpha);
        }

        if (_inverseAlphaAddBlend.Handle is null)
        {
            _inverseAlphaAddBlend = MakeBlend(device, Blend.InvSrcAlpha, Blend.One);
        }

        if (_testNoWrite.Handle is null)
        {
            _testNoWrite = MakeDepth(device, enable: true, DepthWriteMask.Zero);
        }

        if (_testAndWrite.Handle is null)
        {
            _testAndWrite = MakeDepth(device, enable: true, DepthWriteMask.All);
        }

        if (_depthOff.Handle is null)
        {
            _depthOff = MakeDepth(device, enable: false, DepthWriteMask.Zero);
        }

        if (_noCull.Handle is null)
        {
            RasterizerDesc description = new()
            {
                FillMode = FillMode.Solid,
                CullMode = CullMode.None,
                DepthClipEnable = 1,
            };

            ComPtr<ID3D11RasterizerState> raster = default;
            SilkMarshal.ThrowHResult(device.CreateRasterizerState(in description, ref raster));

            _noCull = raster;
        }
    }

    private void UploadCamera(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        float[] viewProjection)
    {
        EnsureBuffers(device, 0);

        MappedSubresource mapped = default;

        SilkMarshal.ThrowHResult(context.Map(_camera, 0, Map.WriteDiscard, 0, ref mapped));

        fixed (float* first = viewProjection)
        {
            Unsafe.CopyBlock(mapped.PData, first, CameraConstants * sizeof(float));
        }

        context.Unmap(_camera, 0);
    }

    private static ComPtr<ID3D10Blob> Compile(
        D3DCompiler compiler, string entryPoint, string profile)
    {
        ComPtr<ID3D10Blob> bytecode = default;
        ComPtr<ID3D10Blob> errors = default;

        byte[] source = System.Text.Encoding.ASCII.GetBytes(ShaderSource);

        fixed (byte* first = source)
        {
            int result = compiler.Compile(
                first,
                (nuint)source.Length,
                (byte*)null,
                ref Unsafe.NullRef<D3DShaderMacro>(),
                ref Unsafe.NullRef<ID3DInclude>(),
                entryPoint,
                profile,
                0,
                0,
                ref bytecode,
                ref errors);

            if (result < 0)
            {
                string message = errors.Handle is not null
                    ? SilkMarshal.PtrToString((nint)errors.GetBufferPointer()) ?? "no detail"
                    : "no detail";

                errors.Dispose();

                throw new InvalidOperationException(
                    $"The detail sprite {entryPoint} shader did not compile: {message}");
            }
        }

        errors.Dispose();

        return bytecode;
    }
}
