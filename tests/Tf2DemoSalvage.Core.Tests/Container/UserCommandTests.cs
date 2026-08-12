using System;
using System.IO;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Container;

/// <summary>
/// Tests the <c>dem_usercmd</c> payload against Valve's published <c>WriteUsercmd</c>.
/// </summary>
/// <remarks>
/// Every field is delta-coded against a <em>default-constructed</em> <c>CUserCmd</c> rather than
/// against the previous command — <c>CInput::EncodeUserCmdToBuffer</c> constructs `nullcmd` on the
/// stack for every call — so each command decodes independently and there is no stream state to
/// get wrong. That is the single fact this whole file rests on, and it is why an absent
/// `command_number` means **one**: the writer's steady-increment rule is `from + 1`, and `from` is
/// always zero.
///
/// The hand-built fixtures below are written with a raw <see cref="BitWriter"/> following Valve's
/// source line by line, deliberately not through this project's encoder. A round trip through an
/// encoder cannot falsify a misreading shared by both halves of it.
/// </remarks>
public sealed class UserCommandTests
{
    /// <summary>Presence bits when no weapon is selected: the nested subtype bit is absent.</summary>
    private const int BitsWhenEmpty = 13;

    [Test]
    public void AnAllZeroPayloadMeansCommandOneNotCommandZero()
    {
        // Thirteen zero presence bits, which is the smallest legal command and the one the engine
        // writes constantly. Two bytes, because bf_write pads to a byte boundary.
        byte[] payload = new BitWriter().Write(0, BitsWhenEmpty).Build();
        payload.Length.ShouldBe(2);

        UserCommand command = UserCommand.Decode(payload);

        // The quirk. `to->command_number != from->command_number + 1` with from zeroed means the
        // bit is cleared for the value ONE, so a decoder that defaults the field to zero is off by
        // one on every command that omits it - which is nearly all of them.
        command.CommandNumber.ShouldBe(1);
        command.TickCount.ShouldBe(1);

        // Everything else genuinely defaults to zero, and none of it was transmitted.
        command.RawCommandNumber.ShouldBeNull();
        command.Yaw.ShouldBe(0f);
        command.ForwardMove.ShouldBe(0f);
        command.Buttons.ShouldBe(0u);
        command.MouseDx.ShouldBe((short)0);
    }

    [Test]
    public void EveryFieldIsReadInValvesOrderAtValvesWidth()
    {
        // One decisive fixture with every presence bit set, each field carrying a value that
        // could not be produced by reading a neighbouring field: any transposed pair, or any
        // width that is off by even one bit, moves at least one of these.
        BitWriter writer = new();
        writer.WriteBit(true).Write(0x11223344, 32);          // command_number
        writer.WriteBit(true).Write(0x55667788, 32);          // tick_count
        WriteFloat(writer, -12.5f);                           // viewangles[0], pitch
        WriteFloat(writer, 137.25f);                          // viewangles[1], yaw
        WriteFloat(writer, 0.75f);                            // viewangles[2], roll
        WriteFloat(writer, 450f);                             // forwardmove
        WriteFloat(writer, -450f);                            // sidemove
        WriteFloat(writer, 320f);                             // upmove
        writer.WriteBit(true).Write(0x00000A09, 32);          // buttons
        writer.WriteBit(true).Write(101, 8);                  // impulse
        writer.WriteBit(true).Write(1337, 11);                // weaponselect, MAX_EDICT_BITS
        writer.WriteBit(true).Write(37, 6);                   // weaponsubtype, WEAPON_SUBTYPE_BITS
        writer.WriteBit(true).Write(unchecked((uint)-9), 16); // mousedx, WriteShort
        writer.WriteBit(true).Write(4321, 16);                // mousedy

        UserCommand command = UserCommand.Decode(writer.Build());

        command.CommandNumber.ShouldBe(0x11223344);
        command.TickCount.ShouldBe(0x55667788);
        command.Pitch.ShouldBe(-12.5f);
        command.Yaw.ShouldBe(137.25f);
        command.Roll.ShouldBe(0.75f);
        command.ForwardMove.ShouldBe(450f);
        command.SideMove.ShouldBe(-450f);
        command.UpMove.ShouldBe(320f);
        command.Buttons.ShouldBe(0x00000A09u);
        command.Impulse.ShouldBe((byte)101);
        command.WeaponSelect.ShouldBe(1337);
        command.WeaponSubtype.ShouldBe(37);

        // WriteShort is signed and ReadShort sign-extends. Read as unsigned this is 65527, which
        // is a plausible mouse delta right up until the camera spins.
        command.MouseDx.ShouldBe((short)-9);
        command.MouseDy.ShouldBe((short)4321);
    }

