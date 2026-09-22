using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>A version-21 BSP assembled from lump payloads, laid out end to end after the header.</summary>
internal static class SyntheticBsp
{
    /// <summary>The file.</summary>
    /// <param name="lumps">Each lump's payload, by lump index.</param>
    /// <returns>The map's bytes.</returns>
    public static byte[] Build(IReadOnlyDictionary<int, byte[]> lumps)
    {
        int total = BspHeader.SizeBytes;

        foreach (byte[] payload in lumps.Values)
        {
            total += payload.Length;
        }

        byte[] file = new byte[total];
        Encoding.ASCII.GetBytes("VBSP").CopyTo(file, 0);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), 21);

        int at = BspHeader.SizeBytes;

        foreach ((int index, byte[] payload) in lumps)
        {
            payload.CopyTo(file, at);
            int entry = 8 + (index * 16);
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(entry), at);
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(entry + 4), payload.Length);
            at += payload.Length;
        }

        return file;
    }
}
