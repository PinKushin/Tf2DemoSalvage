using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Schema;

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
/// - <c>svc_SetPause</c> and <c>svc_Menu</c> produce no message at all, so nothing reaches here.
/// - String tables and user messages decode into values, and re-encoding those means an encoder
///   per format — the string table one also has to reproduce a compressed payload exactly.
/// - A <see cref="GameEventMessage"/> that arrived before its definition, which decodes to an id
///   and nothing else and therefore cannot be rebuilt.
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
        if (!CanWrite(message) || !HasDefinition(message, state))
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
        WriteBody(writer, message, state, state.ServerInfo?.NetworkProtocol ?? 0);
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
            GetCvarValueMessage or SetViewMessage or SignOnStateMessage or
            GameEventListMessage or VoiceInitMessage or VoiceDataMessage or EntityMessage or
            UserMessage or ChatMessage or ClassInfoMessage or BspDecalMessage => true,

            // Only when the message came from a demo. A table built in a test has no wire form to
            // reproduce, and inventing one would be a different message.
            CreateStringTableMessage { Wire: not null } => true,
            UpdateStringTableMessage { Wire: not null } => true,

            // Its fields are a dictionary, so the definition is what supplies their order. An
            // event that arrived before its own definition has neither.
            GameEventMessage gameEvent => gameEvent.IsDecoded,
            _ => false,
        };
    }

    /// <summary>Whether the state holds what a message needs beyond its own fields.</summary>
    /// <remarks>
    /// Only game events need this, and the reason is worth stating: their values are a dictionary,
    /// so field ORDER lives in the definition rather than in the message. Writing them in
    /// enumeration order would produce a body that decodes to the same values and does not match
    /// the demo.
    /// </remarks>
    private static bool HasDefinition(INetMessage message, NetDecodeState state) =>
        message is not GameEventMessage gameEvent ||
        state.EventDefinitions.ContainsKey(gameEvent.EventId);

    private static void WriteBody(
        BitWriter writer, INetMessage message, NetDecodeState state, int protocol)
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

            case GameEventListMessage list:
                GameEventCodec.WriteList(writer, list);
                break;

            case CreateStringTableMessage table:
                WriteCreateStringTable(writer, table, protocol);
                break;

            case UpdateStringTableMessage update:
                writer.Write((uint)update.TableId, 5);
                if (update.Wire!.EntryCount == 1)
                {
                    // A single changed entry is the inferred case: the count field is absent and
                    // its flag bit says so.
                    writer.WriteBit(false);
                }
                else
                {
                    writer.WriteBit(true).Write((uint)update.Wire.EntryCount, 16);
                }

                writer.Write((uint)update.Wire.BodyBits, 20)
                    .AppendBits(update.Wire.Body.Span, update.Wire.BodyBits);
                break;

            case BspDecalMessage decal:
                // Three presence bits first, then only the axes that were sent. Writing all three
                // unconditionally would add coordinates the wire does not have.
                writer.WriteBit(decal.X.HasValue)
                    .WriteBit(decal.Y.HasValue)
                    .WriteBit(decal.Z.HasValue);

                foreach (float? axis in new[] { decal.X, decal.Y, decal.Z })
                {
                    if (axis is { } coordinate)
                    {
                        SendPropEncoder.WriteCoord(writer, coordinate);
                    }
                }

                writer.Write((uint)decal.TextureIndex, NetMessageReader.DecalTextureBits)
                    .WriteBit(decal.OnEntity);

                if (decal.OnEntity)
                {
                    writer.Write((uint)decal.EntityIndex, NetMessageReader.EntityIndexBits)
                        .Write((uint)decal.ModelIndex, NetMessageReader.ModelIndexBits);
                }

                writer.WriteBit(decal.IsLowPriority);
                break;

            case ClassInfoMessage classes:
                // The create-on-client flag suppresses the entry list entirely, and the entries
                // are written at a width derived from the count rather than transmitted.
                writer.Write((uint)classes.ClassCount, 16).WriteBit(classes.CreateOnClient);
                if (!classes.CreateOnClient)
                {
                    foreach (ServerClass serverClass in classes.Classes)
                    {
                        writer.Write((uint)serverClass.Id, classes.ClassIdBits)
                            .WriteString(serverClass.ClassName)
                            .WriteString(serverClass.TableName);
                    }
                }

                break;

            case VoiceInitMessage voiceInit:
                // The sample rate rides in the quality field's 255 escape, so writing sixteen
                // bits unconditionally would add a field the wire does not have.
                writer.WriteString(voiceInit.Codec);
                if (voiceInit.SampleRate is { } rate)
                {
                    writer.Write(NetMessageReader.VoiceVariableRateQuality, 8).Write((uint)rate, 16);
                }
                else
                {
                    writer.Write((uint)voiceInit.Quality, 8);
                }

                break;

            case VoiceDataMessage voice:
                writer.Write((uint)voice.Client, 8)
                    .Write((uint)voice.Proximity, 8)
                    .Write((uint)voice.BodyBits, NetMessageReader.VoiceDataLengthBits)
                    .AppendBits(voice.Body.Span, voice.BodyBits);
                break;

            case EntityMessage entity:
                writer.Write((uint)entity.EntityIndex, NetMessageReader.EntityMessageIndexBits)
                    .Write((uint)entity.ClassId, NetMessageReader.EntityMessageClassBits)
                    .Write((uint)entity.BodyBits, NetMessageReader.UserMessageLengthBits)
                    .AppendBits(entity.Body.Span, entity.BodyBits);
                break;

            case ChatMessage chat:
                // Chat has no message id of its own - it is one of forty-odd payloads sharing
                // svc_UserMessage, and it is written back as the user message it arrived in.
                writer.Write(ChatMessage.SayText2Type, 8)
                    .Write((uint)chat.BodyBits, NetMessageReader.UserMessageLengthBits)
                    .AppendBits(chat.Body.Span, chat.BodyBits);
                break;

            case UserMessage user:
                writer.Write((uint)user.UserMessageType, 8)
                    .Write((uint)user.BodyBits, NetMessageReader.UserMessageLengthBits)
                    .AppendBits(user.Body.Span, user.BodyBits);
                break;

            case GameEventMessage gameEvent:
                GameEventCodec.WriteEvent(
                    writer, gameEvent, state.EventDefinitions[gameEvent.EventId]);
                break;

            default:
                // Unreachable: CanWrite gates every call, and the two lists are the same list.
                throw new NotSupportedException(message.Type.ToString());
        }
    }

    /// <summary>Writes a <c>svc_CreateStringTable</c> back from its wire form.</summary>
    /// <remarks>
    /// The payload goes back compressed if it arrived compressed. Recompressing would mean
    /// reproducing a particular Snappy implementation's output byte for byte, which no parser can
    /// promise — so the compressed bits are what is kept and what is written.
    /// </remarks>
    private static void WriteCreateStringTable(
        BitWriter writer, CreateStringTableMessage table, int protocol)
    {
        CreateStringTableWire wire = table.Wire!;

        writer.WriteString(table.Name)
            .Write((uint)table.MaxEntries, 16)
            .Write((uint)wire.EntryCount, WireWidths.StringTableEntryCount(table.MaxEntries));

        if (protocol > StringTableCodec.VarIntLengthProtocol)
        {
            VarInt.WriteUInt32(writer, (uint)wire.BodyBits);
        }
        else
        {
            writer.Write((uint)wire.BodyBits, 20);
        }

        if (wire.FixedUserDataSizeBytes is { } bytes)
        {
            writer.WriteBit(true).Write((uint)bytes, 12).Write((uint)wire.FixedUserDataSizeBits, 4);
        }
        else
        {
            writer.WriteBit(false);
        }

        // The compression flag arrived at protocol 15; below that the bit is not on the wire at
        // all, so writing "not compressed" there would insert one.
        if (protocol > StringTableCodec.CompressionFlagProtocol)
        {
            writer.WriteBit(table.IsCompressed);
        }

        writer.AppendBits(wire.Body.Span, wire.BodyBits);
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
