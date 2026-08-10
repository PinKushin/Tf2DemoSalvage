using System;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Fixture sugar over the production <see cref="BitWriter"/>.
/// </summary>
/// <remarks>
/// The test project had its own bit writer for months, which was right while nothing in
/// production needed to write bits. Once something did, keeping both would have meant two
/// implementations of the one thing every fixture in the suite depends on being right — and a
/// fixture that packs bits the wrong way makes a decoder that reads them the wrong way look
/// correct.
///
/// So the packing moved to <see cref="BitWriter"/> and what remains here is the part that is only
/// useful to a fixture: naming a message type, writing a whole <c>net_Tick</c> in one call. As
/// extension methods rather than a wrapper, so every existing fixture compiles unchanged.
/// </remarks>
internal static class BitWriterFixtureExtensions
{
    /// <summary>Writes a message type field.</summary>
    public static BitWriter Message(this BitWriter writer, NetMessageType type)
    {
        ArgumentNullException.ThrowIfNull(writer);
        return writer.Write((uint)type, NetMessage.TypeBits);
    }

    /// <summary>Writes a complete <c>net_Tick</c>, the message fixtures use as a control.</summary>
    /// <remarks>
    /// It appears after whatever is under test far more often than it appears as the subject: a
    /// decoder that reads one bit too many or too few leaves the reader mid-stream, so a tick that
    /// still decodes to its expected number is what proves the message before it consumed exactly
    /// its own bits.
    /// </remarks>
    public static BitWriter NetTick(
        this BitWriter writer, uint tick, ushort frameTime, ushort stdDev) =>
        writer.Message(NetMessageType.NetTick).Write(tick, 32).Write(frameTime, 16)
            .Write(stdDev, 16);

    /// <summary>Writes a value in Source's variable-width form.</summary>
    public static BitWriter UBitVar(this BitWriter writer, uint value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        return writer.WriteUBitVar(value);
    }

    /// <summary>Writes a NUL-terminated UTF-8 string.</summary>
    public static BitWriter String(this BitWriter writer, string value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        return writer.WriteString(value);
    }

    /// <summary>Appends another writer's bits, without padding to a byte boundary.</summary>
    public static BitWriter Append(this BitWriter writer, BitWriter other)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(other);

        return writer.AppendBits(other.Build(), other.BitCount);
    }
}
