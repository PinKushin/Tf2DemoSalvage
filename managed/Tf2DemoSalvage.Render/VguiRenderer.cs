using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Render;

/// <summary>Draws a <see cref="VguiDrawList"/>: the adapter half of the VGUI surface.</summary>
/// <remarks>
/// Everything that decides WHAT is drawn — offsets, clipping, alpha — happened in the draw list, which reproduces
/// `CMatSystemSurface`. This only uploads the quads and binds each run's texture. A VGUI material is `UnlitGeneric` with
/// `$vertexcolor $vertexalpha`: texture times vertex colour, blended, the texture's alpha kept — additive when the material
/// says `$additive`. A solid fill samples a white texel.
/// </remarks>
internal sealed unsafe class VguiRenderer : IDisposable
{
    private const int VerticesPerQuad = 6;
    private const int FloatsPerVertex = 8;
    private const int VertexStride = sizeof(float) * FloatsPerVertex;

    private const string ShaderSource = """
        Texture2D Base : register(t0);
        SamplerState Sampler : register(s0);

        struct VsIn  { float2 pos : POSITION; float2 uv : TEXCOORD; float4 col : COLOR; };
        struct VsOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD; float4 col : COLOR; };

        VsOut VsMain(VsIn input)
        {
            VsOut output;
            output.pos = float4(input.pos, 0.0f, 1.0f);
            output.uv = input.uv;
            output.col = input.col;
            return output;
        }

        Texture2D DetailTexture : register(t1);

        // c5-c9 of vertexlit_and_unlit_generic_ps2x.fxc (:146-171), then which effects the material switched on.
        cbuffer Shading : register(b0)
        {
            float4 GlowParams;     // GLOW_UV_OFFSET, OUTER_GLOW_MIN_DVALUE, OUTER_GLOW_MAX_DVALUE
            float4 GlowColour;     // OUTER_GLOW_COLOR
            float4 MaskRange;      // SOFT_MASK_MAX, SOFT_MASK_MIN
            float4 OutlineColour;  // g_OutlineColor
            float4 OutlineParams;  // g_OutlineParams
            float4 Switches;       // DISTANCEALPHA, SOFT_MASK, OUTLINE, OUTER_GLOW
            float4 DetailSwitches; // DETAILTEXTURE, DISTANCEALPHAFROMDETAIL, DETAIL_BLEND_MODE, g_DetailBlendFactor
            float4 DetailScale;    // $detailscale, which scales the detail's coordinates (vertexlitgeneric_dx9_helper.cpp:983-986)
        };
        """ + DetailCombineShader.Source + """
        float4 PsMain(VsOut input) : SV_TARGET
        {
            float4 baseColor = Base.Sample(Sampler, input.uv);
            float2 detailUv = input.uv * DetailScale.xy;
            int detailMode = (int)round(DetailSwitches.z);

            // `#if DISTANCEALPHA && (DISTANCEALPHAFROMDETAIL == 0)` (:257-259): the base's alpha, taken BEFORE the combine
            // that modes 3, 4, 8 and 9 rewrite it in. A FROMDETAIL material without a detail has no combo (:51); the white
            // texel bound in its place reads as a mask of 1.
            float distAlphaMask = baseColor.a;

            // `g_DetailTint` is c10, which UnlitGeneric always sets to white (vertexlitgeneric_dx9_helper.cpp:988-993).
            float4 detailColor = DetailTexture.Sample(Sampler, detailUv);

            if (DetailSwitches.y > 0.5)
            {
                distAlphaMask = detailColor.a;
                detailColor.a = 1.0;
            }

            // `#if DETAILTEXTURE` (:262-279): every $detail material, every mode, with or without distance alpha.
            if (DetailSwitches.x > 0.5)
            {
                baseColor = CombineDetail(baseColor, detailColor, detailMode, DetailSwitches.w);
            }

            // `#if DISTANCEALPHA` (:281-329).
            if (Switches.x > 0.5)
            {
                if (Switches.z > 0.5)
                {
                    float4 oFactors = smoothstep(OutlineParams.xyzw, OutlineParams.wzyx, distAlphaMask);
                    baseColor = lerp(baseColor, OutlineColour, oFactors.x * oFactors.y);
                }

                float mskUsed;

                if (Switches.y > 0.5)
                {
                    mskUsed = smoothstep(MaskRange.y, MaskRange.x, distAlphaMask);
                    baseColor.a *= mskUsed;
                }
                else
                {
                    mskUsed = distAlphaMask >= 0.5;
                    baseColor.a = DetailSwitches.x > 0.5 ? baseColor.a * mskUsed : mskUsed;
                }

                if (Switches.w > 0.5)
                {
                    float4 glowTexel = DetailSwitches.y > 0.5
                        ? DetailTexture.Sample(Sampler, detailUv + GlowParams.xy)
                        : Base.Sample(Sampler, input.uv + GlowParams.xy);
                    float4 glowc = GlowColour * smoothstep(GlowParams.z, GlowParams.w, glowTexel.a);
                    baseColor = lerp(glowc, baseColor, mskUsed);
                }
            }

            float4 result = baseColor * input.col;

            // `TextureCombinePostLighting` (:423-426): modes 5 and 6 add the detail after the colour multiply.
            if (DetailSwitches.x > 0.5)
            {
                result.rgb = CombineDetailAfterLighting(result.rgb, detailColor, detailMode, DetailSwitches.w);
            }

            return result;
        }
        """;

