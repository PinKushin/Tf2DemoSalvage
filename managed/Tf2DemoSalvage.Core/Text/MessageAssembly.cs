using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// Renders individual network messages as assembly text, and reads them back.
/// </summary>
/// <remarks>
/// **The step that turns a hex dump into a decompiler.** <see cref="DemoAssembly"/> round-trips a
/// demo byte for byte; this promotes messages out of the <c>raw</c> hex one type at a time, and
/// anything without a text form stays as <c>raw</c>.
///
/// **A promotion cannot break the round trip, by construction.** The writer assembles each
/// candidate back into bits and compares them against the demo before emitting it, falling back to
/// <c>raw</c> on any difference. A text form that is subtly wrong therefore costs coverage rather
/// than correctness, and shows up as a number that did not move.
///
/// **The fields are wire values, not display values.** <c>net_tick</c> carries its two raw 16-bit
/// counters rather than the seconds a trace prints, because seconds are a division and this has to
/// compile back to the same bits. Floats use the round-trip format for the same reason.
///
/// Longer messages open a brace block with one line per element — a sound, a class. That is what
/// makes the format worth reading rather than merely complete: somebody writing a viewer wants to
/// see the sounds, not a hex string that contains them.
/// </remarks>
public static class MessageAssembly
{
    /// <summary>Keyword for a message with no text form yet.</summary>
    private const string RawKeyword = "raw";

    /// <summary>Closes a message's block.</summary>
    private const string BlockEnd = "}";

    /// <summary>Whether this message has a text form.</summary>
    /// <param name="message">The message.</param>
    /// <returns><c>true</c> when <see cref="Write"/> can render it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
    public static bool CanWrite(INetMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message is NetEmptyMessage or NetTickMessage or PrintMessage or StringCmdMessage or
            SetConVarMessage or SignOnStateMessage or SetViewMessage or FixAngleMessage or
            FileMessage or GetCvarValueMessage or PrefetchMessage or ServerInfoMessage or
            ClassInfoMessage or VoiceInitMessage or BspDecalMessage or EntityMessage or
            VoiceDataMessage or UserMessage or ChatMessage or SoundsMessage or
            PacketEntitiesMessage or GameEventListMessage or GameEventMessage or
            TempEntitiesMessage or CreateStringTableMessage or UpdateStringTableMessage;
    }

    /// <summary>Renders a message as one or more lines of assembly.</summary>
    /// <param name="message">The message.</param>
    /// <param name="protocol">The demo's protocol, which sizes some fields.</param>
    /// <param name="entities">
    /// The schema, for messages that cannot be read without one. <c>null</c> before
    /// <c>dem_datatables</c> has gone past, which is when those messages stay as bits.
    /// </param>
    /// <returns>
    /// The lines, without newlines and without the caller's indentation, or <c>null</c> when this
    /// message needs a schema that is not available or carries a body that will not decode.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException">The message has no text form.</exception>
    public static IReadOnlyList<string>? Write(
        INetMessage message, ushort protocol, EntityDecoder? entities)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message switch
        {
            // Null rather than an exception when the schema has not arrived or the body will not
            // decode. The caller falls back to raw, which is what those bits already were.
            PacketEntitiesMessage snapshot =>
                entities is null ? null : EntityAssembly.Write(snapshot, entities),

            TempEntitiesMessage effects =>
                entities is null ? null : EntityAssembly.WriteEffects(effects, entities),

            GameEventListMessage list => EventAssembly.WriteList(list),

            CreateStringTableMessage table => StringTableAssembly.WriteCreate(table),

            UpdateStringTableMessage update => StringTableAssembly.WriteUpdate(update),

            // An event that arrived before its definition decoded to an id and nothing else, so
            // there is nothing to write down.
            GameEventMessage gameEvent =>
                gameEvent.IsDecoded ? EventAssembly.WriteEvent(gameEvent) : null,

            NetEmptyMessage => ["net_nop"],

            NetTickMessage tick =>
                [Line("net_tick", tick.Tick, tick.HostFrameTimeRaw, tick.HostFrameTimeStdDevRaw)],

            PrintMessage print => [$"svc_print {Quote(print.Text)}"],

            StringCmdMessage command => [$"net_stringcmd {Quote(command.Command)}"],

            SetConVarMessage convars => [WriteConVars(convars)],

            SignOnStateMessage signon =>
                [Line("net_signonstate", signon.State, signon.SpawnCount)],

            SetViewMessage view => [Line("svc_setview", view.EntityIndex)],

            PrefetchMessage prefetch => [Line("svc_prefetch", prefetch.SoundIndex)],

            FixAngleMessage angle =>
            [
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"svc_fixangle {(angle.IsRelative ? 1 : 0)} " +
                    $"{Round(angle.Pitch)} {Round(angle.Yaw)} {Round(angle.Roll)}"),
            ],

