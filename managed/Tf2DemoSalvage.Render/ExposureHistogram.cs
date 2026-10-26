using System;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary>The device half of <c>CLuminanceHistogramSystem</c>: one occlusion query a histogram entry (B575, B514).</summary>
/// <remarks>
/// *Read from published source*, <c>CHistogram_entry_t::IssueQuery</c> (`viewpostprocess.cpp:413-526`) and the shipped
/// <c>dev/lumcompare.vmt</c> (<c>hl2_misc</c>): <c>screenspace_general</c> with <c>luminance_compare_ps20</c> over
/// <c>_rt_FullFrameFB</c>, <c>$ALPHATESTED 1</c>, <c>$DISABLE_COLOR_WRITES 1</c>. The shader outputs
/// <c>step( min, L ) * step( L, max )</c> on the 0.2125/0.7154/0.0721 luminance of the frame times <c>$C0_Z</c>
/// (`luminance_compare_ps2x.fxc:34-48`), and the alpha test is <c>SHADER_ALPHAFUNC_GREATER, 0.0</c>
/// (`screenspace_general.cpp:189-193`), so the query counts exactly the pixels in range. <c>$C0_Z</c> is 1 except under
/// float HDR (`:456-460`); this viewer's exposure walks only under integer HDR, so it is 1 here.
/// <c>mat_tonemapping_occlusion_use_stencil</c> is 0 (`:49`): the query wraps the compare draw itself.
///
/// The region is <c>mat_exposure_center_region_x/y</c>'s centre of the viewport (`:434-453`, `:485-495`), every pixel.
/// The frame is sampled through the sRGB curve, as <c>screenspace_general</c> reads it (finding 76), from a copy of the
/// frame — the engine's <c>UpdateScreenEffectTexture</c> (`:1607-1612`). Here a <c>discard</c> is the alpha test.
///
/// *Interpolated*: that the engine's <c>OcclusionQuery_GetNumPixelsRendered</c> does not flush (<c>shaderapidx9</c>
/// is not published); this reads with <c>D3D11_ASYNC_GETDATA_DONOTFLUSH</c>, and the frame's present flushes.
/// </remarks>
public sealed unsafe class ExposureHistogram : IHistogramQueries, IDisposable
{
    private const string ShaderSource = """
        cbuffer Range : register(b0) { float4 range; };
        Texture2D<float4> frame : register(t0);

        float4 VsFull(uint id : SV_VertexID) : SV_POSITION
        {
            float2 corner = float2((id << 1) & 2, id & 2);
            return float4(corner * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
        }

        float4 PsCompare(float4 position : SV_POSITION) : SV_TARGET
        {
            float luminance = dot(frame.Load(int3(position.xy, 0)).rgb, float3(0.2125f, 0.7154f, 0.0721f));

            // The alpha test, GREATER than 0, of step( min, L ) * step( L, max ).
            clip(step(range.x, luminance) * step(luminance, range.y) - 0.5f);
            return 0.0f;
        }
        """;

    private readonly ComPtr<ID3D11Query>[] _queries = new ComPtr<ID3D11Query>[LuminanceHistogramSystem.Entries];

    private ComPtr<ID3D11DeviceContext> _context;
    private ComPtr<ID3D11VertexShader> _vertex;
    private ComPtr<ID3D11PixelShader> _compare;
    private ComPtr<ID3D11Buffer> _constants;
    private ComPtr<ID3D11BlendState> _noColour;
    private ComPtr<ID3D11DepthStencilState> _noDepth;
    private ComPtr<ID3D11RasterizerState> _noCull;

    private (int Width, int Height) _size;
    private ComPtr<ID3D11Texture2D> _copy;
    private ComPtr<ID3D11ShaderResourceView> _copyView;

    private ExposureHistogram()
    {
    }

    /// <summary>Wait for each answer rather than taking none: a test target has no later frame to read it in.</summary>
    public bool Wait { get; set; }

    /// <summary>Compiles the compare pass and makes its fixed state.</summary>
    /// <param name="device">The device.</param>
    /// <returns>The histogram's device half, with no query issued.</returns>
    public static ExposureHistogram Create(ComPtr<ID3D11Device> device)
    {
        ExposureHistogram? histogram = new();

        try
        {
            using D3DCompiler compiler = D3DCompiler.GetApi();

            histogram._vertex = BloomRenderer.Vertex(device, compiler, ShaderSource, "VsFull");
            histogram._compare = BloomRenderer.Pixel(device, compiler, ShaderSource, "PsCompare");

            BufferDesc buffer = new()
            {
                ByteWidth = 4 * sizeof(float),
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.ConstantBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
            };
            SilkMarshal.ThrowHResult(device.CreateBuffer(in buffer, null, ref histogram._constants));

            // `$DISABLE_COLOR_WRITES 1`.
            BlendDesc noColour = default;
            noColour.RenderTarget[0].RenderTargetWriteMask = 0;
            SilkMarshal.ThrowHResult(device.CreateBlendState(in noColour, ref histogram._noColour));

            DepthStencilDesc depth = default;
            SilkMarshal.ThrowHResult(device.CreateDepthStencilState(in depth, ref histogram._noDepth));

            RasterizerDesc raster = new() { FillMode = FillMode.Solid, CullMode = CullMode.None, DepthClipEnable = 1 };
            SilkMarshal.ThrowHResult(device.CreateRasterizerState(in raster, ref histogram._noCull));

            QueryDesc occlusion = new() { Query = Query.Occlusion };

            for (int i = 0; i < histogram._queries.Length; i++)
            {
                SilkMarshal.ThrowHResult(device.CreateQuery(in occlusion, ref histogram._queries[i]));
            }

            ExposureHistogram built = histogram;
            histogram = null;
            return built;
        }
        finally
        {
            histogram?.Dispose();
        }
    }