    /// <summary>The `Shading` cbuffer's floats: eight float4s.</summary>
    internal const int ConstantFloats = 32;

    /// <summary>The shader source, so a test can hold the cbuffer's declaration against <see cref="ConstantFloats"/>.</summary>
    internal static string ShaderSourceText => ShaderSource;

    private readonly Dictionary<string, (ComPtr<ID3D11ShaderResourceView> View, bool Additive, ComPtr<ID3D11ShaderResourceView> Detail, VguiShading? Shading)> _textures =
        new(StringComparer.OrdinalIgnoreCase);

    private ComPtr<ID3D11Buffer> _constants;

    private readonly Dictionary<string, (ComPtr<ID3D11ShaderResourceView> View, int Version)> _pages =
        new(StringComparer.Ordinal);

    private ComPtr<ID3D11VertexShader> _vertexShader;
    private ComPtr<ID3D11PixelShader> _pixelShader;
    private ComPtr<ID3D11InputLayout> _layout;
    private ComPtr<ID3D11Buffer> _vertices;
    private ComPtr<ID3D11SamplerState> _sampler;
    private ComPtr<ID3D11BlendState> _translucent;
    private ComPtr<ID3D11BlendState> _additive;
    private ComPtr<ID3D11ShaderResourceView> _white;
    private int _vertexCapacity;

    private VguiRenderer()
    {
    }

    /// <summary>Compiles the shaders and creates the fixed state.</summary>
    /// <param name="device">The device.</param>
    /// <param name="context">The context, for the white texel's upload.</param>
    /// <returns>The renderer.</returns>
    public static VguiRenderer Create(ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context)
    {
        VguiRenderer renderer = new();

        try
        {
            renderer.Initialise(device, context);
            return renderer;
        }
        catch
        {
            // Whatever was created before the failure is released; the failure itself goes to the caller.
            renderer.Dispose();
            throw;
        }
    }

