using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

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
internal readonly record struct WorldVertex(
    float X, float Y, float Depth, float U, float V, float LightU, float LightV, float Alpha);

/// <summary>A run of triangles sharing one texture.</summary>
/// <param name="MaterialIndex">Which material, indexed into the map's table.</param>
/// <param name="FirstVertex">Where the run starts.</param>
/// <param name="VertexCount">How many vertices it covers.</param>
internal readonly record struct WorldBatch(int MaterialIndex, int FirstVertex, int VertexCount);

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
    /// <summary>Bytes per vertex: three of position, two of texture, two of lightmap, one blend.</summary>
    private const int VertexStride = sizeof(float) * 8;

    private const string ShaderSource = """
        struct VsIn
        {
            float3 pos : POSITION;
            float2 uv  : TEXCOORD0;
            float2 luv : TEXCOORD1;
            float  a   : TEXCOORD2;
        };

        struct VsOut
        {
            float4 pos : SV_POSITION;
            float2 uv  : TEXCOORD0;
            float2 luv : TEXCOORD1;
            float  a   : TEXCOORD2;
        };

        Texture2D    albedoMap   : register(t0);
        Texture2D    lightMap    : register(t1);
        Texture2D    blendMap    : register(t2);
        SamplerState wrapSampler : register(s0);
        SamplerState clampSampler: register(s1);

        VsOut VsMain(VsIn input)
        {
            VsOut output;
            output.pos = float4(input.pos, 1.0f);
            output.uv = input.uv;
            output.luv = input.luv;
            output.a = input.a;
            return output;
        }

        float4 PsMain(VsOut input) : SV_TARGET
        {
            // **Two textures mixed by the vertex's alpha, which is what terrain is.** A
            // WorldVertexTransition material carries dirt and grass, and a displacement's vertices
            // say how much of each. Where a material has only one texture the second is bound to
            // the same image, so the mix is an identity and costs a sample.
            float4 first = albedoMap.Sample(wrapSampler, input.uv);
            float4 second = blendMap.Sample(wrapSampler, input.uv);
            float4 albedo = lerp(first, second, saturate(input.a));
            float3 light = lightMap.Sample(clampSampler, input.luv).rgb;

            // **No doubling here.** Source's own shaders multiply an LDR lightmap by two, but that
            // applies to the raw linear samples. BspLightmaps has already taken the sample through
            // its exponent and the gamma curve into display space, so doubling again is the second
            // half of a scaling that was already applied - measured as a map washed out to white.
            return float4(albedo.rgb * light, albedo.a);
        }
        """;

    private ComPtr<ID3D11VertexShader> _vertexShader;
    private ComPtr<ID3D11PixelShader> _pixelShader;
    private ComPtr<ID3D11InputLayout> _layout;
    private ComPtr<ID3D11Buffer> _vertices;
    private ComPtr<ID3D11SamplerState> _wrapSampler;
    private ComPtr<ID3D11SamplerState> _clampSampler;

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
    private readonly List<ComPtr<ID3D11ShaderResourceView>> _blendTextures = [];
    private ComPtr<ID3D11ShaderResourceView> _lightmap;
    private ComPtr<ID3D11ShaderResourceView> _white;

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

        return new WorldRenderer(
            vertexShader,
            pixelShader,
            layout,
            Sampler(device, TextureAddressMode.Wrap),
            Sampler(device, TextureAddressMode.Clamp))
        {
            _bothSides = bothSides,
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

        // A one-pixel white texture stands in for a material whose texture could not be found, so
        // the face still takes its lighting and its shape rather than vanishing.
        _white = CreateTexture(device, context, 1, 1, [255, 255, 255, 255]);

        foreach (MapTexture? texture in assets.Textures)
        {
            _textures.Add(texture is { } present
                ? CreateTexture(device, context, present.Width, present.Height, present.Pixels.Span)
                : default);
        }

        foreach (MapTexture? texture in assets.BlendTextures)
        {
            _blendTextures.Add(texture is { } present
                ? CreateTexture(device, context, present.Width, present.Height, present.Pixels.Span)
                : default);
        }

        _lightmap = CreateTexture(
            device, context, assets.Lightmaps.Width, assets.Lightmaps.Height, assets.Lightmaps.Pixels);
    }

    /// <summary>Uploads a map's projected triangles, replacing anything already there.</summary>
    /// <param name="device">Device to create the vertex buffer on.</param>
    /// <param name="vertices">Every triangle corner, already in clip space.</param>
    /// <param name="batches">The runs, one per material.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void UploadGeometry(
        ComPtr<ID3D11Device> device,
        IReadOnlyList<WorldVertex> vertices,
        IReadOnlyList<WorldBatch> batches)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(batches);

        ReleaseGeometry();

        if (vertices.Count == 0)
        {
            return;
        }

        float[] data = new float[vertices.Count * 8];
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
        }

        CreateVertexBuffer(device, data);

        _batches = batches;
    }

    /// <summary>Whether textures have been uploaded for a map.</summary>
    public bool HasTextures => _lightmap.Handle is not null;

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
        context.PSSetShaderResources(1, 1, ref _lightmap);

        foreach (WorldBatch batch in _batches)
        {
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

            context.PSSetShaderResources(0, 1, ref texture);
            context.PSSetShaderResources(2, 1, ref blend);
            context.Draw((uint)batch.VertexCount, (uint)batch.FirstVertex);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        ReleaseMap();
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
                 _textures.Concat(_blendTextures).Where(texture => texture.Handle is not null))
        {
            texture.Dispose();
        }

        _textures.Clear();
        _blendTextures.Clear();

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
    private static ComPtr<ID3D11ShaderResourceView> CreateTexture(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        int width,
        int height,
        ReadOnlySpan<byte> pixels)
    {
        Texture2DDesc description = new()
        {
            Width = (uint)width,
            Height = (uint)height,

            // Zero means "every level down to 1x1", which the driver fills in.
            MipLevels = 0,
            ArraySize = 1,
            Format = Silk.NET.DXGI.Format.FormatR8G8B8A8Unorm,
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
