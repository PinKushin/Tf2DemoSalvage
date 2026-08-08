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

    public byte[] Build() => [.. _bytes];
}
