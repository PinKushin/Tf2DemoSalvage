using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Core.Tests.Container;

/// <summary>
/// Reads every corpus demo and writes it back, asserting the bytes match.
/// </summary>
/// <remarks>
/// **The strongest correctness statement available, and the one this project was missing.**
/// Everything else asks whether the parser produces plausible output; this asks whether it
/// understood the file. A field read at the wrong width, a block silently skipped, a length
/// misinterpreted — none of those have to produce visibly wrong output, and all of them produce
/// different bytes.
///
/// It is the property <c>lmpc</c> has for Quake demos and the reason this project's trace is
/// modelled on it: decompile, compile, get the same file back. This covers the container; the
/// message stream inside each payload is carried verbatim rather than re-encoded, so a failure
/// here is a container-level misunderstanding specifically.
///
/// This test found its first bug immediately — <c>democmdinfo_t</c> and the packet sequence
/// numbers had been read and discarded since the reader was written, which made a byte-exact
/// rewrite impossible.
/// </remarks>
public sealed class CorpusRoundTripTests
{
    [Test]
    public void EveryDemo_ReadsAndWritesBackToTheSameBytes()
    {
        foreach (string path in Corpus.Files())
        {
            string name = Path.GetFileName(path);
            byte[] original = File.ReadAllBytes(path);

            DemoHeader header = DemoHeader.Parse(original);
            List<DemoCommand> commands =
                [.. DemoCommandReader.Read(original.AsMemory(DemoHeader.SizeBytes))];

            byte[] rewritten = DemoWriter.Write(header, commands);

            // The command stream only. A header's text fields keep whatever TF2 left in the
            // buffer after the terminator, so those bytes are not reproducible from decoded
            // values and comparing them would fail for a reason that is not a decoding error.
            ReadOnlySpan<byte> before = original.AsSpan(DemoHeader.SizeBytes);
            ReadOnlySpan<byte> after = rewritten.AsSpan(DemoHeader.SizeBytes);

            if (!before.SequenceEqual(after))
            {
                int at = FirstDifference(before, after);
                Assert.Fail(
                    $"{name}: command stream differs at byte {at} of {before.Length} " +
                    $"(rewrote {after.Length}). {Describe(commands, at)}");
            }

            TestContext.Out.WriteLine($"{name}: {before.Length} bytes, {commands.Count} commands, exact");
        }
    }

    [Test]
    public void EveryDemo_RewritesEveryHeaderField()
    {
        // The header's padding is not reproducible, but its fields are - and a header written
        // from decoded values must read back as the same values or the writer is lying about
        // what it preserved.
        foreach (string path in Corpus.Files())
        {
            byte[] original = File.ReadAllBytes(path);
            DemoHeader header = DemoHeader.Parse(original);

            DemoHeader reread = DemoHeader.Parse(
                DemoWriter.Write(header, [new DemoCommand(DemoCommandType.Stop, 0, default)]));

            reread.ShouldBe(header, Path.GetFileName(path));
        }
    }

    private static int FirstDifference(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        int shared = Math.Min(left.Length, right.Length);
        for (int i = 0; i < shared; i++)
        {
            if (left[i] != right[i])
            {
                return i;
            }
        }

        return shared;
    }

    /// <summary>Names the command a byte offset falls in, so a failure says where rather than how far.</summary>
    private static string Describe(IReadOnlyList<DemoCommand> commands, int offset)
    {
        int position = 0;
        foreach (DemoCommand command in commands)
        {
            int size = 5 + command.Prologue.Length +
                (command.Type is DemoCommandType.SyncTick or DemoCommandType.Stop
                    ? 0
                    : 4 + command.Payload.Length);

            if (offset < position + size)
            {
                return $"Inside {command.Type} at tick {command.Tick}, " +
                       $"{offset - position} bytes into a {size}-byte command.";
            }

            position += size;
        }

        return "Past the last command.";
    }
}
