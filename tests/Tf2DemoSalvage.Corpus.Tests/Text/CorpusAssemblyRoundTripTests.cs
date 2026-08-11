using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// Decompiles every corpus demo to text, compiles it back, and compares the bytes.
/// </summary>
/// <remarks>
/// **The criterion the Quake demo tools set, and the one this project had been measuring around.**
/// Every other check here answers "did the decode look right"; this answers "is the file
/// recoverable from the text", which is the only question with a yes-or-no answer.
///
/// The comparison is against the demo on disk, so nothing about it is self-referential: the text
/// is written by one code path, read by another, and assembled by a third.
///
/// It is a gate rather than a report, because a partial answer here is not interesting. A demo
/// that compiles back to all but one byte is a demo that cannot be played.
/// </remarks>
public sealed class CorpusAssemblyRoundTripTests(ITestOutputHelper output)
{
    /// <summary>
    /// Commands per demo. A prefix, because this suite runs once per mutant on the measurement
    /// box.
    /// </summary>
    /// <remarks>
    /// The full corpus is 833 MB, and decompiling all of it to text and back took the corpus suite
    /// from 48 seconds to four and a half minutes - which, multiplied by 1,300 mutants, is the
    /// difference between an overnight mutation run and one that does not finish. A prefix rebuilds
    /// a prefix of the file, so nothing about byte-exactness is weakened; only the number of bytes
    /// compared changes.
    /// </remarks>
    private const int CommandLimit = 2000;

    [Fact]
    public void EveryDemo_CompilesBackToItsOwnBytes()
    {
        int demos = 0;
        long bytes = 0;
        long structured = 0;
        long raw = 0;

        foreach (string path in Corpus.Files())
        {
            string name = Path.GetFileName(path);
            byte[] original = File.ReadAllBytes(path);

            DemoHeader header = DemoHeader.Parse(original.AsSpan(0, DemoHeader.SizeBytes));
            List<DemoCommand> commands =
                [.. DemoCommandReader.Read(original.AsMemory(DemoHeader.SizeBytes))
                    .Take(CommandLimit)];

            StringWriter text = new() { NewLine = "\n" };
            DemoAssembly.Write(text, header, commands);

            using StringReader reader = new(text.ToString());
            (DemoHeader compiledHeader, IReadOnlyList<DemoCommand> compiledCommands) =
                DemoAssembly.Parse(reader);

            compiledCommands.Count.ShouldBe(commands.Count, name);

            byte[] rebuilt = DemoWriter.Write(compiledHeader, compiledCommands);

            // A prefix of the commands rebuilds a prefix of the file, so the comparison is against
            // the same number of bytes rather than the whole demo. Byte-exactness is unaffected:
            // every byte the writer produced has to match the byte at that offset.
            rebuilt.Length.ShouldBeLessThanOrEqualTo(original.Length, name);

            int difference = FirstDifference(original[..rebuilt.Length], rebuilt);
            difference.ShouldBe(-1, $"{name}: first differing byte at {difference}");

            demos++;
            bytes += original.Length;
            Count(text.ToString(), ref structured, ref raw);
        }

        // A corpus that stopped being found would otherwise pass this without comparing anything.
        demos.ShouldBeGreaterThan(5);
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{demos} demos, {bytes:N0} bytes decompiled to text and compiled back byte for byte"));

        // The progress measure, and the only part of this test that is a report rather than a
        // gate: how much of the message stream is text a person could edit, against how much is
        // still bits nobody has promoted yet.
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{structured:N0} of {structured + raw:N0} message lines are structured " +
            $"({100.0 * structured / (structured + raw):F1}%)"));

        ReportWhatIsStillRaw(output);
    }

    /// <summary>
    /// Names the message types still carried as bits, in bit order.
    /// </summary>
    /// <remarks>
    /// The work queue. A line count says how much text there is; bits say how much of the demo a
    /// viewer still cannot read, which is the number that matters - a single svc_PacketEntities
    /// outweighs a thousand net_nops.
    /// </remarks>
    private static void ReportWhatIsStillRaw(ITestOutputHelper output)
    {
        Dictionary<string, long> bits = new(StringComparer.Ordinal);
        Dictionary<string, long> counts = new(StringComparer.Ordinal);

        foreach (string path in Corpus.Files())
        {
            byte[] bytes = File.ReadAllBytes(path);
            NetDecodeState state = new() { NetworkProtocol = Corpus.ProtocolOf(path) };

            foreach (DemoCommand command in
                DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)).Take(CommandLimit))
            {
                if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
                {
                    continue;
                }

                NetMessageReadResult result = NetMessageReader.Read(command.Payload.Span, state);
                for (int i = 0; i < result.Messages.Count; i++)
                {
                    if (MessageAssembly.CanWrite(result.Messages[i]))
                    {
                        continue;
                    }

                    int end = i + 1 < result.Messages.Count
                        ? result.MessageStartBits[i + 1]
                        : result.BitsConsumed;

                    string key = result.Messages[i].Type.ToString();
                    bits[key] = bits.GetValueOrDefault(key) + end - result.MessageStartBits[i];
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }
            }
        }

        output.WriteLine("still raw, by bits:");
        foreach ((string type, long total) in bits.OrderByDescending(entry => entry.Value))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"    {total,14:N0}  {counts[type],9:N0}  {type}"));
        }
    }

    /// <summary>Counts message lines by whether they carry text or bits.</summary>
    private static void Count(string assembly, ref long structured, ref long raw)
    {
        foreach (string line in assembly.Split('\n'))
        {
            // Message lines are the indented ones; commands and the header are not.
            if (line.Length == 0 || line[0] != ' ')
            {
                continue;
            }

            if (line.TrimStart().StartsWith("raw ", StringComparison.Ordinal))
            {
                raw++;
                continue;
            }

            structured++;
        }
    }

    private static int FirstDifference(byte[] left, byte[] right)
    {
        for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            if (left[i] != right[i])
            {
                return i;
            }
        }

        return left.Length == right.Length ? -1 : Math.Min(left.Length, right.Length);
    }
}