    private void Initialise(ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context)
    {
        VguiRenderer renderer = this;
        using D3DCompiler compiler = D3DCompiler.GetApi();

        ComPtr<ID3D10Blob> vertexBytecode = Compile(compiler, "VsMain", "vs_5_0");
        ComPtr<ID3D10Blob> pixelBytecode = Compile(compiler, "PsMain", "ps_5_0");

        SilkMarshal.ThrowHResult(device.CreateVertexShader(
            vertexBytecode.GetBufferPointer(), vertexBytecode.GetBufferSize(), ref Unsafe.NullRef<ID3D11ClassLinkage>(), ref renderer._vertexShader));
        SilkMarshal.ThrowHResult(device.CreatePixelShader(
            pixelBytecode.GetBufferPointer(), pixelBytecode.GetBufferSize(), ref Unsafe.NullRef<ID3D11ClassLinkage>(), ref renderer._pixelShader));

        byte* position = (byte*)SilkMarshal.StringToPtr("POSITION");
        byte* texture = (byte*)SilkMarshal.StringToPtr("TEXCOORD");
        byte* colour = (byte*)SilkMarshal.StringToPtr("COLOR");

        InputElementDesc[] elements =
        [
            new() { SemanticName = position, Format = Silk.NET.DXGI.Format.FormatR32G32Float, AlignedByteOffset = 0, InputSlotClass = InputClassification.PerVertexData },
            new() { SemanticName = texture, Format = Silk.NET.DXGI.Format.FormatR32G32Float, AlignedByteOffset = sizeof(float) * 2, InputSlotClass = InputClassification.PerVertexData },
            new() { SemanticName = colour, Format = Silk.NET.DXGI.Format.FormatR32G32B32A32Float, AlignedByteOffset = sizeof(float) * 4, InputSlotClass = InputClassification.PerVertexData },
        ];

        fixed (InputElementDesc* first = elements)
        {
            SilkMarshal.ThrowHResult(device.CreateInputLayout(
                first, (uint)elements.Length, vertexBytecode.GetBufferPointer(), vertexBytecode.GetBufferSize(), ref renderer._layout));
        }

        SilkMarshal.Free((nint)position);
        SilkMarshal.Free((nint)texture);
        SilkMarshal.Free((nint)colour);
        vertexBytecode.Dispose();
        pixelBytecode.Dispose();

        SamplerDesc samplerDescription = new()
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            ComparisonFunc = ComparisonFunc.Never,
            MaxLOD = float.MaxValue,
        };

        SilkMarshal.ThrowHResult(device.CreateSamplerState(in samplerDescription, ref renderer._sampler));

        BlendDesc translucent = BlendStates.Translucent;
        BlendDesc additive = BlendStates.Additive;

        SilkMarshal.ThrowHResult(device.CreateBlendState(in translucent, ref renderer._translucent));
        SilkMarshal.ThrowHResult(device.CreateBlendState(in additive, ref renderer._additive));

        MapTexture white = new(1, 1, 1, 1, TextureImage.Rgba(new byte[] { 255, 255, 255, 255 }), IsTransparent: true);

        renderer._white = WorldRenderer.UploadTexture(device, context, white);

        BufferDesc constants = new()
        {
            ByteWidth = ConstantFloats * sizeof(float),
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.ConstantBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write,
        };

