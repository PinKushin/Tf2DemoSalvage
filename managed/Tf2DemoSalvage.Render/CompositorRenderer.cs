using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// The war paint compositor's passes on the GPU (B754): `CTextureCompositor`'s full-screen draws, each into its own RGBA8
/// sRGB target (compositor.cpp `DrawCompositorStage_ps20b`, compositor_ps2x.fxc, compositor_vs20.fxc), executing the
/// <see cref="CompositorPlan"/> that <c>TextureCompositor</c> resolves and executes on the CPU as the suite's reference.
/// </summary>
/// <remarks>
/// **On the GPU because the engine composites there and because the CPU cannot keep up**: the reference executor took
/// 3.2 s for a 512² Night Owl and 33 s at 2048² (probe `paint-kits composite`, 2026-11-16), where a weapon's composite is
/// asked for as soon as it is drawn.
///
/// *Interpolated, each named where it is decided:* source textures read through hardware sRGB and trilinear filtering
/// (the reference uses the exact curve and a footprint-chosen mip, findings 111 §6); a source without a file mip chain
/// gets one generated; the result keeps RGBA8 with generated mips where the engine compresses to runtime DXT5 (§3.8).
/// </remarks>
public sealed unsafe class CompositorRenderer : IDisposable
{
    private const string ShaderSource = """
        cbuffer Stage : register(b0)
        {
            float4 rows[8];      // input i: rows[2i] = (m00, m01, m02, 0), rows[2i+1] = (m10, m11, m12, 0)
            float4 levels[4];    // input i: black, white, gamma, 1 when the levels are not (0, 1, 1)
            float4 selectors[4]; // sixteen selectors, each already times 1/16
            int4 mode;           // x the combine, y how many inputs this pass reads
        };

        Texture2D input0 : register(t0);
        Texture2D input1 : register(t1);
        Texture2D input2 : register(t2);
        Texture2D input3 : register(t3);
        SamplerState wrap : register(s0);

        struct VsOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

        VsOut VsFull(uint id : SV_VertexID)
        {
            VsOut o;
            float2 corner = float2((id << 1) & 2, id & 2);
            o.uv = corner;
            o.pos = float4(corner * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
            return o;
        }

        float ToSrgb(float v) { return v <= 0.0031308f ? 12.92f * v : 1.055f * pow(v, 1.0f / 2.4f) - 0.055f; }
        float ToLinear(float v) { return v <= 0.04045f ? v / 12.92f : pow((v + 0.055f) / 1.055f, 2.4f); }

        // compositor_ps2x.fxc AdjustLevels: Photoshop levels on the sRGB-encoded colour, alpha as stored.
        float Level(float v, float4 l, bool srgb)
        {
            float encoded = srgb ? ToSrgb(v) : v;
            float adjusted = pow(saturate((encoded - l.x) / (l.y - l.x)), l.z);
            return saturate(srgb ? ToLinear(adjusted) : adjusted);
        }

        float4 Adjust(float4 c, float4 l)
        {
            if (l.w == 0.0f)
            {
                return c;
            }

            return float4(Level(c.r, l, true), Level(c.g, l, true), Level(c.b, l, true), Level(c.a, l, false));
        }

        float2 Uv(float2 uv, int i)
        {
            return float2(dot(rows[2 * i].xyz, float3(uv, 1.0f)), dot(rows[2 * i + 1].xyz, float3(uv, 1.0f)));
        }

        float4 PsComposite(VsOut i) : SV_TARGET
        {
            int count = mode.y;
            float4 c0 = Adjust(input0.Sample(wrap, Uv(i.uv, 0)), levels[0]);
            float4 c1 = count > 1 ? Adjust(input1.Sample(wrap, Uv(i.uv, 1)), levels[1]) : float4(0, 0, 0, 0);
            float4 c2 = count > 2 ? Adjust(input2.Sample(wrap, Uv(i.uv, 2)), levels[2]) : float4(0, 0, 0, 0);
            float4 c3 = count > 3 ? Adjust(input3.Sample(wrap, Uv(i.uv, 3)), levels[3]) : float4(0, 0, 0, 0);

            if (count < 1)
            {
                c0 = float4(0, 0, 0, 0);
            }

            if (mode.x == 0)
            {
                // MULTIPLY, cSafeColor 1 for a missing input.
                float4 one = float4(1, 1, 1, 1);
                return (count > 0 ? c0 : one) * (count > 1 ? c1 : one) * (count > 2 ? c2 : one) * (count > 3 ? c3 : one);
            }

            if (mode.x == 1)
            {
                // ADD, cSafeColor 0 for a missing input.
                return c0 + c1 + c2 + c3;
            }

            if (mode.x == 2)
            {
                // main_lerp needs exactly three inputs, red otherwise.
                return count == 3 ? lerp(c0, c1, c2.r) : float4(1, 0, 0, 1);
            }

            if (mode.x == 3)
            {
                // SELECT: the groups value in sixteenths against each non-zero selector; yellow unless one input.
                if (count != 1)
                {
                    return float4(1, 1, 0, 1);
                }

                float test = round(c0.r * 255.0f / 16.0f);

                [unroll]
                for (int k = 0; k < 16; k++)
                {
                    float s = selectors[k / 4][k % 4];

                    if (s != 0.0f && round(s) == test)
                    {
                        return float4(1, 1, 1, 1);
                    }
                }

                return float4(0, 0, 0, 0);
            }

            // BLEND (main_simple): the sticker over what lies under it by its alpha, its spec's red into alpha.
            float a = c1.a;
            return float4((1.0f - a) * c0.rgb + a * c1.rgb, (1.0f - a) * c0.a + a * c2.r);
        }
        """;