    [Test]
    public void TheWeaponSubtypeBitExistsOnlyInsideTheWeaponSelectBranch()
    {
        // The one nested field, and the only place in the layout where a presence bit is
        // conditional. A decoder that reads the subtype bit unconditionally consumes one bit too
        // many here and desynchronises everything after it - so the measurement is not the weapon
        // at all, it is the mouse delta that follows.
        BitWriter selected = new();
        selected.WriteBit(false).WriteBit(false);              // command_number, tick_count
        for (int i = 0; i < 6; i++)
        {
            selected.WriteBit(false);                          // angles and movement
        }

        selected.WriteBit(false).WriteBit(false);              // buttons, impulse
        selected.WriteBit(true).Write(19, 11);                 // weaponselect present
        selected.WriteBit(false);                              // subtype absent - the nested bit
        selected.WriteBit(true).Write(0x0101, 16);             // mousedx, the desync detector
        selected.WriteBit(false);                              // mousedy

        UserCommand withWeapon = UserCommand.Decode(selected.Build());
        withWeapon.WeaponSelect.ShouldBe(19);
        withWeapon.WeaponSubtype.ShouldBe(0);
        withWeapon.MouseDx.ShouldBe((short)0x0101);

        // The control: the same bits with weaponselect ABSENT. The nested bit must not be read at
        // all, so the eleven-bit id and the subtype bit both vanish and mousedx still lands.
        BitWriter unselected = new();
        for (int i = 0; i < 10; i++)
        {
            unselected.WriteBit(false);
        }

        unselected.WriteBit(false);                            // weaponselect absent
        unselected.WriteBit(true).Write(0x0101, 16);           // mousedx
        unselected.WriteBit(false);                            // mousedy

        UserCommand noWeapon = UserCommand.Decode(unselected.Build());
        noWeapon.WeaponSelect.ShouldBe(0);
        noWeapon.MouseDx.ShouldBe((short)0x0101);
    }

    [Test]
    public void APayloadThatDoesNotEndWhereTheFieldsDoIsRejected()
    {
        // The stated length is the only check available: the command carries no count, no
        // terminator and no checksum, so a misread width shows up as leftover bytes or as running
        // off the end, and nothing else ever reports it.
        byte[] good = new BitWriter().Write(0, BitsWhenEmpty).Build();

        Should.NotThrow(() => UserCommand.Decode(good));
        Should.Throw<InvalidDataException>(() => UserCommand.Decode([.. good, 0]));
        Should.Throw<InvalidDataException>(() => UserCommand.Decode(good.AsSpan(0, 1).ToArray()));
    }

    [Test]
    public void ACommandSurvivesReEncoding()
    {
        // Byte-exact, not field-exact. The presence bits are recoverable from the values because
        // the baseline is a constant, so unlike svc_sounds this record does not have to carry the
        // shape it was written in - but that is a claim about the encoding, and this is the check
        // that it holds.
        BitWriter writer = new();
        writer.WriteBit(true).Write(9001, 32);
        writer.WriteBit(false);
        WriteFloat(writer, 90f);
        writer.WriteBit(false).WriteBit(false);
        WriteFloat(writer, -400f);
        writer.WriteBit(false).WriteBit(false);
        writer.WriteBit(true).Write(1, 32);
        writer.WriteBit(false);
        writer.WriteBit(true).Write(7, 11);
        writer.WriteBit(false);
        writer.WriteBit(true).Write(unchecked((uint)-2), 16);
        writer.WriteBit(false);

        byte[] original = writer.Build();
        UserCommand.Decode(original).Encode().ShouldBe(original);

        // And the degenerate one, where every field sits at the baseline.
        byte[] empty = new BitWriter().Write(0, BitsWhenEmpty).Build();
        UserCommand.Decode(empty).Encode().ShouldBe(empty);
    }

    [Test]
    public void ATickCountOfOneIsIndistinguishableFromAnAbsentOneOnTheWire()
    {
        // Not a defect, and worth pinning: the writer's condition is value-based, so the encoder
        // must clear the bit for exactly the value the steady-increment rule would produce. A
        // round trip that set the bit here would still decode correctly and would still be wrong,
        // because it would not be the bytes the engine wrote.
        UserCommand steady = UserCommand.Decode(
            new BitWriter().Write(0, BitsWhenEmpty).Build());

        steady.TickCount.ShouldBe(1);
        steady.RawTickCount.ShouldBeNull();
        steady.Encode().ShouldBe(new BitWriter().Write(0, BitsWhenEmpty).Build());
    }

    [Test]
    public void TheBitsAfterTheLastFieldAreCarriedRatherThanZeroed()
    {
        // The regression fixture for the finding this test file was written to catch. The engine
        // writes user commands into an uninitialised stack buffer, and bf_write's tail is a
        // read-modify-write, so the bits between the last field and the byte boundary are
        // whatever was already there. Measured across the corpus: three bits over, taking every
        // value from 0 to 7.
        //
        // Thirteen presence bits is five bits into the second byte, so three bits of padding -
        // the same width the corpus overwhelmingly shows.
        byte[] withPadding = new BitWriter()
            .Write(0, BitsWhenEmpty)
            .Write(0b111, 3)
            .Build();

        UserCommand command = UserCommand.Decode(withPadding);

        command.Padding.ShouldBe((byte)0b111);
        command.CommandNumber.ShouldBe(1);

        // And it must survive the way back out. A codec that recomputed this as zero would
        // rebuild a demo differing from the original in nearly every command, while every
        // decoded field still read correctly.
        command.Encode().ShouldBe(withPadding);

        // The control: identical fields, different padding, different bytes. Without this the
        // test above cannot distinguish "carried the padding" from "happened to write zeros".
        byte[] zeroPadding = new BitWriter().Write(0, BitsWhenEmpty).Write(0, 3).Build();
        zeroPadding.ShouldNotBe(withPadding);
        UserCommand.Decode(zeroPadding).Padding.ShouldBe((byte)0);
        UserCommand.Decode(zeroPadding).Encode().ShouldBe(zeroPadding);
    }

    private static void WriteFloat(BitWriter writer, float value) =>
        writer.WriteBit(true).Write(unchecked((uint)BitConverter.SingleToInt32Bits(value)), 32);
}
