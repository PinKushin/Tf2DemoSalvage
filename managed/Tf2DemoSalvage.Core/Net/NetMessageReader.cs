using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Walks the network message stream inside a <c>dem_packet</c> payload.
/// </summary>
/// <remarks>
/// A packet payload is <c>[6-bit type][body]</c> repeated, with no length prefix on any
/// message. That single fact shapes everything here: **there is no skip**. The next message
/// begins wherever the previous body ended, so reaching a type this reader cannot decode means
/// the remainder of the packet is unreachable, not empty.
///
/// So the reader stops and reports, rather than guessing a width or silently returning what it
/// managed. Support is added one message type at a time, and each addition unlocks whatever
/// followed it.
/// </remarks>
public static class NetMessageReader
{
    /// <summary>Protocol above which <c>svc_Prefetch</c> carries a 14-bit index.</summary>
    internal const int PrefetchWidthProtocol = 22;

    /// <summary>Width of that index, before and after.</summary>
    internal const int PrefetchBitsModern = 14;
    internal const int PrefetchBitsLegacy = 13;

    /// <summary>Width of an unreliable <c>svc_Sounds</c> payload length.</summary>
    internal const int SoundsLengthBits = 16;

    /// <summary>Width of a reliable one, which also omits the count byte entirely.</summary>
    internal const int SoundsReliableLengthBits = 8;

    /// <summary>Protocol above which <c>svc_TempEntities</c> uses a varint length.</summary>
    internal const int TempEntitiesVarIntProtocol = 23;

    /// <summary>Its fixed length width before that.</summary>
    internal const int TempEntitiesLegacyLengthBits = 17;

    /// <summary>
    /// Width of the length field on <c>svc_UserMessage</c> and <c>svc_EntityMessage</c>.
    /// </summary>
    internal const int UserMessageLengthBits = 11;

    /// <summary>Width of <c>svc_EntityMessage</c>'s entity index.</summary>
    internal const int EntityMessageIndexBits = 11;

    /// <summary>Width of its class id, which is fixed rather than derived from the class count.</summary>
    internal const int EntityMessageClassBits = 9;

    /// <summary>Width of <c>svc_VoiceData</c>'s payload length.</summary>
    internal const int VoiceDataLengthBits = 16;

    /// <summary>Width of a decal's texture index: the SDK's MAX_DECAL_INDEX_BITS.</summary>
    internal const int DecalTextureBits = 9;

    /// <summary>Width of an entity index in svc_BspDecal: MAX_EDICT_BITS.</summary>
    internal const int EntityIndexBits = 11;

    /// <summary>Width of a model index in svc_BspDecal: SP_MODEL_INDEX_BITS.</summary>
    internal const int ModelIndexBits = 13;

    /// <summary>Width of <c>svc_SetView</c>'s entity index.</summary>
    internal const int SetViewBits = 11;

    /// <summary>Quality value at which <c>svc_VoiceInit</c> also carries a sample rate.</summary>
    internal const int VoiceVariableRateQuality = 255;

    /// <summary>Bits per byte, for the two messages whose lengths are stated in bytes.</summary>
    private const int BitsPerByte = 8;

    /// <summary>
    /// A stand-in property describing <c>SPROP_COORD</c>, so <c>svc_BspDecal</c>'s positions
    /// reuse the entity coordinate decoder rather than a second copy of that bit layout.
    /// </summary>
    private static readonly Schema.SendProperty CoordProperty =
        new(Schema.SendPropType.Float, "bspdecal", 1 << 1, string.Empty, 0f, 0f, 0, 0);

    /// <summary>Width of <c>net_Tick</c>'s body: a 32-bit tick and two 16-bit values.</summary>
    private const int NetTickBodyBits = 64;

    /// <summary>Reads as much of <paramref name="payload"/> as is currently supported.</summary>
    /// <param name="payload">One <c>dem_packet</c> payload.</param>
    /// <returns>The messages decoded and where the walk ended.</returns>
    public static NetMessageReadResult Read(ReadOnlySpan<byte> payload) =>
        Read(payload, new NetDecodeState());

