using System;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary>TF2's bloom: <c>Generate8BitBloomTexture</c> and the post pass that adds it, over the finished 3D view (B514).</summary>
/// <remarks>
/// **Every step is Valve's** (`viewpostprocess.cpp:1522-1598`), read with sRGB off as each shader asks on Windows
/// (`EnableSRGBRead( false )`, `downsample_nohdr.cpp:61-63`, `BlurFilterY.cpp:56-58`), so the arithmetic is on GAMMA
/// values:
///
/// 1. <c>dev/downsample_non_hdr</c> to a quarter-size target: four taps, each <c>pow( c, 2.2 ) · dot( c, (0.3, 0.59,
///    0.11) )</c> — <c>r_bloomtint*</c> and <c>r_bloomtintexponent</c> (`Downsample_nohdr_ps2x.fxc:24-44`,
///    `downsample_nohdr.cpp:18-21, 97-112`), averaged. Bright pixels survive it; dark ones vanish.
/// 2. <c>dev/blurfilterx_nohdr</c>: thirteen taps at ±1.3366 … ±11.4401 texels with weights 0.2013, 0.2185 …
///    (`BlurFilter_ps2x.fxc:56-79`, `BlurFilterX.cpp:85-105`), scale 1.
/// 3. <c>dev/blurfiltery_nohdr</c>: the same taps vertically, times <c>$bloomamount</c> — **and its texel step is one
///    over the target's WIDTH** (`BlurFilterY.cpp:88-89`, <c>int height = src_texture->GetActualWidth()</c>), so the
///    vertical blur is narrower than the horizontal by the aspect ratio. Kept, because it is what TF2 draws.
/// 4. The add: the bloom target, bilinearly upsampled, added to the frame.
///
/// **Interpolated: the add.** It is <c>engine_post</c>'s, whose source is not published (only its combo header,
/// `include/engine_post_ps20b.inc`, which names <c>LINEAR_INPUT</c>/<c>LINEAR_OUTPUT</c>). This adds in gamma, as the
/// 2007 <c>bloomadd</c> shader did and as the gamma-space bloom target implies; the golden comparison is what checks it.
///
/// **Interpolated: the tap positions within a block.** The downsample's four taps sit 0.5 and 2.5 texels into each
/// 4×4 block; where the quad's half-texel offset lands them is D3D9 rasterisation this does not reproduce to the
/// fraction of a texel. The blur that follows spreads ±45 full-size pixels, so a sub-texel shift is not visible.
/// </remarks>
public sealed unsafe class BloomRenderer : IDisposable
{
    private const string ShaderSource = """
        cbuffer Bloom : register(b0)
        {
            float4 source;   // xy one over the source size, z the blur scale
            float4 step;     // xy one blur texel step in uv
        };

        Texture2D input : register(t0);
        SamplerState linearClamp : register(s0);

        struct VsOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

        VsOut VsFull(uint id : SV_VertexID)
        {
            VsOut o;
            float2 corner = float2((id << 1) & 2, id & 2);
            o.uv = corner;
            o.pos = float4(corner * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
            return o;
        }

        float3 Shape(float2 uv)
        {
            float3 c = input.Sample(linearClamp, uv).rgb;
            return pow(c, 2.2f) * dot(c, float3(0.3f, 0.59f, 0.11f));
        }

        float4 PsDownsample(VsOut i) : SV_TARGET
        {
            // The block's top-left texel corner, then Valve's taps at 0.5 and 2.5 texels — between texel centres.
            float2 corner = i.uv - 2.0f * source.xy;
            float3 sum = Shape(corner + float2(1.0f, 1.0f) * source.xy) + Shape(corner + float2(3.0f, 1.0f) * source.xy)
                       + Shape(corner + float2(1.0f, 3.0f) * source.xy) + Shape(corner + float2(3.0f, 3.0f) * source.xy);
            return float4(sum * 0.25f, 1.0f);
        }

        static const float Offsets[6] = { 1.3366f, 3.4295f, 5.4264f, 7.4359f, 9.4436f, 11.4401f };
        static const float Weights[6] = { 0.2185f, 0.0821f, 0.0461f, 0.0262f, 0.0162f, 0.0102f };

        float4 PsBlur(VsOut i) : SV_TARGET
        {
            float3 c = input.Sample(linearClamp, i.uv).rgb * 0.2013f;

            [unroll]
            for (int k = 0; k < 6; k++)
            {
                float2 o = step.xy * Offsets[k];
                c += (input.Sample(linearClamp, i.uv + o).rgb + input.Sample(linearClamp, i.uv - o).rgb) * Weights[k];
            }

            return float4(c * source.z, 1.0f);
        }

        float4 PsAdd(VsOut i) : SV_TARGET
        {
            return float4(input.Sample(linearClamp, i.uv).rgb, 0.0f);
        }
        """;

