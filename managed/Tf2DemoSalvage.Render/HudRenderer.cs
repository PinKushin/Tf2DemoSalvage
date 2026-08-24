using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary>One rectangle of an atlas, drawn at a place on screen in a colour.</summary>
/// <param name="X">Left edge on screen, in pixels.</param>
/// <param name="Y">Top edge on screen, in pixels.</param>
/// <param name="Width">Width in pixels, in the atlas and on screen alike.</param>
/// <param name="Height">Height in pixels, in the atlas and on screen alike.</param>
/// <param name="SourceX">Left edge in the atlas, in pixels.</param>
/// <param name="SourceY">Top edge in the atlas, in pixels.</param>
/// <param name="Red">Red channel of the tint.</param>
/// <param name="Green">Green channel of the tint.</param>
/// <param name="Blue">Blue channel of the tint.</param>
/// <param name="Alpha">Opacity.</param>
/// <remarks>
/// **One size, used for both rectangles, because a HUD font is never scaled.** VGUI picks a size by
/// declaring a font at a `tall` in the scheme and rasterising it there; nothing stretches a glyph
/// afterwards, and a stretched glyph is exactly what makes a HUD look wrong. Separate source and
/// destination sizes can be added when something needs them.
/// </remarks>
public readonly record struct HudQuad(
    int X,
    int Y,
    int Width,
    int Height,
    int SourceX,
    int SourceY,
    byte Red,
    byte Green,
    byte Blue,
    byte Alpha);

/// <summary>
/// Draws HUD quads from a glyph atlas, the way VGUI's 2D surface does.
/// </summary>
/// <remarks>
/// **The pixel shader multiplies the texture's RGB by the quad's colour, and that one line is what
/// makes an outlined font work** (D84). `CFPSPanel` draws one font handle at three different colours
/// with a black outline in all of them, so a single texture has to render tinted with an untinted
/// outline. Multiplication gives it for nothing: white becomes the colour, and black stays black
/// because <c>0 × c = 0</c>. There is no outline-specific code anywhere in this class, and there
/// should not be.
///
/// **Point sampling, not linear.** A glyph is drawn at exactly the size it was rasterised, so any
/// filtering can only blur it — and VGUI agrees: `DrawSetTextureRGBA` takes a `hardwareFilter`
/// argument precisely because fonts want it off.
///
/// **Screen coordinates convert to clip space on the CPU**, as <see cref="PointRenderer"/> does and
/// for the same reason: the arithmetic that decides where a thing appears should be assertable
/// without a GPU.
/// </remarks>
internal sealed unsafe class HudRenderer : IDisposable
{
    /// <summary>Vertices per quad: two triangles.</summary>
    private const int VerticesPerQuad = 6;

    /// <summary>Floats per vertex: two of position, two of texture, four of colour.</summary>
    private const int FloatsPerVertex = 8;

    /// <summary>Bytes per vertex.</summary>
    private const int VertexStride = sizeof(float) * FloatsPerVertex;

    private const string ShaderSource = """
        Texture2D Atlas : register(t0);
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
            float4 texel = Atlas.Sample(Sampler, input.uv);

            // The whole outline mechanism. Black in the texture survives any tint.
            return float4(texel.rgb * input.col.rgb, texel.a * input.col.a);
        }
        """;

    private ComPtr<ID3D11VertexShader> _vertexShader;
    private ComPtr<ID3D11PixelShader> _pixelShader;
    private ComPtr<ID3D11InputLayout> _layout;
    private ComPtr<ID3D11Buffer> _vertices;
    private ComPtr<ID3D11SamplerState> _sampler;
    private ComPtr<ID3D11BlendState> _blend;
    private ComPtr<ID3D11Texture2D> _atlas;
    private ComPtr<ID3D11ShaderResourceView> _atlasView;

    private int _vertexCapacity;
    private int _atlasWidth;
    private int _atlasHeight;

    private HudRenderer(
        ComPtr<ID3D11VertexShader> vertexShader,
        ComPtr<ID3D11PixelShader> pixelShader,
        ComPtr<ID3D11InputLayout> layout,
        ComPtr<ID3D11SamplerState> sampler,
        ComPtr<ID3D11BlendState> blend)
    {
        _vertexShader = vertexShader;
        _pixelShader = pixelShader;
        _layout = layout;
        _sampler = sampler;
        _blend = blend;
    }

    /// <summary>Whether an atlas has been uploaded, so there is anything to draw with.</summary>
    public bool HasAtlas => _atlasView.Handle is not null;

    /// <summary>Compiles the shaders and creates the sampler and blend state.</summary>
    /// <param name="device">Device to create resources on.</param>
    /// <returns>The renderer.</returns>
    public static HudRenderer Create(ComPtr<ID3D11Device> device)
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
        byte* texture = (byte*)SilkMarshal.StringToPtr("TEXCOORD");
        byte* colour = (byte*)SilkMarshal.StringToPtr("COLOR");

