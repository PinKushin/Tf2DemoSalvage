using System;
using System.Numerics;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary>One sky face's pixel shader, <c>c0</c> and <c>g_vTextureSizeInfo</c> (B461).</summary>
/// <param name="Shader">The pixel shader.</param>
/// <param name="InputScale"><c>c0</c>.</param>
/// <param name="TexelInfo">The vertex shader's half-texel offsets and texture size.</param>
public readonly record struct SkyFaceDraw(
    SkyPixelShader Shader, (float Red, float Green, float Blue) InputScale, (float X, float Y, float W, float H) TexelInfo)
{
    /// <summary>The draw state for a face as the sky shader sets it up.</summary>
    /// <param name="face">The loaded face.</param>
    /// <returns>Its draw; <c>sky_ps2x</c> at a scale of one when the face carries no sky shading.</returns>
    public static SkyFaceDraw For(MapTexture face) =>
        face.Sky is { } sky
            ? new SkyFaceDraw(sky.Shader, sky.InputScale, SkySurface.TexelInfo(sky.Shader, face.Width, face.Height))
            : Plain;

    /// <summary><c>sky_ps2x</c> at a scale of one, for a face given no shading.</summary>
    public static SkyFaceDraw Plain { get; } = new(SkyPixelShader.Sky, (1f, 1f, 1f), default);
}

/// <summary>
/// Draws the 2D skybox — six textured quads around the eye.
/// </summary>
/// <remarks>
/// **What a TF2 map has behind its horizon.** This viewer threw every <c>SURF_SKY</c> face away at
/// read time under a comment calling the skybox *"irrelevant to a map overview"*, so the colour
/// behind the world was the window's clear colour (B303).
///
/// **The `sky` shader forces two flags whatever the VMT says** — `SHADER_INIT_PARAMS` in
/// <c>sky_dx9.cpp:28</c>:
///
/// <code>
///   SET_FLAGS( MATERIAL_VAR_NOFOG );
///   SET_FLAGS( MATERIAL_VAR_IGNOREZ );
/// </code>
///
/// so the shipped `"$nofog" "1"` and `"$ignorez" "1"` in every sky VMT are redundant rather than
/// load-bearing. `IGNOREZ` is what puts the sky behind everything: it neither tests nor writes
/// depth, so draw ORDER decides, and this draws first.
///
/// **The box follows the eye.** A sky is infinitely far away, so moving must not change it and
/// only turning may — which is a translation of the box to the camera, applied on the CPU because
/// the vertices are rebuilt only when the eye moves.
///
/// **Its own shader rather than the world's**, because the world's samples a lightmap and the sky
/// has none. Six draws, one per face, each binding its own texture — which is also what makes the
/// face-to-direction mapping visible rather than buried in a cube sampler.
/// </remarks>
public sealed unsafe class SkyboxRenderer : IDisposable
{
    /// <summary>Position and texture coordinate.</summary>
    private const int VertexStride = sizeof(float) * 5;

    /// <summary>Sixteen floats of camera, in the slot the other renderers use.</summary>
    private const int CameraConstants = 16;

    /// <summary>The camera, then <c>inputScale</c>, <c>texelInfo</c> and <c>shading</c>, rewritten per face.</summary>
    private const int BufferConstants = CameraConstants + 12;

    /// <summary>The constant buffer slot, matching <c>WorldRenderer</c>'s.</summary>
    private const uint CameraSlot = 4;

    /// <summary>Corners in the whole box: six faces of two triangles.</summary>
    private const int Corners = SkyboxGeometry.Faces * SkyboxGeometry.CornersPerFace;

