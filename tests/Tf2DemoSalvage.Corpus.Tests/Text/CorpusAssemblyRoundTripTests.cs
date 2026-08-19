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
public sealed class CorpusAssemblyRoundTripTests
{
    // **There is no command limit any more, and removing it is the point of this comment.**
    //
    // This read the first 600 commands of each demo. The reasoning was sound at the time — the
    // suite runs once per mutant, the full corpus is 833 MB, and an uncapped run took it from 48
    // seconds to four and a half minutes, which multiplied by 1,300 mutants is the difference
    // between an overnight mutation run and one that does not finish. The comment even argued that
    // coverage was unaffected: "a prefix rebuilds a prefix of the file, so nothing about
    // byte-exactness is weakened; only the number of bytes compared changes."
    //
    // That last sentence is the wrong one, and it cost two real bugs on 2026-08-19:
    //
    // - An empty voice body could not be assembled back. Voice data does not appear until players
    //   start talking, which is thousands of commands in.
    // - A carriage return inside a string split the line. It came from `cappers` on
    //   teamplay_point_captured, whose field is raw player-index bytes, so the player in slot 13
    //   put 0x0D in a string. A point capture is also thousands of commands in.
    //
    // Both were found by round-tripping WHOLE demos through the CLI, and neither could have been
    // found here. **A cap on stream POSITION is not the same as a cap on size**: it does not sample
    // the file, it removes the entire late half of it, and a demo's late half is where anything
    // that depends on the match having started lives. Signon and the first seconds are the most
    // uniform part of a recording and the least likely to hold a surprise.
    //
    // The cost is affordable now for two reasons that were not true when the cap was written.
    // Daily mutation moved to the Oracle box, which has no GitHub six-hour job ceiling and runs on
    // a schedule with margin. And D25 split the projects: the daily mutation workhorse is
    // Core.Tests, which is entirely synthetic, while this project is weekly and overnight by
    // design. Slowing an overnight job to catch bugs the fast one structurally cannot is the trade
    // this suite exists to make.

    [Test]
    public void EveryDemo_CompilesBackToItsOwnBytes()
    {
        int demos = 0;
        long bytes = 0;
        long structured = 0;
        long raw = 0;

        // Every demo's text, kept so the report counts what was written rather than what could
        // have been.
        System.Text.StringBuilder everything = new();

        foreach (string path in Corpus.Files())
        {
            string name = Path.GetFileName(path);
            byte[] original = File.ReadAllBytes(path);

            DemoHeader header = DemoHeader.Parse(original.AsSpan(0, DemoHeader.SizeBytes));
            List<DemoCommand> commands =
                [.. DemoCommandReader.Read(original.AsMemory(DemoHeader.SizeBytes))];

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
            everything.Append(text.ToString());
        }

        // A corpus that stopped being found would otherwise pass this without comparing anything.
        demos.ShouldBeGreaterThan(5);
        TestContext.Out.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{demos} demos, {bytes:N0} bytes decompiled to text and compiled back byte for byte"));

        // The progress measure, and the only part of this test that is a report rather than a
        // gate: how much of the message stream is text a person could edit, against how much is
        // still bits nobody has promoted yet.
        //
        // There is a floor and it is not 100%. Roughly one raw line per packet is the padding
        // after the last message - bits rather than a message, and never anything else - and a
        // compressed string table keeps its payload because reproducing a Snappy stream byte for
        // byte is not something a parser can promise.
        TestContext.Out.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{structured:N0} of {structured + raw:N0} message lines are structured " +
            $"({100.0 * structured / (structured + raw):F1}%)"));

        ReportWhatIsStillRaw(everything.ToString());
    }

    /// <summary>
    /// Names what is still carried as bits, by what the writer actually emitted.
    /// </summary>
    /// <remarks>
    /// **Measured from the output, not from <c>CanWrite</c>, because that is the mistake this
    /// report made for two commits.** Asking which types have a text form answers a different
    /// question from asking which messages got one: a type can have a text form that declines on
    /// every instance, and the report will happily say the queue is empty. It did - 6.3 million
    /// bits were still hex while this printed nothing at all.
    ///
    /// The writer labels each raw line with what it stands for and whether the type had a text
    /// form that declined, so counting the output cannot disagree with the output.
    /// </remarks>
    private static void ReportWhatIsStillRaw(string assembly)
    {
        Dictionary<string, long> bits = new(StringComparer.Ordinal);
        Dictionary<string, long> counts = new(StringComparer.Ordinal);

        foreach (string line in assembly.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("raw ", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int marker = trimmed.IndexOf("# ", StringComparison.Ordinal);
            string label = marker < 0 ? "unlabelled" : trimmed[(marker + 2)..];

            bits[label] = bits.GetValueOrDefault(label) +
                int.Parse(parts[1], CultureInfo.InvariantCulture);
            counts[label] = counts.GetValueOrDefault(label) + 1;
        }

        TestContext.Out.WriteLine("still bits, by what they are:");
        foreach ((string label, long total) in bits.OrderByDescending(entry => entry.Value))
        {
            TestContext.Out.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"    {total,14:N0}  {counts[label],9:N0}  {label}"));
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
