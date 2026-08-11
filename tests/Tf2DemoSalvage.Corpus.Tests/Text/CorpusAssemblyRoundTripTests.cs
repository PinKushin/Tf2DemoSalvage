using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Container;
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
    [Fact]
    public void EveryDemo_CompilesBackToItsOwnBytes()
    {
        int demos = 0;
        long bytes = 0;

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

            // Length first: a mismatch there is a different failure from a mismatch in content,
            // and comparing arrays of different lengths reports the least useful of the two.
            rebuilt.Length.ShouldBe(original.Length, name);

            int difference = FirstDifference(original, rebuilt);
            difference.ShouldBe(-1, $"{name}: first differing byte at {difference}");

            demos++;
            bytes += original.Length;
        }

        // A corpus that stopped being found would otherwise pass this without comparing anything.
        demos.ShouldBeGreaterThan(5);
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{demos} demos, {bytes:N0} bytes decompiled to text and compiled back byte for byte"));
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