    /// <summary>
    /// Reads a packet, carrying decode state forward from earlier packets.
    /// </summary>
    /// <param name="payload">One <c>dem_packet</c> payload.</param>
    /// <param name="state">
    /// State accumulated from earlier packets. Game events cannot be decoded without the
    /// definitions from a prior <c>svc_GameEventList</c>, so a packet read in isolation will
    /// report its events as undecoded.
    /// </param>
    /// <returns>The messages decoded and where the walk ended.</returns>
    public static NetMessageReadResult Read(ReadOnlySpan<byte> payload, NetDecodeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        BitReader reader = new(payload);
        List<INetMessage> messages = new();
        List<int> messageStarts = new();
        int lastGoodBit = 0;

        // Fewer bits left than a type field means trailing padding: packets are padded to a
        // byte boundary, so this is the normal way a healthy packet ends.
        //
        // The walk is wrapped because a packet can end part-way through a message body. That is
        // reported rather than thrown: the messages already read are good, and the caller
        // decides whether a partial packet is usable. It could not happen until
        // svc_TempEntities, svc_Sounds and svc_Prefetch were implemented — before that an
        // unimplemented type stopped the walk before any real body could run off the end.
        try
        {
            int typeBits = state.MessageTypeBits;
            while (reader.BitsRemaining >= typeBits)
            {
                int typeStartBit = reader.BitsRead;
                uint rawType = reader.ReadUInt32(typeBits);

                if (!Enum.IsDefined((NetMessageType)rawType))
                {
                    return Stopped(messages, messageStarts, lastGoodBit, null, string.Create(
                        CultureInfo.InvariantCulture,
                        $"Unrecognised message id {rawType} at bit {typeStartBit}. Ids 1, 9, " +
                        $"16, 20 and 22 are unused at network protocol 24 - an id in that set " +
                        $"usually means an earlier message overread, not a new message type."));
                }

                NetMessageType type = (NetMessageType)rawType;
                int bodyStartBit = reader.BitsRead;
                int messageCountBefore = messages.Count;

                switch (type)
                {
                    case NetMessageType.Empty:
                        // net_NOP is padding: the type field and nothing else. Still reported,
                        // so the message count reflects what the stream actually holds.
                        messages.Add(NetEmptyMessage.Instance);
                        break;

                    case NetMessageType.NetTick:
                        if (reader.BitsRemaining < NetTickBodyBits)
                        {
                            return Stopped(messages, messageStarts, lastGoodBit, null, string.Create(
                                CultureInfo.InvariantCulture,
                                $"Packet is truncated: {type} at bit {typeStartBit} needs " +
                                $"{NetTickBodyBits} body bits but only {reader.BitsRemaining} remain."));
                        }

                        messages.Add(new NetTickMessage(
                            (int)reader.ReadUInt32(32),
                            (ushort)reader.ReadUInt32(16),
                            (ushort)reader.ReadUInt32(16)));
                        break;

                    case NetMessageType.Print:
                        messages.Add(new PrintMessage(NetBitReading.ReadString(ref reader)));
                        break;

                    case NetMessageType.StringCmd:
                        messages.Add(new StringCmdMessage(NetBitReading.ReadString(ref reader)));
                        break;

                    case NetMessageType.SetConVar:
                        {
                            int count = (int)reader.ReadUInt32(8);
                            List<KeyValuePair<string, string>> variables = new(count);
                            for (int i = 0; i < count; i++)
                            {
                                string key = NetBitReading.ReadString(ref reader);
                                variables.Add(new KeyValuePair<string, string>(
                                    key, NetBitReading.ReadString(ref reader)));
                            }

                            messages.Add(new SetConVarMessage(variables));
                            break;
                        }

                    case NetMessageType.ServerInfo:
                        {
                            ServerInfoMessage info = ReadServerInfo(ref reader);
                            state.ServerInfo = info;
                            messages.Add(info);
                            break;
                        }

                    case NetMessageType.PacketEntities:
                        {
                            int maxEntries = (int)reader.ReadUInt32(11);
                            bool isDelta = reader.ReadBit();
                            int? deltaFrom = isDelta ? (int)reader.ReadUInt32(32) : null;
                            bool baseline = reader.ReadBit();
                            int updatedEntries = (int)reader.ReadUInt32(11);
                            int entityBits = (int)reader.ReadUInt32(20);
                            bool updateBaseline = reader.ReadBit();

                            // Copied out rather than decoded in place. EntityDecoder needs the schema,
                            // which arrives in dem_datatables - a different demo command - so the body
                            // is carried until a caller has both.
                            byte[] body = NetBitReading.CopyBits(ref reader, entityBits);

                            messages.Add(new PacketEntitiesMessage(
                                maxEntries, isDelta, deltaFrom, baseline, updatedEntries, entityBits,
                                updateBaseline, body));
                            break;
                        }

                    case NetMessageType.ClassInfo:
                        {
                            ClassInfoMessage info = ReadClassInfo(ref reader);
                            state.ClassInfo = info;
                            messages.Add(info);
                            break;
                        }

                    case NetMessageType.CreateStringTable:
                        {
                            CreateStringTableMessage table = StringTableCodec.ReadCreate(ref reader, state);
                            state.AddStringTable(table.Name, table.MaxEntries);
                            messages.Add(table);
                            break;
                        }

                    case NetMessageType.UpdateStringTable:
                        messages.Add(StringTableCodec.ReadUpdate(ref reader, state));
                        break;

                    case NetMessageType.GameEventList:
                        {
                            GameEventListMessage list = GameEventCodec.ReadList(ref reader);
                            state.AddEventDefinitions(list.Definitions);
                            messages.Add(list);
                            break;
                        }

                    case NetMessageType.GameEvent:
                        messages.Add(GameEventCodec.ReadEvent(ref reader, state));
                        break;

                    case NetMessageType.Prefetch:
                        {
                            // A single index, one bit wider from protocol 23 onward. Nothing else.
                            int protocol = state.ServerInfo?.NetworkProtocol ?? 0;
                            int soundIndex = (int)reader.ReadUInt32(protocol > PrefetchWidthProtocol
                                ? PrefetchBitsModern
                                : PrefetchBitsLegacy);
                            messages.Add(new PrefetchMessage(soundIndex));
                            break;
                        }

                    case NetMessageType.Sounds:
                        {
                            // The reliable flag changes two fields at once: a reliable message implies a
                            // single sound and shrinks its length field to eight bits, while an
                            // unreliable one sends a count byte and a sixteen-bit length. Reading one
                            // shape for the other consumes the wrong number of bits.
                            bool reliable = reader.ReadBit();

                            // Reliable implies exactly one sound and sends no count byte.
                            int soundCount = reliable ? 1 : (int)reader.ReadUInt32(8);

                            int soundBits = (int)reader.ReadUInt32(reliable
                                ? SoundsReliableLengthBits
                                : SoundsLengthBits);
                            byte[] soundBody = NetBitReading.CopyBits(ref reader, soundBits);

                            // The body is carried rather than dropped, and SoundDecoder reads it.
                            // It stayed opaque for a long time because the reference parser leaves
                            // it opaque - which was a fact about that parser's priorities, not
                            // about the format, and svc_Sounds was the last undeciphered message
                            // in the codec because of it.
                            messages.Add(new SoundsMessage(
                                reliable, soundCount, soundBits, soundBody));
                            break;
                        }

                    case NetMessageType.TempEntities:
                        {
                            // Length-prefixed like svc_PacketEntities, so the payload can be stepped
                            // over exactly without understanding the events inside it.
                            int effectCount = (int)reader.ReadUInt32(8);
                            int protocol = state.ServerInfo?.NetworkProtocol ?? 0;
                            int eventBits = protocol > TempEntitiesVarIntProtocol
                                ? (int)VarInt.ReadUInt32(ref reader)
                                : (int)reader.ReadUInt32(TempEntitiesLegacyLengthBits);
                            byte[] eventBody = NetBitReading.CopyBits(ref reader, eventBits);
                            messages.Add(new TempEntitiesMessage(
                                effectCount, eventBits, eventBody));
                            break;
                        }

                    case NetMessageType.FixAngle:
                        // A relative flag and three 16-bit angles. Read separately because the
                        // total is 49 bits and ReadUInt32 tops out at 32.
                        bool relativeAngle = reader.ReadBit();
                        float pitch = ReadAngle(ref reader);
                        float yaw = ReadAngle(ref reader);
                        float roll = ReadAngle(ref reader);
                        messages.Add(new FixAngleMessage(relativeAngle, pitch, yaw, roll));
                        break;

                    case NetMessageType.File:
                    {
                        uint transferId = reader.ReadUInt32(32);
                        string fileName = NetBitReading.ReadString(ref reader);
                        messages.Add(new FileMessage(transferId, fileName, reader.ReadBit()));
                        break;
                    }

                    case NetMessageType.GetCvarValue:
                        messages.Add(new GetCvarValueMessage(
                            reader.ReadUInt32(32), NetBitReading.ReadString(ref reader)));
                        break;

                    case NetMessageType.Menu:
                    {
                        // Length in BYTES, unlike every other length in this format. Reading it
                        // as bits consumes an eighth of the payload and leaves the remainder to
                        // be misread as messages.
                        _ = reader.ReadUInt32(16);
                        int menuBytes = (int)reader.ReadUInt32(16);
                        if (!TryReadByteBody(ref reader, menuBytes, out string? menuProblem))
                        {
                            return Stopped(messages, messageStarts, lastGoodBit, type, menuProblem!);
                        }

                        break;
                    }

                    case NetMessageType.CmdKeyValues:
                    {
                        int keyValueBytes = (int)reader.ReadUInt32(32);
                        if (!TryReadByteBody(ref reader, keyValueBytes, out string? keyProblem))
                        {
                            return Stopped(messages, messageStarts, lastGoodBit, type, keyProblem!);
                        }

                        break;
                    }

                    case NetMessageType.BspDecal:
                    {
                        // The only variable-width message here. Three presence bits choose
                        // which axes follow, and each present axis is SPROP_COORD - the same
                        // encoding entity origins use, so the decoder is shared rather than
                        // reimplemented. Reading three coordinates unconditionally would
                        // consume bits that are not on the wire.
                        bool hasX = reader.ReadBit();
                        bool hasY = reader.ReadBit();
                        bool hasZ = reader.ReadBit();

                        // Written out rather than looped: the reader is a ref struct and cannot
                        // cross a lambda, and all three presence bits are read before any
                        // coordinate is, so the order matters.
                        //
                        // Kept rather than discarded. This is where a bullet hole or a scorch mark
                        // is, which is exactly the sort of thing a viewer draws, and it was being
                        // decoded correctly and then thrown away.
                        float? decalX = hasX
                            ? Schema.SendPropDecoder.ReadFloat(ref reader, CoordProperty)
                            : null;

                        float? decalY = hasY
                            ? Schema.SendPropDecoder.ReadFloat(ref reader, CoordProperty)
                            : null;

                        float? decalZ = hasZ
                            ? Schema.SendPropDecoder.ReadFloat(ref reader, CoordProperty)
                            : null;

                        // Nine bits, not sixteen. An earlier version of this read three 16-bit
                        // fields here because the reference parser's *struct* declares them as
                        // u16 - but the struct is its in-memory shape, and its reader uses
                        // explicit widths that are nothing like it. Reading the struct instead
                        // of the reader cost a 14-to-38-bit overread (RISKS B16).
                        int decalTexture = (int)reader.ReadUInt32(DecalTextureBits);

                        // The entity and model indices are present only when this flag is set,
                        // which is most of the difference: a world decal carries neither.
                        bool onEntity = reader.ReadBit();
                        int decalEntity = 0;
                        int decalModel = 0;
                        if (onEntity)
                        {
                            decalEntity = (int)reader.ReadUInt32(EntityIndexBits);
                            decalModel = (int)reader.ReadUInt32(ModelIndexBits);
                        }

                        bool lowPriority = reader.ReadBit();
                        messages.Add(new BspDecalMessage(
                            onEntity, decalEntity, decalModel,
                            decalX, decalY, decalZ, decalTexture, lowPriority));
                        break;
                    }

                    case NetMessageType.VoiceData:
                    {
                        // Client and proximity bytes, then a 16-bit length. Both were already
                        // being read and thrown away, so a trace reported only a bit count and
                        // lost the one thing a voice packet says: who was talking. The codec
                        // payload stays opaque - decoding Speex or CELT is a different project.
                        int voiceClient = (int)reader.ReadUInt32(8);
                        int voiceProximity = (int)reader.ReadUInt32(8);
                        int voiceBits = (int)reader.ReadUInt32(VoiceDataLengthBits);
                        byte[] voiceBody = NetBitReading.CopyBits(ref reader, voiceBits);

                        messages.Add(new VoiceDataMessage(
                            voiceClient, voiceProximity, voiceBits, voiceBody));
                        break;
                    }

                    case NetMessageType.SetPause:
                        _ = reader.ReadBit();
                        break;

                    case NetMessageType.UserMessage:
                    {
                        // A type byte, then an 11-bit length. Only chat is decoded; the rest are
                        // stepped over, which the length makes safe.
                        int userType = (int)reader.ReadUInt32(8);
                        int userBits = (int)reader.ReadUInt32(UserMessageLengthBits);
                        byte[] userBody = NetBitReading.CopyBits(ref reader, userBits);

                        if (userType == ChatMessage.SayText2Type &&
                            ChatMessage.Parse(userBody) is ChatMessage chat)
                        {
                            messages.Add(chat with { BodyBits = userBits, Body = userBody });
                            break;
                        }

                        // Everything else is named, and the handful worth reading also has its
                        // body decoded. A type with no layout, or one whose layout does not
                        // consume the body exactly, keeps its name and length and reports no
                        // fields - see UserMessageBody for why that refusal is the point.
                        messages.Add(UserMessageBody.Decode(
                                userType,
                                UserMessageNames.Lookup(userType, state.NetworkProtocol),
                                userBody, userBits,
                                state.NetworkProtocol,
                                UserMessageNames.Alternate(userType, state.NetworkProtocol))
                            with { Body = userBody });
                        break;
                    }

                    case NetMessageType.EntityMessage:
                    {
                        // Index and class come before the length, so a reader that went
                        // straight for the length would take twenty of their bits instead.
                        int targetEntity = (int)reader.ReadUInt32(EntityMessageIndexBits);
                        int targetClass = (int)reader.ReadUInt32(EntityMessageClassBits);
                        int entityMessageBits = (int)reader.ReadUInt32(UserMessageLengthBits);
                        byte[] entityMessageBody =
                            NetBitReading.CopyBits(ref reader, entityMessageBits);
                        messages.Add(new EntityMessage(
                            targetEntity, targetClass, entityMessageBits, entityMessageBody));
                        break;
                    }

                    case NetMessageType.SetView:
                        // Which entity the client's view follows. One index, nothing else.
                        messages.Add(new SetViewMessage((int)reader.ReadUInt32(SetViewBits)));
                        break;

                    case NetMessageType.SignOnState:
                        messages.Add(new SignOnStateMessage(
                            (int)reader.ReadUInt32(8), (int)reader.ReadUInt32(32)));
                        break;

                    case NetMessageType.VoiceInit:
                    {
                        // The sample rate is only transmitted at quality 255. Older messages
                        // imply it from the codec name — 22050 for celt, 11025 otherwise — so
                        // reading sixteen bits unconditionally would consume what follows.
                        string codec = NetBitReading.ReadString(ref reader);
                        int quality = (int)reader.ReadUInt32(8);
                        int? sampleRate = null;
                        if (quality == VoiceVariableRateQuality)
                        {
                            quality = (int)reader.ReadUInt32(16);
                            sampleRate = quality;
                        }

                        messages.Add(new VoiceInitMessage(codec, quality, sampleRate));
                        break;
                    }

                    default:
                        return Stopped(messages, messageStarts, lastGoodBit, type, string.Create(
                            CultureInfo.InvariantCulture,
                            $"{type} at bit {typeStartBit} is not decoded yet. Messages carry no " +
                            $"length prefix, so the rest of this packet cannot be reached until " +
                            $"it is implemented."));
                }

                // Every message accounts for itself, including the ones read purely for
                // alignment. A trace that omitted them would show consumed bits with nothing to
                // attribute them to - see SkippedMessage.
                if (messages.Count == messageCountBefore)
                {
                    messages.Add(new SkippedMessage(type, reader.BitsRead - bodyStartBit));
                }

                // Exactly one message per iteration, which is what keeps this list parallel to
                // the message list. The check above is what guarantees it: a case that adds
                // nothing gets a SkippedMessage instead.
                messageStarts.Add(typeStartBit);
                lastGoodBit = reader.BitsRead;
            }
        }
        catch (EndOfStreamException exception)
        {
            return Stopped(messages, messageStarts, lastGoodBit, null, string.Create(
                CultureInfo.InvariantCulture,
                $"A message body ran past the end of the packet: {exception.Message}"));
        }
        catch (InvalidDataException exception)
        {
            // An impossible count is the same kind of event as running off the end - this packet
            // cannot be read from here - and so it gets the same treatment: report where the walk
            // stopped rather than throwing at the caller. The trace's whole contract is that it
            // says what it could not read.
            //
            // Worth being precise about what this catch changed, because it is not a loosening.
            // Before the bounds check existed, a misaligned stream reaching svc_ClassInfo read a
            // garbage count and built thousands of nonsense classes in silence; the corpus tests
            // that now report a stop here were previously consuming that garbage without
            // complaint. Two of them were reading counts of 32876 and 18961 classes against a
            // game that has a few hundred.
            return Stopped(messages, messageStarts, lastGoodBit, null, string.Create(
                CultureInfo.InvariantCulture,
                $"A message declared more than the packet can hold: {exception.Message}"));
        }

        return new NetMessageReadResult
        {
            Messages = messages,
            MessageStartBits = messageStarts,
            BitsConsumed = lastGoodBit,
        };
    }

