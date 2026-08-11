using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Net;



/// <summary>
/// Decodes the bodies of the user messages worth reading, and refuses the rest.
/// </summary>
/// <remarks>
/// **Every layout here is a hypothesis, and the stated length is what tests it.** A user message
/// carries its body's size in bits; a correct layout consumes exactly that many. A wrong one lands
/// on the boundary only by coincidence, so exact consumption is a genuine check on a guessed
/// format — and it is the only one available without the game DLL that defines these.
///
/// **A body that does not consume exactly is reported as undecoded, not as fields.** Wrong values
/// that look plausible are worse than no values. That is RISKS B16 stated as a rule: a message
/// implemented from its struct's C types rather than its read function desynchronised a whole
/// packet while every individual number it produced looked reasonable.
///
/// The types here are chosen from what the corpus actually contains, ordered by how many opaque
/// bits each one accounts for rather than by how useful it sounds.
///
/// **"Opaque" and "carries nothing anyone wants" are different statements, and it is worth being
/// able to make the second.** This file used to say <c>Rumble</c> would never be decoded because
/// it drives a controller. It is decoded now — three bytes, from
/// <c>__MsgFunc_Rumble</c> — and the point is that the claim is now established rather than
/// assumed. Declining to read something is only a judgement once you can read it.
/// </remarks>
public static class UserMessageBody
{
    /// <summary>Builds a <see cref="UserMessage"/>, decoding the body when a layout is known.</summary>
    /// <param name="userMessageType">The game-defined message id.</param>
    /// <param name="name">The registered name, or <c>null</c> if the id is past the table.</param>
    /// <param name="body">The body bytes, as copied from the stream.</param>
    /// <param name="bodyBits">The body's stated length in bits.</param>
    /// <param name="networkProtocol">The demo header's network protocol.</param>
    /// <returns>
    /// The message, with <see cref="UserMessage.Fields"/> set only when a known layout consumed
    /// the body exactly.
    /// </returns>
    public static UserMessage Decode(
        int userMessageType, string? name, ReadOnlySpan<byte> body, int bodyBits,
        int networkProtocol)
    {
        List<KeyValuePair<string, object?>>? fields = name switch
        {
            "TextMsg" => TextMsg(body, bodyBits),
            "SayText" => SayText(body, bodyBits),
            "ItemPickup" => SingleString(body, bodyBits, "item"),
            "Geiger" => SingleByte(body, bodyBits, "range"),
            "Train" => SingleByte(body, bodyBits, "state"),
            "VoiceSubtitle" => VoiceSubtitle(body, bodyBits),
            "Damage" => Damage(body, bodyBits, networkProtocol),
            "Fade" => Fade(body, bodyBits),
            "Shake" => Shake(body, bodyBits),
            "Rumble" => Rumble(body, bodyBits),
            "ResetHUD" => SingleByte(body, bodyBits, "unused"),
            "VGUIMenu" => VguiMenu(body, bodyBits),
            "PlayerStatsUpdate" => PlayerStatsUpdate(body, bodyBits),
            "MapStatsUpdate" => MapStatsUpdate(body, bodyBits),
            _ => null,
        };

        return new UserMessage(userMessageType, name, bodyBits, fields);
    }

