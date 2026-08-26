using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary>Draws debug lines in WORLD space, with depth testing optional.</summary>
/// <remarks>
/// **This is Valve's debug-line shape, and it exists because ours was not.** Every debug overlay in
/// the SDK takes absolute world coordinates and a depth flag —
/// <c>DebugDrawLine( const Vector&amp; vecAbsStart, const Vector&amp; vecAbsEnd, int r, int g, int b,
/// bool test, float duration )</c> and the <c>bool noDepthTest</c> field on the overlay record
/// itself (`game/server/ndebugoverlay.h:24`, <c>:28</c>). The engine never asks a caller to project
/// anything; it transforms on the GPU like every other primitive, and lets each overlay say whether
/// it should be hidden by the world.
///
/// **Ours projected on the CPU and could not be hidden at all.** `MainForm.LeafBoxLines` multiplied
/// eight box corners through the view matrix by hand and handed <see cref="PointRenderer"/> flat
/// clip-space pairs, which are drawn with no depth buffer. That was a divergence chosen by this
/// project rather than by the engine, and it was recorded in a comment instead of being asked
/// about — the owner's standing rule being to assume Valve knew more, every time.
///
/// **Two things follow from doing it Valve's way, and both are improvements.** The caller stops
/// owning a transform it had already got wrong once — the first version indexed the matrix as a
/// column-vector transform and collapsed a room-sized box into "a dot that gets kinda triangular" —
/// and the leaf box can finally be OCCLUDED, which is what makes it describe geometry rather than
/// float over it.
///
/// **Depth is a per-call flag rather than a property of this type**, exactly as Valve has it. A leaf
/// box describes the world and should be hidden by it; a player marker is an annotation about
/// somewhere you cannot see, and hiding it behind a wall would defeat it.
/// </remarks>
public sealed unsafe class WorldLineRenderer : IDisposable
{
    /// <summary>Floats per vertex: three of position, three of colour.</summary>
    private const int VertexStride = sizeof(float) * 6;

    /// <summary>Floats in the camera constant buffer, which holds one matrix.</summary>
    private const int CameraConstants = 16;

    /// <summary>Which vertex-shader constant slot the camera goes in.</summary>
    /// <remarks>
    /// **Slot FOUR, deliberately, and the first version used slot zero — which is a bug this file
    /// would have shipped.** `WorldRenderer` occupies b0 through b3: Camera, Material, Model, Bones.
    /// Binding this camera to b0 overwrites the world's, and `Device3D` already carries the warning
    /// about exactly that — the world camera is set on a view CHANGE rather than per frame, so
    /// "anything that overwrites it has to restore it or the map keeps the wrong projection until
    /// the user next moves".
    ///
    /// **Restoring afterwards would have worked and is the weaker fix**, because it can be
    /// forgotten by the next person and fails silently when it is. An unused slot cannot collide,
    /// so the mistake becomes unavailable rather than merely undone.
    /// </remarks>
    private const uint CameraSlot = 4;