    /// <summary>Reads a 16-bit fixed-point angle as degrees.</summary>
    /// <remarks>
    /// Source sends angles as a fraction of a full turn, so the conversion is
    /// <c>value x 360 / 65536</c>. Reporting the raw integer would be honest but useless - the
    /// value of svc_FixAngle to a reader is where the player was made to look.
    /// </remarks>
    internal static float ReadAngle(ref BitReader reader) =>
        reader.ReadUInt32(16) * (360f / 65536f);

    /// <summary>
    /// Consumes a body whose length is stated in bytes, rejecting a length the packet cannot
    /// hold.
    /// </summary>
    /// <param name="reader">Reader positioned at the body.</param>
    /// <param name="byteCount">Declared length, in bytes.</param>
    /// <param name="problem">Why the length was rejected, when it was.</param>
    /// <returns><c>false</c> when the length is impossible.</returns>
    /// <remarks>
    /// The check is not defensive padding. <c>svc_CmdKeyValues</c> declares a 32-bit byte
    /// count, and multiplying an implausible one by eight overflows <see cref="int"/> before it
    /// can be compared against anything — which is how this surfaced, as an
    /// <see cref="OverflowException"/> escaping the reader on real demos.
    ///
    /// Reported rather than thrown, and reported <em>as a stop</em>, because an impossible
    /// length means either this message's layout is wrong or the reader reached it misaligned.
    /// Both deserve to be visible in <c>StoppedAt</c> rather than silently skipped — see
    /// <c>RISKS.md</c> B15.
    /// </remarks>
    private static bool TryReadByteBody(ref BitReader reader, int byteCount, out string? problem)
    {
        problem = null;

        if (byteCount < 0 || (long)byteCount * BitsPerByte > reader.BitsRemaining)
        {
            problem = string.Create(
                CultureInfo.InvariantCulture,
                $"declares {byteCount} bytes of body but only " +
                $"{reader.BitsRemaining / BitsPerByte} remain in the packet.");
            return false;
        }

        _ = NetBitReading.CopyBits(ref reader, byteCount * BitsPerByte);
        return true;
    }