    private const int ConstantFloats = (8 + 4 + 4 + 4) * 4;

    private ComPtr<ID3D11VertexShader> _vertex;
    private ComPtr<ID3D11PixelShader> _pixel;
    private ComPtr<ID3D11Buffer> _constants;
    private ComPtr<ID3D11SamplerState> _wrap;
    private ComPtr<ID3D11DepthStencilState> _noDepth;
    private ComPtr<ID3D11RasterizerState> _noCull;

    /// <summary>Each source texture uploaded once, with an sRGB and a linear view of the same texels.</summary>
    private readonly Dictionary<string, Views?> _sources = new(StringComparer.OrdinalIgnoreCase);

    private CompositorRenderer()
    {
    }

    /// <summary>A texture and its two views: read through the sRGB curve, and as stored.</summary>
    private sealed record Views(ComPtr<ID3D11Texture2D> Texture, ComPtr<ID3D11ShaderResourceView> Srgb, ComPtr<ID3D11ShaderResourceView> Linear)
    {
        public void Release()
        {
            Linear.Dispose();
            Srgb.Dispose();
            Texture.Dispose();
        }
    }

    /// <summary>Compiles the pass and makes the fixed state.</summary>
    /// <param name="device">The device.</param>
    /// <returns>The renderer.</returns>
    public static CompositorRenderer Create(ComPtr<ID3D11Device> device)
    {
        CompositorRenderer? compositor = new();

        try
        {
            compositor.Build(device);
            CompositorRenderer built = compositor;
            compositor = null;
            return built;
        }
        finally
        {
            compositor?.Dispose();
        }
    }

    private void Build(ComPtr<ID3D11Device> device)
    {
        using D3DCompiler compiler = D3DCompiler.GetApi();

        _vertex = BloomRenderer.Vertex(device, compiler, ShaderSource, "VsFull");
        _pixel = BloomRenderer.Pixel(device, compiler, ShaderSource, "PsComposite");

        BufferDesc buffer = new()
        {
            ByteWidth = ConstantFloats * sizeof(float),
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.ConstantBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write,
        };
        SilkMarshal.ThrowHResult(device.CreateBuffer(in buffer, null, ref _constants));

        // No clamp flag is set on any compositor texture or target (findings 111 §4.1), so every sampler wraps.
        SamplerDesc sampler = new()
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Wrap,
            AddressV = TextureAddressMode.Wrap,
            AddressW = TextureAddressMode.Wrap,
            MaxLOD = float.MaxValue,
        };
        SilkMarshal.ThrowHResult(device.CreateSamplerState(in sampler, ref _wrap));

        DepthStencilDesc depth = default;
        SilkMarshal.ThrowHResult(device.CreateDepthStencilState(in depth, ref _noDepth));

