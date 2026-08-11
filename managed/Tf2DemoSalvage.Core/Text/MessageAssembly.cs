using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// Renders individual network messages as assembly text, and reads them back.
/// </summary>
/// <remarks>
/// **The step that turns a hex dump into a decompiler.** <see cref="DemoAssembly"/> already
/// round-trips a demo byte for byte with every packet payload as hex; this promotes messages out
/// of that hex one type at a time, and anything without a text form stays as <c>raw</c>. The round
/// trip is a gate throughout, so a type can only be promoted by being exactly reproducible.
///
/// **The fields are wire values, not display values.** <c>net_tick</c> carries its two raw 16-bit
/// counters rather than the seconds a trace prints, because seconds are a division and the file
/// has to compile back to the same bits. Where a value is genuinely a float on the wire it is
/// written with the round-trip format for the same reason.
///
/// A <c>raw</c> line is not a failure. It states its bit length and its bits, which is exactly what
/// the decoder knew, and it keeps the file complete while the type it stands for is still opaque
/// or simply not done yet.
/// </remarks>
public static class MessageAssembly
{
    /// <summary>Keyword for a message with no text form yet.</summary>
    private const string RawKeyword = "raw";

    /// <summary>Whether this message has a text form.</summary>
    /// <param name="message">The message.</param>
    /// <returns><c>true</c> when <see cref="Write"/> can render it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
    public static bool CanWrite(INetMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // svc_Prefetch is deliberately absent despite being trivial. Its index is 13 bits at or
        // below protocol 22 and 14 above, and the writer takes that from svc_ServerInfo - which
        // arrives as a raw line and so never reaches the assembler's state. A message whose width
        // depends on another message cannot be promoted before that one is.
        return message is NetEmptyMessage or NetTickMessage or PrintMessage or StringCmdMessage or
            SetConVarMessage or SignOnStateMessage or SetViewMessage or
            FixAngleMessage or FileMessage or GetCvarValueMessage;
    }

    /// <summary>Renders a message as one line of assembly.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The line, without its newline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException">The message has no text form.</exception>
    public static string Write(INetMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message switch
        {
            NetEmptyMessage => "net_nop",

            NetTickMessage tick => Line(
                "net_tick", tick.Tick, tick.HostFrameTimeRaw, tick.HostFrameTimeStdDevRaw),

            PrintMessage print => $"svc_print {Quote(print.Text)}",

            StringCmdMessage command => $"net_stringcmd {Quote(command.Command)}",

            SetConVarMessage convars => WriteConVars(convars),

            SignOnStateMessage signon => Line("net_signonstate", signon.State, signon.SpawnCount),

            SetViewMessage view => Line("svc_setview", view.EntityIndex),

            FixAngleMessage angle => string.Create(
                CultureInfo.InvariantCulture,
                $"svc_fixangle {(angle.IsRelative ? 1 : 0)} " +
                $"{Round(angle.Pitch)} {Round(angle.Yaw)} {Round(angle.Roll)}"),

            FileMessage file => string.Create(
                CultureInfo.InvariantCulture,
                $"svc_file {file.TransferId} {Quote(file.FileName)} {(file.IsRequested ? 1 : 0)}"),

            GetCvarValueMessage cvar =>
                $"svc_getcvarvalue {cvar.Cookie.ToString(CultureInfo.InvariantCulture)} " +
                Quote(cvar.CvarName),

            _ => throw new NotSupportedException(message.Type.ToString()),
        };
    }

    /// <summary>Renders bits with no text form as a <c>raw</c> line.</summary>
    /// <param name="bits">Buffer holding the bits, starting at bit zero.</param>
    /// <param name="bitCount">How many of them are meaningful.</param>
    /// <returns>The line.</returns>
    public static string WriteRaw(ReadOnlySpan<byte> bits, int bitCount) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{RawKeyword} {bitCount} {Convert.ToHexString(bits)}");

    /// <summary>Reads one assembly line back into bits.</summary>
    /// <param name="line">The line.</param>
    /// <param name="writer">Destination for the message's bits.</param>
    /// <param name="state">Decode state, which sizes the type field and conditional fields.</param>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    /// <exception cref="InvalidDataException">The line is not a message this can assemble.</exception>
    public static void Assemble(string line, BitWriter writer, NetDecodeState state)
    {
        ArgumentNullException.ThrowIfNull(line);
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
            int bits = int.Parse(tokens[1], CultureInfo.InvariantCulture);
            writer.AppendBits(Convert.FromHexString(tokens[2]), bits);
            return;
        }

        INetMessage message = Build(tokens);
        if (!NetMessageWriter.TryWrite(writer, message, state))
        {
            throw new InvalidDataException(
                $"'{tokens[0]}' has a text form but no encoder, which cannot happen by design.");
        }
    }

    private static INetMessage Build(List<string> tokens) => tokens[0] switch
    {
        "net_nop" => NetEmptyMessage.Instance,

        "net_tick" => new NetTickMessage(
            Integer(tokens, 1), (ushort)Integer(tokens, 2), (ushort)Integer(tokens, 3)),

        "svc_print" => new PrintMessage(tokens[1]),

        "net_stringcmd" => new StringCmdMessage(tokens[1]),

        "net_setconvar" => BuildConVars(tokens),

        "net_signonstate" => new SignOnStateMessage(Integer(tokens, 1), Integer(tokens, 2)),

        "svc_setview" => new SetViewMessage(Integer(tokens, 1)),

        "svc_fixangle" => new FixAngleMessage(
            Integer(tokens, 1) != 0, Real(tokens, 2), Real(tokens, 3), Real(tokens, 4)),

        "svc_file" => new FileMessage(
            (uint)Integer(tokens, 1), tokens[2], Integer(tokens, 3) != 0),

        "svc_getcvarvalue" => new GetCvarValueMessage((uint)Integer(tokens, 1), tokens[2]),

        _ => throw new InvalidDataException($"Unknown message '{tokens[0]}'."),
    };

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
        // The count is on the wire as its own byte, so it is written rather than inferred from
        // the pairs that follow - a message declaring more than it carries is a real shape and
        // has to survive the round trip.
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