    private ComPtr<ID3D11VertexShader> _vertex;
    private ComPtr<ID3D11PixelShader> _downsample;
    private ComPtr<ID3D11PixelShader> _blur;
    private ComPtr<ID3D11PixelShader> _add;
    private ComPtr<ID3D11Buffer> _constants;
    private ComPtr<ID3D11SamplerState> _sampler;
    private ComPtr<ID3D11BlendState> _additive;
    private ComPtr<ID3D11DepthStencilState> _noDepth;
    private ComPtr<ID3D11RasterizerState> _noCull;

    private (int Width, int Height) _size;
    private ComPtr<ID3D11Texture2D> _frame;
    private ComPtr<ID3D11ShaderResourceView> _frameView;
    private readonly ComPtr<ID3D11Texture2D>[] _small = new ComPtr<ID3D11Texture2D>[2];
    private readonly ComPtr<ID3D11ShaderResourceView>[] _smallView = new ComPtr<ID3D11ShaderResourceView>[2];
    private readonly ComPtr<ID3D11RenderTargetView>[] _smallTarget = new ComPtr<ID3D11RenderTargetView>[2];

    private BloomRenderer()
    {
    }

    /// <summary>Compiles the passes and makes the fixed state.</summary>
    /// <param name="device">The device.</param>
    /// <returns>The renderer.</returns>
    public static BloomRenderer Create(ComPtr<ID3D11Device> device)
    {
        BloomRenderer? bloom = new();

        try
        {
            bloom.Build(device);
            BloomRenderer built = bloom;
            bloom = null;
            return built;
        }
        finally
        {
            bloom?.Dispose();
        }
    }

    private void Build(ComPtr<ID3D11Device> device)
    {
        BloomRenderer bloom = this;
        using D3DCompiler compiler = D3DCompiler.GetApi();

        ComPtr<ID3D10Blob> code = Compile(compiler, "VsFull", "vs_5_0");
        SilkMarshal.ThrowHResult(device.CreateVertexShader(
            code.GetBufferPointer(), code.GetBufferSize(), ref Unsafe.NullRef<ID3D11ClassLinkage>(), ref bloom._vertex));
        code.Dispose();

        bloom._downsample = Pixel(device, compiler, "PsDownsample");
        bloom._blur = Pixel(device, compiler, "PsBlur");
        bloom._add = Pixel(device, compiler, "PsAdd");

        BufferDesc buffer = new()
        {
            ByteWidth = 8 * sizeof(float),
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.ConstantBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write,
        };
        SilkMarshal.ThrowHResult(device.CreateBuffer(in buffer, null, ref bloom._constants));

        SamplerDesc sampler = new()
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            MaxLOD = float.MaxValue,
        };
        SilkMarshal.ThrowHResult(device.CreateSamplerState(in sampler, ref bloom._sampler));

        BlendDesc blend = default;
        blend.RenderTarget[0] = new RenderTargetBlendDesc
        {
            BlendEnable = 1,
            SrcBlend = Blend.One,
            DestBlend = Blend.One,
            BlendOp = BlendOp.Add,
            SrcBlendAlpha = Blend.Zero,
            DestBlendAlpha = Blend.One,
            BlendOpAlpha = BlendOp.Add,
            RenderTargetWriteMask = (byte)ColorWriteEnable.All,
        };
        SilkMarshal.ThrowHResult(device.CreateBlendState(in blend, ref bloom._additive));

