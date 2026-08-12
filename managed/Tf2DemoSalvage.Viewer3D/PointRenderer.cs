using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>One point to draw, already projected into normalised device coordinates.</summary>
/// <param name="X">Horizontal position, -1 to 1.</param>
/// <param name="Y">Vertical position, -1 to 1.</param>
/// <param name="Red">Red channel, 0 to 1.</param>
/// <param name="Green">Green channel, 0 to 1.</param>
/// <param name="Blue">Blue channel, 0 to 1.</param>
internal readonly record struct ScenePoint(float X, float Y, float Red, float Green, float Blue);

/// <summary>
/// Draws a set of points as small quads.
/// </summary>
/// <remarks>
/// **Quads rather than D3D's point topology.** A <c>PointList</c> draws exactly one pixel per
/// point, which is invisible on a modern display and impossible to assert on reliably. Expanding
/// each point into two triangles on the CPU costs nothing at these counts — a TF2 server tracks a
/// couple of thousand entities at most — and it gives a size that can be scaled later without a
/// geometry shader.
///
/// **It draws into whatever render target it is handed** and owns no swap chain. That is what
/// lets the tests render into an offscreen texture and read the pixels back, with no window, no
/// desktop and no swap chain involved: the same code path the viewport uses, verified by
/// measurement rather than by looking at a screen.
/// </remarks>
internal sealed unsafe class PointRenderer : IDisposable
{
    /// <summary>Vertices per point: two triangles.</summary>
    private const int VerticesPerPoint = 6;

    /// <summary>Bytes per vertex: two floats of position, three of colour.</summary>
    private const int VertexStride = sizeof(float) * 5;

    /// <summary>
    /// A pass-through vertex shader and a flat pixel shader.
    /// </summary>
    /// <remarks>
    /// Positions arrive already in clip space, because the projection is
    /// <see cref="TopDownCamera"/>'s job and it is tested on its own. Keeping the transform out of
    /// the shader means the thing that decides where a player appears is ordinary arithmetic that
    /// can be asserted exactly, rather than something only observable through a GPU.
    /// </remarks>
    private const string ShaderSource = """
        struct VsIn  { float2 pos : POSITION; float3 col : COLOR; };
        struct VsOut { float4 pos : SV_POSITION; float3 col : COLOR; };

        VsOut VsMain(VsIn input)
        {
            VsOut output;
            output.pos = float4(input.pos, 0.0f, 1.0f);
            output.col = input.col;
            return output;
        }

        float4 PsMain(VsOut input) : SV_TARGET
        {
            return float4(input.col, 1.0f);
        }
        """;

    private ComPtr<ID3D11VertexShader> _vertexShader;
    private ComPtr<ID3D11PixelShader> _pixelShader;
    private ComPtr<ID3D11InputLayout> _layout;
    private ComPtr<ID3D11Buffer> _vertices;
    private int _vertexCapacity;

    private PointRenderer(
        ComPtr<ID3D11VertexShader> vertexShader,
        ComPtr<ID3D11PixelShader> pixelShader,
        ComPtr<ID3D11InputLayout> layout)
    {
        _vertexShader = vertexShader;
        _pixelShader = pixelShader;
        _layout = layout;
    }

    /// <summary>Compiles the shaders and prepares the input layout.</summary>
    /// <param name="device">Device to create resources on.</param>
    /// <returns>The renderer.</returns>
    public static PointRenderer Create(ComPtr<ID3D11Device> device)
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
                SemanticName = colour,
                Format = Silk.NET.DXGI.Format.FormatR32G32B32Float,
                AlignedByteOffset = sizeof(float) * 2,
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
        SilkMarshal.Free((nint)colour);
        vertexBytecode.Dispose();
        pixelBytecode.Dispose();