    /// <summary>
    /// **`row_major`, multiplied from the LEFT**, matching every other renderer here and
    /// `FreeCamera.ToMatrix`. A second convention would be the third in a project that deliberately
    /// keeps two.
    /// </summary>
    private const string ShaderSource = """
        cbuffer Camera : register(b4)
        {
            row_major float4x4 viewProjection;
            float4 inputScale;  // c0: $color with the shader's own factor (sky_hdr_dx9.cpp:208-229, :268-279)
            float4 texelInfo;   // g_vTextureSizeInfo: RGBS half-texel offsets and size, else zero (:220-224, :244)
            float4 shading;     // x the pixel shader (SkyPixelShader), y LINEAR_LIGHT_SCALE
        };

        Texture2D face : register(t0);
        SamplerState linearClamp : register(s0);

        struct VsIn  { float3 pos : POSITION; float2 uv : TEXCOORD0; };

        // sky_vs20.fxc's outputs: the four taps and the first one in texels.
        struct VsOut
        {
            float4 pos : SV_POSITION;
            float2 t00 : TEXCOORD0;
            float2 t01 : TEXCOORD1;
            float2 t10 : TEXCOORD2;
            float2 t11 : TEXCOORD3;
            float2 inPixels : TEXCOORD4;
        };

        VsOut VsMain(VsIn input)
        {
            VsOut output;
            output.pos = mul(float4(input.pos, 1.0f), viewProjection);

            // The coordinate already carries $basetexturetransform (SkySurface.Coordinate), as sky_vs20.fxc:35-38 dots it.
            float2 uv = input.uv;

            output.t00 = float2(uv.x - texelInfo.x, uv.y - texelInfo.y);
            output.t10 = float2(uv.x + texelInfo.x, uv.y - texelInfo.y);
            output.t01 = float2(uv.x - texelInfo.x, uv.y + texelInfo.y);
            output.t11 = float2(uv.x + texelInfo.x, uv.y + texelInfo.y);
            output.inPixels = output.t00 * texelInfo.zw;
            return output;
        }

        float4 PsMain(VsOut input) : SV_TARGET
        {
            float3 result;

            if (shading.x < 0.5f)
            {
                // sky_ps2x.fxc: one tap at TEXCOORD0, times InputScale.
                result = face.Sample(linearClamp, input.t00).rgb * inputScale.rgb;
            }
            else if (shading.x < 1.5f)
            {
                // sky_hdr_compressed_rgbs_ps2x.fxc, the PC branch verbatim: each tap rgb *= a, then a bilinear lerp.
                float4 s00 = face.Sample(linearClamp, input.t00);
                float4 s10 = face.Sample(linearClamp, input.t10);
                float4 s01 = face.Sample(linearClamp, input.t01);
                float4 s11 = face.Sample(linearClamp, input.t11);
                float2 fracCoord = frac(input.inPixels);

                s00.rgb *= s00.a;
                s10.rgb *= s10.a;
                s00.xyz = lerp(s00, s10, fracCoord.x).xyz;
                s01.rgb *= s01.a;
                s11.rgb *= s11.a;
                s01.xyz = lerp(s01, s11, fracCoord.x).xyz;
                result = lerp(s00, s01, fracCoord.y).xyz * inputScale.rgb;
            }
            else
            {
                // sky_hdr_compressed_ps2x.fxc as published: its blend of three exposures is commented out and it returns red.
                result = float3(1.0f, 0.0f, 0.0f);
            }

            // FinalOutput( ..., PIXEL_FOG_TYPE_NONE, TONEMAP_SCALE_LINEAR ) (common_ps_fxc.h:345-350): never fogged.
            return float4(result * shading.y, 1.0f);
        }
        """;

    private ComPtr<ID3D11VertexShader> _vertexShader;
    private ComPtr<ID3D11PixelShader> _pixelShader;
    private ComPtr<ID3D11InputLayout> _layout;
    private ComPtr<ID3D11Buffer> _vertices;
    private ComPtr<ID3D11Buffer> _camera;
    private ComPtr<ID3D11SamplerState> _sampler;
    private ComPtr<ID3D11DepthStencilState> _ignoreZ;
    private ComPtr<ID3D11RasterizerState> _noCull;

    private SkyboxRenderer(
        ComPtr<ID3D11VertexShader> vertexShader,
        ComPtr<ID3D11PixelShader> pixelShader,
        ComPtr<ID3D11InputLayout> layout)
    {
        _vertexShader = vertexShader;
        _pixelShader = pixelShader;
        _layout = layout;
    }

