using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// The JSON Lines writer against real demos.
/// </summary>
/// <remarks>
/// Split out of <c>DemoJsonLinesWriterTests</c> when the corpus tests moved to their own project
/// (DECISIONS.md D25). That file keeps the synthetic cases, which mutate in seconds; this one
/// needs a 305 MB corpus and runs weekly.
/// </remarks>
public sealed class CorpusJsonLinesTests
{
    /// <summary>Parses each line as its own JSON document, which is the format's whole claim.</summary>
    private static List<JsonDocument> Lines(string output) =>
    [
        .. output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line)),
    ];

    [Fact]
    public void EveryLineKind_IsProducedFromARealDemo()
    {
        // The other half of the 19.5%: 47 mutants with no coverage at all, because the player,
        // chat and event branches never ran. A hand-built fixture carrying a userinfo table, a
        // chat message and a game event means writing three interlocking wire formats
        // correctly, and every attempt at that in this project has produced a fixture that
        // parsed to nothing rather than a test that failed usefully. A real demo is cheaper and
        // stronger evidence.
        //
        // Values are checked, not just line kinds. A player line naming nobody, or an event
        // line with no name, is the failure this is for.
        IReadOnlyList<string> corpus = Corpus.Files();
        if (corpus.Count == 0)
        {
            return;                                  // corpus not checked out
        }

        // A SourceTV demo by preference: it carries a full roster, where a POV demo of a solo
        // listen server names one player and would make "player lines exist" a weaker claim.
        string path = corpus.FirstOrDefault(
            p => Path.GetFileName(p).Contains("stv", StringComparison.Ordinal)) ?? corpus[0];

        byte[] bytes = File.ReadAllBytes(path);
        DemoHeader header = DemoHeader.Parse(bytes);
        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)).Take(3000)];

        StringWriter writer = new() { NewLine = "\n" };
        DemoJsonLinesWriter.Write(writer, Path.GetFileName(path), header, commands);

        List<JsonDocument> lines = Lines(writer.ToString()).ToList();
        Dictionary<string, int> kinds = [];
        foreach (JsonDocument line in lines)
        {
            string kind = line.RootElement.GetProperty("type").GetString()!;
            kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
        }

        kinds.ShouldContainKey("header");
        kinds.ShouldContainKey("player");
        kinds.ShouldContainKey("event");

        JsonElement player = lines
            .First(l => l.RootElement.GetProperty("type").GetString() == "player").RootElement;
        player.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        player.GetProperty("userId").GetInt32().ShouldBeGreaterThanOrEqualTo(0);

        JsonElement fired = lines
            .First(l => l.RootElement.GetProperty("type").GetString() == "event").RootElement;
        fired.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        fired.GetProperty("tick").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
    }
}
