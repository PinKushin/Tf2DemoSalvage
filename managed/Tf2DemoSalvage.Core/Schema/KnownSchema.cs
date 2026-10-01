using System;
using System.IO;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>
/// The one build whose <c>dem_datatables</c> is known to arrive cut, and its whole schema (B24).
/// </summary>
/// <remarks>
/// **TF2's launch build (3258) wrote its SourceTV schema into a 65,536-byte buffer, and the schema
/// is 85,063 bytes.** Every SourceTV recording that build made carries the first 2^16 bytes and
/// nothing else; its POV recordings carry the whole thing, byte-identical across maps. The cut
/// payload's first 65,535 bytes equal the whole schema's, and its 65,536th is the byte the buffer
/// ended inside. So a payload of exactly that length whose first 65,535 bytes match IS this schema,
/// and decoding it with the whole one is exact rather than a guess. Anything else is not completed.
///
/// **This is deliberately better than Valve.** The 2007 engine reads the cut schema without checking
/// <c>bf_read</c>'s overflow, ends up with zero server classes, and <c>Host_Error</c>s on the first
/// entity (<c>CL_CopyNewEntity: invalid class index</c>) — read from build 3258's <c>engine.dll</c>,
/// see RISKS B24. Here the same demos decode their entities.
///
/// The bytes are the payload of <c>tools/corpus/demos/tf2-2007-build3258-pov-cp_granary.dem</c>'s
/// <c>dem_datatables</c>, SHA-256 <c>ea020a813d6ab230800b6a6410b833cef3daf5538b33c9dca8da6b63cf87e637</c>.
/// Only the decode uses them: whatever writes a demo back writes the demo's own cut bytes.
/// </remarks>
internal static class KnownSchema
{
    /// <summary>Where build 3258's SourceTV writer cut <c>dem_datatables</c>: 2^16 bytes.</summary>
    internal const int CutLength = 65_536;

    /// <summary>Build 3258's whole <c>dem_datatables</c> payload, 85,063 bytes.</summary>
    internal static ReadOnlyMemory<byte> Build3258 { get; } = Load("Tf2DemoSalvage.Core.build3258-datatables.bin");

    /// <summary>The payload to decode: the known whole schema when this is its cut, else the payload.</summary>
    /// <param name="payload">A demo's <c>dem_datatables</c> payload.</param>
    /// <returns>Build 3258's whole schema for its cut, otherwise <paramref name="payload"/> unchanged.</returns>
    internal static ReadOnlySpan<byte> Complete(ReadOnlySpan<byte> payload) => Complete(payload, Build3258.Span);

    /// <summary>
    /// <paramref name="known"/> when <paramref name="payload"/> is it cut at <see cref="CutLength"/>
    /// bytes, else <paramref name="payload"/>.
    /// </summary>
    /// <remarks>
    /// The last byte is left out of the comparison because the cut fell inside it: its bits after the
    /// cut were never written (measured: <c>0E</c> in the SourceTV payload, <c>2E</c> in the whole one).
    /// </remarks>
    internal static ReadOnlySpan<byte> Complete(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> known) =>
        payload.Length == CutLength
        && payload[..(CutLength - 1)].SequenceEqual(known[..(CutLength - 1)])
            ? known
            : payload;

    private static byte[] Load(string name)
    {
        using Stream stream = typeof(KnownSchema).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded resource {name} is missing from the build.");
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
