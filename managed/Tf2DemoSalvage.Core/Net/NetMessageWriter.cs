using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Re-encodes a decoded message back to the bits it came from.
/// </summary>
/// <remarks>
/// **The only check that can show the decode is lossless rather than merely plausible.** Every
/// message body in this project decodes and each one self-checks against its stated length, but a
/// length check cannot see a field that was read and discarded — the reader stays aligned, the
/// trace looks complete, and the information is gone. Writing the message back and comparing bits
/// has no such blind spot: a dropped field is a bit that cannot be reproduced.
///
/// It is also the foundation for compiling text back into a demo, which is the standard the Quake
/// demo tools set and the honest definition of "fully deciphered".
///
/// **Partial by construction, and that is the point.** A message this cannot write returns
/// <c>false</c> rather than guessing, and the corpus scoreboard reports how many payload bits
/// round-trip because of it. Three shapes are currently unwritable and each one names a specific
/// gap rather than an oversight:
///
/// - <see cref="EntityMessage"/> and <see cref="VoiceDataMessage"/> keep a body length but not the
///   body, so there is nothing to write back.
/// - <c>svc_SetPause</c> and <c>svc_Menu</c> produce no message at all, so nothing reaches here.
/// - String tables, game events and user messages decode into values, and re-encoding those means
///   an encoder per format — the string table one has to reproduce a compressed payload exactly.
///
/// Filling those in is what moves the number, and the number is what says whether it worked.
/// </remarks>
public static class NetMessageWriter
{
    /// <summary>Writes a message, including its type field.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="message">The message to re-encode.</param>
    /// <param name="state">
    /// Decode state, which sizes the type field and every protocol-conditional field. Several
    /// messages cannot be written without it for the same reason they cannot be read without it.
    /// </param>
    /// <returns><c>false</c> when this message has no encoder yet, having written nothing.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="writer"/>, <paramref name="message"/> or <paramref name="state"/> is
    /// <c>null</c>.
    /// </exception>
    public static bool TryWrite(BitWriter writer, INetMessage message, NetDecodeState state)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(state);

        // Checked before the type field is written, so a refusal leaves the stream untouched
        // rather than half-written. A caller comparing bits has to be able to stop cleanly.
        if (!CanWrite(message))
        {
            return false;
        }

        // The type field is five bits at or below protocol 15 and six above it, which is the
        // same branch the reader takes. Writing six unconditionally would shift every message
        // after the first on an old demo.
        writer.Write((uint)message.Type, state.MessageTypeBits);

