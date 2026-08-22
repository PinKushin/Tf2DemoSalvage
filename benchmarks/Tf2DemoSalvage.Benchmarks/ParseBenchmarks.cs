using System;
using System.IO;
using System.Linq;

using BenchmarkDotNet.Attributes;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Benchmarks;

/// <summary>
/// The parse, staged — container walk, net message decode, full timeline.
/// </summary>
/// <remarks>
/// **Staged rather than one number, because one number cannot attribute a regression.** The same
/// argument as the rendering ladder: "the parse got slower" tells you nothing, while "the container
/// walk is unchanged and the timeline build doubled" names the layer. Each stage here includes the
/// ones below it, so the differences between them are the costs.
///
/// **The file is read once, in setup, and the bytes are reused.** Timing `File.ReadAllBytes`
/// alongside the parse would measure the page cache, which is neither stable nor interesting — and
/// on a 40 MB demo it would dominate the cheap stages entirely.
///
/// **Run locally, never on the measurement boxes.** `CLAUDE.md` is explicit that shared cloud vCPUs
/// are too noisy for BenchmarkDotNet; the Oracle boxes take mutation and fuzzing, which tolerate a
/// noisy neighbour because they count survivors and crashes rather than nanoseconds.
/// </remarks>
[MemoryDiagnoser]
public class ParseBenchmarks
{
    private byte[] _demo = [];

    /// <summary>Which corpus demo to measure.</summary>
    /// <remarks>
    /// Two sizes on purpose. A 2-minute era specimen and a full modern match differ by more than a
    /// constant factor — the entity delta path scales with player count and tick count together —
    /// so a single file cannot show a change that only appears at length.
    /// </remarks>
    [Params("tf2-2013-build1729296-stv-cp_foundry.dem", "demostf-cp_process_f12-2026-08-07.dem")]
    public string Demo { get; set; } = string.Empty;

    [GlobalSetup]
    public void Load()
    {
        string root = Repository();

        foreach (string folder in new[] { "tools/corpus/demos", "tools/corpus/local" })
        {
            string path = Path.Combine(root, folder, Demo);

            if (File.Exists(path))
            {
                _demo = File.ReadAllBytes(path);
                return;
            }
        }

        throw new FileNotFoundException(
            $"{Demo} is in neither corpus folder. A benchmark with no input measures nothing, so "
            + "this throws rather than reporting a time for an empty array.");
    }

    /// <summary>The header alone: 1,072 bytes, and the floor everything else sits on.</summary>
    [Benchmark(Baseline = true)]
    public int Header() => DemoHeader.Parse(_demo).PlaybackTicks;

    /// <summary>Walking every command without decoding any payload.</summary>
    /// <remarks>
    /// The container layer on its own — command headers read, payloads stepped over. What
    /// <c>DemoSurvey</c> does when a header declares no length.
    /// </remarks>
    [Benchmark]
    public int Commands() =>
        DemoCommandReader.Read(_demo.AsMemory(DemoHeader.SizeBytes)).Count();

    /// <summary>Decoding every net message in every packet, without building scene state.</summary>
    /// <remarks>
    /// **This is the stage comparable to the Rust parser's own benchmark.** Its `AllMessages`
    /// handler visits every message and black-boxes it, which is "parse everything, build nothing" —
    /// so a comparison against `Timeline` below would be measuring different work and flattering
    /// whichever side did less.
    /// </remarks>
    [Benchmark]
    public int Messages()
    {
        NetDecodeState state = new()
        {
            NetworkProtocol = (ushort)DemoHeader.Parse(_demo).NetworkProtocol,
        };
        int messages = 0;

        foreach (DemoCommand command in DemoCommandReader.Read(_demo.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            messages += NetMessageReader.Read(command.Payload.Span, state).Messages.Count;
        }

        return messages;
    }

    /// <summary>The whole thing: entity state, poses, everything a viewer needs.</summary>
    [Benchmark]
    public int Timeline() => DemoTimeline.Build(_demo).FogSamples.Count;

    /// <summary>Walks up from the binary to the repository root.</summary>
    private static string Repository()
    {
        DirectoryInfo? at = new(AppContext.BaseDirectory);

        while (at is not null && !File.Exists(Path.Combine(at.FullName, "Tf2DemoSalvage.slnx")))
        {
            at = at.Parent;
        }

        return at?.FullName ?? throw new DirectoryNotFoundException("repository root not found");
    }
}
