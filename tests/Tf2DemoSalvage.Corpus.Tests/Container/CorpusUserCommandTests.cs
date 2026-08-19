using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Container;

/// <summary>
/// Decodes every <c>dem_usercmd</c> in the corpus and writes each one back.
/// </summary>
/// <remarks>
/// **The hand-built fixtures cannot falsify a misreading of Valve's source; this can.** The
/// fixtures are written from the same reading of <c>WriteUsercmd</c> that the decoder is, so they
/// agree with it by construction. Real commands were written by the engine, and every one of them
/// is an independent chance for that reading to be wrong.
///
/// Exact re-encoding is the measurement rather than "it decoded without throwing", because a
/// command has no terminator and no checksum: a field read one bit too narrow shifts everything
/// after it into plausible-looking values, and the only thing that ever notices is the length.
/// </remarks>
public sealed class CorpusUserCommandTests
{
    [Test]
    public void EveryUserCommand_DecodesAndReEncodesExactly()
    {
        int total = 0;
        int demosWithInput = 0;

        foreach (string path in Corpus.Files())
        {
            string name = Path.GetFileName(path);
            byte[] file = File.ReadAllBytes(path);

            List<DemoCommand> commands =
                [.. DemoCommandReader.Read(file.AsMemory(DemoHeader.SizeBytes))];

            int inDemo = 0;

            foreach (DemoCommand command in commands
                .Where(c => c.Type == DemoCommandType.UserCmd))
            {
                byte[] payload = command.Payload.ToArray();
                UserCommand decoded = UserCommand.Decode(payload);

                decoded.Encode().ShouldBe(
                    payload,
                    $"{name}: user command at tick {command.Tick} did not re-encode");

                inDemo++;
            }

            if (inDemo == 0)
            {
                continue;
            }

            demosWithInput++;
            total += inDemo;
            TestContext.Out.WriteLine($"{name}: {inDemo} user commands, all exact");
        }

        // SourceTV demos contain none of these - there is no player behind the camera - so this
        // is a claim about the corpus as well as about the codec, and it is the assertion that
        // fails loudly if the corpus ever loses its point-of-view recordings.
        demosWithInput.ShouldBeGreaterThan(0, "no demo in the corpus carried player input");
        total.ShouldBeGreaterThan(0);
        TestContext.Out.WriteLine($"{total} user commands across {demosWithInput} demos");
    }

    [Test]
    public void UserCommand_ViewAngles_AgreeWithTheCameraTrack()
    {
        // **The one check here that does not depend on this project's reading of Valve's source.**
        // Everything else - the fixtures, the round trip - tests the codec against the same
        // interpretation that produced it. This tests it against the engine.
        //
        // A demo records the same three angles twice by two unrelated routes: democmdinfo_t
        // stores them as plain little-endian floats ahead of every packet, and the user command
        // stores them bit-packed behind presence bits. Nothing in either path can see the other,
        // so if the bit-level decode had a field transposed or a width wrong, the two would
        // disagree. They cannot agree by accident.
        int matched = 0;
        int compared = 0;

        foreach (string path in Corpus.Files())
        {
            UserCommand? latest = null;

            foreach (DemoCommand command in
                DemoCommandReader.Read(File.ReadAllBytes(path).AsMemory(DemoHeader.SizeBytes)))
            {
                if (command.Type == DemoCommandType.UserCmd && !command.Payload.IsEmpty)
                {
                    latest = UserCommand.Decode(command.Payload.Span);
                    continue;
                }

                if (command.View is not { } view || latest is null)
                {
                    continue;
                }

                compared++;

                // Compared as bits rather than with a tolerance. Nothing computes these - both
                // sides are the same float the engine held, written twice - so an epsilon would
                // only widen the test enough to hide a real disagreement.
                if (SameBits(view.Pitch, latest.Pitch) && SameBits(view.Yaw, latest.Yaw) &&
                    SameBits(view.Roll, latest.Roll))
                {
                    matched++;
                }
            }
        }

        compared.ShouldBeGreaterThan(0, "no packet followed a user command in any demo");

        // A rate rather than an equality, because not every packet lands on a command boundary:
        // the client sends input faster than the server sends snapshots, and a dropped command
        // leaves a stale one in hand. Measured at 329,969 of 330,853 - 99.7% - across the ten
        // point-of-view demos. The floor is set well below that and still nowhere near what an
        // incorrect decode could reach.
        double rate = (double)matched / compared;
        TestContext.Out.WriteLine($"{matched} of {compared} packets matched the last command ({rate:P1})");
        rate.ShouldBeGreaterThan(0.95);
    }

