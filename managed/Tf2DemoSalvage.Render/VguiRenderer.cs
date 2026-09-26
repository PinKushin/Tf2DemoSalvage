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

        float4 PsMain(VsOut input) : SV_TARGET
        {
            return Base.Sample(Sampler, input.uv) * input.col;
        }
        """;

    private readonly Dictionary<string, (ComPtr<ID3D11ShaderResourceView> View, bool Additive)> _textures =
        new(StringComparer.OrdinalIgnoreCase);

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
    }

    /// <summary>Draws the quads into the bound render target.</summary>
    /// <param name="device">The device, for textures and the vertex buffer.</param>
    /// <param name="context">The context.</param>
    /// <param name="quads">The draw list's quads, in paint order.</param>
    /// <param name="resolve">A material name to its texture, null when it does not resolve.</param>
    /// <param name="viewportWidth">Render target width.</param>
    /// <param name="viewportHeight">Render target height.</param>
    public void Draw(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        IReadOnlyList<VguiQuad> quads,
        Func<string, MapTexture?> resolve,
        int viewportWidth,
        int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(quads);
        ArgumentNullException.ThrowIfNull(resolve);

        if (quads.Count == 0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        float[] vertices = BuildVertices(quads, viewportWidth, viewportHeight);

        EnsureCapacity(device, quads.Count);
        Upload(context, vertices);

        uint stride = VertexStride;
        uint offset = 0;

        context.IASetInputLayout(_layout);
        context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context.IASetVertexBuffers(0, 1, ref _vertices, in stride, in offset);
        context.VSSetShader(_vertexShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShader(_pixelShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetSamplers(0, 1, ref _sampler);

        int start = 0;

        while (start < quads.Count)
        {
            string? texture = quads[start].Texture;
            int end = start + 1;

            while (end < quads.Count && string.Equals(quads[end].Texture, texture, StringComparison.OrdinalIgnoreCase))
            {
                end++;
            }

            (ComPtr<ID3D11ShaderResourceView> view, bool additive) = texture is null ? (_white, false) : Texture(device, context, texture, resolve);

            if (view.Handle is not null)
            {
                Span<float> factor = [1f, 1f, 1f, 1f];

                fixed (float* blendFactor = factor)
                {
                    context.OMSetBlendState(additive ? _additive : _translucent, blendFactor, 0xFFFFFFFF);
                }

                context.PSSetShaderResources(0, 1, ref view);
                context.Draw((uint)((end - start) * VerticesPerQuad), (uint)(start * VerticesPerQuad));
            }

            start = end;
        }
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
        int at = 0;

        foreach (VguiQuad quad in quads)
        {
            float left = (quad.X0 / viewportWidth * 2f) - 1f;
            float right = (quad.X1 / viewportWidth * 2f) - 1f;
            float top = 1f - (quad.Y0 / viewportHeight * 2f);
            float bottom = 1f - (quad.Y1 / viewportHeight * 2f);
            float red = quad.Red / 255f;
            float green = quad.Green / 255f;
            float blue = quad.Blue / 255f;

            // (tl, tr, br) and (tl, br, bl).
            Append(data, ref at, left, top, quad.S0, quad.T0, red, green, blue, quad.AlphaTopLeft);
            Append(data, ref at, right, top, quad.S1, quad.T0, red, green, blue, quad.AlphaTopRight);
            Append(data, ref at, right, bottom, quad.S1, quad.T1, red, green, blue, quad.AlphaBottomRight);
            Append(data, ref at, left, top, quad.S0, quad.T0, red, green, blue, quad.AlphaTopLeft);
            Append(data, ref at, right, bottom, quad.S1, quad.T1, red, green, blue, quad.AlphaBottomRight);
            Append(data, ref at, left, bottom, quad.S0, quad.T1, red, green, blue, quad.AlphaBottomLeft);
        }

        return data;
    }

    private static void Append(float[] data, ref int at, float x, float y, float s, float t, float red, float green, float blue, byte alpha)
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

    /// <summary>A material's view, uploaded once; an unresolved material is remembered as absent and draws nothing.</summary>
    private (ComPtr<ID3D11ShaderResourceView> View, bool Additive) Texture(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, string name, Func<string, MapTexture?> resolve)
    {
        if (_textures.TryGetValue(name, out (ComPtr<ID3D11ShaderResourceView> View, bool Additive) held))
        {
            return held;
        }

        // `$vertexalpha` blends and keeps the texture's alpha, so the upload must not force it opaque.
        MapTexture? texture = resolve(name) is { } found ? found with { IsTransparent = true } : null;
        (ComPtr<ID3D11ShaderResourceView>, bool) entry = (texture is null ? default : WorldRenderer.UploadTexture(device, context, texture), texture?.IsAdditive ?? false);

        _textures[name] = entry;

        return entry;
    }

    private void Upload(ComPtr<ID3D11DeviceContext> context, float[] vertices)
    {
        MappedSubresource mapped = default;
        SilkMarshal.ThrowHResult(context.Map(_vertices, 0, Map.WriteDiscard, 0, ref mapped));

        fixed (float* source = vertices)
        {
            System.Buffer.MemoryCopy(source, mapped.PData, vertices.Length * sizeof(float), vertices.Length * sizeof(float));
        }

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
        foreach ((ComPtr<ID3D11ShaderResourceView> view, _) in _textures.Values)
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