    /// <summary>
    /// Reads <c>svc_ServerInfo</c>. No length prefix, so every width below must be exact or
    /// the entire signon stream behind it becomes unreadable.
    /// </summary>
    private static ServerInfoMessage ReadServerInfo(ref BitReader reader)
    {
        ushort protocol = (ushort)reader.ReadUInt32(16);
        uint serverCount = reader.ReadUInt32(32);
        bool sourceTv = reader.ReadBit();
        bool dedicated = reader.ReadBit();
        uint mapCrc = reader.ReadUInt32(32);
        ushort maxClasses = (ushort)reader.ReadUInt32(16);

        // Protocol 18 replaced the 4-byte map CRC with a 16-byte hash. Our corpus is all
        // protocol 24; the older branch is written from the reference implementation and has
        // no specimen to verify it against, so it is flagged rather than trusted.
        byte[] mapHash = new byte[protocol > 17 ? 16 : 4];
        for (int i = 0; i < mapHash.Length; i++)
        {
            mapHash[i] = reader.ReadByte();
        }

        byte playerSlot = reader.ReadByte();
        byte maxPlayers = reader.ReadByte();
        float intervalPerTick = BitConverter.Int32BitsToSingle((int)reader.ReadUInt32(32));
        char platform = (char)reader.ReadByte();

        string game = NetBitReading.ReadString(ref reader);
        string map = NetBitReading.ReadString(ref reader);
        string skybox = NetBitReading.ReadString(ref reader);
        string serverName = NetBitReading.ReadString(ref reader);

        bool replay = protocol > 15 && reader.ReadBit();

        return new ServerInfoMessage(
            protocol,
            serverCount,
            sourceTv,
            dedicated,
            mapCrc,
            maxClasses,
            mapHash,
            playerSlot,
            maxPlayers,
            intervalPerTick,
            platform,
            game,
            map,
            skybox,
            serverName,
            replay);
    }

