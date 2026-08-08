using System.Collections.Generic;
using System.Text;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Reading conventions shared by every network message.
/// </summary>
internal static class NetBitReading
{
    /// <summary>
    /// Reads a NUL-terminated string, the encoding Source uses throughout its bit streams.
    /// </summary>
    /// <remarks>
    /// Not length-prefixed: the terminator is the only thing that ends it. A decoder that has
    /// lost bit alignment will therefore read until it happens to find a zero byte, which is
    /// why garbage strings are the first visible symptom of a desynchronised stream rather
    /// than an exception.
    /// </remarks>
    internal static string ReadString(ref BitReader reader)
    {
        List<byte> bytes = new();

        while (true)
        {
            byte value = reader.ReadByte();
            if (value == 0)
            {
                break;
            }

            bytes.Add(value);
        }

        return Encoding.UTF8.GetString([.. bytes]);
    }
}