    [Test]
    public void UserCommand_PaddingBits_AreThePreviousCommands()
    {
        // This test exists because the first account of the padding was wrong. It was written up
        // as uninitialised process memory - a leak - on the strength of the bits being non-zero
        // and varying. Non-zero and varying is consistent with several mechanisms, and "leak" was
        // the one that got asserted rather than the one that got tested.
        //
        // The condition that separates them: if the buffer is simply reused and never cleared,
        // the unwritten tail still holds what the PREVIOUS command put at those exact bit
        // offsets, and that is predictable. Foreign memory is not.
        int testable = 0;
        int predicted = 0;

        foreach (string path in Corpus.Files())
        {
            byte[]? previous = null;

            foreach (DemoCommand command in
                DemoCommandReader.Read(File.ReadAllBytes(path).AsMemory(DemoHeader.SizeBytes))
                    .Where(c => c.Type == DemoCommandType.UserCmd))
            {
                byte[] payload = command.Payload.ToArray();
                UserCommand decoded = UserCommand.Decode(payload);
                int fieldBits = UserCommand.FieldBits(payload);
                int padWidth = (BitsPerByte - (fieldBits % BitsPerByte)) % BitsPerByte;

                // Only commands where the previous payload actually extended far enough to have
                // written those positions can say anything, so the rest are excluded rather than
                // counted as misses.
                if (previous is not null && decoded.Padding != 0 && padWidth > 0 &&
                    previous.Length * BitsPerByte >= fieldBits + padWidth)
                {
                    testable++;

                    if (ReadAt(previous, fieldBits, padWidth) == decoded.Padding)
                    {
                        predicted++;
                    }
                }

                previous = payload;
            }
        }

        testable.ShouldBeGreaterThan(0);

        // Measured at 86-97% per demo, against a chance floor of about one in seven for a
        // three-bit field that is known to be non-zero. The floor here is set well below the
        // measurement and still far above anything foreign memory could reach.
        double rate = (double)predicted / testable;
        TestContext.Out.WriteLine($"{predicted} of {testable} non-zero pads matched the previous " +
                         $"command's bits at the same offsets ({rate:P1})");
        rate.ShouldBeGreaterThan(0.5);
    }

    private const int BitsPerByte = 8;

    private static uint ReadAt(byte[] data, int bitOffset, int width)
    {
        BitReader reader = new(data);
        int skipped = 0;

        while (skipped + 32 <= bitOffset)
        {
            _ = reader.ReadUInt32(32);
            skipped += 32;
        }

        if (bitOffset - skipped > 0)
        {
            _ = reader.ReadUInt32(bitOffset - skipped);
        }

        return reader.ReadUInt32(width);
    }

    private static bool SameBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);

    [Test]
    public void UserCommand_PlayerInput_VariesFromItsDefaults()
    {
        // The control for the test above. Re-encoding a payload whose every presence bit is zero
        // is nearly free, so a codec that silently produced empty commands would pass it on any
        // corpus. Real recordings must show angles moving and buttons being pressed.
        HashSet<float> yaws = [];
        uint buttonsSeen = 0;
        bool anyWeaponSwitch = false;

        foreach (string path in Corpus.Files())
        {
            byte[] file = File.ReadAllBytes(path);

            foreach (DemoCommand command in
                DemoCommandReader.Read(file.AsMemory(DemoHeader.SizeBytes))
                    .Where(c => c.Type == DemoCommandType.UserCmd))
            {
                UserCommand decoded = UserCommand.Decode(command.Payload.Span);
                yaws.Add(decoded.Yaw);
                buttonsSeen |= decoded.Buttons;
                anyWeaponSwitch |= decoded.WeaponSelect != 0;
            }
        }

        // A player who turned at all produces many distinct yaws; a decoder returning a constant
        // produces one.
        yaws.Count.ShouldBeGreaterThan(10);

        // IN_ATTACK and IN_FORWARD are the two nobody records a demo without.
        (buttonsSeen & 1).ShouldBe(1u, "IN_ATTACK never appeared in any recorded input");
        (buttonsSeen & (1u << 3)).ShouldBe(1u << 3, "IN_FORWARD never appeared");

        anyWeaponSwitch.ShouldBeTrue("no weapon switch appeared in any recorded input");

        TestContext.Out.WriteLine(
            $"{yaws.Count} distinct yaws, buttons union {UserCommandButtons.Describe(buttonsSeen)}");
    }
}