    /// <summary>One frame of <c>CLuminanceHistogramSystem::Update</c> against the frame just drawn.</summary>
    /// <param name="device">The device.</param>
    /// <param name="context">The context; its target, viewport and states are left for the caller to restore.</param>
    /// <param name="frame">The frame, a <c>B8G8R8A8</c> texture.</param>
    /// <param name="target">A view of any target the frame's size, bound with colour writes off.</param>
    /// <param name="width">The frame's width.</param>
    /// <param name="height">The frame's height.</param>
    /// <param name="system">The histogram's states, which decide what is read and issued.</param>
    /// <exception cref="ArgumentNullException"><paramref name="system"/> is null.</exception>
    public void Measure(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        ComPtr<ID3D11Texture2D> frame,
        ComPtr<ID3D11RenderTargetView> target,
        int width,
        int height,
        LuminanceHistogramSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        int skipX = (int)(width * 0.5f * (1f - AutoExposure.CentreRegionX));
        int skipY = (int)(height * 0.5f * (1f - AutoExposure.CentreRegionY));

        if (width - (2 * skipX) <= 0 || height - (2 * skipY) <= 0)
        {
            return;
        }

        EnsureCopy(device, width, height);
        context.CopyResource(_copy, frame);

        float* factor = stackalloc float[4] { 1f, 1f, 1f, 1f };
        Viewport region = new(skipX, skipY, width - (2 * skipX), height - (2 * skipY), 0f, 1f);

        context.IASetInputLayout(default(ComPtr<ID3D11InputLayout>));
        context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context.VSSetShader(_vertex, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShader(_compare, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetConstantBuffers(0, 1, ref _constants);
        context.OMSetRenderTargets(1, ref target, default(ComPtr<ID3D11DepthStencilView>));
        context.PSSetShaderResources(0, 1, ref _copyView);
        context.OMSetBlendState(_noColour, factor, 0xFFFFFFFF);
        context.OMSetDepthStencilState(_noDepth, 0);
        context.RSSetState(_noCull);
        context.RSSetViewports(1, in region);

        _context = context;
        system.Update(this);
        _context = default;

        ComPtr<ID3D11ShaderResourceView> none = default;
        context.PSSetShaderResources(0, 1, ref none);
        context.OMSetBlendState(default(ComPtr<ID3D11BlendState>), factor, 0xFFFFFFFF);
    }

    /// <inheritdoc/>
    void IHistogramQueries.Issue(int entry, float min, float max)
    {
        MappedSubresource mapped = default;
        SubmitCounters.Upload();
        SilkMarshal.ThrowHResult(_context.Map(_constants, 0, Map.WriteDiscard, 0, ref mapped));
        float* into = (float*)mapped.PData;
        (into[0], into[1], into[2], into[3]) = (min, max, 1f, 0f);
        _context.Unmap(_constants, 0);

        ID3D11Asynchronous* query = (ID3D11Asynchronous*)_queries[entry].Handle;
        _context.Begin(query);
        SubmitCounters.Draw();
        _context.Draw(3, 0);
        _context.End(query);
    }

    /// <inheritdoc/>
    int? IHistogramQueries.TryRead(int entry)
    {
        ulong samples = 0;
        int result;

        do
        {
            result = _context.GetData(
                (ID3D11Asynchronous*)_queries[entry].Handle, &samples, sizeof(ulong), Wait ? 0u : (uint)AsyncGetdataFlag.Donotflush);
            SilkMarshal.ThrowHResult(result);
        }
        while (result != 0 && Wait);

        return result == 0 ? (int)Math.Min(samples, int.MaxValue) : null;
    }

    private void EnsureCopy(ComPtr<ID3D11Device> device, int width, int height)
    {
        if (_size == (width, height))
        {
            return;
        }

        ReleaseCopy();
        _size = (width, height);

        // Typeless, so the copy is read through the sRGB curve whatever view the frame itself was written through.
        Texture2DDesc copy = new()
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Silk.NET.DXGI.Format.FormatB8G8R8A8Typeless,
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.ShaderResource,
        };
        SilkMarshal.ThrowHResult(device.CreateTexture2D(in copy, null, ref _copy));

        ShaderResourceViewDesc linear = new()
        {
            Format = Silk.NET.DXGI.Format.FormatB8G8R8A8UnormSrgb,
            ViewDimension = Silk.NET.Core.Native.D3DSrvDimension.D3DSrvDimensionTexture2D,
        };
        linear.Texture2D.MipLevels = 1;
        SilkMarshal.ThrowHResult(device.CreateShaderResourceView(_copy, in linear, ref _copyView));
    }

    private void ReleaseCopy()
    {
        _copyView.Dispose();
        _copy.Dispose();
        _copyView = default;
        _copy = default;
        _size = default;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        ReleaseCopy();

        for (int i = 0; i < _queries.Length; i++)
        {
            _queries[i].Dispose();
            _queries[i] = default;
        }

        _vertex.Dispose();
        _compare.Dispose();
        _constants.Dispose();
        _noColour.Dispose();
        _noDepth.Dispose();
        _noCull.Dispose();
    }
}
