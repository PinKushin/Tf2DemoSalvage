using System;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// String tables against real demos.
/// </summary>
/// <remarks>
/// The <c>userinfo</c> table is the payoff: it maps entity slots to players. Everything else
/// here is precache data — model, sound and decal names — which is useful mainly because those
/// names are recognisable, and recognisable strings are how a bit-level decoder proves it is
/// still aligned.
/// </remarks>
public sealed class CorpusStringTableTests(ITestOutputHelper output)
{
    [Fact]
    public void ReportStringTables()
    {
        foreach (string path in Corpus.Files())
        {
            NetDecodeState state = new();
            int decoded = 0;
            int undecoded = 0;

            output.WriteLine($"--- {Path.GetFileName(path)} ---");

            foreach (DemoCommand command in SignonAndPackets(path).Take(400))
            {
                NetMessageReadResult result = NetMessageReader.Read(command.Payload.Span, state);

                foreach (CreateStringTableMessage table in
                    result.Messages.OfType<CreateStringTableMessage>())
                {
                    if (table.IsDecoded)
                    {
                        decoded++;
                        string sample = string.Join(", ", table.Entries
                            .Where(e => e.Text is not null)
                            .Take(3)
                            .Select(e => e.Text));
                        output.WriteLine(
                            $"  {table.Name,-24} {table.Entries.Count,5} entries  cap {table.MaxEntries,-6} {sample}");
                    }
                    else
                    {
                        undecoded++;
                        output.WriteLine($"  {table.Name,-24} NOT DECODED: {table.UndecodedReason}");
                    }
                }
            }

            output.WriteLine($"  => {decoded} decoded, {undecoded} not");
            output.WriteLine(string.Empty);
        }

        Corpus.Files().ShouldNotBeEmpty();
    }

    private static DemoCommand[] SignonAndPackets(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))
            .Where(c => c.Type is DemoCommandType.Signon or DemoCommandType.Packet)];
    }
}