        InputElementDesc[] elements =
        [
            new()
            {
                SemanticName = position,
                Format = Silk.NET.DXGI.Format.FormatR32G32Float,
                AlignedByteOffset = 0,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = texture,
                Format = Silk.NET.DXGI.Format.FormatR32G32Float,
                AlignedByteOffset = sizeof(float) * 2,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = colour,
                Format = Silk.NET.DXGI.Format.FormatR32G32B32A32Float,
                AlignedByteOffset = sizeof(float) * 4,
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

        // Point and clamp. Filtering can only blur a glyph drawn at its own size, and clamping stops
        // a quad on the atlas edge sampling the glyph on the far side.
        SamplerDesc samplerDescription = new()
        {
            Filter = Filter.MinMagMipPoint,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            ComparisonFunc = ComparisonFunc.Never,
            MaxLOD = float.MaxValue,
        };

        ComPtr<ID3D11SamplerState> sampler = default;
        SilkMarshal.ThrowHResult(device.CreateSamplerState(in samplerDescription, ref sampler));

        // `BT_BLEND` from BaseShader.h - src * srcAlpha + dst * (1-srcAlpha) - which is what a
        // non-additive VGUI text draw is. Reused rather than rebuilt so the factors stay in the one
        // place that cites Valve's own definition.
        BlendDesc blendDescription = BlendStates.Translucent;

        ComPtr<ID3D11BlendState> blend = default;
        SilkMarshal.ThrowHResult(device.CreateBlendState(in blendDescription, ref blend));

        return new HudRenderer(vertexShader, pixelShader, layout, sampler, blend);
    }

    /// <summary>Uploads a glyph atlas, replacing whichever one was there.</summary>
    /// <param name="device">Device to create the texture on.</param>
    /// <param name="pixels">RGBA, <c>width * height * 4</c> bytes, row-major.</param>
    /// <param name="width">Atlas width in pixels.</param>
    /// <param name="height">Atlas height in pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">The size does not match the pixels.</exception>
    public void SetAtlas(ComPtr<ID3D11Device> device, ReadOnlySpan<byte> pixels, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (pixels.Length != width * height * 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixels),
                $"{pixels.Length} bytes for a {width}x{height} RGBA atlas, expected {width * height * 4}.");
        }

        _atlasView.Dispose();
        _atlas.Dispose();
        _atlasView = default;
        _atlas = default;

