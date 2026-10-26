using System;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// Where the next window of a constant ring goes, in sixteen-byte constants: the arithmetic of <see cref="ConstantRing"/>
/// with no device, so its rules are tested on their own (B597).
/// </summary>
/// <remarks>
/// *Read from published documentation* (`ID3D11DeviceContext1::VSSetConstantBuffers1`): a window's first constant and its
/// count are each a multiple of 16 constants (256 bytes), and a window is at most 4096 constants. **A window is never
/// handed out twice between two discards**: the cursor only moves forward, and the reservation that would pass the end
/// starts again at zero and asks for a discard, which gives the buffer new memory, so nothing the GPU may still read is
/// written over (`D3D11_MAP_WRITE_NO_OVERWRITE` for every other map).
/// </remarks>
public sealed class ConstantRingCursor
{
    /// <summary>A window's first constant and count are multiples of this.</summary>
    public const int Alignment = 16;

    /// <summary>The most constants one window may hold.</summary>
    public const int MaxWindow = 4096;

    private int _next;
    private bool _fresh = true;

    /// <summary>A cursor over a ring of <paramref name="capacity"/> constants.</summary>
    /// <param name="capacity">Constants in the ring; a positive multiple of <see cref="Alignment"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The capacity is not a positive multiple of the alignment.</exception>
    public ConstantRingCursor(int capacity)
    {
        if (capacity <= 0 || capacity % Alignment != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "A ring is a positive multiple of 16 constants.");
        }

        Capacity = capacity;
    }

    /// <summary>Constants in the ring.</summary>
    public int Capacity { get; }

    /// <summary>The window that would hold <paramref name="constants"/>: its count rounded up to the alignment.</summary>
    /// <param name="constants">Constants the caller writes.</param>
    /// <returns>The window's size in constants.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Zero or fewer, or more than one window holds.</exception>
    public static int WindowFor(int constants)
    {
        if (constants <= 0 || constants > MaxWindow)
        {
            throw new ArgumentOutOfRangeException(nameof(constants), constants, "A window holds 1 to 4096 constants.");
        }

        return (constants + Alignment - 1) / Alignment * Alignment;
    }

    /// <summary>Hands out the next window.</summary>
    /// <param name="constants">Constants the caller writes.</param>
    /// <returns>
    /// The window's first constant and its count, and whether this map must discard: the first map of the ring, and every
    /// map that starts it again at zero.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">The window is larger than the whole ring; the owner must grow it.</exception>
    public (int First, int Count, bool Discard) Reserve(int constants)
    {
        int count = WindowFor(constants);

        if (count > Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(constants), constants, "The window is larger than the ring.");
        }

        bool discard = _fresh;

        if (_next + count > Capacity)
        {
            _next = 0;
            discard = true;
        }

        _fresh = false;

        int first = _next;

        _next += count;

        return (first, count, discard);
    }
}

/// <summary>
/// One dynamic constant buffer written by window and bound by offset (D3D11.1), in place of a buffer discarded per draw
/// (B597). One ring per binding slot: a discard renames the whole buffer, so a slot bound from this ring is valid only
/// until the next reservation from it — which rebinds that slot at once.
/// </summary>
public sealed unsafe class ConstantRing : IDisposable
{
    private readonly ComPtr<ID3D11DeviceContext1> _context;
    private readonly ConstantRingCursor _cursor;
    private ComPtr<ID3D11Buffer> _buffer;
    private uint _first;
    private uint _count;

    private ConstantRing(ComPtr<ID3D11DeviceContext1> context, ComPtr<ID3D11Buffer> buffer, int capacity)
    {
        _context = context;
        _buffer = buffer;
        _cursor = new ConstantRingCursor(capacity);
    }

    /// <summary>
    /// A ring of <paramref name="capacity"/> constants, or null where the device cannot offset a constant binding or map a
    /// dynamic constant buffer without overwriting — the caller keeps its discard-per-draw buffer there.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="context">The immediate context.</param>
    /// <param name="capacity">Constants in the ring, a multiple of 16.</param>
    /// <param name="why">Why there is none, or "on".</param>
    /// <returns>The ring, or null.</returns>
    public static ConstantRing? TryCreate(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, int capacity, out string why)
    {
        FeatureDataD3D11Options options = default;

        if (device.CheckFeatureSupport(Feature.D3D11Options, &options, (uint)sizeof(FeatureDataD3D11Options)) != 0)
        {
            why = "off: D3D11_FEATURE_D3D11_OPTIONS not answered";
            return null;
        }

        if (!options.ConstantBufferOffsetting || !options.MapNoOverwriteOnDynamicConstantBuffer)
        {
            why = $"off: ConstantBufferOffsetting {(bool)options.ConstantBufferOffsetting}, " +
                $"MapNoOverwriteOnDynamicConstantBuffer {(bool)options.MapNoOverwriteOnDynamicConstantBuffer}";
            return null;
        }

        if (context.QueryInterface(out ComPtr<ID3D11DeviceContext1> context1) != 0)
        {
            why = "off: no ID3D11DeviceContext1";
            return null;
        }

        why = "on";

        BufferDesc description = new()
        {
            ByteWidth = (uint)(capacity * 16),
            Usage = Usage.Dynamic,
            BindFlags = (uint)BindFlag.ConstantBuffer,
            CPUAccessFlags = (uint)CpuAccessFlag.Write,
        };

        ComPtr<ID3D11Buffer> buffer = default;

        SilkMarshal.ThrowHResult(device.CreateBuffer(in description, null, ref buffer));

        return new ConstantRing(context1, buffer, capacity);
    }

    /// <summary>
    /// Maps the next window and hands back all of it, ZEROED, so a shader reading past what the caller writes reads zero as
    /// it did past the end of an exactly sized buffer. <see cref="Unmap"/> follows before anything draws.
    /// </summary>
    /// <param name="floats">Floats the caller writes.</param>
    /// <returns>The window's floats.</returns>
    public Span<float> Map(int floats)
    {
        (int first, int count, bool discard) = _cursor.Reserve((floats + 3) / 4);
        MappedSubresource mapped = default;

        SubmitCounters.Upload();
        SilkMarshal.ThrowHResult(_context.Map(_buffer, 0, discard ? Silk.NET.Direct3D11.Map.WriteDiscard : Silk.NET.Direct3D11.Map.WriteNoOverwrite, 0, ref mapped));

        _first = (uint)first;
        _count = (uint)count;

        Span<float> window = new((float*)mapped.PData + (first * 4), count * 4);

        window.Clear();

        return window;
    }

    /// <summary>Ends the write begun by <see cref="Map"/>.</summary>
    public void Unmap() => _context.Unmap(_buffer, 0);

    /// <summary>Binds the window last written to a vertex-shader slot.</summary>
    /// <param name="slot">The slot.</param>
    public void BindVertex(uint slot)
    {
        ID3D11Buffer* buffer = _buffer.Handle;
        uint first = _first;
        uint count = _count;

        _context.VSSetConstantBuffers1(slot, 1, &buffer, &first, &count);
    }

    /// <summary>Binds the window last written to a pixel-shader slot.</summary>
    /// <param name="slot">The slot.</param>
    public void BindPixel(uint slot)
    {
        ID3D11Buffer* buffer = _buffer.Handle;
        uint first = _first;
        uint count = _count;

        _context.PSSetConstantBuffers1(slot, 1, &buffer, &first, &count);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _buffer.Dispose();
        _context.Dispose();
    }
}