        DepthStencilDesc depth = default;
        SilkMarshal.ThrowHResult(device.CreateDepthStencilState(in depth, ref bloom._noDepth));

        RasterizerDesc raster = new() { FillMode = FillMode.Solid, CullMode = CullMode.None, DepthClipEnable = 1 };
        SilkMarshal.ThrowHResult(device.CreateRasterizerState(in raster, ref bloom._noCull));
    }

    /// <summary>Blooms the back buffer in place: downsample, blur twice, add.</summary>
    /// <param name="device">The device.</param>
    /// <param name="context">The context; its render target and viewport are left for the caller to restore.</param>
    /// <param name="backBuffer">The frame, <c>B8G8R8A8_UNORM</c>.</param>
    /// <param name="gammaTarget">A NON-sRGB view of the frame, so the add happens on the stored gamma values.</param>
    /// <param name="width">The frame's width.</param>
    /// <param name="height">The frame's height.</param>
    /// <param name="amount"><c>GetBloomAmount()</c>, the vertical blur's <c>$bloomamount</c>.</param>
    public void Draw(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        ComPtr<ID3D11Texture2D> backBuffer,
        ComPtr<ID3D11RenderTargetView> gammaTarget,
        int width,
        int height,
        float amount)
    {
        if (amount <= 0f || width < 4 || height < 4)
        {
            return;
        }

        EnsureTargets(device, width, height);
        context.CopyResource(_frame, backBuffer);

        int smallWidth = width / 4;
        int smallHeight = height / 4;
        float* blendFactor = stackalloc float[4] { 1f, 1f, 1f, 1f };

        context.IASetInputLayout(default(ComPtr<ID3D11InputLayout>));
        context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context.VSSetShader(_vertex, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetConstantBuffers(0, 1, ref _constants);
        context.PSSetSamplers(0, 1, ref _sampler);
        context.OMSetDepthStencilState(_noDepth, 0);
        context.RSSetState(_noCull);
        context.OMSetBlendState(default(ComPtr<ID3D11BlendState>), blendFactor, 0xFFFFFFFF);

        Viewport small = new(0f, 0f, smallWidth, smallHeight, 0f, 1f);
        context.RSSetViewports(1, in small);

        Pass(context, _downsample, _frameView, _smallTarget[0], (1f / width, 1f / height, 0f), (0f, 0f));
        Pass(context, _blur, _smallView[0], _smallTarget[1], (0f, 0f, 1f), (1f / smallWidth, 0f));
        Pass(context, _blur, _smallView[1], _smallTarget[0], (0f, 0f, amount), (0f, 1f / smallWidth));

        Viewport full = new(0f, 0f, width, height, 0f, 1f);
        context.RSSetViewports(1, in full);
        context.OMSetBlendState(_additive, blendFactor, 0xFFFFFFFF);
        Pass(context, _add, _smallView[0], gammaTarget, (0f, 0f, 0f), (0f, 0f));

        ComPtr<ID3D11ShaderResourceView> none = default;
        context.PSSetShaderResources(0, 1, ref none);
        context.OMSetBlendState(default(ComPtr<ID3D11BlendState>), blendFactor, 0xFFFFFFFF);
    }

    private void Pass(
        ComPtr<ID3D11DeviceContext> context,
        ComPtr<ID3D11PixelShader> shader,
        ComPtr<ID3D11ShaderResourceView> input,
        ComPtr<ID3D11RenderTargetView> output,
        (float X, float Y, float Scale) source,
        (float X, float Y) step)
    {
        // Unbind the input before it can be an output of the next pass, then bind this pass's pair.
        ComPtr<ID3D11ShaderResourceView> none = default;
        context.PSSetShaderResources(0, 1, ref none);
        context.OMSetRenderTargets(1, ref output, default(ComPtr<ID3D11DepthStencilView>));

        MappedSubresource mapped = default;
        SilkMarshal.ThrowHResult(context.Map(_constants, 0, Map.WriteDiscard, 0, ref mapped));
        float* into = (float*)mapped.PData;
        (into[0], into[1], into[2], into[3]) = (source.X, source.Y, source.Scale, 0f);
        (into[4], into[5], into[6], into[7]) = (step.X, step.Y, 0f, 0f);
        context.Unmap(_constants, 0);

        context.PSSetShader(shader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShaderResources(0, 1, ref input);
        context.Draw(3, 0);
    }

    private void EnsureTargets(ComPtr<ID3D11Device> device, int width, int height)
    {
        if (_size == (width, height))
        {
            return;
        }

        ReleaseTargets();
        _size = (width, height);

        // The copy of the frame: the back buffer's own format, read WITHOUT the sRGB curve.
        Texture2DDesc frame = new()
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Silk.NET.DXGI.Format.FormatB8G8R8A8Unorm,
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.ShaderResource,
        };
        SilkMarshal.ThrowHResult(device.CreateTexture2D(in frame, null, ref _frame));
        SilkMarshal.ThrowHResult(device.CreateShaderResourceView(_frame, null, ref _frameView));

        // `_rt_SmallFB0/1`: a quarter of the frame each way, eight bits per channel.
        Texture2DDesc small = frame with
        {
            Width = (uint)(width / 4),
            Height = (uint)(height / 4),
            BindFlags = (uint)(BindFlag.ShaderResource | BindFlag.RenderTarget),
        };

        for (int i = 0; i < 2; i++)
        {
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in small, null, ref _small[i]));
            SilkMarshal.ThrowHResult(device.CreateShaderResourceView(_small[i], null, ref _smallView[i]));
            SilkMarshal.ThrowHResult(device.CreateRenderTargetView(_small[i], null, ref _smallTarget[i]));
        }
    }

    private void ReleaseTargets()
    {
        _frameView.Dispose();
        _frame.Dispose();
        _frameView = default;
        _frame = default;

        for (int i = 0; i < 2; i++)
        {
            _smallTarget[i].Dispose();
            _smallView[i].Dispose();
            _small[i].Dispose();
            _smallTarget[i] = default;
            _smallView[i] = default;
            _small[i] = default;
        }

        _size = default;
    }

    private static ComPtr<ID3D11PixelShader> Pixel(ComPtr<ID3D11Device> device, D3DCompiler compiler, string entry)
    {
        ComPtr<ID3D10Blob> code = Compile(compiler, entry, "ps_5_0");
        ComPtr<ID3D11PixelShader> shader = default;
        SilkMarshal.ThrowHResult(device.CreatePixelShader(
            code.GetBufferPointer(), code.GetBufferSize(), ref Unsafe.NullRef<ID3D11ClassLinkage>(), ref shader));
        code.Dispose();
        return shader;
    }

    private static ComPtr<ID3D10Blob> Compile(D3DCompiler compiler, string entry, string profile)
    {
        byte[] source = System.Text.Encoding.ASCII.GetBytes(ShaderSource);
        ComPtr<ID3D10Blob> code = default;
        ComPtr<ID3D10Blob> errors = default;

        fixed (byte* text = source)
        {
            int result = compiler.Compile(
                text, (nuint)source.Length, "bloom", null, ref Unsafe.NullRef<ID3DInclude>(), entry, profile, 0, 0, ref code, ref errors);

            if (result < 0)
            {
                string message = errors.Handle is null ? $"0x{result:X8}" : SilkMarshal.PtrToString((nint)errors.GetBufferPointer()) ?? string.Empty;
                errors.Dispose();
                throw new InvalidOperationException($"bloom shader {entry}: {message}");
            }
        }

        errors.Dispose();
        return code;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        ReleaseTargets();
        _vertex.Dispose();
        _downsample.Dispose();
        _blur.Dispose();
        _add.Dispose();
        _constants.Dispose();
        _sampler.Dispose();
        _additive.Dispose();
        _noDepth.Dispose();
        _noCull.Dispose();
    }
}
