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
        };

        VsOut VsMain(VsIn input)
        {
            VsOut output;
            output.pos = mul(float4(input.pos, 1.0f), viewProjection);
            output.uv = input.uv;
            output.col = input.col;
            output.next = input.next;
            return output;
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

    /// <summary>Which of the three blends this pass draws with.</summary>
    private SpriteBlend _mode = SpriteBlend.Translucent;
    private ComPtr<ID3D11DepthStencilState> _testNoWrite;
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

    private DetailSpriteRenderer(
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
    /// <returns>A renderer ready to be given geometry.</returns>
    public static DetailSpriteRenderer Create(ComPtr<ID3D11Device> device)
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

        return new DetailSpriteRenderer(vertexShader, pixelShader, layout);
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
        float[] viewProjection)
    {
        ArgumentNullException.ThrowIfNull(viewProjection);

        if (viewProjection.Length != CameraConstants)
        {
            throw new ArgumentException(
                "A camera matrix is sixteen floats.", nameof(viewProjection));
        }

        if (!HasSprites)
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
        context.OMSetBlendState(
            _mode switch
            {
                SpriteBlend.Additive => _additiveBlend,
                SpriteBlend.AddOver => _addOverBlend,
                _ => _blend,
            },
            factor,
            0xFFFFFFFF);
        context.OMSetDepthStencilState(_testNoWrite, 0);
        context.RSSetState(_noCull);

        context.Draw((uint)_corners, 0);

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
        _testNoWrite.Dispose();
        _noCull.Dispose();
        _layout.Dispose();
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

        if (_testNoWrite.Handle is null)
        {
            DepthStencilDesc description = new()
            {
                DepthEnable = 1,
                DepthWriteMask = DepthWriteMask.Zero,
                DepthFunc = ComparisonFunc.Less,
            };

            ComPtr<ID3D11DepthStencilState> depth = default;
            SilkMarshal.ThrowHResult(device.CreateDepthStencilState(in description, ref depth));

            _testNoWrite = depth;
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