        RasterizerDesc raster = new() { FillMode = FillMode.Solid, CullMode = CullMode.None, DepthClipEnable = 1 };
        SilkMarshal.ThrowHResult(device.CreateRasterizerState(in raster, ref _noCull));
    }

    /// <summary>
    /// Draws every pass of <paramref name="plan"/> and answers the composite as an sRGB view with a full mip chain, or a
    /// default handle where a required texture does not load (the engine's `ECRS_Error`, which keeps the original).
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="context">The context; its render target, viewport and pipeline are left for the caller to restore.</param>
    /// <param name="plan">The resolved compositor tree.</param>
    /// <param name="source">A source texture by its name under `materials/`, or null where it does not load.</param>
    /// <returns>The composite's view; the caller owns it.</returns>
    public ComPtr<ID3D11ShaderResourceView> Composite(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, CompositorPlan plan, Func<string, MapTexture?> source)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(source);

        foreach (string name in plan.Required)
        {
            if (Source(device, context, name, source) is null)
            {
                return default;
            }
        }

        List<Views> targets = [];

        try
        {
            Viewport viewport = new(0f, 0f, plan.Width, plan.Height, 0f, 1f);
            float* blendFactor = stackalloc float[4] { 1f, 1f, 1f, 1f };

            context.IASetInputLayout(default(ComPtr<ID3D11InputLayout>));
            context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
            context.VSSetShader(_vertex, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
            context.PSSetShader(_pixel, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
            context.PSSetConstantBuffers(0, 1, ref _constants);
            context.PSSetSamplers(0, 1, ref _wrap);
            context.OMSetDepthStencilState(_noDepth, 0);
            context.RSSetState(_noCull);
            context.OMSetBlendState(default(ComPtr<ID3D11BlendState>), blendFactor, 0xFFFFFFFF);
            context.RSSetViewports(1, in viewport);

            foreach (CompositorPass pass in plan.Passes)
            {
                if (!Draw(device, context, plan, pass, targets, source))
                {
                    return default;
                }
            }

            return Finish(device, context, plan, targets[^1]);
        }
        finally
        {
            ComPtr<ID3D11ShaderResourceView> none = default;

            for (uint slot = 0; slot < 4; slot++)
            {
                context.PSSetShaderResources(slot, 1, ref none);
            }

            context.OMSetRenderTarget(default(ComPtr<ID3D11RenderTargetView>), default);

            foreach (Views target in targets)
            {
                target.Release();
            }
        }
    }

    /// <summary>One pass into a new target of its own, appended to <paramref name="targets"/>.</summary>
    private bool Draw(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        CompositorPlan plan,
        CompositorPass pass,
        List<Views> targets,
        Func<string, MapTexture?> source)
    {
        int count = Math.Min(pass.Inputs.Count, 4);
        ComPtr<ID3D11ShaderResourceView>[] views = new ComPtr<ID3D11ShaderResourceView>[4];

        for (int i = 0; i < count; i++)
        {
            CompositorInput input = pass.Inputs[i];
            Views? bound = input.Pass >= 0 ? targets[input.Pass] : Source(device, context, input.Texture ?? string.Empty, source);

            if (bound is null && input.BlackIfMissing)
            {
                bound = Source(device, context, BlackName, _ => Black);
            }

            if (bound is null)
            {
                return false;
            }

            views[i] = input.Srgb ? bound.Srgb : bound.Linear;
        }

        Views target = Target(device, plan.Width, plan.Height, mips: 1, generate: false);
        targets.Add(target);

        ComPtr<ID3D11RenderTargetView> output = default;
        RenderTargetViewDesc description = new()
        {
            Format = Silk.NET.DXGI.Format.FormatR8G8B8A8UnormSrgb,
            ViewDimension = RtvDimension.Texture2D,
        };
        SilkMarshal.ThrowHResult(device.CreateRenderTargetView(target.Texture, in description, ref output));

        try
        {
            ComPtr<ID3D11ShaderResourceView> none = default;

            for (uint slot = 0; slot < 4; slot++)
            {
                context.PSSetShaderResources(slot, 1, ref none);
            }

            context.OMSetRenderTarget(output, default);
            WriteConstants(context, pass, count);

            for (int i = 0; i < count; i++)
            {
                context.PSSetShaderResources((uint)i, 1, ref views[i]);
            }

            SubmitCounters.Draw();
            context.Draw(3, 0);
        }
        finally
        {
            output.Dispose();
        }

        return true;
    }

    private void WriteConstants(ComPtr<ID3D11DeviceContext> context, CompositorPass pass, int count)
    {
        MappedSubresource mapped = default;
        SubmitCounters.Upload();
        SilkMarshal.ThrowHResult(context.Map(_constants, 0, Map.WriteDiscard, 0, ref mapped));
        float* into = (float*)mapped.PData;
        new Span<float>(into, ConstantFloats).Clear();

        for (int i = 0; i < count; i++)
        {
            CompositorInput input = pass.Inputs[i];
            CompositorAffine m = input.Matrix;
            (into[(i * 8) + 0], into[(i * 8) + 1], into[(i * 8) + 2]) = (m.M00, m.M01, m.M02);
            (into[(i * 8) + 4], into[(i * 8) + 5], into[(i * 8) + 6]) = (m.M10, m.M11, m.M12);

            float* level = into + 32 + (i * 4);
            bool identity = input.Black.CompareTo(0f) == 0 && input.White.CompareTo(1f) == 0 && input.Gamma.CompareTo(1f) == 0;
            (level[0], level[1], level[2], level[3]) = (input.Black, input.White, input.Gamma, identity ? 0f : 1f);
        }

        for (int k = 0; k < Math.Min(pass.Selectors.Count, 16); k++)
        {
            into[48 + k] = pass.Selectors[k];
        }

        int* modes = (int*)(into + 64);
        modes[0] = (int)pass.Combine;
        modes[1] = count;
        context.Unmap(_constants, 0);
    }

    /// <summary>The last target copied into a texture with a full chain, its mips generated through the sRGB view.</summary>
    private static ComPtr<ID3D11ShaderResourceView> Finish(ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, CompositorPlan plan, Views last)
    {
        Views result = Target(device, plan.Width, plan.Height, mips: 0, generate: true);

        try
        {
            context.CopySubresourceRegion(result.Texture, 0, 0, 0, 0, last.Texture, 0, ref Unsafe.NullRef<Box>());
            context.GenerateMips(result.Srgb);
        }
        catch
        {
            result.Release();
            throw;
        }

        ComPtr<ID3D11ShaderResourceView> view = result.Srgb;
        result.Linear.Dispose();
        result.Texture.Dispose();
        return view;
    }

    /// <summary>An RGBA8 typeless target: one level for a pass, a full chain for the result.</summary>
    private static Views Target(ComPtr<ID3D11Device> device, int width, int height, int mips, bool generate)
    {
        Texture2DDesc description = new()
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = (uint)mips,
            ArraySize = 1,
            Format = Silk.NET.DXGI.Format.FormatR8G8B8A8Typeless,
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)(BindFlag.ShaderResource | BindFlag.RenderTarget),
            MiscFlags = generate ? (uint)ResourceMiscFlag.GenerateMips : 0u,
        };

        ComPtr<ID3D11Texture2D> texture = default;
        SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, null, ref texture));
        return WithViews(device, texture, Silk.NET.DXGI.Format.FormatR8G8B8A8UnormSrgb, Silk.NET.DXGI.Format.FormatR8G8B8A8Unorm);
    }

    private static Views WithViews(ComPtr<ID3D11Device> device, ComPtr<ID3D11Texture2D> texture, Silk.NET.DXGI.Format srgb, Silk.NET.DXGI.Format linear)
    {
        ComPtr<ID3D11ShaderResourceView> srgbView = default;
        ComPtr<ID3D11ShaderResourceView> linearView = default;
        ShaderResourceViewDesc view = new() { Format = srgb, ViewDimension = D3DSrvDimension.D3DSrvDimensionTexture2D };
        view.Texture2D.MipLevels = uint.MaxValue;

        try
        {
            SilkMarshal.ThrowHResult(device.CreateShaderResourceView(texture, in view, ref srgbView));
            view.Format = linear;
            SilkMarshal.ThrowHResult(device.CreateShaderResourceView(texture, in view, ref linearView));
            return new Views(texture, srgbView, linearView);
        }
        catch
        {
            // The views made so far and the texture handed in, which nothing else will release.
            linearView.Dispose();
            srgbView.Dispose();
            texture.Dispose();
            throw;
        }
    }

    private const string BlackName = "\0black";

    /// <summary>The engine's "black", which a sticker's missing spec becomes (findings 111 §3.7).</summary>
    private static readonly MapTexture Black = new(1, 1, 1, 1, TextureImage.Rgba(new byte[] { 0, 0, 0, 255 }), IsTransparent: false);

    /// <summary>A source texture, uploaded once into a typeless texture so it can be read with or without sRGB.</summary>
    private Views? Source(ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, string name, Func<string, MapTexture?> source)
    {
        if (_sources.TryGetValue(name, out Views? cached))
        {
            return cached;
        }

        Views? views = source(name) is { IsErrorTexture: false, Image.IsEmpty: false } texture ? Upload(device, context, texture) : null;
        _sources[name] = views;
        return views;
    }

    private static Views? Upload(ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, MapTexture texture)
    {
        TextureImage image = texture.Image;

        if (image.IsBlockCompressed)
        {
            (Silk.NET.DXGI.Format typeless, Silk.NET.DXGI.Format srgb, Silk.NET.DXGI.Format linear) = image.Format switch
            {
                VtfFormat.Dxt1 or VtfFormat.Dxt1OneBitAlpha =>
                    (Silk.NET.DXGI.Format.FormatBC1Typeless, Silk.NET.DXGI.Format.FormatBC1UnormSrgb, Silk.NET.DXGI.Format.FormatBC1Unorm),
                VtfFormat.Dxt3 =>
                    (Silk.NET.DXGI.Format.FormatBC2Typeless, Silk.NET.DXGI.Format.FormatBC2UnormSrgb, Silk.NET.DXGI.Format.FormatBC2Unorm),
                _ => (Silk.NET.DXGI.Format.FormatBC3Typeless, Silk.NET.DXGI.Format.FormatBC3UnormSrgb, Silk.NET.DXGI.Format.FormatBC3Unorm),
            };

            return WithViews(device, Blocks(device, texture, typeless), srgb, linear);
        }

        if (image.Format is VtfFormat.Rgba16161616F)
        {
            return null;
        }

        // Widened texels, one level: the chain generated through the sRGB view.
        Views target = Target(device, texture.Width, texture.Height, mips: 0, generate: true);

        try
        {
            fixed (byte* pixels = image.Top.Span)
            {
                context.UpdateSubresource(target.Texture, 0, ref Unsafe.NullRef<Box>(), pixels, (uint)(texture.Width * 4), 0);
            }

            context.GenerateMips(target.Srgb);
            return target;
        }
        catch
        {
            target.Release();
            throw;
        }
    }

    private static ComPtr<ID3D11Texture2D> Blocks(ComPtr<ID3D11Device> device, MapTexture texture, Silk.NET.DXGI.Format format)
    {
        TextureImage image = texture.Image;
        int levels = image.Levels.Count;
        Texture2DDesc description = new()
        {
            Width = (uint)texture.Width,
            Height = (uint)texture.Height,
            MipLevels = (uint)levels,
            ArraySize = 1,
            Format = format,
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Usage = Usage.Immutable,
            BindFlags = (uint)BindFlag.ShaderResource,
        };

        System.Buffers.MemoryHandle[] pins = new System.Buffers.MemoryHandle[levels];
        SubresourceData[] data = new SubresourceData[levels];

        try
        {
            for (int level = 0; level < levels; level++)
            {
                pins[level] = image.Levels[level].Pin();
                data[level] = new SubresourceData
                {
                    PSysMem = pins[level].Pointer,
                    SysMemPitch = (uint)WorldRenderer.BlockPitch(image.Format, texture.Width, level),
                };
            }

            ComPtr<ID3D11Texture2D> created = default;

            fixed (SubresourceData* first = data)
            {
                SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, first, ref created));
            }

            return created;
        }
        finally
        {
            foreach (System.Buffers.MemoryHandle pin in pins)
            {
                pin.Dispose();
            }
        }
    }

    /// <summary>The composite's top level read back as RGBA8, sRGB-encoded as stored: the tests' view of the GPU's answer.</summary>
    /// <param name="device">The device.</param>
    /// <param name="context">The context.</param>
    /// <param name="plan">The plan.</param>
    /// <param name="source">The source textures.</param>
    /// <returns>The texels, or null where the composite failed.</returns>
    internal byte[]? CompositeToRgba(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, CompositorPlan plan, Func<string, MapTexture?> source)
    {
        using ComPtr<ID3D11ShaderResourceView> view = Composite(device, context, plan, source);

        if (view.Handle is null)
        {
            return null;
        }

        ComPtr<ID3D11Resource> resource = default;
        view.GetResource(ref resource);
        ComPtr<ID3D11Texture2D> staging = default;
        Texture2DDesc description = new()
        {
            Width = (uint)plan.Width,
            Height = (uint)plan.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Silk.NET.DXGI.Format.FormatR8G8B8A8Typeless,
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Usage = Usage.Staging,
            CPUAccessFlags = (uint)CpuAccessFlag.Read,
        };

        try
        {
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, null, ref staging));
            context.CopySubresourceRegion(staging, 0, 0, 0, 0, resource, 0, ref Unsafe.NullRef<Box>());

            MappedSubresource mapped = default;
            SilkMarshal.ThrowHResult(context.Map(staging, 0, Map.Read, 0, ref mapped));
            byte[] pixels = new byte[plan.Width * plan.Height * 4];

            for (int y = 0; y < plan.Height; y++)
            {
                new ReadOnlySpan<byte>((byte*)mapped.PData + (y * mapped.RowPitch), plan.Width * 4).CopyTo(pixels.AsSpan(y * plan.Width * 4));
            }

            context.Unmap(staging, 0);
            return pixels;
        }
        finally
        {
            staging.Dispose();
            resource.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (Views? views in _sources.Values)
        {
            views?.Release();
        }

        _sources.Clear();
        _vertex.Dispose();
        _pixel.Dispose();
        _constants.Dispose();
        _wrap.Dispose();
        _noDepth.Dispose();
        _noCull.Dispose();
    }
}
