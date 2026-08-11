using System;
using System.Globalization;
using System.IO;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Container;

/// <summary>
/// One <c>dem_usercmd</c> payload: what the recording player's hands were doing on that tick.
/// </summary>
/// <param name="RawCommandNumber">Command number, or <c>null</c> when the writer omitted it.</param>
/// <param name="RawTickCount">Client tick, or <c>null</c> when the writer omitted it.</param>
/// <param name="Pitch">View angle, pitch.</param>
/// <param name="Yaw">View angle, yaw.</param>
/// <param name="Roll">View angle, roll.</param>
/// <param name="ForwardMove">Intended forward velocity.</param>
/// <param name="SideMove">Intended sideways velocity.</param>
/// <param name="UpMove">Intended vertical velocity.</param>
/// <param name="Buttons">The <c>IN_</c> bitfield; see <see cref="UserCommandButtons"/>.</param>
/// <param name="Impulse">Impulse command issued this tick.</param>
/// <param name="WeaponSelect">Entity index of a weapon switched to, or zero.</param>
/// <param name="WeaponSubtype">Weapon subtype, only meaningful alongside a selection.</param>
/// <param name="MouseDx">Raw horizontal mouse delta.</param>
/// <param name="MouseDy">Raw vertical mouse delta.</param>
/// <param name="Padding">
/// The bits between the last field and the byte boundary, which are <em>not</em> zero and are not
/// derivable from anything. See the remarks.
/// </param>
/// <remarks>
/// **This is the one thing in a demo that describes the player rather than the world.** Entity
/// snapshots say where someone ended up; this says what they pressed to get there — the actual
/// input trace, at command rate, for the client that recorded the file. It only exists in
/// point-of-view demos, because SourceTV has no player to record.
///
/// Layout read from Valve's published <c>game/shared/usercmd.cpp</c>, and the encoder rather than
/// the decoder, because <c>WriteUsercmd</c> states which condition clears each presence bit and
/// <c>ReadUsercmd</c> only implies it.
///
/// **Every field is delta-coded against a default-constructed <c>CUserCmd</c>, not against the
/// previous command.** <c>CInput::EncodeUserCmdToBuffer</c> puts <c>CUserCmd nullcmd;</c> on the
/// stack for every single call, so the baseline is a constant and each command is independently
/// decodable. A decoder that carried state between commands would work on well-formed files and
/// desynchronise on the first one with a gap.
///
/// That baseline produces the one genuinely surprising rule here. The writer's condition is
/// <c>to->command_number != from->command_number + 1</c>, and <c>from</c> is always zero, so an
/// **absent command number means one, not zero** — and the same for the tick count. Both are
/// exposed twice for that reason: <see cref="RawCommandNumber"/> is what the wire carried, and
/// <see cref="CommandNumber"/> is what the engine would use.
///
/// Two fields in <c>WriteUsercmd</c> are deliberately not here. <c>entitygroundcontact</c> is
/// wrapped in <c>#if defined( HL2_CLIENT_DLL )</c> on the write side and
/// <c>#if defined( HL2_DLL )</c> on the read side — **different macros for the two halves of one
/// wire format**, which would be a live desynchronisation bug in any configuration that defined
/// exactly one of them. TF2 defines neither, so its commands simply end after the mouse deltas.
/// And <c>random_seed</c> is never transmitted at all: <c>ReadUsercmd</c> derives it as
/// <c>MD5_PseudoRandom( command_number ) &amp; 0x7fffffff</c>, so it is a function of a field that
/// is already here rather than something the file carries.
///
/// **The trailing bits are stale bits of the previous command, not zero.** <c>bf_write</c>
/// composes each partial tail dword with a read-modify-write that preserves every bit outside its
/// mask, and <c>StartWriting</c> never clears the buffer it is handed, so bits a write does not
/// cover keep whatever was already there. Measured across the corpus: 385,236 commands, 99.8% of
/// them ending three bits short of a byte, those bits taking every value from 0 to 7 — and
/// 150,606 of the 199,929 non-zero ones are bit-for-bit what the *previous* command wrote at the
/// same absolute offsets.
///
/// That last number is the point. An earlier version of this comment called the padding
/// uninitialised process memory and described it as a leak, which was an assertion rather than a
/// measurement; nothing escapes the file that the file did not already contain. See
/// `docs/findings/01-container.md`.
///
/// It does mean a command **cannot be re-encoded from its values**, which is why
/// <see cref="Padding"/> is carried. A decoder that assumed zero here would rebuild a file that
/// differs from the original in nearly every user command.
/// </remarks>
public sealed record UserCommand(
    int? RawCommandNumber,
    int? RawTickCount,
    float Pitch,
    float Yaw,
    float Roll,
    float ForwardMove,
    float SideMove,
    float UpMove,
    uint Buttons,
    byte Impulse,
    int WeaponSelect,
    int WeaponSubtype,
    short MouseDx,
    short MouseDy,
    byte Padding)
{
    /// <summary><c>MAX_EDICT_BITS</c> from <c>public/const.h</c>.</summary>
    private const int WeaponSelectBits = 11;

    /// <summary><c>WEAPON_SUBTYPE_BITS</c>, defined at the top of <c>usercmd.cpp</c>.</summary>
    private const int WeaponSubtypeBits = 6;

    private const int Int32Bits = 32;
    private const int ImpulseBits = 8;
    private const int ShortBits = 16;
    private const int BitsPerByte = 8;

    /// <summary>
    /// The command number the engine would use: what the wire carried, or the steady-increment
    /// value one when it carried nothing.
    /// </summary>
    public int CommandNumber => RawCommandNumber ?? 1;

    /// <summary>The client tick, resolved the same way as <see cref="CommandNumber"/>.</summary>
    public int TickCount => RawTickCount ?? 1;

    /// <summary>Reads a <c>dem_usercmd</c> payload.</summary>
    /// <param name="payload">The command's bytes, without the length prefix.</param>
    /// <returns>The decoded command.</returns>
    /// <exception cref="InvalidDataException">
    /// The payload ends before the fields do, or continues past them.
    /// </exception>
    /// <remarks>
    /// The length check is the only integrity signal available: a user command carries no count,
    /// no terminator and no checksum, so a field read at the wrong width shows up here as leftover
    /// bytes and nowhere else at all.
    /// </remarks>
    public static UserCommand Decode(ReadOnlySpan<byte> payload)
    {
        BitReader reader = new(payload);
        UserCommand command;

        try
        {
            command = Read(ref reader);
        }
        catch (EndOfStreamException exhausted)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A user command payload of {payload.Length} bytes ended before its fields " +
                    $"did, so one of them was read at the wrong width."),
                exhausted);
        }

        int expectedBytes = (reader.BitsRead + BitsPerByte - 1) / BitsPerByte;

        if (expectedBytes != payload.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A user command's fields occupy {reader.BitsRead} bits, which is " +
                $"{expectedBytes} bytes, but the payload is {payload.Length}."));
        }

        // Read rather than assumed. These bits are the engine's uninitialised stack and they are
        // the difference between a rebuilt demo that matches and one that does not.
        int padWidth = PaddingWidthFor(reader.BitsRead);

        return padWidth == 0
            ? command
            : command with { Padding = (byte)reader.ReadUInt32(padWidth) };
    }

    /// <summary>Writes this command back in the form <c>WriteUsercmd</c> would have written it.</summary>
    /// <returns>The payload bytes, padded to a byte boundary with zeros as <c>bf_write</c> pads.</returns>
    /// <remarks>
    /// Byte-exact rather than field-exact, and it can be: the presence bits are all
    /// value-conditional against a constant baseline, so which fields were sent is recoverable
    /// from what they hold. That is not true of every message in this format — <c>svc_sounds</c>
    /// has to carry its own shape — and the round-trip test is what keeps the claim honest.
    /// </remarks>
    public byte[] Encode()
    {
        BitWriter writer = new();

        WriteOptional(writer, RawCommandNumber is not null, unchecked((uint)CommandNumber), Int32Bits);
        WriteOptional(writer, RawTickCount is not null, unchecked((uint)TickCount), Int32Bits);
        WriteFloat(writer, Pitch);
        WriteFloat(writer, Yaw);
        WriteFloat(writer, Roll);
        WriteFloat(writer, ForwardMove);
        WriteFloat(writer, SideMove);
        WriteFloat(writer, UpMove);
        WriteOptional(writer, Buttons != 0, Buttons, Int32Bits);
        WriteOptional(writer, Impulse != 0, Impulse, ImpulseBits);

        if (WeaponSelect != 0)
        {
            writer.WriteBit(true).Write((uint)WeaponSelect, WeaponSelectBits);
            WriteOptional(writer, WeaponSubtype != 0, (uint)WeaponSubtype, WeaponSubtypeBits);
        }
        else
        {
            writer.WriteBit(false);
        }

        WriteOptional(writer, MouseDx != 0, unchecked((uint)MouseDx), ShortBits);
        WriteOptional(writer, MouseDy != 0, unchecked((uint)MouseDy), ShortBits);

        int padWidth = PaddingWidthFor(writer.BitCount);

        if (padWidth > 0)
        {
            writer.Write(Padding, padWidth);
        }

        return writer.Build();
    }

    /// <summary>Where a payload's fields stop, in bits, before the padding begins.</summary>
    /// <param name="payload">The command's bytes, without the length prefix.</param>
    /// <returns>The bit offset of the first padding bit.</returns>
    /// <remarks>
    /// Exposed because the padding's origin is only checkable against absolute bit offsets: the
    /// bits a command leaves untouched are the ones the *previous* command wrote at the same
    /// positions, and locating those needs this number.
    /// </remarks>
    public static int FieldBits(ReadOnlySpan<byte> payload)
    {
        BitReader reader = new(payload);
        _ = Read(ref reader);
        return reader.BitsRead;
    }

    /// <summary>How many bits sit between the last field and the next byte boundary.</summary>
    private static int PaddingWidthFor(int fieldBits) =>
        (BitsPerByte - (fieldBits % BitsPerByte)) % BitsPerByte;

    private static UserCommand Read(ref BitReader reader)
    {
        int? commandNumber = reader.ReadBit() ? unchecked((int)reader.ReadUInt32(Int32Bits)) : null;
        int? tickCount = reader.ReadBit() ? unchecked((int)reader.ReadUInt32(Int32Bits)) : null;

        float pitch = ReadFloat(ref reader);
        float yaw = ReadFloat(ref reader);
        float roll = ReadFloat(ref reader);
        float forwardMove = ReadFloat(ref reader);
        float sideMove = ReadFloat(ref reader);
        float upMove = ReadFloat(ref reader);

        uint buttons = reader.ReadBit() ? reader.ReadUInt32(Int32Bits) : 0;
        byte impulse = reader.ReadBit() ? (byte)reader.ReadUInt32(ImpulseBits) : (byte)0;

        int weaponSelect = 0;
        int weaponSubtype = 0;

        // The subtype's presence bit is nested inside the selection's, so it is not read at all
        // when no weapon was switched to. Reading it unconditionally costs one bit and
        // desynchronises both mouse deltas, which is the failure this shape exists to avoid.
        if (reader.ReadBit())
        {
            weaponSelect = (int)reader.ReadUInt32(WeaponSelectBits);

            if (reader.ReadBit())
            {
                weaponSubtype = (int)reader.ReadUInt32(WeaponSubtypeBits);
            }
        }

        // WriteShort is signed and ReadShort sign-extends. Read as unsigned, a small leftward
        // flick becomes a value near 65535 - a number that looks like data and turns into a
        // camera spin the moment anything integrates it.
        short mouseDx = reader.ReadBit() ? unchecked((short)reader.ReadUInt32(ShortBits)) : (short)0;
        short mouseDy = reader.ReadBit() ? unchecked((short)reader.ReadUInt32(ShortBits)) : (short)0;

        return new UserCommand(
            commandNumber, tickCount, pitch, yaw, roll, forwardMove, sideMove, upMove,
            buttons, impulse, weaponSelect, weaponSubtype, mouseDx, mouseDy, 0);
    }

    private static float ReadFloat(ref BitReader reader) =>
        reader.ReadBit()
            ? BitConverter.Int32BitsToSingle(unchecked((int)reader.ReadUInt32(Int32Bits)))
            : 0f;

    private static void WriteFloat(BitWriter writer, float value) =>
        WriteOptional(
            writer,
            // Compared as bits, not as a float, because that is what `!=` on two floats in
            // WriteUsercmd compiles to for every value the engine can produce here. It also keeps
            // negative zero distinguishable from zero, which a float comparison would merge.
            BitConverter.SingleToInt32Bits(value) != 0,
            unchecked((uint)BitConverter.SingleToInt32Bits(value)),
            Int32Bits);

    private static void WriteOptional(BitWriter writer, bool present, uint value, int bits)
    {
        writer.WriteBit(present);

        if (present)
        {
            writer.Write(value, bits);
        }
    }
}
