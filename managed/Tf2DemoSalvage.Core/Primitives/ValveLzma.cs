using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;

using SevenZip.Compression.LZMA;

namespace Tf2DemoSalvage.Core.Primitives;

/// <summary>
/// LZMA decompression, as used by Source's compressed BSP lumps.
/// </summary>
/// <remarks>
/// **A thin wrapper over Igor Pavlov's reference SDK rather than an implementation.** Unlike
/// Snappy and LZSS elsewhere in this project, LZMA is not part of the format being reverse
/// engineered — it is a general-purpose codec that Valve simply calls, so writing a range decoder
/// here would add risk without adding understanding.
///
/// What is ours is the boundary: the SDK decodes, this decides what it is allowed to be asked to
/// decode. A map is hostile input (D32), and the declared output size comes out of the file.
/// </remarks>
public static class ValveLzma
{
    /// <summary>Bytes of encoder properties preceding the stream.</summary>
    public const int PropertiesBytes = 5;

    /// <summary>
    /// The largest properties byte that encodes a real configuration.
    /// </summary>
    /// <remarks>
    /// lc, lp and pb pack into one byte as <c>lc + 9 * (lp + 5 * pb)</c>, with lc &lt; 9, lp &lt; 5
    /// and pb &lt; 5 — so 224 is the maximum and 225 upward decode to nothing. The SDK rejects
    /// these itself; the check is here so the failure arrives as this project's exception type.
    /// </remarks>
    private const byte MaximumPropertiesByte = 224;

    /// <summary>The five property bytes, with the dictionary cut to what this output can ever reference.</summary>
    /// <remarks>
    /// **The decoder allocates its whole window up front, sized by the header** (B407). A static prop's `.vhv` is a few kilobytes
    /// behind a header declaring megabytes, so decoding a map's thousand of them allocated 25 GB. A match can only reach back
    /// into bytes already produced, so a window as large as the output decodes every valid stream the same — and the decoder's
    /// own <c>rep0 &gt;= position</c> check still refuses one that reaches further. The floor is the decoder's own minimum.
    /// </remarks>
    private static byte[] Windowed(ReadOnlySpan<byte> properties, int outputLength)
    {
        byte[] bytes = properties[..PropertiesBytes].ToArray();
        uint needed = (uint)Math.Max(outputLength, 1 << 12);

        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(1)) > needed)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1), needed);
        }

        return bytes;
    }

    /// <summary>Decompresses a raw LZMA stream of known output length.</summary>
    /// <param name="properties">The five encoder property bytes.</param>
    /// <param name="input">The compressed stream, with no length prefix of its own.</param>
    /// <param name="outputLength">Exact number of bytes to produce.</param>
    /// <returns>The decompressed bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="outputLength"/> is negative.</exception>
    /// <exception cref="InvalidDataException">The properties or the stream are not decodable.</exception>
    /// <remarks>
    /// **The output length is a parameter rather than something read from the stream**, because in
    /// Valve's container it genuinely is: the eight-byte size field of a standard <c>.lzma</c> file
    /// is not present, and <c>lzma_header_t</c> carries the size instead. The caller is also the
    /// only party that can decide whether a declared size is plausible.
    /// </remarks>
    public static byte[] Decode(
        ReadOnlySpan<byte> properties, ReadOnlySpan<byte> input, int outputLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(outputLength);

        if (properties.Length < PropertiesBytes)
        {
            // Stryker disable all : the String mutator wraps the interpolated literal in a ternary
            // that cannot bind to string.Create's interpolated-string handler (CS1620), and Safe
            // Mode then drops every mutation in this method — B410.
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"An LZMA stream needs {PropertiesBytes} property bytes but only " +
                $"{properties.Length} are present."));

            // Stryker restore all
        }

        if (properties[0] > MaximumPropertiesByte)
        {
            // Stryker disable all : the String mutator wraps the interpolated literal in a ternary
            // that cannot bind to string.Create's interpolated-string handler (CS1620), and Safe
            // Mode then drops every mutation in this method — B410.
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"{properties[0]} is not an LZMA properties byte: the largest that encodes a real " +
                $"lc/lp/pb combination is {MaximumPropertiesByte}."));

            // Stryker restore all
        }

        if (outputLength == 0)
        {
            return [];
        }

        byte[] output = new byte[outputLength];

        try
        {
            Decoder decoder = new();
            decoder.SetDecoderProperties(Windowed(properties, outputLength));

            using MemoryStream source = new(input.ToArray(), writable: false);
            using BoundedSink destination = new(output);

            decoder.Code(source, destination, input.Length, outputLength, null);

            if (destination.Written != outputLength)
            {
                // The stream ran out before producing what was promised. Left alone this returns a
                // buffer whose tail is zeroes, which for geometry means a map that reads as valid
                // and is quietly wrong - the failure mode this codebase keeps meeting.
                // Stryker disable all : the String mutator wraps the interpolated literal in a
                // ternary that cannot bind to string.Create's interpolated-string handler
                // (CS1620), and Safe Mode then drops every mutation in this method — B410.
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"An LZMA stream promised {outputLength} bytes but produced " +
                    $"{destination.Written}."));

                // Stryker restore all
            }
        }
        catch (Exception failure) when (failure is not InvalidDataException
                                            and not OutOfMemoryException)
        {
            // The SDK raises its own exception types, and one of them is a bare
            // InvalidOperationException. Hostile input must not surface as something a caller
            // would read as a bug in this program.
            throw new InvalidDataException(
                "An LZMA stream could not be decoded: " + failure.Message, failure);
        }

        return output;
    }

    /// <summary>A write-only sink over a fixed buffer that discards anything past the end.</summary>
    /// <remarks>
    /// **The SDK's output size is not a hard stop, and a plain <see cref="MemoryStream"/> over an
    /// exactly-sized buffer therefore fails.** Its decode loop checks the limit once per symbol, so
    /// a match that *begins* below the limit is copied in full and can carry the stream up to a
    /// maximum match length — 273 bytes — past what was asked for. Against a non-expandable
    /// MemoryStream that surfaces as "Memory stream is not expandable", which reads like a bug in
    /// the caller rather than a documented property of the decoder.
    ///
    /// Padding the buffer instead would work and then need a copy to trim it. Discarding the tail
    /// costs neither, and the byte count is kept so a stream that produced too *little* — the case
    /// that actually matters, because it means truncation — is still caught.
    /// </remarks>
    private sealed class BoundedSink(byte[] destination) : Stream
    {
        /// <summary>Bytes accepted into the buffer, ignoring any overshoot.</summary>
        public int Written { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => Written;

        public override long Position
        {
            get => Written;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            int room = Math.Min(buffer.Length, destination.Length - Written);

            if (room <= 0)
            {
                return;
            }

            buffer[..room].CopyTo(destination.AsSpan(Written));
            Written += room;
        }

        public override void WriteByte(byte value)
        {
            if (Written < destination.Length)
            {
                destination[Written++] = value;
            }
        }

        public override void Flush()
        {
            // Nothing is buffered; the writes land in the caller's array directly.
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