        // From ServerInfo rather than from state.NetworkProtocol, because that is what the reader
        // uses: a message arriving before ServerInfo is read at protocol 0, and writing it at the
        // demo's real protocol would produce a field of a different width than the one decoded.
        WriteBody(writer, message, state.ServerInfo?.NetworkProtocol ?? 0);
        return true;
    }

    /// <summary>Whether <see cref="TryWrite"/> can reproduce this message.</summary>
    /// <param name="message">The message.</param>
    /// <returns><c>true</c> when an encoder exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
    public static bool CanWrite(INetMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message switch
        {
            NetEmptyMessage or NetTickMessage or PrintMessage or StringCmdMessage or
            SetConVarMessage or ServerInfoMessage or PacketEntitiesMessage or PrefetchMessage or
            SoundsMessage or TempEntitiesMessage or FixAngleMessage or FileMessage or
            GetCvarValueMessage or SetViewMessage or SignOnStateMessage => true,
            _ => false,
        };
    }

    private static void WriteBody(BitWriter writer, INetMessage message, int protocol)
    {
        switch (message)
        {
            case NetEmptyMessage:
                // net_NOP is the type field and nothing else.
                break;

            case NetTickMessage tick:
                writer.Write((uint)tick.Tick, 32)
                    .Write(tick.HostFrameTimeRaw, 16)
                    .Write(tick.HostFrameTimeStdDevRaw, 16);
                break;

            case PrintMessage print:
                writer.WriteString(print.Text);
                break;

            case StringCmdMessage command:
                writer.WriteString(command.Command);
                break;

            case SetConVarMessage convars:
                writer.Write((uint)convars.Variables.Count, 8);
                foreach (KeyValuePair<string, string> variable in convars.Variables)
                {
                    writer.WriteString(variable.Key).WriteString(variable.Value);
                }

                break;

            case ServerInfoMessage info:
                WriteServerInfo(writer, info);
                break;

            case PacketEntitiesMessage entities:
                writer.Write((uint)entities.MaxEntries, 11).WriteBit(entities.IsDelta);
                if (entities.DeltaFromTick is { } from)
                {
                    writer.Write((uint)from, 32);
                }

                writer.WriteBit(entities.BaselineIndex)
                    .Write((uint)entities.UpdatedEntries, 11)
                    .Write((uint)entities.LengthBits, 20)
                    .WriteBit(entities.UpdateBaseline)
                    .AppendBits(entities.Body.Span, entities.LengthBits);
                break;

            case PrefetchMessage prefetch:
                writer.Write(
                    (uint)prefetch.SoundIndex,
                    protocol > NetMessageReader.PrefetchWidthProtocol
                        ? NetMessageReader.PrefetchBitsModern
                        : NetMessageReader.PrefetchBitsLegacy);
                break;

            case SoundsMessage sounds:
                // The reliable flag suppresses the count byte and narrows the length field, so
                // the shape has to be reproduced rather than the fields written unconditionally.
                writer.WriteBit(sounds.IsReliable);
                if (!sounds.IsReliable)
                {
                    writer.Write((uint)sounds.Count, 8);
                }

                writer.Write(
                        (uint)sounds.BodyBits,
                        sounds.IsReliable
                            ? NetMessageReader.SoundsReliableLengthBits
                            : NetMessageReader.SoundsLengthBits)
                    .AppendBits(sounds.Body.Span, sounds.BodyBits);
                break;

            case TempEntitiesMessage effects:
                writer.Write((uint)effects.Count, 8);
                if (protocol > NetMessageReader.TempEntitiesVarIntProtocol)
                {
                    VarInt.WriteUInt32(writer, (uint)effects.BodyBits);
                }
                else
                {
                    writer.Write(
                        (uint)effects.BodyBits, NetMessageReader.TempEntitiesLegacyLengthBits);
                }

                writer.AppendBits(effects.Body.Span, effects.BodyBits);
                break;

            case FixAngleMessage angle:
                writer.WriteBit(angle.IsRelative);
                WriteAngle(writer, angle.Pitch);
                WriteAngle(writer, angle.Yaw);
                WriteAngle(writer, angle.Roll);
                break;

            case FileMessage file:
                writer.Write(file.TransferId, 32)
                    .WriteString(file.FileName)
                    .WriteBit(file.IsRequested);
                break;

            case GetCvarValueMessage cvar:
                writer.Write(cvar.Cookie, 32).WriteString(cvar.CvarName);
                break;

            case SetViewMessage view:
                writer.Write((uint)view.EntityIndex, NetMessageReader.SetViewBits);
                break;

            case SignOnStateMessage signon:
                writer.Write((uint)signon.State, 8).Write((uint)signon.SpawnCount, 32);
                break;

            default:
                // Unreachable: CanWrite gates every call, and the two lists are the same list.
                throw new NotSupportedException(message.Type.ToString());
        }
    }

    private static void WriteServerInfo(BitWriter writer, ServerInfoMessage info)
    {
        writer.Write(info.NetworkProtocol, 16)
            .Write(info.ServerCount, 32)
            .WriteBit(info.IsSourceTv)
            .WriteBit(info.IsDedicated)
            .Write(info.MapCrc, 32)
            .Write(info.MaxClasses, 16)
            .WriteBytes([.. info.MapHash])
            .Write(info.PlayerSlot, 8)
            .Write(info.MaxPlayers, 8)
            .Write((uint)BitConverter.SingleToInt32Bits(info.IntervalPerTick), 32)
            .Write(info.Platform, 8)
            .WriteString(info.GameDirectory)
            .WriteString(info.Map)
            .WriteString(info.Skybox)
            .WriteString(info.ServerName);

        // Only present above protocol 15, exactly as the reader only consumes it there.
        if (info.NetworkProtocol > 15)
        {
            writer.WriteBit(info.IsReplay);
        }
    }

    /// <summary>Writes an angle in the 16-bit form <c>svc_FixAngle</c> uses.</summary>
    /// <remarks>
    /// The inverse of <c>angle = raw * 360 / 65536</c>, rounded rather than truncated. Truncation
    /// loses the round trip on about half of all values: the decoded float is not exactly
    /// representable, so dividing it back lands just under the integer it came from and the cast
    /// drops a whole step.
    /// </remarks>
    private static void WriteAngle(BitWriter writer, float degrees) =>
        writer.Write((uint)MathF.Round(degrees * (65536f / 360f)) & 0xFFFF, 16);
}
