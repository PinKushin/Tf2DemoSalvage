using System;
using System.Buffers.Binary;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>The get side of the engine's <c>CUtlBuffer</c>, as a compiled scene is read through it (B376).</summary>
/// <remarks>
/// **Overflow is sticky and silent, and that is the whole reason this type exists.**
/// <c>CUtlBuffer::CheckGet</c> (<c>utlbuffer.cpp:801</c>) refuses any read once <c>GET_OVERFLOW</c>
/// is set, and sets it when a read does not fit; <c>GetTypeBin</c> (<c>utlbuffer.h:639</c>) then
/// yields 0 and does NOT advance. So a read that runs off the end zeroes itself and every read after
/// it — even a one-byte read with bytes left — and nothing above notices, because no scene restore
/// checks the buffer's state. A reader that bounds-checks its own way agrees with the engine on every
/// well-formed scene and disagrees on exactly the ones the engine desyncs on.
/// </remarks>
internal ref struct EngineBuffer
{
    private readonly ReadOnlySpan<byte> _span;

    /// <summary>Starts a read at the first byte.</summary>
    /// <param name="span">The buffer.</param>
    public EngineBuffer(ReadOnlySpan<byte> span) => _span = span;

    /// <summary>The get cursor, <c>m_Get</c>.</summary>
    public int At { get; private set; }

    /// <summary>Whether <c>GET_OVERFLOW</c> is set.</summary>
    public bool Overflowed { get; private set; }

    /// <summary><c>GetUnsignedChar</c>, and <c>GetChar</c> where the caller only compares.</summary>
    public byte Byte() => Take(1) ? _span[At - 1] : (byte)0;

    /// <summary><c>GetShort</c>, signed.</summary>
    public short Short() => Take(2) ? BinaryPrimitives.ReadInt16LittleEndian(_span[(At - 2)..]) : (short)0;

    /// <summary><c>GetUnsignedShort</c>.</summary>
    public ushort UnsignedShort() =>
        Take(2) ? BinaryPrimitives.ReadUInt16LittleEndian(_span[(At - 2)..]) : (ushort)0;

    /// <summary><c>GetInt</c>.</summary>
    public int Int() => Take(4) ? BinaryPrimitives.ReadInt32LittleEndian(_span[(At - 4)..]) : 0;

    /// <summary><c>GetFloat</c>.</summary>
    public float Float() => Take(4) ? BinaryPrimitives.ReadSingleLittleEndian(_span[(At - 4)..]) : 0f;

    /// <summary><c>CheckGet</c> followed by the advance.</summary>
    private bool Take(int size)
    {
        if (Overflowed || At + size > _span.Length)
        {
            Overflowed = true;

            return false;
        }

        At += size;

        return true;
    }
}