    /// <summary>
    /// Transforms on the GPU, which is the whole point of the type.
    /// </summary>
    /// <remarks>
    /// **`row_major` and multiplied from the LEFT**, matching `WorldRenderer`'s camera buffer and
    /// `FreeCamera.ToMatrix`, which sets `projection[11] = 1`. This project uses two matrix
    /// conventions on purpose and crosses between them in one place; a second convention here would
    /// be the third.
    /// </remarks>
    private const string ShaderSource = """
        cbuffer Camera : register(b4)
        {
            row_major float4x4 viewProjection;
        };

        struct VsIn  { float3 pos : POSITION; float3 col : COLOR; };
        struct VsOut { float4 pos : SV_POSITION; float3 col : COLOR; };

        VsOut VsMain(VsIn input)
        {
            VsOut output;
            output.pos = mul(float4(input.pos, 1.0f), viewProjection);
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
    private ComPtr<ID3D11Buffer> _camera;
    private int _vertexCapacity;

    private WorldLineRenderer(
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
    public static WorldLineRenderer Create(ComPtr<ID3D11Device> device)
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
                Format = Silk.NET.DXGI.Format.FormatR32G32B32Float,
                AlignedByteOffset = 0,
                InputSlotClass = InputClassification.PerVertexData,
            },
            new()
            {
                SemanticName = colour,
                Format = Silk.NET.DXGI.Format.FormatR32G32B32Float,
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
        SilkMarshal.Free((nint)colour);
        vertexBytecode.Dispose();
        pixelBytecode.Dispose();

        return new WorldLineRenderer(vertexShader, pixelShader, layout);
    }

    /// <summary>Draws world-space line segments through the given view-projection.</summary>
    /// <param name="device">Device, for growing the buffers.</param>
    /// <param name="context">Context to issue the draw on.</param>
    /// <param name="segments">The lines, in Source units.</param>
    /// <param name="viewProjection">The camera, row major, sixteen floats.</param>
    /// <param name="red">Colour, 0 to 1.</param>
    /// <param name="green">Colour, 0 to 1.</param>
    /// <param name="blue">Colour, 0 to 1.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="viewProjection"/> is not sixteen floats.</exception>
    /// <remarks>
    /// **The caller sets the depth state, not this method.** Valve's flag lives on the overlay
    /// record rather than inside the drawing code, and the depth states are already owned by
    /// <c>Device3D</c> — so duplicating them here would be a second place for one decision.
    ///
    /// A line list is two vertices per segment with nothing shared: a strip would join the twelve
    /// unrelated edges of a box into one polyline across its diagonal.
    /// </remarks>
    public void Draw(
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        IReadOnlyList<((float X, float Y, float Z) From, (float X, float Y, float Z) To)> segments,
        float[] viewProjection,
        float red,
        float green,
        float blue)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(viewProjection);

        if (viewProjection.Length != CameraConstants)
        {
            throw new ArgumentException(
                "A camera matrix is sixteen floats.", nameof(viewProjection));
        }

        if (segments.Count == 0)
        {
            return;
        }

        float[] vertices = new float[segments.Count * 2 * 6];
        int at = 0;

        foreach (((float X, float Y, float Z) from, (float X, float Y, float Z) to) in segments)
        {
            Append(vertices, ref at, from, red, green, blue);
            Append(vertices, ref at, to, red, green, blue);
        }

        EnsureCapacity(device, segments.Count * 2);
        Upload(context, vertices);
        UploadCamera(device, context, viewProjection);

        uint stride = VertexStride;
        uint offset = 0;

        context.IASetInputLayout(_layout);
        context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyLinelist);
        context.IASetVertexBuffers(0, 1, ref _vertices, in stride, in offset);
        context.VSSetShader(_vertexShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
        context.VSSetConstantBuffers(CameraSlot, 1, ref _camera);
        context.PSSetShader(_pixelShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);

        context.Draw((uint)(segments.Count * 2), 0);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _vertices.Dispose();
        _camera.Dispose();
        _layout.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
    }

    /// <summary>Writes one vertex: three floats of position, three of colour.</summary>
    private static void Append(
        float[] data, ref int at, (float X, float Y, float Z) point, float red, float green, float blue)
    {
        data[at++] = point.X;
        data[at++] = point.Y;
        data[at++] = point.Z;
        data[at++] = red;
        data[at++] = green;
        data[at++] = blue;
    }

    /// <summary>Compiles one entry point out of <see cref="ShaderSource"/>.</summary>
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
                    $"compiling the world line {entryPoint} shader: {message}");
            }
        }

        errors.Dispose();

        return bytecode;
    }

    /// <summary>Grows the vertex buffer to hold at least this many vertices.</summary>
    private void EnsureCapacity(ComPtr<ID3D11Device> device, int vertexCount)
    {
        if (vertexCount <= _vertexCapacity && _vertices.Handle is not null)
        {
            return;
        }

        _vertices.Dispose();

        // Powers of two, so a scene that gains one edge does not reallocate every frame.
        int capacity = Math.Max(128, (int)BitOperations.RoundUpToPowerOf2((uint)vertexCount));

        BufferDesc description = new()
        {
            ByteWidth = (uint)(capacity * VertexStride),
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

    /// <summary>Copies the vertices into the dynamic buffer.</summary>
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

    /// <summary>Creates the camera buffer on first use and writes the matrix into it.</summary>
    private void UploadCamera(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, float[] matrix)
    {
        if (_camera.Handle is null)
        {
            BufferDesc description = new()
            {
                ByteWidth = sizeof(float) * CameraConstants,
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.ConstantBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
            };

            ComPtr<ID3D11Buffer> buffer = default;
            SilkMarshal.ThrowHResult(device.CreateBuffer(in description, null, ref buffer));

            _camera = buffer;
        }

        MappedSubresource mapped = default;
        SilkMarshal.ThrowHResult(context.Map(_camera, 0, Map.WriteDiscard, 0, ref mapped));

        fixed (float* source = matrix)
        {
            System.Buffer.MemoryCopy(
                source, mapped.PData, sizeof(float) * CameraConstants, sizeof(float) * CameraConstants);
        }

        context.Unmap(_camera, 0);
    }
}
