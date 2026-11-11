using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Every SILK frame of a demo's Steam Voice through one decoder per speaker, and what a rejected one is (B611).</summary>
/// <remarks>
/// At a rejection it reports the frame's place, length and first bytes, the lengths of the frames before it, and whether a
/// FRESH decoder takes the same bytes: a fresh decoder accepting them says the stream state was the fault, not the frame.
/// The control is the count of frames decoded before it.
/// </remarks>
public sealed class SilkPacketsProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "silk-packets";

    /// <inheritdoc/>
    public string Summary => "a demo's SILK frames per speaker; at a rejection, its place, bytes and whether a fresh decoder takes it: silk-packets <demo>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1)
        {
            output.WriteLine(Summary);
            return;
        }

        byte[] bytes = File.ReadAllBytes(arguments[0]);
        NetDecodeState state = new() { NetworkProtocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol };
        Dictionary<ulong, SilkVoiceDecoder> speakers = [];
        Dictionary<ulong, List<int>> lengths = [];
        int packets = 0;
        int frames = 0;
        int rejected = 0;
        Dictionary<byte, int> firstBytes = [];

        try
        {
            foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
            {
                if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
                {
                    continue;
                }

                foreach (INetMessage message in NetMessageReader.Read(command.Payload.Span, state).Messages)
                {
                    if (message is not VoiceDataMessage voice || voice.BodyBits <= 0 ||
                        !SteamVoicePayload.TryDecode(voice.Body.Span, out VoicePacket? packet))
                    {
                        continue;
                    }

                    packets++;

                    if (!speakers.TryGetValue(packet.SteamId, out SilkVoiceDecoder? decoder))
                    {
                        decoder = new SilkVoiceDecoder();
                        speakers[packet.SteamId] = decoder;
                        lengths[packet.SteamId] = [];
                    }

                    for (int index = 0; index < packet.Chunks.Count; index++)
                    {
                        ReadOnlySpan<byte> data = packet.Chunks[index].Data.Span;
                        List<int> before = lengths[packet.SteamId];

                        if (data.Length > 0)
                        {
                            firstBytes[data[0]] = firstBytes.GetValueOrDefault(data[0]) + 1;
                        }

                        try
                        {
                            decoder.Decode(data);
                            frames++;
                        }
                        catch (InvalidOperationException failure)
                        {
                            rejected++;
                            string fresh;

                            try
                            {
                                using SilkVoiceDecoder clean = new();
                                fresh = $"fresh decoder: {clean.Decode(data).Length} samples";
                            }
                            catch (InvalidOperationException again)
                            {
                                fresh = $"fresh decoder: {again.Message}";
                            }
                            output.WriteLine(string.Create(
                                CultureInfo.InvariantCulture,
                                $"tick {command.Tick} packet {packets} frame {index}/{packet.Chunks.Count} of speaker {packet.SteamId}: {failure.Message}"));
                            output.WriteLine($"  length {data.Length}, first bytes {Convert.ToHexString(data[..Math.Min(16, data.Length)])}; packet rate {packet.SampleRate}; {fresh}");
                            output.WriteLine($"  the {Math.Min(12, before.Count)} frames before: {string.Join(' ', before.TakeLast(12))}; terminated {packet.IsTerminated}");

                            // Carry on from a clean state, as a new talk burst would.
                            decoder.Dispose();
                            decoder = new SilkVoiceDecoder();
                            speakers[packet.SteamId] = decoder;
                        }

                        before.Add(data.Length);
                    }
                }
            }
        }
        finally
        {
            foreach (SilkVoiceDecoder decoder in speakers.Values)
            {
                decoder.Dispose();
            }
        }

        output.WriteLine($"{packets} Steam Voice packets, {frames} SILK frames decoded, {rejected} rejected, {speakers.Count} speakers");
        output.WriteLine("first bytes: " + string.Join(' ', firstBytes.OrderByDescending(pair => pair.Value).Take(8).Select(pair => $"{pair.Key:X2}x{pair.Value}")));
    }
}