    /// <summary>Compiles the shaders and builds the input layout.</summary>
    /// <param name="device">The device to create against.</param>
    /// <returns>A renderer ready to draw.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is null.</exception>
    public static SkyboxRenderer Create(ComPtr<ID3D11Device> device)
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

        vertexBytecode.Dispose();
        pixelBytecode.Dispose();

        return new SkyboxRenderer(vertexShader, pixelShader, layout);
    }

    /// <summary>The six faces' textures, in <c>SkyboxGeometry</c>'s order.</summary>
    /// <param name="faces">
    /// One shader resource view per face, or an empty span for a map whose sky would not load.
    /// </param>
    /// <remarks>
    /// **A method rather than a property because the caller owns the views' lifetime**, and a
    /// property returning the array would hand out a mutable reference to state this type draws
    /// from every frame.
    /// </remarks>
    /// <param name="transforms">Each face's `$basetexturetransform`, or null for none — applied as `sky_vs20.fxc` applies it.</param>
    /// <param name="draws">Each face's shader and constants (B461), or null for <c>sky_ps2x</c> at a scale of one.</param>
    public void SetFaces(
        ReadOnlySpan<ComPtr<ID3D11ShaderResourceView>> faces,
        System.Collections.Generic.IReadOnlyList<TextureTransform?>? transforms = null,
        System.Collections.Generic.IReadOnlyList<SkyFaceDraw>? draws = null)
    {
        _faces = faces.Length == SkyboxGeometry.Faces ? faces.ToArray() : [];
        _transforms = transforms is { Count: SkyboxGeometry.Faces } given ? [.. given] : new TextureTransform?[SkyboxGeometry.Faces];
        _draws = draws is { Count: SkyboxGeometry.Faces } stated ? [.. stated] : PlainDraws();
    }

    /// <summary>Uploads six loaded faces and draws with them from now on.</summary>
    /// <param name="device">The device.</param>
    /// <param name="context">The context.</param>
    /// <param name="faces">The six faces, from <c>MapAssets.SkyFaces</c>.</param>
    /// <param name="held">Receives the views, which the caller owns and disposes.</param>
    /// <remarks>Sampler zero reads through the sRGB curve only where the sky shader asks it to (B461) — never for RGBS.</remarks>
    internal void UploadFaces(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        System.Collections.Generic.IReadOnlyList<MapTexture?> faces,
        System.Collections.Generic.List<ComPtr<ID3D11ShaderResourceView>> held)
    {
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentNullException.ThrowIfNull(held);

        foreach (MapTexture? face in faces)
        {
            held.Add(WorldRenderer.UploadTexture(device, context, face, face?.Sky?.Srgb ?? true));
        }

        SetFaces(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(held),
            [.. System.Linq.Enumerable.Select(faces, static face => face?.BaseTransform)],
            [.. System.Linq.Enumerable.Select(faces, static face => face is { } present ? SkyFaceDraw.For(present) : SkyFaceDraw.Plain)]);
    }

    private static SkyFaceDraw[] PlainDraws() => [.. System.Linq.Enumerable.Repeat(SkyFaceDraw.Plain, SkyboxGeometry.Faces)];

    private TextureTransform?[] _transforms = new TextureTransform?[SkyboxGeometry.Faces];
    private SkyFaceDraw[] _draws = PlainDraws();

    /// <summary>Whether there is a sky to draw.</summary>
    public bool HasSky => _faces.Length == SkyboxGeometry.Faces;

    private ComPtr<ID3D11ShaderResourceView>[] _faces = [];

    /// <summary>Draws the box around the eye.</summary>
    /// <param name="device">The device.</param>
    /// <param name="context">The device context.</param>
    /// <param name="eye">Where the camera is; the box is centred here.</param>
    /// <param name="viewProjection">The camera, row major, sixteen floats.</param>
    /// <param name="reach">How far from the eye to put the box.</param>
    /// <param name="linearLightScale"><c>LINEAR_LIGHT_SCALE</c>: a quarter in an integer-HDR reflection view, else one (B62).</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewProjection"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="viewProjection"/> is not sixteen floats.</exception>
    /// <remarks>
    /// **Nothing is drawn when a face is missing**, rather than a chequer or a colour. A sky that
    /// failed to load should look like the sky that was there before this existed — the clear
    /// colour — because the alternative is a magenta dome over every map whose sky this reader
    /// cannot open.
    /// </remarks>
    public void Draw(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        (float X, float Y, float Z) eye,
        float[] viewProjection,
        float reach,
        float linearLightScale)
    {
        ArgumentNullException.ThrowIfNull(viewProjection);

        if (viewProjection.Length != CameraConstants)
        {
            throw new ArgumentException(
                "A camera matrix is sixteen floats.", nameof(viewProjection));
        }

        if (!HasSky)
        {
            return;
        }

        float[] vertices = new float[Corners * 5];
        int at = 0;

        for (int face = 0; face < SkyboxGeometry.Faces; face++)
        {
            foreach (SkyboxGeometry.Corner corner in SkyboxGeometry.Face(face, reach))
            {
                (float u, float v) = SkySurface.Coordinate((corner.U, corner.V), _transforms[face]);

                vertices[at++] = corner.X + eye.X;
                vertices[at++] = corner.Y + eye.Y;
                vertices[at++] = corner.Z + eye.Z;
                vertices[at++] = u;
                vertices[at++] = v;
            }
        }

        EnsureBuffers(device);
        Upload(context, vertices);

        uint stride = VertexStride;
        uint offset = 0;

        context.IASetInputLayout(_layout);
        context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context.IASetVertexBuffers(0, 1, ref _vertices, in stride, in offset);
        context.VSSetShader(_vertexShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShader(_pixelShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetSamplers(0, 1, ref _sampler);

        // **`MATERIAL_VAR_IGNOREZ`, which the shader sets rather than the material** — no depth
        // test and no depth write, so the sky is behind everything by DRAW ORDER and the world
        // painted afterwards covers it without needing to be nearer.
        context.OMSetDepthStencilState(_ignoreZ, 0);

        // **No culling, and this is a correctness choice rather than a lazy one.** A box drawn
        // around the eye cannot occlude itself, so culling saves at most six of twelve triangles —
        // while getting the winding convention backwards costs the entire sky, invisibly. Which
        // winding is front-facing is a property of the RASTERISER (`FrontCounterClockwise`), not of
        // the geometry, and this renderer inherits whatever state the last pass left; the first
        // version set none at all and drew nothing for exactly that reason.
        context.RSSetState(_noCull);

        for (int face = 0; face < SkyboxGeometry.Faces; face++)
        {
            if (_faces[face].Handle is null)
            {
                continue;
            }

            ComPtr<ID3D11ShaderResourceView> bound = _faces[face];

            // The face's own constants — SHADER_DRAW's DYNAMIC_STATE runs per material, and each face is one.
            UploadCamera(device, context, viewProjection, _draws[face], linearLightScale);
            context.VSSetConstantBuffers(CameraSlot, 1, ref _camera);
            context.PSSetConstantBuffers(CameraSlot, 1, ref _camera);
            context.PSSetShaderResources(0, 1, ref bound);
            context.Draw(SkyboxGeometry.CornersPerFace, (uint)(face * SkyboxGeometry.CornersPerFace));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _vertices.Dispose();
        _camera.Dispose();
        _sampler.Dispose();
        _ignoreZ.Dispose();
        _noCull.Dispose();
        _layout.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
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
                string message = errors.Handle is null
                    ? "unknown"
                    : SilkMarshal.PtrToString((nint)errors.GetBufferPointer()) ?? "unknown";

                errors.Dispose();

                throw new InvalidOperationException(
                    $"compiling the skybox {entryPoint} shader: {message}");
            }
        }

        errors.Dispose();

        return bytecode;
    }

    /// <summary>Creates the vertex buffer, sampler and depth state on first use.</summary>
    /// <remarks>
    /// **The box never changes size, so the vertex buffer never grows** — unlike the line
    /// renderer's, which is why this has no capacity to track. Thirty-six corners, always.
    ///
    /// **CLAMP rather than wrap, which is the whole reason the sampler is here.** A sky face
    /// samples right to its own edge, and a wrapping sampler bleeds the opposite edge across the
    /// seam — a bright line along every join, brightest where the sky is brightest.
    /// </remarks>
    private void EnsureBuffers(ComPtr<ID3D11Device> device)
    {
        if (_vertices.Handle is null)
        {
            BufferDesc description = new()
            {
                ByteWidth = (uint)(Corners * VertexStride),
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.VertexBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
            };

            ComPtr<ID3D11Buffer> buffer = default;
            SilkMarshal.ThrowHResult(device.CreateBuffer(
                in description, ref Unsafe.NullRef<SubresourceData>(), ref buffer));

            _vertices = buffer;
        }

        if (_sampler.Handle is null)
        {
            SamplerDesc sampler = new()
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                ComparisonFunc = ComparisonFunc.Never,
                MaxLOD = float.MaxValue,
            };

            ComPtr<ID3D11SamplerState> state = default;
            SilkMarshal.ThrowHResult(device.CreateSamplerState(in sampler, ref state));

            _sampler = state;
        }

        if (_ignoreZ.Handle is null)
        {
            DepthStencilDesc depth = new()
            {
                DepthEnable = 0,
                DepthWriteMask = DepthWriteMask.Zero,
                DepthFunc = ComparisonFunc.Always,
                StencilEnable = 0,
            };

            ComPtr<ID3D11DepthStencilState> state = default;
            SilkMarshal.ThrowHResult(device.CreateDepthStencilState(in depth, ref state));

            _ignoreZ = state;
        }

        if (_noCull.Handle is null)
        {
            RasterizerDesc raster = new()
            {
                FillMode = FillMode.Solid,
                CullMode = CullMode.None,
                DepthClipEnable = 1,
            };

            ComPtr<ID3D11RasterizerState> state = default;
            SilkMarshal.ThrowHResult(device.CreateRasterizerState(in raster, ref state));

            _noCull = state;
        }
    }

    private void Upload(ComPtr<ID3D11DeviceContext> context, float[] vertices)
    {
        MappedSubresource mapped = default;
        SilkMarshal.ThrowHResult(context.Map(_vertices, 0, Map.WriteDiscard, 0, ref mapped));

        fixed (float* source = vertices)
        {
            System.Buffer.MemoryCopy(
                source,
                mapped.PData,
                vertices.Length * sizeof(float),
                vertices.Length * sizeof(float));
        }

        context.Unmap(_vertices, 0);
    }

    private void UploadCamera(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, float[] matrix, SkyFaceDraw draw, float linearLightScale)
    {
        if (_camera.Handle is null)
        {
            BufferDesc description = new()
            {
                ByteWidth = sizeof(float) * BufferConstants,
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.ConstantBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
            };

            ComPtr<ID3D11Buffer> buffer = default;
            SilkMarshal.ThrowHResult(device.CreateBuffer(in description, null, ref buffer));

            _camera = buffer;
        }

        float[] contents =
        [
            .. matrix,
            draw.InputScale.Red, draw.InputScale.Green, draw.InputScale.Blue, 1f,
            draw.TexelInfo.X, draw.TexelInfo.Y, draw.TexelInfo.W, draw.TexelInfo.H,
            (float)draw.Shader, linearLightScale, 0f, 0f,
        ];

        MappedSubresource mapped = default;
        SilkMarshal.ThrowHResult(context.Map(_camera, 0, Map.WriteDiscard, 0, ref mapped));

        fixed (float* source = contents)
        {
            System.Buffer.MemoryCopy(
                source,
                mapped.PData,
                sizeof(float) * BufferConstants,
                sizeof(float) * BufferConstants);
        }

        context.Unmap(_camera, 0);
    }
}