        SilkMarshal.ThrowHResult(device.CreateBuffer(in constants, ref Unsafe.NullRef<SubresourceData>(), ref renderer._constants));
    }

    /// <summary>A run's c5-c9, its switches and its detail; all zero but the detail scale for a material with neither.</summary>
    /// <param name="shading">The material's detail and distance alpha, or null.</param>
    /// <param name="viewportWidth">The back buffer's width.</param>
    /// <param name="viewportHeight">Its height.</param>
    /// <returns>The constant buffer's floats.</returns>
    internal static float[] ShadingConstants(VguiShading? shading, int viewportWidth, int viewportHeight)
    {
        float[] data = new float[ConstantFloats];
        (data[28], data[29]) = (1f, 1f);

        if (shading?.DistanceAlpha is { } shape)
        {
            shape.Constants(viewportWidth, viewportHeight).CopyTo(data, 0);

            (data[20], data[21], data[22], data[23]) = (1f, shape.SoftMask ? 1f : 0f, shape.Outline ? 1f : 0f, shape.Glow ? 1f : 0f);
            data[25] = shape.FromDetail ? 1f : 0f;
        }

        if (shading?.Detail is { } detail)
        {
            (data[24], data[26], data[27]) = (1f, detail.Mode, detail.BlendFactor);
            (data[28], data[29]) = detail.Scale;
        }

        return data;
    }

    /// <summary>Draws the quads into the bound render target.</summary>
    /// <param name="device">The device, for textures and the vertex buffer.</param>
    /// <param name="context">The context.</param>
    /// <param name="list">The draw list: its quads in paint order, and the glyph pages they sample.</param>
    /// <param name="resolve">A material name to its texture, null when it does not resolve.</param>
    /// <param name="viewportWidth">Render target width.</param>
    /// <param name="viewportHeight">Render target height.</param>
    /// <param name="drawModels">
    /// Draws one panel's 3D paint (<see cref="VguiModelDraw"/>) and leaves the full-screen HUD viewport, depth off,
    /// behind it; null to skip them. Each is drawn at its place in paint order, between the quads around it.
    /// </param>
    public void Draw(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        VguiDrawList list,
        Func<string, MapTexture?> resolve,
        int viewportWidth,
        int viewportHeight,
        Action<VguiModelDraw>? drawModels = null)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(resolve);

        IReadOnlyList<VguiQuad> quads = list.Quads;
        IReadOnlyList<VguiModelDraw> models = list.Models;

        UploadPages(device, context, list.Pages);

        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        if (quads.Count > 0)
        {
            EnsureCapacity(device, quads.Count);
            Upload(context, quads, viewportWidth, viewportHeight);
        }

        // A 3D paint changes shaders, buffers and viewport, so the quads' own state is bound again after each.
        bool bound = false;

        foreach ((int model, int start, int end) in Steps(quads, models))
        {
            if (model >= 0)
            {
                drawModels?.Invoke(models[model]);
                bound = false;
                continue;
            }

            if (!bound)
            {
                Bind(context);
                bound = true;
            }

            (ComPtr<ID3D11ShaderResourceView> view, bool additive, ComPtr<ID3D11ShaderResourceView> detail, VguiShading? shading) =
                Source(device, context, quads[start].Texture, resolve);

            additive |= quads[start].Additive;

            if (view.Handle is not null)
            {
                Span<float> factor = [1f, 1f, 1f, 1f];

                fixed (float* blendFactor = factor)
                {
                    context.OMSetBlendState(additive ? _additive : _translucent, blendFactor, 0xFFFFFFFF);
                }

                UploadConstants(context, ShadingConstants(shading, viewportWidth, viewportHeight));
                context.PSSetConstantBuffers(0, 1, ref _constants);

                ComPtr<ID3D11ShaderResourceView> detailView = detail.Handle is not null ? detail : _white;

                context.PSSetShaderResources(1, 1, ref detailView);
                context.PSSetShaderResources(0, 1, ref view);
                SubmitCounters.Draw(); context.Draw((uint)((end - start) * VerticesPerQuad), (uint)(start * VerticesPerQuad));
            }
        }
    }

    /// <summary>
    /// The draw order: runs of quads sharing a texture and blend, broken at each 3D paint, which comes exactly where the
    /// panel painted it — after the quads recorded before it.
    /// </summary>
    /// <param name="quads">The quads.</param>
    /// <param name="models">The 3D paints.</param>
    /// <returns>Each step: a model's index (its range unused), or -1 with a run's quad range.</returns>
    internal static List<(int Model, int Start, int End)> Steps(IReadOnlyList<VguiQuad> quads, IReadOnlyList<VguiModelDraw> models)
    {
        ArgumentNullException.ThrowIfNull(quads);
        ArgumentNullException.ThrowIfNull(models);

        List<(int, int, int)> steps = [];
        int nextModel = 0;
        int start = 0;

        while (start < quads.Count || nextModel < models.Count)
        {
            if (nextModel < models.Count && models[nextModel].Before <= start)
            {
                steps.Add((nextModel, 0, 0));
                nextModel++;
                continue;
            }

            string? texture = quads[start].Texture;
            bool forcedAdditive = quads[start].Additive;
            int limit = nextModel < models.Count ? Math.Min(models[nextModel].Before, quads.Count) : quads.Count;
            int end = start + 1;

            while (end < limit
                && string.Equals(quads[end].Texture, texture, StringComparison.OrdinalIgnoreCase)
                && quads[end].Additive == forcedAdditive)
            {
                end++;
            }

            steps.Add((-1, start, end));
            start = end;
        }

        return steps;
    }

    /// <summary>The quads' pipeline: layout, triangle list, their vertex buffer, shaders and sampler.</summary>
    private void Bind(ComPtr<ID3D11DeviceContext> context)
    {
        uint stride = VertexStride;
        uint offset = 0;

        context.IASetInputLayout(_layout);
        context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context.IASetVertexBuffers(0, 1, ref _vertices, in stride, in offset);
        context.VSSetShader(_vertexShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShader(_pixelShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetSamplers(0, 1, ref _sampler);
    }

    /// <summary>Screen quads to clip-space triangles, colour per corner.</summary>
    /// <param name="quads">The quads.</param>
    /// <param name="viewportWidth">Render target width.</param>
    /// <param name="viewportHeight">Render target height.</param>
    /// <returns>Interleaved position, texture coordinate and colour.</returns>
    internal static float[] BuildVertices(IReadOnlyList<VguiQuad> quads, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(quads);

        float[] data = new float[quads.Count * VerticesPerQuad * FloatsPerVertex];
        BuildVertices(quads, viewportWidth, viewportHeight, data);
        return data;
    }

    /// <summary><see cref="BuildVertices(IReadOnlyList{VguiQuad}, int, int)"/> into storage the caller owns — the mapped vertex buffer (B574).</summary>
    /// <param name="quads">The quads.</param>
    /// <param name="viewportWidth">Render target width.</param>
    /// <param name="viewportHeight">Render target height.</param>
    /// <param name="data">At least six vertices of eight floats per quad.</param>
    internal static void BuildVertices(IReadOnlyList<VguiQuad> quads, int viewportWidth, int viewportHeight, Span<float> data)
    {
        ArgumentNullException.ThrowIfNull(quads);

        int at = 0;

        for (int index = 0; index < quads.Count; index++)
        {
            VguiQuad quad = quads[index];
            float left = (quad.X0 / viewportWidth * 2f) - 1f;
            float right = (quad.X1 / viewportWidth * 2f) - 1f;
            float top = 1f - (quad.Y0 / viewportHeight * 2f);
            float bottom = 1f - (quad.Y1 / viewportHeight * 2f);
            // `GammaToLinear( v.vColor.rgb )` (vertexlit_and_unlit_generic_vs20.fxc:238): a VGUI colour is gamma, and the
            // frame's sRGB write would otherwise brighten it.
            float red = GammaToLinear(quad.Red);
            float green = GammaToLinear(quad.Green);
            float blue = GammaToLinear(quad.Blue);

            // A polygon's fan triangle, then a degenerate one so every item stays six vertices.
            if (quad.Triangle is ({ } a, { } b, { } c))
            {
                foreach (Scene.Hud.VguiVertex corner in (ReadOnlySpan<Scene.Hud.VguiVertex>)[a, b, c, a, a, a])
                {
                    Append(
                        data, ref at, (corner.X / viewportWidth * 2f) - 1f, 1f - (corner.Y / viewportHeight * 2f), corner.S, corner.T,
                        red, green, blue, quad.AlphaTopLeft);
                }

                continue;
            }

            // (tl, tr, br) and (tl, br, bl).
            Append(data, ref at, left, top, quad.S0, quad.T0, red, green, blue, quad.AlphaTopLeft);
            Append(data, ref at, right, top, quad.S1, quad.T0, red, green, blue, quad.AlphaTopRight);
            Append(data, ref at, right, bottom, quad.S1, quad.T1, red, green, blue, quad.AlphaBottomRight);
            Append(data, ref at, left, top, quad.S0, quad.T0, red, green, blue, quad.AlphaTopLeft);
            Append(data, ref at, right, bottom, quad.S1, quad.T1, red, green, blue, quad.AlphaBottomRight);
            Append(data, ref at, left, bottom, quad.S0, quad.T1, red, green, blue, quad.AlphaBottomLeft);
        }
    }

    /// <summary>`GammaToLinear` (common_fxc.h): <c>pow( x, 2.2 )</c>.</summary>
    private static float GammaToLinear(byte channel) => MathF.Pow(channel / 255f, 2.2f);

    private static void Append(Span<float> data, ref int at, float x, float y, float s, float t, float red, float green, float blue, byte alpha)
    {
        data[at++] = x;
        data[at++] = y;
        data[at++] = s;
        data[at++] = t;
        data[at++] = red;
        data[at++] = green;
        data[at++] = blue;
        data[at++] = alpha / 255f;
    }

    /// <summary>What a run samples: the white texel for a fill, a glyph page, or a material.</summary>
    private (ComPtr<ID3D11ShaderResourceView> View, bool Additive, ComPtr<ID3D11ShaderResourceView> Detail, VguiShading? Shading) Source(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, string? texture, Func<string, MapTexture?> resolve)
    {
        if (texture is null)
        {
            return (_white, false, default, null);
        }

        return _pages.TryGetValue(texture, out (ComPtr<ID3D11ShaderResourceView> View, int Version) page)
            ? (page.View, false, default, null)
            : Texture(device, context, texture, resolve);
    }

    private void UploadConstants(ComPtr<ID3D11DeviceContext> context, float[] data)
    {
        MappedSubresource mapped = default;
        SubmitCounters.Upload(); SilkMarshal.ThrowHResult(context.Map(_constants, 0, Map.WriteDiscard, 0, ref mapped));

        fixed (float* source = data)
        {
            System.Buffer.MemoryCopy(source, mapped.PData, ConstantFloats * sizeof(float), data.Length * sizeof(float));
        }

        context.Unmap(_constants, 0);
    }

    /// <summary>Each glyph page uploaded, and uploaded again when a glyph was added since.</summary>
    private void UploadPages(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        IReadOnlyDictionary<string, (int Wide, int Tall, byte[] Rgba, int Version)> pages)
    {
        foreach ((string name, (int wide, int tall, byte[] rgba, int version)) in pages)
        {
            if (_pages.TryGetValue(name, out (ComPtr<ID3D11ShaderResourceView> View, int Version) held) && held.Version == version)
            {
                continue;
            }

            held.View.Dispose();

            MapTexture texture = new(wide, tall, wide, tall, TextureImage.Rgba(rgba), IsTransparent: true);

            _pages[name] = (WorldRenderer.UploadTexture(device, context, texture), version);
        }
    }

    /// <summary>A material's view, uploaded once; an unresolved material is remembered as absent and draws nothing.</summary>
    private (ComPtr<ID3D11ShaderResourceView> View, bool Additive, ComPtr<ID3D11ShaderResourceView> Detail, VguiShading? Shading) Texture(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, string name, Func<string, MapTexture?> resolve)
    {
        if (_textures.TryGetValue(name, out (ComPtr<ID3D11ShaderResourceView> View, bool Additive, ComPtr<ID3D11ShaderResourceView> Detail, VguiShading? Shading) held))
        {
            return held;
        }

        // `$vertexalpha` blends and keeps the texture's alpha, so the upload must not force it opaque.
        MapTexture? texture = resolve(name) is { } found ? found with { IsTransparent = true } : null;
        VguiShading? shading = texture?.Vgui;

        // Read through the sRGB curve for every mode but mod2x (vertexlitgeneric_dx9_helper.cpp:607-608).
        ComPtr<ID3D11ShaderResourceView> detail = shading?.Detail is { } combined
            ? WorldRenderer.UploadTexture(
                device, context, combined.Texture with { IsTransparent = true }, SamplerSrgb.DetailReadsAsSrgb("UnlitGeneric", combined.Mode))
            : default;
        (ComPtr<ID3D11ShaderResourceView>, bool, ComPtr<ID3D11ShaderResourceView>, VguiShading?) entry =
            (texture is null ? default : WorldRenderer.UploadTexture(device, context, texture), texture?.IsAdditive ?? false, detail, shading);

        _textures[name] = entry;

        return entry;
    }

    /// <summary>Writes the quads' vertices straight into the mapped buffer, as the engine's mesh builder writes into its locked one (B574).</summary>
    private void Upload(ComPtr<ID3D11DeviceContext> context, IReadOnlyList<VguiQuad> quads, int viewportWidth, int viewportHeight)
    {
        MappedSubresource mapped = default;
        SubmitCounters.Upload(); SilkMarshal.ThrowHResult(context.Map(_vertices, 0, Map.WriteDiscard, 0, ref mapped));

        // EnsureCapacity sized the buffer for at least this many quads.
        BuildVertices(
            quads,
            viewportWidth,
            viewportHeight,
            new Span<float>(mapped.PData, quads.Count * VerticesPerQuad * FloatsPerVertex));

        context.Unmap(_vertices, 0);
    }

    private void EnsureCapacity(ComPtr<ID3D11Device> device, int quadCount)
    {
        if (quadCount <= _vertexCapacity && _vertices.Handle is not null)
        {
            return;
        }

        _vertices.Dispose();

        int capacity = Math.Max(256, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)quadCount));

        BufferDesc description = new()
        {
            ByteWidth = (uint)(capacity * VerticesPerQuad * VertexStride),
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.VertexBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write,
        };

        SilkMarshal.ThrowHResult(device.CreateBuffer(in description, ref Unsafe.NullRef<SubresourceData>(), ref _vertices));
        _vertexCapacity = capacity;
    }

    private static ComPtr<ID3D10Blob> Compile(D3DCompiler compiler, string entryPoint, string profile)
    {
        ComPtr<ID3D10Blob> bytecode = default;
        ComPtr<ID3D10Blob> errors = default;
        byte[] source = System.Text.Encoding.ASCII.GetBytes(ShaderSource);

        fixed (byte* text = source)
        {
            int result = compiler.Compile(
                text, (nuint)source.Length, (byte*)null, null, ref Unsafe.NullRef<ID3DInclude>(), entryPoint, profile, 0, 0, ref bytecode, ref errors);

            if (result < 0)
            {
                string message = errors.Handle is not null
                    ? SilkMarshal.PtrToString((nint)errors.GetBufferPointer()) ?? "no detail"
                    : "no detail";

                errors.Dispose();
                throw new InvalidOperationException($"Compiling {entryPoint} ({profile}) failed: {message}");
            }
        }

        errors.Dispose();
        return bytecode;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach ((ComPtr<ID3D11ShaderResourceView> view, _, ComPtr<ID3D11ShaderResourceView> detail, _) in _textures.Values)
        {
            view.Dispose();
            detail.Dispose();
        }

        _constants.Dispose();

        foreach ((ComPtr<ID3D11ShaderResourceView> view, _) in _pages.Values)
        {
            view.Dispose();
        }

        _white.Dispose();
        _additive.Dispose();
        _translucent.Dispose();
        _sampler.Dispose();
        _vertices.Dispose();
        _layout.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
    }
}
