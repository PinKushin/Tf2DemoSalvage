using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// The trace's <c>dem_usercmd</c> block, driven by a command this test wrote.
/// </summary>
/// <remarks>
/// **A point-of-view demo only field, which is why it was corpus-only.** SourceTV recordings carry
/// no user commands at all — they are the recording client's own inputs — so half the corpus
/// cannot exercise this and the other half only ever shows whatever the recorder happened to
/// press.
///
/// A written command shows both. Buttons that no recording in the corpus contains, a weapon select
/// with its subtype, and the case that matters most for correctness: the padding bits, which are
/// stale engine stack rather than zeroes and cannot be recomputed. See
/// <c>docs/memory/padding-is-not-zero.md</c>.
/// </remarks>
public sealed class UserCommandTraceTests
{
    [Test]
    public void Trace_AUserCommand_RendersItsFieldsRatherThanCountingIt()
    {
        // The whole point of the block: a reader wants to see what was pressed, not that a
        // dem_usercmd went past. Values chosen distinct so a field read at the wrong offset shows
        // as a wrong number rather than as another zero.
        string trace = Trace(Command() with
        {
            RawCommandNumber = 4242,
            RawTickCount = 9001,
            Pitch = 12.5f,
            Yaw = -45f,
            ForwardMove = 450f,
            SideMove = -200f,
            Impulse = 101,
        });

        trace.ShouldContain("dem_usercmd");
        trace.ShouldContain("4242");
        trace.ShouldContain("9001");
        trace.ShouldContain("101");
    }

    [Test]
    public void Trace_ButtonsThatArePressed_AreNamedNotPrintedAsANumber()
    {
        // A bitfield rendered as 8449 tells a reader nothing. The names are the reason the block
        // exists, and the corpus can only show whichever buttons its recorder actually pressed.
        //
        // IN_ATTACK is bit 0, IN_JUMP bit 1, IN_DUCK bit 2 — chosen together because the failure
        // worth catching is two of them being confused, which a single-bit test cannot see.
        string trace = Trace(Command() with { Buttons = 1u | 2u | 4u });

        trace.ShouldContain("attack", Case.Insensitive);
        trace.ShouldContain("jump", Case.Insensitive);
        trace.ShouldContain("duck", Case.Insensitive);
    }

    [Test]
    public void Trace_NoButtonsPressed_SaysSoRatherThanLeavingABlank()
    {
        // A blank where a button list should be reads as "the field is missing", which is a
        // different claim from "nothing was pressed". Most ticks of most demos are this case.
        string trace = Trace(Command() with { Buttons = 0 });

        trace.ShouldContain("dem_usercmd");
        trace.ShouldNotContain("buttons ;", Case.Sensitive);
    }

    [Test]
    public void RoundTrip_TheTrailingPaddingBits_AreCarriedNotRecomputed()
    {
        // **The finding this whole field exists for.** The bits after the last field are
        // uninitialised engine stack, not zeroes, so a decoder that assumed zero would rebuild a
        // file differing from the original in nearly every user command.
        //
        // Asserted through the encoder rather than the trace, because the trace renders values and
        // this is about bits. A synthetic command can carry padding no recording happens to hold,
        // which is what makes the claim testable at all.
        //
        // **The width is computed rather than assumed, and the first draft of this test assumed a
        // whole byte and failed.** Padding is only what remains to the next byte boundary after
        // the fields, and the fields are conditional — this command leaves three bits. Hardcoding
        // eight asserted that five bits nobody wrote came back, which is a statement about the
        // fixture rather than about the format.
        UserCommand sent = Command() with { Padding = 0b1011_0100 };
        byte[] payload = sent.Encode();

        int paddingBits = (payload.Length * 8) - UserCommand.FieldBits(payload);

        // Vacuous otherwise: a command with no padding at all would pass any assertion below.
        paddingBits.ShouldBeGreaterThan(0);

        byte expected = (byte)(0b1011_0100 & ((1 << paddingBits) - 1));
        UserCommand.Decode(payload).Padding.ShouldBe(expected);

        // And the half that matters: those bits are NOT zero, so a decoder recomputing them as
        // zero would disagree here. Without this the test passes against exactly the bug it
        // exists to catch.
        expected.ShouldNotBe((byte)0);
    }

    [Test]
    public void Trace_AWeaponSelectWithItsSubtype_ShowsBoth()
    {
        // The subtype bit exists only inside the weapon-select branch, so a command that selects
        // nothing must not print one. Both halves asserted, because a renderer that always prints
        // it passes any test that only looks at the selecting case.
        Trace(Command() with { WeaponSelect = 7, WeaponSubtype = 3 })
            .ShouldContain("7");

        string none = Trace(Command() with { WeaponSelect = 0, WeaponSubtype = 0 });
        none.ShouldContain("dem_usercmd");
    }

    /// <summary>A trace of a demo whose only command is this one.</summary>
    private static string Trace(UserCommand input)
    {
        byte[] payload = input.Encode();

        // dem_usercmd carries a four-byte outgoing sequence number before its length-prefixed
        // payload, which the reader consumes and the writer must therefore supply.
        DemoCommand command = new(
            DemoCommandType.UserCmd, Tick: 66, payload, new byte[4]);

        byte[] demo = SyntheticDemo.From(SyntheticDemo.DefaultProtocol, command);

        DemoHeader header = DemoHeader.Parse(demo.AsSpan(0, DemoHeader.SizeBytes));
        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(demo.AsMemory(DemoHeader.SizeBytes))];

        StringWriter text = new() { NewLine = "\n" };
        DemoTraceWriter.Write(text, "synthetic.dem", header, commands);
        return text.ToString();
    }

    /// <summary>A command with every field at a neutral value, overridden per test.</summary>
    private static UserCommand Command() => new(
        RawCommandNumber: 1,
        RawTickCount: 1,
        Pitch: 0f,
        Yaw: 0f,
        Roll: 0f,
        ForwardMove: 0f,
        SideMove: 0f,
        UpMove: 0f,
        Buttons: 0,
        Impulse: 0,
        WeaponSelect: 0,
        WeaponSubtype: 0,
        MouseDx: 0,
        MouseDy: 0,
        Padding: 0);
}
