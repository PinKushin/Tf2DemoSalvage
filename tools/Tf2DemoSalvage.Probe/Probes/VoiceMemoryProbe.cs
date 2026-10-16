using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What demo voice costs in memory: the plan, and the renders playback holds.</summary>
/// <remarks>
/// **Given a directory, it first ranks the demos by voice bits**, read with the message reader alone (no timeline), and
/// measures the heaviest. The plan holds only the bodies the timeline already holds; a render is mono float samples,
/// held while its spurt is pending, starved or playing — at most five channels' worth plus what is queued.
///
/// **Its control**: it prints the packet count it planned from; a plan of nothing would cost nothing.
/// It asserts nothing — a measurement is not a test (D38).
/// </remarks>
public sealed class VoiceMemoryProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "voice-memory";

    /// <inheritdoc/>
    public string Summary => "heap of a demo's voice plan and the size of its renders; given a directory, the heaviest voice demo in it: voice-memory <demo|dir>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("voice-memory <demo|dir>");
            return;
        }

        string? path = Directory.Exists(arguments[0]) ? Heaviest(arguments[0], output) : DemoCorpus.Find(arguments[0], output);

        if (path is null)
        {
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        // Once to load the codec libraries' availability, then timed: the viewer lays this out on the frame thread.
        _ = VoicePlan.From(timeline.VoiceInits, timeline.Voice, timeline.IntervalPerTick);
        long before = DemoCensus.LiveHeap();
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        VoicePlanResult plan = VoicePlan.From(timeline.VoiceInits, timeline.Voice, timeline.IntervalPerTick);
        TimeSpan took = System.Diagnostics.Stopwatch.GetElapsedTime(started);
        long after = DemoCensus.LiveHeap();
        output.WriteLine($"  plan time        {took.TotalMilliseconds:0.0} ms");

        long[] renders = [.. plan.Spurts.Select(spurt => (long)spurt.Length * sizeof(float))];
        long bodies = timeline.Voice.Sum(voice => (long)voice.Body.Length);

        output.WriteLine($"{Path.GetFileName(path)}: {timeline.Voice.Count} packets ({bodies:N0} body bytes, held by the timeline), {plan.Spurts.Count} spurts");
        output.WriteLine($"  plan heap        {after - before:N0} bytes");
        output.WriteLine($"  largest render   {(renders.Length == 0 ? 0 : renders.Max()):N0} bytes");
        output.WriteLine($"  five largest     {renders.OrderByDescending(bytes => bytes).Take(VoicePlan.Channels).Sum():N0} bytes (the most five channels hold at once)");
        output.WriteLine($"  every render     {renders.Sum():N0} bytes (what holding the whole demo's PCM would cost)");
        output.WriteLine($"  refusal          {plan.Refusal ?? "none"}");
        GC.KeepAlive(plan);
    }

    /// <summary>The demo in a directory with the most voice bits, by the message reader alone.</summary>
    private static string? Heaviest(string directory, TextWriter output)
    {
        string? best = null;
        long bestBits = -1;

        foreach (string file in Directory.EnumerateFiles(directory, "*.dem", SearchOption.AllDirectories))
        {
            long bits = 0;

            try
            {
                byte[] demo = File.ReadAllBytes(file);
                DemoHeader header = DemoHeader.Parse(demo.AsSpan(0, DemoHeader.SizeBytes));
                NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

                foreach (DemoCommand command in DemoCommandReader.Read(demo.AsMemory(DemoHeader.SizeBytes)))
                {
                    if (command.Type is not (DemoCommandType.Packet or DemoCommandType.Signon))
                    {
                        continue;
                    }

                    foreach (INetMessage message in NetMessageReader.Read(command.Payload.Span, state).Messages)
                    {
                        if (message is VoiceDataMessage voice)
                        {
                            bits += voice.BodyBits;
                        }
                    }
                }
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or IndexOutOfRangeException)
            {
                output.WriteLine($"  skipped {Path.GetFileName(file)}: {error.GetType().Name}: {error.Message}");
                continue;
            }

            if (bits > bestBits)
            {
                bestBits = bits;
                best = file;
            }
        }

        output.WriteLine($"heaviest voice: {(best is null ? "none" : Path.GetFileName(best))}, {bestBits / 8:N0} bytes of voice");
        return best;
    }
}