    /// <summary><c>Damage</c> — how much, and where it came from.</summary>
    /// <remarks>
    /// **This is what draws a damage number in a POV demo**, and it is the only place the
    /// direction of incoming damage is recorded: entity positions say where everyone stood, this
    /// says which of them hurt you and by how much.
    ///
    /// The layout is Valve's own client rather than a reading of the bytes —
    /// <c>CHudDamageIndicator::MsgFunc_Damage</c> in <c>tf_hud_damageindicator.cpp</c>:
    ///
    /// <code>
    /// damage.iScale = msg.ReadShort();
    /// msg.ReadLong();                       // read and ignored
    /// if ( !msg.ReadOneBit() ) return;
    /// msg.ReadBitVec3Coord( vecOrigin );
    /// </code>
    ///
    /// **The ignored long still has to be read.** The game discards it, but it occupies 32 bits,
    /// and a decoder that skips it takes the position from the wrong place — which would produce
    /// a plausible coordinate rather than an error.
    ///
    /// The vector is three presence bits followed by only the axes that were sent, the same shape
    /// <c>svc_BspDecal</c> uses. An absent axis is zero, which the engine relies on: it treats an
    /// all-zero origin as "no direction" and points the indicator at the camera instead.
    ///
    /// **Protocol 14 and below send a different message, not a variant of this one**: one byte of
    /// damage and the vector, with no damage-type long and no bit saying whether a position
    /// follows. Established by arithmetic on the corpus rather than by trying layouts until one
    /// fitted — the March 2008 demo's bodies are 77 and 72 bits, a `BitVec3Coord` is 69 or 64,
    /// and the difference is eight. The five-bit gap between those two lengths is the same one
    /// between the modern 118 and 113: an axis sent without its fraction. The leading byte then
    /// reads 36, 40, 50 and 44 across the demo, which are damage values.
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? Damage(
        ReadOnlySpan<byte> body, int bodyBits, int networkProtocol)
    {
        try
        {
            BitReader reader = new(body);
            List<KeyValuePair<string, object?>> fields;

            if (networkProtocol > ByteDamageProtocol)
            {
                if (bodyBits < ModernHeaderBits)
                {
                    return null;
                }

                int damage = (int)reader.ReadUInt32(16);
                uint ignored = reader.ReadUInt32(32);
                fields = [new("damage", damage), new("bits", (int)ignored)];

                // A clear bit ends the message - the game returns there, so the body stops too.
                if (reader.ReadBit())
                {
                    ReadOrigin(ref reader, fields);
                }
            }
            else
            {
                if (bodyBits < OldHeaderBits)
                {
                    return null;
                }

                // No "bits" field: that era does not send one, and reporting a zero would say the
                // damage carried no type flags rather than that the era never stated any.
                fields = [new("damage", (int)reader.ReadUInt32(8))];
                ReadOrigin(ref reader, fields);
            }

            // Exactly, not merely within. The stated length is in bits and these bodies end
            // mid-byte, so a layout that stops short has read a prefix of the body rather than
            // fitted it - which is precisely how the modern layout passed for a protocol-14
            // demo and reported five-figure damage for a game whose maximum hit is about 450.
            return reader.BitsRead == bodyBits ? fields : null;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }

    /// <summary>Reads a <c>BitVec3Coord</c>: three presence bits, then the axes that were sent.</summary>
    private static void ReadOrigin(
        ref BitReader reader, List<KeyValuePair<string, object?>> fields)
    {
        // All three flags first, then the values - reading each axis as its flag is met would
        // still be correct here, but the engine's own order is what a later encoder has to match.
        bool hasX = reader.ReadBit();
        bool hasY = reader.ReadBit();
        bool hasZ = reader.ReadBit();

        if (hasX)
        {
            fields.Add(new("x", SendPropDecoder.ReadFloat(ref reader, Coordinate)));
        }

        if (hasY)
        {
            fields.Add(new("y", SendPropDecoder.ReadFloat(ref reader, Coordinate)));
        }

        if (hasZ)
        {
            fields.Add(new("z", SendPropDecoder.ReadFloat(ref reader, Coordinate)));
        }
    }

    /// <summary>Last protocol whose damage message was a byte and a vector.</summary>
    /// <remarks>
    /// Measured at 11, 14 and 15. The March 2008 demo (protocol 14) carries 24 of these and none
    /// fits the modern layout; the June 2009 demo (protocol 15) carries 16 and all of them do; a
    /// protocol-11 recording made specifically to produce them carries 43, all on this layout at
    /// the same 77 and 72 bits. Protocols 12 and 13 have no specimen, so the rule is interpolated
    /// across those two and nowhere else.
    /// </remarks>
    private const int ByteDamageProtocol = 14;

    /// <summary>Short, long and the flag: what the modern layout needs before anything optional.</summary>
    private const int ModernHeaderBits = 49;

    /// <summary>A byte and the vector's three presence bits.</summary>
    private const int OldHeaderBits = 11;

    /// <summary>A plain <c>SPROP_COORD</c>, which is what a damage origin is sent as.</summary>
    private static SendProperty Coordinate { get; } =
        new(SendPropType.Float, "damage_origin", 1 << 1, string.Empty, 0f, 0f, 0, 0);

    /// <summary>
    /// <c>TextMsg</c> — a destination, a localisation key, and up to four substitutions.
    /// </summary>
    /// <remarks>
    /// The string count is not stated; the body simply ends. Reading until the bits run out is
    /// therefore the format, not a shortcut, and it is why the exact-consumption check matters
    /// more here than anywhere else: an over-read runs into whatever follows and still produces
    /// strings.
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? TextMsg(
        ReadOnlySpan<byte> body, int bodyBits)
    {
        if (!IsWholeBytes(bodyBits, body) || body.Length < 1)
        {
            return null;
        }

        List<KeyValuePair<string, object?>> fields =
            [new("destination", (int)body[0])];

        int offset = 1;
        int index = 0;
        while (offset < ByteLength(bodyBits))
        {
            if (!ReadString(body, ByteLength(bodyBits), ref offset, out string value))
            {
                return null;
            }

            // The four substitution slots are always sent and are usually all empty, so listing
            // them unconditionally put `param1="" param2="" param3="" param4=""` on the end of
            // every announcement in the trace. The key text is kept even when empty, because an
            // empty message is a fact about the message; an unused slot is not.
            if (index == 0)
            {
                fields.Add(new("text", value));
            }
            else if (value.Length > 0)
            {
                fields.Add(new(Substitution(index), value));
            }

            index++;
        }

        return offset == ByteLength(bodyBits) ? fields : null;
    }

    /// <summary><c>SayText</c> — a client slot, the line, and whether it counts as chat.</summary>
    private static List<KeyValuePair<string, object?>>? SayText(
        ReadOnlySpan<byte> body, int bodyBits)
    {
        if (!IsWholeBytes(bodyBits, body) || body.Length < 3)
        {
            return null;
        }

        int end = ByteLength(bodyBits);
        int offset = 1;
        if (!ReadString(body, end, ref offset, out string text) || offset + 1 != end)
        {
            return null;
        }

        return
        [
            new("client", (int)body[0]),
            new("text", text),
            new("chat", body[offset] != 0),
        ];
    }

    /// <summary><c>VoiceSubtitle</c> — which player, and which line of which menu.</summary>
    private static List<KeyValuePair<string, object?>>? VoiceSubtitle(
        ReadOnlySpan<byte> body, int bodyBits)
    {
        if (bodyBits != 24)
        {
            return null;
        }

        return
        [
            new("client", (int)body[0]),
            new("menu", (int)body[1]),
            new("item", (int)body[2]),
        ];
    }

    /// <summary><c>Fade</c> — a full-screen colour wash, used for spawns and stuns.</summary>
    /// <remarks>
    /// <c>__MsgFunc_Fade</c> in <c>game/client/view_effects.cpp</c>: three shorts and four bytes,
    /// which is 80 bits — and every Fade in the corpus is exactly 80 bits. The width was a
    /// prediction from the source before a single body was read.
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? Fade(
        ReadOnlySpan<byte> body, int bodyBits)
    {
        if (bodyBits != FadeBits)
        {
            return null;
        }

        return
        [
            new("duration", (int)BinaryPrimitives.ReadUInt16LittleEndian(body)),
            new("holdtime", (int)BinaryPrimitives.ReadUInt16LittleEndian(body[2..])),
            new("flags", (int)BinaryPrimitives.ReadUInt16LittleEndian(body[4..])),
            new("r", (int)body[6]),
            new("g", (int)body[7]),
            new("b", (int)body[8]),
            new("a", (int)body[9]),
        ];
    }

    /// <summary><c>Shake</c> — screen shake, which is what an explosion looks like from inside.</summary>
    /// <remarks>
    /// <c>__MsgFunc_Shake</c> in <c>game/client/view_effects.cpp</c>: a byte and three floats,
    /// 104 bits. Every Shake in the corpus is 104 bits.
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? Shake(
        ReadOnlySpan<byte> body, int bodyBits)
    {
        if (bodyBits != ShakeBits)
        {
            return null;
        }

        return
        [
            new("command", (int)body[0]),
            new("amplitude", BinaryPrimitives.ReadSingleLittleEndian(body[1..])),
            new("frequency", BinaryPrimitives.ReadSingleLittleEndian(body[5..])),
            new("duration", BinaryPrimitives.ReadSingleLittleEndian(body[9..])),
        ];
    }

    /// <summary><c>Rumble</c> — controller vibration. Three bytes, and no use to a replay.</summary>
    /// <remarks>
    /// Decoded anyway because it is cheap and because "opaque" and "carries nothing anyone wants"
    /// are different statements. This project can now say the second about Rumble; before, it
    /// could only say the first. <c>__MsgFunc_Rumble</c> in <c>clientmode_shared.cpp</c>.
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? Rumble(
        ReadOnlySpan<byte> body, int bodyBits) =>
        bodyBits != RumbleBits
            ? null
            : [new("waveform", (int)body[0]), new("data", (int)body[1]), new("flags", (int)body[2])];

    /// <summary><c>VGUIMenu</c> — show or hide a panel, with optional key/value data.</summary>
    /// <remarks>
    /// <c>__MsgFunc_VGUIMenu</c> in <c>clientmode_shared.cpp</c>: a panel name, a show flag, a
    /// count, and that many name/value string pairs. The MOTD arrives this way, which is why the
    /// corpus's bodies are mostly `"info" show=0` and a handful of `"MOTD"`.
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? VguiMenu(
        ReadOnlySpan<byte> body, int bodyBits)
    {
        if (!IsWholeBytes(bodyBits, body))
        {
            return null;
        }

        int end = ByteLength(bodyBits);
        int offset = 0;
        if (!ReadString(body, end, ref offset, out string panel) || offset + 2 > end)
        {
            return null;
        }

        List<KeyValuePair<string, object?>> fields =
            [new("panel", panel), new("show", body[offset] != 0)];

        int count = body[offset + 1];
        offset += 2;

        for (int pair = 0; pair < count; pair++)
        {
            if (!ReadString(body, end, ref offset, out string key) ||
                !ReadString(body, end, ref offset, out string value))
            {
                return null;
            }

            fields.Add(new(key, value));
        }

        return offset == end ? fields : null;
    }

    /// <summary><c>PlayerStatsUpdate</c> — a round's scoreboard numbers for one class.</summary>
    /// <remarks>
    /// <c>CTFStatPanel::MsgFunc_PlayerStatsUpdate</c> in <c>tf_hud_statpanel.cpp</c>: a class
    /// byte, an alive byte, a 32-bit field saying which stats follow, and one 32-bit value per set
    /// bit. So the body is 48 bits plus 32 per stat, and every width the corpus contains — 112,
    /// 144, 176, 208, 240, 272 — is exactly that.
    ///
    /// **Valve's own reader enforces exact consumption here**, and says why: it checks
    /// <c>0 == msg.GetNumBytesLeft()</c> and bails "rather than risk polluting player stats with
    /// garbage". That is the same rule this file applies to every layout, arrived at
    /// independently and for the same reason.
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? PlayerStatsUpdate(
        ReadOnlySpan<byte> body, int bodyBits)
    {
        if (!IsWholeBytes(bodyBits, body) || ByteLength(bodyBits) < 6)
        {
            return null;
        }

        List<KeyValuePair<string, object?>> fields =
        [
            new("class", ClassName(body[0])),
            new("alive", body[1] != 0),
        ];

        return Stats(body, ByteLength(bodyBits), 2, StatNames, fields);
    }

    /// <summary><c>MapStatsUpdate</c> — the same shape, keyed by map rather than by class.</summary>
    /// <remarks>
    /// <c>CTFStatPanel::MsgFunc_MapStatsUpdate</c>: a 32-bit map id and the same set-bit field.
    /// Only one map stat has ever existed, so every body in the corpus is 96 bits — the 64-bit
    /// header plus one value.
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? MapStatsUpdate(
        ReadOnlySpan<byte> body, int bodyBits)
    {
        if (!IsWholeBytes(bodyBits, body) || ByteLength(bodyBits) < 8)
        {
            return null;
        }

        List<KeyValuePair<string, object?>> fields =
            [new("map", (int)BinaryPrimitives.ReadUInt32LittleEndian(body))];

        return Stats(body, ByteLength(bodyBits), 4, MapStatNames, fields);
    }

    /// <summary>Reads the set-bit field and one 32-bit value per stat it names.</summary>
    /// <remarks>
    /// **The loop stops at the end of the name table as well as when the bits run out**, because
    /// that is what the game does: <c>while ( iSendBits > 0 &amp;&amp; iStat &lt;= TFSTAT_LAST )</c>.
    ///
    /// **In this build that guard is unreachable, and noticing why is the useful part.** The
    /// field is 32 bits and <c>TFStatType_t</c> runs to 44, so bit 31 selects stat 32 and stats 33
    /// through 44 cannot be sent through this message at all. The guard only bites when the table
    /// is SHORTER than 32 entries — which is what an older era looks like, and is why the era
    /// caveat on <see cref="StatNames"/> is about labels rather than about widths.
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? Stats(
        ReadOnlySpan<byte> body, int end, int offset, string[] names,
        List<KeyValuePair<string, object?>> fields)
    {
        uint sent = BinaryPrimitives.ReadUInt32LittleEndian(body[offset..]);
        offset += 4;

        for (int stat = 1; stat < names.Length && sent > 0; stat++, sent >>= 1)
        {
            if ((sent & 1) == 0)
            {
                continue;
            }

            if (offset + 4 > end)
            {
                return null;
            }

            fields.Add(new(names[stat], BinaryPrimitives.ReadInt32LittleEndian(body[offset..])));
            offset += 4;
        }

        return offset == end ? fields : null;
    }

    /// <summary>The class a stats update belongs to.</summary>
    private static string ClassName(byte value) =>
        value < Classes.Length ? Classes[value] : value.ToString(CultureInfo.InvariantCulture);

    // Stryker disable String: transcribed from tf_shareddefs.h and tf_gamestats_shared.h. A
    // per-name mutant can only be killed by asserting that name back - change detectors that
    // break on every SDK update and catch nothing. What can go wrong is ALIGNMENT, which the
    // width arithmetic and the exact-consumption check cover.
    private static readonly string[] Classes =
        ["none", "scout", "sniper", "soldier", "demoman", "medic", "heavy", "pyro", "spy", "engineer"];

    /// <summary>
    /// <c>TFStatType_t</c>, in order. Index 0 is <c>TFSTAT_UNDEFINED</c> and is never sent.
    /// </summary>
    /// <remarks>
    /// **Era caveat, and it is real here in a way it was not for the message ids.** Stats were
    /// appended over the game's life, so this 2013-SDK list is longer than what a 2008 build sent.
    /// Appending is the safe direction — a low index means the same stat in every era — but a stat
    /// inserted rather than appended would rename everything after it, and there is no old SDK to
    /// diff against. The VALUES do not depend on this table at all, only on the set-bit count, so
    /// a wrong name here cannot cause a misread; it can only mislabel.
    /// </remarks>
    private static readonly string[] StatNames =
    [
        "undefined", "shots_hit", "shots_fired", "kills", "deaths", "damage", "captures",
        "defenses", "dominations", "revenge", "points_scored", "buildings_destroyed", "headshots",
        "playtime", "healing", "invulns", "kill_assists", "backstabs", "health_leached",
        "buildings_built", "max_sentry_kills", "teleports", "fire_damage", "bonus_points",
        "blast_damage", "damage_taken", "health_kits", "ammo_kits", "class_changes", "crits",
        "suicides", "currency_collected", "damage_assist", "healing_assist", "damage_boss",
        "damage_blocked", "damage_ranged", "damage_ranged_crit_random",
        "damage_ranged_crit_boosted", "revived", "throwable_hit", "throwable_kill",
        "killstreak_max", "kills_runecarrier", "flag_returns",
    ];

    /// <summary><c>TFMapStatType_t</c>. Only one map stat has ever existed.</summary>
    private static readonly string[] MapStatNames = ["undefined", "playtime"];

    // Stryker restore String

    /// <summary>Three shorts and four colour bytes.</summary>
    private const int FadeBits = 80;

    /// <summary>A command byte and three floats.</summary>
    private const int ShakeBits = 104;

    /// <summary>Waveform, data, flags.</summary>
    private const int RumbleBits = 24;

    private static List<KeyValuePair<string, object?>>? SingleString(
        ReadOnlySpan<byte> body, int bodyBits, string field)
    {
        if (!IsWholeBytes(bodyBits, body))
        {
            return null;
        }

        int end = ByteLength(bodyBits);
        int offset = 0;
        if (!ReadString(body, end, ref offset, out string value) || offset != end)
        {
            return null;
        }

        return [new(field, value)];
    }

    private static List<KeyValuePair<string, object?>>? SingleByte(
        ReadOnlySpan<byte> body, int bodyBits, string field) =>
        bodyBits == 8 ? [new(field, (int)body[0])] : null;

    /// <summary>Reads a NUL-terminated string, refusing one that never terminates.</summary>
    private static bool ReadString(
        ReadOnlySpan<byte> body, int end, ref int offset, out string value)
    {
        int terminator = body[offset..end].IndexOf((byte)0);
        if (terminator < 0)
        {
            value = string.Empty;
            return false;
        }

        // UTF-8, like every other string in this parser. TF2 names carry Cyrillic and CJK
        // routinely, and ASCII turns each of those bytes into a question mark.
        value = Encoding.UTF8.GetString(body.Slice(offset, terminator));
        offset += terminator + 1;
        return true;
    }

    /// <summary>
    /// Whether the stated length is a whole number of bytes the body actually contains.
    /// </summary>
    /// <remarks>
    /// The length check is not paranoia about malformed files. A stated length longer than the
    /// bytes present is exactly what a wrong layout looks like from inside a decoder, and without
    /// this the slice throws instead of declining — turning "this format does not fit" into an
    /// exception that would abandon the packet.
    /// </remarks>
    private static bool IsWholeBytes(int bodyBits, ReadOnlySpan<byte> body) =>
        bodyBits > 0 && bodyBits % 8 == 0 && ByteLength(bodyBits) <= body.Length;

    private static int ByteLength(int bodyBits) => bodyBits / 8;

    private static string Substitution(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"param{index}");
}
