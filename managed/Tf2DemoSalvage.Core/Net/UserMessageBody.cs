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
        int networkProtocol) =>
        Decode(userMessageType, name, body, bodyBits, networkProtocol, alternate: null);

    /// <summary>Builds a <see cref="UserMessage"/>, falling back to a second candidate name.</summary>
    /// <param name="userMessageType">The game-defined message id.</param>
    /// <param name="name">The registered name for the era assumed by default.</param>
    /// <param name="body">The body bytes, as copied from the stream.</param>
    /// <param name="bodyBits">The body's stated length in bits.</param>
    /// <param name="networkProtocol">The demo header's network protocol.</param>
    /// <param name="alternate">What another era's table calls this id, or <c>null</c>.</param>
    /// <remarks>
    /// **Protocol 24 is not one era, and an id above 50 has two candidate meanings** —
    /// `RDTeamPointsChanged` was inserted at id 51 some time after March 2013, shifting everything
    /// above it. The header carries no build number, so nothing outside the message itself can say
    /// which table applies (`RISKS.md` B29).
    ///
    /// **The body decides, and only when it is decisive.** The default name is tried first and
    /// stands whenever its layout accepts, so nothing changes for the modern demos that are the
    /// overwhelming majority. The alternate is reached *only* when the primary layout refuses —
    /// which is already this project's evidence that a name is wrong — and it is accepted only
    /// when it fits in turn. If both refuse, neither is claimed and the id is reported bare.
    ///
    /// Measured case: the March 2013 demo's three 32-bit messages at id 69. The modern table calls
    /// that `PlayerLoadoutUpdated`, a single `WRITE_BYTE`, so it refuses; the March 2013 client
    /// registers `HapSetDrag` there, one float of haptic drag, which fits.
    /// </remarks>
    public static UserMessage Decode(
        int userMessageType, string? name, ReadOnlySpan<byte> body, int bodyBits,
        int networkProtocol, string? alternate)
    {
        (bool Known, List<KeyValuePair<string, object?>>? Fields) decoded =
            Layout(name, body, bodyBits, networkProtocol);

        // Only a refusal opens the door. A primary that accepted, or that this project has no
        // layout for, is left alone - trying an alternate there could only replace an answer
        // nothing contradicts.
        if (alternate is not null && decoded is { Known: true, Fields: null })
        {
            (bool Known, List<KeyValuePair<string, object?>>? Fields) second =
                Layout(alternate, body, bodyBits, networkProtocol);
            if (second is not { Known: true, Fields: null })
            {
                return new UserMessage(userMessageType, alternate, bodyBits, second.Fields);
            }
        }

        // A name is a claim, and a layout that refuses is evidence against it. Withholding the
        // name reports the id by number, which is what the older-era gate does for the same reason.
        string? supported = decoded is { Known: true, Fields: null } ? null : name;
        return new UserMessage(userMessageType, supported, bodyBits, decoded.Fields);
    }

    /// <summary>Runs one candidate name's layout, if this project has one.</summary>
    private static (bool Known, List<KeyValuePair<string, object?>>? Fields) Layout(
        string? name, ReadOnlySpan<byte> body, int bodyBits, int networkProtocol)
    {
        return name switch
        {
            "TextMsg" => (true, TextMsg(body, bodyBits)),
            "SayText" => (true, SayText(body, bodyBits)),
            "ItemPickup" => (true, SingleString(body, bodyBits, "item")),
            "Geiger" => (true, SingleByte(body, bodyBits, "range")),
            "Train" => (true, SingleByte(body, bodyBits, "state")),
            "VoiceSubtitle" => (true, VoiceSubtitle(body, bodyBits)),
            "Damage" => (true, Damage(body, bodyBits, networkProtocol)),
            "Fade" => (true, Fade(body, bodyBits)),
            "Shake" => (true, Shake(body, bodyBits)),
            "Rumble" => (true, Rumble(body, bodyBits)),
            "ResetHUD" => (true, SingleByte(body, bodyBits, "unused")),
            "VGUIMenu" => (true, VguiMenu(body, bodyBits)),
            "PlayerStatsUpdate" => (true, PlayerStatsUpdate(body, bodyBits)),
            "MapStatsUpdate" => (true, MapStatsUpdate(body, bodyBits)),
            "BreakModel" => (true, BreakModel(body, bodyBits, skin: true)),
            "BreakModel_Pumpkin" => (true, BreakModel(body, bodyBits, skin: true)),
            "BreakModelRocketDud" => (true, BreakModel(body, bodyBits, skin: false)),
            "CheapBreakModel" => (true, CheapBreakModel(body, bodyBits)),
            "SpawnFlyingBird" => (true, SpawnFlyingBird(body, bodyBits)),
            "PlayerTauntSoundLoopStart" => (true, EntityAndString(body, bodyBits, "sound")),
            "PlayerShieldBlocked" => (true, TwoEntities(body, bodyBits, "attacker", "victim")),
            "PlayerTauntSoundLoopEnd" => (true, SingleByte(body, bodyBits, "entity")),
            "PlayerGodRayEffect" => (true, SingleByte(body, bodyBits, "entity")),
            "PlayerTeleportHomeEffect" => (true, SingleByte(body, bodyBits, "entity")),
            "PlayerLoadoutUpdated" => (true, SingleByte(body, bodyBits, "entity")),
            "MVMResetPlayerStats" => (true, SingleByte(body, bodyBits, "entity")),
            "AchievementEvent" => (true, AchievementEvent(body, bodyBits)),
            "CloseCaption" => (true, CloseCaption(body, bodyBits)),
            "VoteStart" => (true, VoteStart(body, bodyBits)),
            "VotePass" => (true, VotePass(body, bodyBits)),
            "VoteFailed" => (true, VoteFailed(body, bodyBits)),
            "CallVoteFailed" => (true, CallVoteFailed(body, bodyBits)),
            "VoiceMask" => (true, VoiceMask(body, bodyBits, networkProtocol)),
            "PlayerIgnited" => (true, Ignited(body, bodyBits)),
            "PlayerIgnitedInv" => (true, Ignited(body, bodyBits)),
            "PlayerExtinguished" => (true, TwoEntities(body, bodyBits, "healer", "victim")),
            "PlayerJarated" => (true, TwoEntities(body, bodyBits, "thrower", "victim")),
            "PlayerJaratedFade" => (true, TwoEntities(body, bodyBits, "thrower", "victim")),

            // The two haptics messages Valve registers at a fixed size. There is nothing to read
            // — SPHapWeapEvent's four bytes are a weapon effect id this project does not
            // interpret, and HapMeleeContact carries no body at all — but the registered *width*
            // is a layout in its own right, and a decisive one: it is what lets a candidate be
            // falsified for a message whose contents are never decoded.
            "SPHapWeapEvent" => (true, FixedWidth(bodyBits, 32)),
            "HapMeleeContact" => (true, FixedWidth(bodyBits, 0)),

            _ => (false, null),
        };
    }

    /// <summary>Accepts a body of exactly this width, with nothing read out of it.</summary>
    /// <remarks>
    /// An empty field list rather than <c>null</c>, because those mean different things here:
    /// <c>null</c> is "this layout refuses the body" and empty is "the body is the right size and
    /// holds nothing worth naming". Only the first withholds the message's name.
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? FixedWidth(int bodyBits, int expected) =>
        bodyBits == expected ? [] : null;

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

    /// <summary>
    /// <c>BreakModel</c> and friends — a model shattering somewhere, with an orientation.
    /// </summary>
    /// <remarks>
    /// Written by anything that breaks: `tf_item_wearable.cpp` on a cosmetic, `tf_pumpkin_bomb`,
    /// `tf_weaponbase_rocket` for a dud. The shape is a model index, a position, an orientation,
    /// and for the full form a skin:
    ///
    /// <code>
    /// WRITE_SHORT( GetModelIndex() );
    /// WRITE_VEC3COORD( GetAbsOrigin() );
    /// WRITE_ANGLES( GetAbsAngles() );
    /// WRITE_SHORT( GetSkin() );          // BreakModel only, not the rocket dud
    /// </code>
    ///
    /// **`WRITE_ANGLES` is `WRITE_VEC3COORD`.** `bf_write::WriteBitAngles` copies the angle triple
    /// into a `Vector` and calls `WriteBitVec3Coord` — and carries a standing fix-me comment from
    /// Valve saying as much. So an orientation is encoded as a position, three presence bits and
    /// coordinate axes, and an angle costs exactly what a coordinate costs. Worth knowing before
    /// deriving any width that involves angles: there is no separate angle encoding to look for.
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? BreakModel(
        ReadOnlySpan<byte> body, int bodyBits, bool skin)
    {
        try
        {
            BitReader reader = new(body);
            List<KeyValuePair<string, object?>> fields =
                [new("model", (int)reader.ReadUInt32(16))];

            ReadVector(ref reader, fields, string.Empty);
            ReadVector(ref reader, fields, "ang_");

            if (skin)
            {
                fields.Add(new("skin", (int)reader.ReadUInt32(16)));
            }

            return reader.BitsRead == bodyBits ? fields : null;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }

    /// <summary><c>CheapBreakModel</c> — the same without an orientation or a skin.</summary>
    /// <remarks>
    /// A model index and a position, so a full body is 16 + 3 + 66 = **85 bits**, and that width
    /// is distinctive enough to act as a fingerprint for the message's own id. It is what revealed
    /// that the id table shifts between eras — see <see cref="UserMessageNames"/>.
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? CheapBreakModel(
        ReadOnlySpan<byte> body, int bodyBits)
    {
        try
        {
            BitReader reader = new(body);
            List<KeyValuePair<string, object?>> fields =
                [new("model", (int)reader.ReadUInt32(16))];

            ReadVector(ref reader, fields, string.Empty);

            return reader.BitsRead == bodyBits ? fields : null;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }

    /// <summary><c>SpawnFlyingBird</c> — a position and five floats of flight parameters.</summary>
    /// <remarks>
    /// `entity_bird.cpp`. The five floats are 160 bits, so a body with a full position is 229 —
    /// which is exactly what every protocol-24 demo carrying one contains.
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? SpawnFlyingBird(
        ReadOnlySpan<byte> body, int bodyBits)
    {
        try
        {
            BitReader reader = new(body);
            List<KeyValuePair<string, object?>> fields = [];
            ReadVector(ref reader, fields, string.Empty);

            foreach (string name in BirdFields)
            {
                fields.Add(new(name, BitConverter.Int32BitsToSingle((int)reader.ReadUInt32(32))));
            }

            return reader.BitsRead == bodyBits ? fields : null;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }

    private static readonly string[] BirdFields =
        ["fly_angle", "fly_angle_rate", "accel_z", "speed", "glide_time"];

    /// <summary><c>AchievementEvent</c> — which achievement progressed, and how far.</summary>
    /// <remarks>
    /// **A message that grew, at a fixed id.** The modern writer sends two shorts —
    /// `WRITE_SHORT( iAchievement ); WRITE_SHORT( iCount );` in `basemultiplayerplayer.cpp` — for
    /// 32 bits. The 2009 demo's is **16 bits**: the achievement only. The count was added later.
    ///
    /// This is a third distinct kind of era change, alongside the two already known. `Damage`
    /// changed *layout* at a fixed id; the table around `CheapBreakModel` changed *ids* at a fixed
    /// layout; this changes *length* at a fixed id and a compatible prefix.
    ///
    /// Both widths are accepted, and that is not a guess dressed as a fallback: 16 and 32 are
    /// exact, they are the only two forms the writer has ever had, and the achievement id occupies
    /// the same leading short in both. Anything else is refused. Keying it on protocol instead
    /// would need a boundary, and the corpus gives none — the only evidence is 16 bits at protocol
    /// 15 and 32 at 24, with nothing in between.
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? AchievementEvent(
        ReadOnlySpan<byte> body, int bodyBits) => bodyBits switch
        {
            32 =>
            [
                new("achievement", (int)BinaryPrimitives.ReadUInt16LittleEndian(body)),
                new("count", (int)BinaryPrimitives.ReadUInt16LittleEndian(body[2..])),
            ],
            16 => [new("achievement", (int)BinaryPrimitives.ReadUInt16LittleEndian(body))],
            _ => null,
        };

    /// <summary><c>CloseCaption</c> — subtitle token, how long to show it, and who said it.</summary>
    /// <remarks>
    /// <c>CHudCloseCaption::MsgFunc_CloseCaption</c> in <c>hud_closecaption.cpp</c>: a token
    /// string, a short of duration in tenths of a second, and a flag byte.
    ///
    /// **By count this is the largest message in the game's traffic** — 616 of them in a
    /// seven-minute pub round, more than every other user message in that demo combined. Every
    /// voice line, every announcer call. It was invisible until a real multiplayer demo arrived:
    /// the committed corpus, all listen-server recordings with one or two players, contains
    /// almost none.
    ///
    /// The duration is stored in tenths, so it is reported in seconds rather than as the raw
    /// short — a value of 25 means 2.5 seconds and reporting 25 would invite reading it as ticks.
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? CloseCaption(
        ReadOnlySpan<byte> body, int bodyBits)
    {
        if (!IsWholeBytes(bodyBits, body))
        {
            return null;
        }

        int end = ByteLength(bodyBits);
        int offset = 0;
        if (!ReadString(body, end, ref offset, out string token) || offset + 3 != end)
        {
            return null;
        }

        int flags = body[offset + 2];

        return
        [
            new("token", token),
            new("seconds", BinaryPrimitives.ReadUInt16LittleEndian(body[offset..]) / 10f),
            new("warn_if_missing", (flags & 1) != 0),
            new("from_player", (flags & 2) != 0),
            new("male", (flags & 4) != 0),
            new("female", (flags & 8) != 0),
        ];
    }

    /// <summary><c>VoteStart</c> — a vote was called: by whom, about what, and on whom.</summary>
    /// <remarks>
    /// <c>CVoteController::CreateVote</c> in <c>vote_controller.cpp</c>:
    ///
    /// <code>
    /// WRITE_BYTE( m_iOnlyTeamToVote );
    /// WRITE_LONG( m_nVoteIdx );
    /// WRITE_BYTE( m_iEntityHoldingVote );
    /// WRITE_STRING( pCurrentIssue->GetDisplayString() );
    /// WRITE_STRING( pCurrentIssue->GetDetailsString() );
    /// WRITE_BOOL( pCurrentIssue->IsYesNoVote() );
    /// WRITE_BYTE( target ? target->entindex() : 0 );
    /// </code>
    ///
    /// **The <c>WRITE_BOOL</c> is one bit, not a byte, and it lands between two byte fields.** So
    /// the message is byte-aligned up to the strings and bit-aligned afterwards, and its total is
    /// not a multiple of eight — the corpus's only instance is 329 bits. A decoder that treated
    /// the flag as a byte would be seven bits out for the entity index that follows, and would
    /// read a plausible player number rather than failing.
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? VoteStart(
        ReadOnlySpan<byte> body, int bodyBits)
    {
        // Team, index, caller, then two strings, then a bit and a byte.
        if (bodyBits < VoteStartFixedBits || body.Length < 7)
        {
            return null;
        }

        int end = body.Length;
        int offset = VoteHeaderBytes + 1;
        if (!ReadString(body, end, ref offset, out string display) ||
            !ReadString(body, end, ref offset, out string details))
        {
            return null;
        }

        // The trailing nine bits start on a byte boundary, so a reader over the remainder lands
        // in the right place without seeking.
        if (bodyBits != (offset * 8) + 9 || offset >= end)
        {
            return null;
        }

        BitReader reader = new(body[offset..]);
        bool yesNo = reader.ReadBit();

        return
        [
            new("team", (int)body[0]),
            new("vote", (int)BinaryPrimitives.ReadUInt32LittleEndian(body[1..])),
            new("caller", (int)body[5]),
            new("issue", display),
            new("details", details),
            new("yes_no", yesNo),
            new("target", (int)reader.ReadUInt32(8)),
        ];
    }

    /// <summary><c>VotePass</c> — the vote carried, and what it said.</summary>
    private static List<KeyValuePair<string, object?>>? VotePass(
        ReadOnlySpan<byte> body, int bodyBits)
    {
        if (!IsWholeBytes(bodyBits, body) || ByteLength(bodyBits) < VoteHeaderBytes + 2)
        {
            return null;
        }

        int end = ByteLength(bodyBits);
        int offset = VoteHeaderBytes;
        if (!ReadString(body, end, ref offset, out string passed) ||
            !ReadString(body, end, ref offset, out string details) ||
            offset != end)
        {
            return null;
        }

        return
        [
            new("team", (int)body[0]),
            new("vote", (int)BinaryPrimitives.ReadUInt32LittleEndian(body[1..])),
            new("passed", passed),
            new("details", details),
        ];
    }

    /// <summary><c>VoteFailed</c> — the vote did not carry, and why.</summary>
    /// <remarks>
    /// Byte, long, byte — 48 bits, which is exactly what the corpus's instances measure and what
    /// <c>tf_usermessages.cpp</c> registers the message at (6 bytes).
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? VoteFailed(
        ReadOnlySpan<byte> body, int bodyBits) =>
        bodyBits != (VoteHeaderBytes + 1) * 8
            ? null
            :
            [
                new("team", (int)body[0]),
                new("vote", (int)BinaryPrimitives.ReadUInt32LittleEndian(body[1..])),
                new("reason", (int)body[5]),
            ];

    /// <summary><c>CallVoteFailed</c> — this player may not call a vote yet.</summary>
    private static List<KeyValuePair<string, object?>>? CallVoteFailed(
        ReadOnlySpan<byte> body, int bodyBits) =>
        bodyBits != 24
            ? null
            :
            [
                new("reason", (int)body[0]),
                new("seconds", (int)BinaryPrimitives.ReadUInt16LittleEndian(body[1..])),
            ];

    /// <summary>The team byte and the vote index every vote message opens with.</summary>
    private const int VoteHeaderBytes = 5;

    /// <summary>Team, index, caller, the flag bit and the target byte — the strings aside.</summary>
    private const int VoteStartFixedBits = ((VoteHeaderBytes + 1) * 8) + 9;

    /// <summary><c>VoiceMask</c> — who this client may hear, and who they have muted.</summary>
    /// <remarks>
    /// <c>voice_gamemgr.cpp</c> writes the two masks **interleaved**, a dword of each at a time,
    /// then a byte:
    ///
    /// <code>
    /// for ( dw = 0; dw &lt; VOICE_MAX_PLAYERS_DW; dw++ )
    /// {
    ///     WRITE_LONG( gameRulesMask.GetDWord(dw) );
    ///     WRITE_LONG( g_BanMasks[iClient].GetDWord(dw) );
    /// }
    /// WRITE_BYTE( !!g_PlayerModEnable[iClient] );
    /// </code>
    ///
    /// **The registered size predicts the width exactly, through two levels of macro.**
    /// `VOICE_MAX_PLAYERS_DW*4 * 2 + 1` where `VOICE_MAX_PLAYERS` is `MAX_PLAYERS` = 101, so the
    /// dword count is 4 and the body is 33 bytes — and every VoiceMask in the corpus is 264 bits.
    /// Interleaved rather than two contiguous arrays is the kind of detail that produces a
    /// plausible-looking wrong answer if guessed, since both orderings consume the same bits.
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? VoiceMask(
        ReadOnlySpan<byte> body, int bodyBits, int networkProtocol)
    {
        int dwords = VoiceMaskDwordsFor(networkProtocol);
        if (bodyBits != VoiceMaskBitsFor(dwords))
        {
            return null;
        }

        List<KeyValuePair<string, object?>> fields = [];
        for (int word = 0; word < dwords; word++)
        {
            int at = word * 8;
            fields.Add(new(
                Numbered("can_hear", word),
                (int)BinaryPrimitives.ReadUInt32LittleEndian(body[at..])));
            fields.Add(new(
                Numbered("muted", word),
                (int)BinaryPrimitives.ReadUInt32LittleEndian(body[(at + 4)..])));
        }

        fields.Add(new("mod_enable", body[dwords * 8] != 0));
        return fields;
    }

    /// <summary><c>PlayerIgnited</c> — who set whom on fire, and with what.</summary>
    private static List<KeyValuePair<string, object?>>? Ignited(
        ReadOnlySpan<byte> body, int bodyBits) =>
        bodyBits != 24
            ? null
            :
            [
                new("igniter", (int)body[0]),
                new("victim", (int)body[1]),
                new("weapon", (int)body[2]),
            ];

    /// <summary>Two entity indices, which several TF2 messages are exactly.</summary>
    private static List<KeyValuePair<string, object?>>? TwoEntities(
        ReadOnlySpan<byte> body, int bodyBits, string first, string second) =>
        bodyBits != 16 ? null : [new(first, (int)body[0]), new(second, (int)body[1])];

    private static string Numbered(string name, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{name}{index}");

    /// <summary>How many dword pairs the mask carries, which is an era question.</summary>
    /// <remarks>
    /// **`VOICE_MAX_PLAYERS_DW` grew twice, and the registered size records it.** Read from
    /// `usermessages->Register` in the shipped clients on 2026-08-11: `VoiceMask` is **9 bytes**
    /// in the 2007 and 2008 builds, **17** in 2009, and **33** from 2011 on. Those invert through
    /// `dwords * 4 * 2 + 1` to one, two and four dword pairs — a `VOICE_MAX_PLAYERS` of 32, 64 and
    /// 128. The 2007 client and server DLLs agree on 9 independently, which is the control.
    ///
    /// This was a live gap until then: only the 33-byte form existed, so a launch-era `VoiceMask`
    /// was a quarter the expected width. It failed *safely* rather than silently, because the
    /// caller demands exact consumption — under an "at most" check it would have read sixteen
    /// dwords of adjacent bits and reported them as mute state. See `RISKS.md` B28.
    ///
    /// The 2011 boundary is where the measurement is, not where the change necessarily is: 2009
    /// and 2011 are the two nearest specimens, so anything in protocols 16 and above shares the
    /// modern width until a build between them says otherwise.
    /// </remarks>
    private static int VoiceMaskDwordsFor(int networkProtocol) => networkProtocol switch
    {
        <= LaunchVoiceMaskProtocol => 1,
        MidVoiceMaskProtocol => 2,
        _ => 4,
    };

    /// <summary>Two interleaved dword arrays plus the mod-enable byte.</summary>
    private static int VoiceMaskBitsFor(int dwords) => ((dwords * 4 * 2) + 1) * 8;

    /// <summary>Protocol 14 and below: the 2007 and 2008 clients register nine bytes.</summary>
    private const int LaunchVoiceMaskProtocol = 14;

    /// <summary>Protocol 15: the 2009 client registers seventeen.</summary>
    private const int MidVoiceMaskProtocol = 15;

    /// <summary>An entity index followed by a NUL-terminated string.</summary>
    private static List<KeyValuePair<string, object?>>? EntityAndString(
        ReadOnlySpan<byte> body, int bodyBits, string field)
    {
        if (!IsWholeBytes(bodyBits, body) || body.Length < 2)
        {
            return null;
        }

        int end = ByteLength(bodyBits);
        int offset = 1;
        if (!ReadString(body, end, ref offset, out string value) || offset != end)
        {
            return null;
        }

        return [new("entity", (int)body[0]), new(field, value)];
    }

    /// <summary>Reads a <c>BitVec3Coord</c>: three presence bits, then the axes that were sent.</summary>
    private static void ReadVector(
        ref BitReader reader, List<KeyValuePair<string, object?>> fields, string prefix)
    {
        bool hasX = reader.ReadBit();
        bool hasY = reader.ReadBit();
        bool hasZ = reader.ReadBit();

        if (hasX)
        {
            fields.Add(new(prefix + "x", SendPropDecoder.ReadFloat(ref reader, Coordinate)));
        }

        if (hasY)
        {
            fields.Add(new(prefix + "y", SendPropDecoder.ReadFloat(ref reader, Coordinate)));
        }

        if (hasZ)
        {
            fields.Add(new(prefix + "z", SendPropDecoder.ReadFloat(ref reader, Coordinate)));
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
