using System.Collections.Generic;
using System.Text;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Builds bit-level fixtures, packing least-significant-bit first to match Source's order.
/// </summary>
/// <remarks>
/// Shared because every message needs one, and writing packing code twice is how the two
/// copies end up disagreeing about bit order — which would make a decoder look correct
/// against a fixture that is wrong in the same way.
/// </remarks>
internal sealed class BitWriter
{
    private readonly List<byte> _bytes = [];
    private int _bitCount;

    /// <summary>Bits written so far.</summary>
    public int BitCount => _bitCount;

    public BitWriter Write(uint value, int bits)
    {
        for (int i = 0; i < bits; i++)
        {
            if (_bitCount % 8 == 0)
            {
                _bytes.Add(0);
            }

            if (((value >> i) & 1) != 0)
            {
                _bytes[^1] |= (byte)(1 << (_bitCount % 8));
            }

            _bitCount++;
        }

        return this;
    }

    public BitWriter Message(NetMessageType type) => Write((uint)type, NetMessage.TypeBits);

    /// <summary>
    /// Writes a value in Source's variable-width form: a two-bit selector, then the narrowest
    /// of 4, 8, 12 or 32 bits that holds it.
    /// </summary>
    /// <remarks>
    /// Written from the encoder's side rather than by inverting the reader, so a fixture and a
    /// misread selector cannot agree with each other.
    /// </remarks>
    public BitWriter UBitVar(uint value)
    {
        (uint selector, int bits) = value switch
        {
            < 1u << 4 => (0u, 4),
            < 1u << 8 => (1u, 8),
            < 1u << 12 => (2u, 12),
            _ => (3u, 32),
        };

        return Write(selector, 2).Write(value, bits);
    }

    public BitWriter NetTick(uint tick, ushort frameTime, ushort stdDev) =>
        Message(NetMessageType.NetTick).Write(tick, 32).Write(frameTime, 16).Write(stdDev, 16);

    /// <summary>Writes a NUL-terminated string, the encoding Source uses in bit streams.</summary>
    public BitWriter String(string value)
    {
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            Write(b, 8);
        }

        return Write(0, 8);
    }

    /// <summary>
    /// Appends another writer's bits, without padding to a byte boundary.
    /// </summary>
    /// <remarks>
    /// Needed whenever a message states its body's length in bits and the body has to be built
    /// separately to measure it. Copying the bytes instead would silently pad to the next byte
    /// and desynchronise everything after the body.
    /// </remarks>
    public BitWriter Append(BitWriter other)
    {
        System.ArgumentNullException.ThrowIfNull(other);

        byte[] bytes = other.Build();
        for (int bit = 0; bit < other.BitCount; bit++)
        {
            Write((uint)((bytes[bit / 8] >> (bit % 8)) & 1), 1);
        }

        return this;
    }

    public byte[] Build() => [.. _bytes];
}