            FileMessage file =>
            [
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"svc_file {file.TransferId} {Quote(file.FileName)} " +
                    $"{(file.IsRequested ? 1 : 0)}"),
            ],

            GetCvarValueMessage cvar =>
            [
                $"svc_getcvarvalue {cvar.Cookie.ToString(CultureInfo.InvariantCulture)} " +
                Quote(cvar.CvarName),
            ],

            ServerInfoMessage info => [WriteServerInfo(info)],

            ClassInfoMessage classes => WriteClassInfo(classes),

            VoiceInitMessage voice => [WriteVoiceInit(voice)],

            BspDecalMessage decal => [WriteDecal(decal)],

            EntityMessage entity =>
            [
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"svc_entitymessage {entity.EntityIndex} {entity.ClassId} {entity.BodyBits} " +
                    $"{Convert.ToHexString(entity.Body.Span)}"),
            ],

            VoiceDataMessage voice =>
            [
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"svc_voicedata {voice.Client} {voice.Proximity} {voice.BodyBits} " +
                    $"{Convert.ToHexString(voice.Body.Span)}"),
            ],

            // Chat is one of forty-odd payloads sharing svc_UserMessage and goes back as the user
            // message it arrived in. Its decoded text belongs in the trace; here the body is what
            // has to survive.
            ChatMessage chat =>
            [
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"svc_usermessage {ChatMessage.SayText2Type} {chat.BodyBits} " +
                    $"{Convert.ToHexString(chat.Body.Span)}"),
            ],

            UserMessage user =>
            [
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"svc_usermessage {user.UserMessageType} {user.BodyBits} " +
                    $"{Convert.ToHexString(user.Body.Span)}"),
            ],

            SoundsMessage sounds => WriteSounds(sounds, protocol),

            _ => throw new NotSupportedException(message.Type.ToString()),
        };
    }

    /// <summary>Renders bits with no text form as a <c>raw</c> line.</summary>
    /// <param name="bits">Buffer holding the bits, starting at bit zero.</param>
    /// <param name="bitCount">How many of them are meaningful.</param>
    /// <param name="label">What the bits are, as a trailing comment.</param>
    /// <returns>The line.</returns>
    /// <remarks>
    /// The label is a comment, so it is dropped on the way back in and cannot affect the round
    /// trip. It is there because a reader who meets a hex string deserves to know what it stands
    /// for — and because counting what is still opaque, by type, is otherwise guesswork.
    /// </remarks>
    public static string WriteRaw(ReadOnlySpan<byte> bits, int bitCount, string label) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{RawKeyword} {bitCount} {Convert.ToHexString(bits)} # {label}");

    /// <summary>Reads one message's lines back into bits.</summary>
    /// <param name="line">The message's first line.</param>
    /// <param name="nextLine">Supplies further lines when the message opened a block.</param>
    /// <param name="writer">Destination for the message's bits.</param>
    /// <param name="state">Decode state, which sizes the type field and conditional fields.</param>
    /// <param name="entities">The schema, for messages whose values are defined by it.</param>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    /// <exception cref="InvalidDataException">The line is not a message this can assemble.</exception>
    public static void Assemble(
        string line,
        Func<string?> nextLine,
        BitWriter writer,
        NetDecodeState state,
        EntityDecoder? entities = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(nextLine);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(state);

        List<string> tokens = Tokenize(line);
        if (tokens.Count == 0)
        {
            throw new InvalidDataException("An empty line is not a message.");
        }

        if (tokens[0] == RawKeyword)
        {
            // Raw bits go back exactly as they came, including the ones past the last whole byte.
            writer.AppendBits(
                Convert.FromHexString(tokens[2]),
                int.Parse(tokens[1], CultureInfo.InvariantCulture));
            return;
        }

        INetMessage message = Build(tokens, nextLine, state, entities);

        if (!NetMessageWriter.TryWrite(writer, message, state))
        {
            throw new InvalidDataException(
                $"'{tokens[0]}' has a text form but no encoder, which cannot happen by design.");
        }

        // The state has to learn here what the reader learned at the same point in the stream, or
        // every later message whose width depends on it is written at the wrong size.
        if (message is ServerInfoMessage info)
        {
            state.ServerInfo = info;
        }
        else if (message is CreateStringTableMessage table)
        {
            // Registered as the reader registers it: an update names a table by creation order
            // and its entry indices are sized from that table's capacity.
            state.AddStringTable(table.Name, table.MaxEntries);
        }
        else if (message is GameEventListMessage list)
        {
            // Every later event is written against these: the field order lives in the
            // definition, not in the event.
            state.AddEventDefinitions(list.Definitions);
        }
    }

    private static INetMessage Build(
        List<string> tokens,
        Func<string?> nextLine,
        NetDecodeState state,
        EntityDecoder? entities) => tokens[0] switch
    {
        "svc_createstringtable" => StringTableAssembly.BuildCreate(tokens, nextLine),

        "svc_updatestringtable" => StringTableAssembly.BuildUpdate(tokens, nextLine, state),

        "svc_gameeventlist" => EventAssembly.BuildList(tokens, nextLine),

        "svc_gameevent" => EventAssembly.BuildEvent(tokens, nextLine, state),

        "svc_tempentities" => EntityAssembly.BuildEffects(
            tokens,
            nextLine,
            entities ?? throw new InvalidDataException(
                "A temp entities block appeared before any dem_datatables command, so there is " +
                "no schema to read its properties against.")),

        "svc_packetentities" => EntityAssembly.Build(
            tokens,
            nextLine,
            entities ?? throw new InvalidDataException(
                "A packet entities block appeared before any dem_datatables command, so there " +
                "is no schema to read its properties against.")),

        "net_nop" => NetEmptyMessage.Instance,

        "net_tick" => new NetTickMessage(
            Integer(tokens, 1), (ushort)Integer(tokens, 2), (ushort)Integer(tokens, 3)),

        "svc_print" => new PrintMessage(tokens[1]),

        "net_stringcmd" => new StringCmdMessage(tokens[1]),

        "net_setconvar" => BuildConVars(tokens),

        "net_signonstate" => new SignOnStateMessage(Integer(tokens, 1), Integer(tokens, 2)),

        "svc_setview" => new SetViewMessage(Integer(tokens, 1)),

        "svc_prefetch" => new PrefetchMessage(Integer(tokens, 1)),

        "svc_fixangle" => new FixAngleMessage(
            Integer(tokens, 1) != 0, Real(tokens, 2), Real(tokens, 3), Real(tokens, 4)),

        "svc_file" => new FileMessage(
            (uint)Integer(tokens, 1), tokens[2], Integer(tokens, 3) != 0),

        "svc_getcvarvalue" => new GetCvarValueMessage((uint)Integer(tokens, 1), tokens[2]),

        "svc_serverinfo" => BuildServerInfo(tokens),

        "svc_classinfo" => BuildClassInfo(tokens, nextLine),

        "svc_voiceinit" => tokens[2] == "rate"
            ? new VoiceInitMessage(tokens[1], Integer(tokens, 3), Integer(tokens, 3))
            : new VoiceInitMessage(tokens[1], Integer(tokens, 3)),

        "svc_bspdecal" => BuildDecal(tokens),

        "svc_entitymessage" => new EntityMessage(
            Integer(tokens, 1), Integer(tokens, 2), Integer(tokens, 3),
            Convert.FromHexString(tokens[4])),

        "svc_voicedata" => new VoiceDataMessage(
            Integer(tokens, 1), Integer(tokens, 2), Integer(tokens, 3),
            Convert.FromHexString(tokens[4])),

        "svc_usermessage" => new UserMessage(
            Integer(tokens, 1), null, Integer(tokens, 2), null,
            Convert.FromHexString(tokens[3])),

        "svc_sounds" => BuildSounds(tokens, nextLine, state),

        _ => throw new InvalidDataException($"Unknown message '{tokens[0]}'."),
    };

    private static string WriteServerInfo(ServerInfoMessage info) => string.Create(
        CultureInfo.InvariantCulture,
        $"svc_serverinfo {info.NetworkProtocol} {info.ServerCount} " +
        $"{(info.IsSourceTv ? 1 : 0)} {(info.IsDedicated ? 1 : 0)} {info.MapCrc} " +
        $"{info.MaxClasses} {Convert.ToHexString([.. info.MapHash])} {info.PlayerSlot} " +
        $"{info.MaxPlayers} {Round(info.IntervalPerTick)} {(int)info.Platform} " +
        $"{Quote(info.GameDirectory)} {Quote(info.Map)} {Quote(info.Skybox)} " +
        $"{Quote(info.ServerName)} {(info.IsReplay ? 1 : 0)}");

    private static ServerInfoMessage BuildServerInfo(List<string> tokens) => new(
        (ushort)Integer(tokens, 1),
        (uint)Integer(tokens, 2),
        Integer(tokens, 3) != 0,
        Integer(tokens, 4) != 0,
        (uint)Integer(tokens, 5),
        (ushort)Integer(tokens, 6),
        Convert.FromHexString(tokens[7]),
        (byte)Integer(tokens, 8),
        (byte)Integer(tokens, 9),
        Real(tokens, 10),
        (char)Integer(tokens, 11),
        tokens[12],
        tokens[13],
        tokens[14],
        tokens[15],
        Integer(tokens, 16) != 0);

    private static List<string> WriteClassInfo(ClassInfoMessage classes)
    {
        List<string> lines =
        [
            string.Create(
                CultureInfo.InvariantCulture,
                $"svc_classinfo {classes.ClassCount} {(classes.CreateOnClient ? 1 : 0)} {{"),
        ];

        foreach (ServerClass serverClass in classes.Classes)
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"  class {serverClass.Id} {Quote(serverClass.ClassName)} " +
                $"{Quote(serverClass.TableName)}"));
        }

        lines.Add(BlockEnd);
        return lines;
    }

    private static ClassInfoMessage BuildClassInfo(List<string> tokens, Func<string?> nextLine)
    {
        List<ServerClass> classes = [];
        foreach (List<string> entry in Block(nextLine))
        {
            classes.Add(new ServerClass(Integer(entry, 1), entry[2], entry[3]));
        }

        return new ClassInfoMessage(Integer(tokens, 1), Integer(tokens, 2) != 0, classes);
    }

    /// <summary>
    /// Writes <c>svc_VoiceInit</c>, whose quality field doubles as an escape.
    /// </summary>
    /// <remarks>
    /// Quality 255 means a sample rate follows, and the reader overwrites the quality with it. The
    /// two shapes are spelled differently here so they cannot collapse into one.
    /// </remarks>
    private static string WriteVoiceInit(VoiceInitMessage voice) => voice.SampleRate is { } rate
        ? string.Create(
            CultureInfo.InvariantCulture, $"svc_voiceinit {Quote(voice.Codec)} rate {rate}")
        : string.Create(
            CultureInfo.InvariantCulture,
            $"svc_voiceinit {Quote(voice.Codec)} quality {voice.Quality}");

    private static string WriteDecal(BspDecalMessage decal) => string.Create(
        CultureInfo.InvariantCulture,
        $"svc_bspdecal {Axis(decal.X)} {Axis(decal.Y)} {Axis(decal.Z)} {decal.TextureIndex} " +
        $"{(decal.OnEntity ? 1 : 0)} {decal.EntityIndex} {decal.ModelIndex} " +
        $"{(decal.IsLowPriority ? 1 : 0)}");

    private static BspDecalMessage BuildDecal(List<string> tokens) => new(
        Integer(tokens, 5) != 0,
        Integer(tokens, 6),
        Integer(tokens, 7),
        Axis(tokens[1]),
        Axis(tokens[2]),
        Axis(tokens[3]),
        Integer(tokens, 4),
        Integer(tokens, 8) != 0);

    /// <summary>An axis that was not transmitted, which is not the same as one that was zero.</summary>
    private static string Axis(float? value) => value is { } present ? Round(present) : "-";

    private static float? Axis(string token) =>
        token == "-" ? null : float.Parse(token, CultureInfo.InvariantCulture);

    private static List<string> WriteSounds(SoundsMessage message, ushort protocol)
    {
        IReadOnlyList<DecodedSound> sounds = SoundDecoder.Decode(
            message.Body.Span, message.Count, message.BodyBits, protocol);

        List<string> lines =
        [
            string.Create(
                CultureInfo.InvariantCulture, $"svc_sounds {(message.IsReliable ? 1 : 0)} {{"),
        ];

        foreach (DecodedSound sound in sounds)
        {
            // Every field, including the ones a trace leaves out. `sent` is the one that looks
            // like an implementation detail and is not: which fields the sender transmitted is
            // not recoverable from the values, so without it the message cannot be rebuilt.
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"  sound entity={sound.EntityIndex} num={sound.SoundNumber} " +
                $"flags={sound.Flags} channel={sound.Channel} " +
                $"ambient={(sound.IsAmbient ? 1 : 0)} sentence={(sound.IsSentence ? 1 : 0)} " +
                $"seq={sound.SequenceNumber} volume={Round(sound.Volume)} " +
                $"level={sound.SoundLevel} pitch={sound.Pitch} " +
                $"delay={Round(sound.DelaySeconds)} x={Round(sound.OriginX)} " +
                $"y={Round(sound.OriginY)} z={Round(sound.OriginZ)} " +
                $"speaker={sound.SpeakerEntity} dsp={sound.SpecialDsp} sent={(int)sound.Sent}"));
        }

        lines.Add(BlockEnd);
        return lines;
    }

    private static SoundsMessage BuildSounds(
        List<string> tokens, Func<string?> nextLine, NetDecodeState state)
    {
        List<DecodedSound> sounds = [];
        foreach (List<string> entry in Block(nextLine))
        {
            Dictionary<string, string> fields = Fields(entry);
            sounds.Add(new DecodedSound(
                Field(fields, "entity"),
                Field(fields, "num"),
                Field(fields, "flags"),
                Field(fields, "channel"),
                Field(fields, "ambient") != 0,
                Field(fields, "sentence") != 0,
                Field(fields, "seq"),
                Fraction(fields, "volume"),
                Field(fields, "level"),
                Field(fields, "pitch"),
                Fraction(fields, "delay"),
                Fraction(fields, "x"),
                Fraction(fields, "y"),
                Fraction(fields, "z"),
                Field(fields, "speaker"),
                Field(fields, "dsp"),
                (SoundFields)Field(fields, "sent")));
        }

        ushort protocol = state.ServerInfo?.NetworkProtocol ?? state.NetworkProtocol;
        (byte[] body, int bits) = SoundEncoder.Encode(sounds, protocol);

        // The count on the wire is the number of sounds unless the message was reliable, in which
        // case it is not sent at all and the writer infers one.
        return new SoundsMessage(Integer(tokens, 1) != 0, sounds.Count, bits, body);
    }

    /// <summary>Reads a brace block's lines, stopping at the closing brace.</summary>
    private static IEnumerable<List<string>> Block(Func<string?> nextLine)
    {
        while (true)
        {
            string line = nextLine()
                ?? throw new InvalidDataException("A message block was not closed with '}'.");

            List<string> tokens = Tokenize(line);
            if (tokens.Count == 0)
            {
                continue;
            }

            if (tokens[0] == BlockEnd)
            {
                yield break;
            }

            yield return tokens;
        }
    }

    /// <summary>Splits <c>key=value</c> tokens into a lookup.</summary>
    private static Dictionary<string, string> Fields(List<string> tokens)
    {
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        foreach (string token in tokens)
        {
            int equals = token.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                fields[token[..equals]] = token[(equals + 1)..];
            }
        }

        return fields;
    }

    private static int Field(Dictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out string? value)
            ? int.Parse(value, CultureInfo.InvariantCulture)
            : throw new InvalidDataException($"A sound has no '{name}' field.");

    private static float Fraction(Dictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out string? value)
            ? float.Parse(value, CultureInfo.InvariantCulture)
            : throw new InvalidDataException($"A sound has no '{name}' field.");

    private static SetConVarMessage BuildConVars(List<string> tokens)
    {
        List<KeyValuePair<string, string>> variables = [];
        for (int i = 2; i + 1 < tokens.Count; i += 2)
        {
            variables.Add(new KeyValuePair<string, string>(tokens[i], tokens[i + 1]));
        }

        return new SetConVarMessage(variables);
    }

    private static string WriteConVars(SetConVarMessage convars)
    {
        // The count is its own byte on the wire, so it is written rather than inferred from the
        // pairs that follow - a message declaring more than it carries is a real shape and has to
        // survive the round trip.
        StringBuilder line = new("net_setconvar ");
        line.Append(convars.Variables.Count.ToString(CultureInfo.InvariantCulture));

        foreach (KeyValuePair<string, string> variable in convars.Variables)
        {
            line.Append(' ').Append(Quote(variable.Key)).Append(' ').Append(Quote(variable.Value));
        }

        return line.ToString();
    }

    private static int Integer(List<string> tokens, int index) =>
        int.TryParse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : (int)uint.Parse(tokens[index], CultureInfo.InvariantCulture);

    private static float Real(List<string> tokens, int index) =>
        float.Parse(tokens[index], CultureInfo.InvariantCulture);

    /// <summary>The round-trip float format, so the value that comes back is the same value.</summary>
    private static string Round(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Line(string keyword, params int[] values)
    {
        StringBuilder text = new(keyword);
        foreach (int value in values)
        {
            text.Append(' ').Append(value.ToString(CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    /// <summary>Quotes a string, escaping what would otherwise end it.</summary>
    /// <remarks>
    /// Console commands and convar values routinely contain spaces, and a server name can contain
    /// a quote. Neither is exotic enough to leave to chance.
    /// </remarks>
    private static string Quote(string value) =>
        "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

    /// <summary>Splits a line into bare tokens and quoted strings.</summary>
    private static List<string> Tokenize(string line)
    {
        List<string> tokens = [];
        StringBuilder current = new();
        bool quoted = false;
        bool escaped = false;
        bool started = false;

        foreach (char character in line)
        {
            if (escaped)
            {
                current.Append(character == 'n' ? '\n' : character);
                escaped = false;
                continue;
            }

            if (quoted && character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == '"')
            {
                quoted = !quoted;
                started = true;
                continue;
            }

            if (!quoted && char.IsWhiteSpace(character))
            {
                if (started || current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    started = false;
                }

                continue;
            }

            current.Append(character);
        }

        if (started || current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