    /// <summary>
    /// Reads <c>svc_ClassInfo</c>. Class ids are written at a width derived from the count,
    /// so the count has to be read before the entries can be.
    /// </summary>
    private static ClassInfoMessage ReadClassInfo(ref BitReader reader)
    {
        int count = (int)reader.ReadUInt32(16);
        bool createOnClient = reader.ReadBit();

        if (createOnClient)
        {
            // Nothing follows. Reading entries anyway would consume bits belonging to the
            // next message.
            return new ClassInfoMessage(count, true, []);
        }

        int idBits = WireWidths.ClassId(count);

        // Before the List is sized, not after: `new List<ServerClass>(count)` allocates for the
        // declared count, so a garbage count is an allocation as well as a loop. Each class is an
        // id plus two null-terminated strings, and an empty string still costs its terminator.
        Primitives.WireBounds.EnsureCountFits(
            "svc_classinfo", count, idBits + 16, reader.BitsRemaining);

        List<ServerClass> classes = new(count);
        for (int i = 0; i < count; i++)
        {
            classes.Add(new ServerClass(
                (int)reader.ReadUInt32(idBits),
                NetBitReading.ReadString(ref reader),
                NetBitReading.ReadString(ref reader)));
        }

        return new ClassInfoMessage(count, false, classes);
    }

    private static NetMessageReadResult Stopped(
        List<INetMessage> messages,
        List<int> messageStarts,
        int bitsConsumed,
        NetMessageType? stoppedAt,
        string reason) =>
        new()
        {
            Messages = messages,
            MessageStartBits = messageStarts,
            BitsConsumed = bitsConsumed,
            StoppedAt = stoppedAt,
            StopReason = reason,
        };
}