        return new PointRenderer(vertexShader, pixelShader, layout);
    }

    /// <summary>Draws line segments into the bound render target.</summary>
    /// <param name="device">Device, for growing the vertex buffer.</param>
    /// <param name="context">Context to issue the draw on.</param>
    /// <param name="segments">Segments in normalised device coordinates.</param>
    /// <param name="red">Line colour, red channel.</param>
    /// <param name="green">Line colour, green channel.</param>
    /// <param name="blue">Line colour, blue channel.</param>
    /// <exception cref="ArgumentNullException"><paramref name="segments"/> is null.</exception>
    /// <remarks>
    /// The same shaders and vertex format as the points; only the topology and the vertex
    /// building differ. A line list is exactly two vertices per segment, with no closing or
    /// sharing between them - a strip would join unrelated edges of the map into one polyline.
    /// </remarks>
    public void DrawLines(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        IReadOnlyList<((float X, float Y) From, (float X, float Y) To)> segments,
        float red,
        float green,
        float blue)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (segments.Count == 0)
        {
            return;
        }

        float[] vertices = new float[segments.Count * 2 * 5];
        int at = 0;

        foreach (((float X, float Y) from, (float X, float Y) to) in segments)
        {
            Append(vertices, ref at, from.X, from.Y, red, green, blue);
            Append(vertices, ref at, to.X, to.Y, red, green, blue);
        }

        // Two vertices per segment against six per point, so the capacity is expressed in the
        // same unit the buffer is sized in.
        EnsureCapacity(device, ((segments.Count * 2) + VerticesPerPoint - 1) / VerticesPerPoint);
        Upload(context, vertices);

        Bind(context, D3DPrimitiveTopology.D3DPrimitiveTopologyLinelist);
        context.Draw((uint)(segments.Count * 2), 0);
    }

    /// <summary>Draws filled triangles into the bound render target.</summary>
    /// <param name="device">Device, for growing the vertex buffer.</param>
    /// <param name="context">Context to issue the draw on.</param>
    /// <param name="corners">Triangle corners in clip space, three per triangle, in draw order.</param>
    /// <param name="tint">Colour the shade is applied to, as red, green and blue.</param>
    /// <exception cref="ArgumentNullException"><paramref name="corners"/> is null.</exception>
    /// <remarks>
    /// **Draw order is the depth test.** There is no depth buffer, deliberately: a flat overhead
    /// view has one axis that does not participate, so the caller sorts by height and the later
    /// triangle wins. A depth buffer would be a resource to resize on every window change for a
    /// comparison the ordering already makes.
    ///
    /// Each corner carries its own shade rather than the whole call sharing one, so an entire map
    /// is a single draw. Per-surface colours would be one draw per face, which on
    /// <c>cp_process_final</c> is 13,821 of them.
    /// </remarks>
    public void DrawTriangles(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        IReadOnlyList<(float X, float Y, float Shade)> corners,
        (float Red, float Green, float Blue) tint)
    {
        ArgumentNullException.ThrowIfNull(corners);

        if (corners.Count < 3)
        {
            return;
        }

        // Whole triangles only. A trailing pair of corners would be read by the rasteriser as the
        // start of one that has no third vertex.
        int usable = corners.Count - (corners.Count % 3);

        float[] vertices = new float[usable * 5];
        int at = 0;

        for (int index = 0; index < usable; index++)
        {
            (float x, float y, float shade) = corners[index];

            Append(vertices, ref at, x, y, tint.Red * shade, tint.Green * shade, tint.Blue * shade);
        }

        EnsureCapacity(device, (usable + VerticesPerPoint - 1) / VerticesPerPoint);
        Upload(context, vertices);

        Bind(context, D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context.Draw((uint)usable, 0);
    }

    /// <summary>Draws the points into the bound render target.</summary>
    /// <param name="device">Device, for growing the vertex buffer.</param>
    /// <param name="context">Context to issue the draw on.</param>
    /// <param name="points">Points in normalised device coordinates.</param>
    /// <param name="halfSize">Half the width of each point, in NDC units.</param>
    /// <remarks>
    /// The caller binds the render target and viewport. This deliberately does not, so the same
    /// renderer serves the swap chain and an offscreen texture without knowing which it has.
    /// </remarks>
    public void Draw(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        IReadOnlyList<ScenePoint> points,
        float halfSize = 0.02f)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count == 0)
        {
            return;
        }

        float[] vertices = BuildVertices(points, halfSize);
        EnsureCapacity(device, points.Count);
        Upload(context, vertices);

        Bind(context, D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        context.Draw((uint)(points.Count * VerticesPerPoint), 0);
    }

    /// <summary>Copies vertices into the dynamic buffer.</summary>
    /// <remarks>
    /// Discard rather than a partial update: the whole set is rebuilt every frame, and
    /// NoOverwrite would promise the GPU that earlier contents are still in use when they are not.
    /// </remarks>
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

    private void Bind(ComPtr<ID3D11DeviceContext> context, D3DPrimitiveTopology topology)
    {
        uint stride = VertexStride;
        uint offset = 0;

        context.IASetInputLayout(_layout);
        context.IASetPrimitiveTopology(topology);
        context.IASetVertexBuffers(0, 1, ref _vertices, in stride, in offset);
        context.VSSetShader(_vertexShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.PSSetShader(_pixelShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _vertices.Dispose();
        _layout.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
    }

    /// <summary>Expands each point into two triangles.</summary>
    private static float[] BuildVertices(IReadOnlyList<ScenePoint> points, float halfSize)
    {
        float[] data = new float[points.Count * VerticesPerPoint * 5];
        int at = 0;

        foreach (ScenePoint point in points)
        {
            float left = point.X - halfSize;
            float right = point.X + halfSize;
            float bottom = point.Y - halfSize;
            float top = point.Y + halfSize;

            // Two triangles, counter-clockwise: (bl, tl, tr) and (bl, tr, br).
            Append(data, ref at, left, bottom, point);
            Append(data, ref at, left, top, point);
            Append(data, ref at, right, top, point);
            Append(data, ref at, left, bottom, point);
            Append(data, ref at, right, top, point);
            Append(data, ref at, right, bottom, point);
        }

        return data;
    }

    private static void Append(float[] data, ref int at, float x, float y, ScenePoint point) =>
        Append(data, ref at, x, y, point.Red, point.Green, point.Blue);

    private static void Append(
        float[] data, ref int at, float x, float y, float red, float green, float blue)
    {
        data[at++] = x;
        data[at++] = y;
        data[at++] = red;
        data[at++] = green;
        data[at++] = blue;
    }

    private void EnsureCapacity(ComPtr<ID3D11Device> device, int pointCount)
    {
        if (pointCount <= _vertexCapacity && _vertices.Handle is not null)
        {
            return;
        }

        _vertices.Dispose();

        // Grown in powers of two so a scene that gains one entity does not reallocate every frame.
        int capacity = Math.Max(64, (int)BitOperations.RoundUpToPowerOf2((uint)pointCount));

        BufferDesc description = new()
        {
            ByteWidth = (uint)(capacity * VerticesPerPoint * VertexStride),
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
}