        Texture2DDesc description = new()
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Silk.NET.DXGI.Format.FormatR8G8B8A8Unorm,
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.ShaderResource,
        };

        ComPtr<ID3D11Texture2D> texture = default;

        fixed (byte* first = pixels)
        {
            SubresourceData initial = new()
            {
                PSysMem = first,
                SysMemPitch = (uint)(width * 4),
            };

            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, in initial, ref texture));
        }

        ComPtr<ID3D11ShaderResourceView> view = default;
        SilkMarshal.ThrowHResult(device.CreateShaderResourceView(
            texture, ref Unsafe.NullRef<ShaderResourceViewDesc>(), ref view));

        _atlas = texture;
        _atlasView = view;
        _atlasWidth = width;
        _atlasHeight = height;
    }

    /// <summary>Draws quads into the bound render target.</summary>
    /// <param name="device">Device, for growing the vertex buffer.</param>
    /// <param name="context">Context to issue the draw on.</param>
    /// <param name="quads">What to draw, in screen pixels.</param>
    /// <param name="viewportWidth">Render target width in pixels.</param>
    /// <param name="viewportHeight">Render target height in pixels.</param>
    /// <exception cref="ArgumentNullException"><paramref name="quads"/> is null.</exception>
    /// <remarks>
    /// **Silently does nothing without an atlas or without quads**, because both are ordinary: the
    /// meter is off most of the time, and the frame loop should not have to ask.
    /// </remarks>
    public void Draw(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        IReadOnlyList<HudQuad> quads,
        int viewportWidth,
        int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(quads);

        if (quads.Count == 0 || !HasAtlas || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        float[] vertices = BuildVertices(
            quads, viewportWidth, viewportHeight, _atlasWidth, _atlasHeight);

        EnsureCapacity(device, quads.Count);
        Upload(context, vertices);

        uint stride = VertexStride;
        uint offset = 0;

        context.IASetInputLayout(_layout);
        context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context.IASetVertexBuffers(0, 1, ref _vertices, in stride, in offset);
        context.VSSetShader(_vertexShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShader(_pixelShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShaderResources(0, 1, ref _atlasView);
        context.PSSetSamplers(0, 1, ref _sampler);

        Span<float> factor = [1f, 1f, 1f, 1f];

        fixed (float* blendFactor = factor)
        {
            context.OMSetBlendState(_blend, blendFactor, 0xFFFFFFFF);
        }

        context.Draw((uint)(quads.Count * VerticesPerQuad), 0);
    }

    /// <summary>Turns screen rectangles into clip-space triangles.</summary>
    /// <param name="quads">What to draw.</param>
    /// <param name="viewportWidth">Render target width.</param>
    /// <param name="viewportHeight">Render target height.</param>
    /// <param name="atlasWidth">Atlas width.</param>
    /// <param name="atlasHeight">Atlas height.</param>
    /// <returns>Interleaved position, texture and colour.</returns>
    /// <remarks>
    /// **Internal rather than private so it can be measured without a device.** Where a quad lands
    /// is arithmetic, and arithmetic that only a GPU can check is arithmetic nobody checks.
    ///
    /// Y is flipped: screen coordinates grow downward and clip space grows upward.
    /// </remarks>
    internal static float[] BuildVertices(
        IReadOnlyList<HudQuad> quads,
        int viewportWidth,
        int viewportHeight,
        int atlasWidth,
        int atlasHeight)
    {
        ArgumentNullException.ThrowIfNull(quads);

        float[] data = new float[quads.Count * VerticesPerQuad * FloatsPerVertex];
        int at = 0;

        foreach (HudQuad quad in quads)
        {
            float left = ((quad.X / (float)viewportWidth) * 2f) - 1f;
            float right = (((quad.X + quad.Width) / (float)viewportWidth) * 2f) - 1f;
            float top = 1f - ((quad.Y / (float)viewportHeight) * 2f);
            float bottom = 1f - (((quad.Y + quad.Height) / (float)viewportHeight) * 2f);

            float u0 = quad.SourceX / (float)atlasWidth;
            float u1 = (quad.SourceX + quad.Width) / (float)atlasWidth;
            float v0 = quad.SourceY / (float)atlasHeight;
            float v1 = (quad.SourceY + quad.Height) / (float)atlasHeight;

            Vector4 colour = new(
                quad.Red / 255f, quad.Green / 255f, quad.Blue / 255f, quad.Alpha / 255f);

            // Two triangles: (tl, tr, br) and (tl, br, bl).
            Append(data, ref at, left, top, u0, v0, colour);
            Append(data, ref at, right, top, u1, v0, colour);
            Append(data, ref at, right, bottom, u1, v1, colour);
            Append(data, ref at, left, top, u0, v0, colour);
            Append(data, ref at, right, bottom, u1, v1, colour);
            Append(data, ref at, left, bottom, u0, v1, colour);
        }

        return data;
    }

    private static void Append(
        float[] data, ref int at, float x, float y, float u, float v, Vector4 colour)
    {
        data[at++] = x;
        data[at++] = y;
        data[at++] = u;
        data[at++] = v;
        data[at++] = colour.X;
        data[at++] = colour.Y;
        data[at++] = colour.Z;
        data[at++] = colour.W;
    }

    private void Upload(ComPtr<ID3D11DeviceContext> context, float[] vertices)
    {
        MappedSubresource mapped = default;
        SilkMarshal.ThrowHResult(context.Map(_vertices, 0, Map.WriteDiscard, 0, ref mapped));

        fixed (float* source = vertices)
        {
            System.Buffer.MemoryCopy(
                source, mapped.PData, vertices.Length * sizeof(float), vertices.Length * sizeof(float));
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

        // Powers of two, so a line that gains a character does not reallocate every frame.
        int capacity = Math.Max(256, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)quadCount));

        BufferDesc description = new()
        {
            ByteWidth = (uint)(capacity * VerticesPerQuad * VertexStride),
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.VertexBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write,
        };

        ComPtr<ID3D11Buffer> buffer = default;
        SilkMarshal.ThrowHResult(device.CreateBuffer(
            in description, ref Unsafe.NullRef<SubresourceData>(), ref buffer));

        _vertices = buffer;
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
                text,
                (nuint)source.Length,
                (byte*)null,
                null,
                ref Unsafe.NullRef<ID3DInclude>(),
                entryPoint,
                profile,
                0,
                0,
                ref bytecode,
                ref errors);

            if (result < 0)
            {
                // The compiler's own message, not just an HRESULT: a shader that fails to compile
                // says exactly which line and why, and losing that turns a typo into a hex code.
                string message = errors.Handle is not null
                    ? SilkMarshal.PtrToString((nint)errors.GetBufferPointer()) ?? "no detail"
                    : "no detail";

                errors.Dispose();
                throw new InvalidOperationException(
                    $"Compiling {entryPoint} ({profile}) failed: {message}");
            }
        }

        errors.Dispose();
        return bytecode;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _atlasView.Dispose();
        _atlas.Dispose();
        _blend.Dispose();
        _sampler.Dispose();
        _vertices.Dispose();
        _layout.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
    }
}
