using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The three corpus checks the scripted era specimens tripped, each reported in full rather than stopped at the
/// first failure: net_Tick offset per packet, Speex silence per demo/speaker/run, and every game-event re-encode
/// mismatch across the corpus.
/// </summary>
/// <remarks>
/// Asserts nothing (D38). Each mode reads through the production reader and writer, the same calls the corpus tests
/// make. Controls: <c>speex</c> prints the encoded bytes of the frames it calls silent, so a decoder that zeroes a
/// real frame is told apart from an encoder that sent a silent one; <c>events</c> counts the messages it CHECKED per
/// demo beside the mismatches, so an empty mismatch list over zero messages is visible.
/// </remarks>
public sealed class SpecimenTriageProbe : IProbe
{
    private const int SpeexFrameBytes = 28;

    /// <inheritdoc/>
    public string Name => "specimen-triage";

    /// <inheritdoc/>
    public string Summary =>
        "net_Tick offsets, Speex silence and game-event re-encode mismatches: specimen-triage nettick <demo> | speex [demo] | events [demo]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        string mode = arguments.Count > 0 ? arguments[0] : "";
        IReadOnlyList<string> demos = arguments.Count > 1 ? [arguments[1]] : DemoCorpus.Files(output);

        switch (mode)
        {
            case "nettick":
                foreach (string demo in demos)
                {
                    NetTicks(output, demo);
                }

                break;
            case "speex":
                foreach (string demo in demos)
                {
                    Speex(output, demo);
                }

                break;
            case "speex-encode":
                SpeexEncodeControl(output);
                break;
            case "events":
                foreach (string demo in demos)
                {
                    Events(output, demo);
                }

                break;
            default:
                output.WriteLine(Summary);
                break;
        }
    }

    private static IEnumerable<(DemoCommand Command, NetMessageReadResult Result)> Walk(byte[] bytes, NetDecodeState state)
    {
        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type is DemoCommandType.Signon or DemoCommandType.Packet)
            {
                yield return (command, NetMessageReader.Read(command.Payload.Span, state));
            }
        }
    }

    private static void NetTicks(TextWriter output, string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        NetDecodeState state = new() { NetworkProtocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol };
        output.WriteLine($"== {Path.GetFileName(path)}");
        output.WriteLine("  pkt#  demoTick  netTick  offset  dDemo  dNet  messages");

        int packetIndex = -1;
        int previousDemo = int.MinValue;
        int previousNet = int.MinValue;
        List<(int DDemo, int DNet, float Frame)> level = [];
        List<(int DDemo, int DNet, float Frame)> lagging = [];
        List<(int DDemo, int DNet, float Frame)> other = [];
        List<List<int>> runs = [[]];
        List<List<int>> clampedRuns = [[]];
        List<List<int>> divergedRuns = [[]];
        int previousIndex = int.MinValue;
        int consecutiveLag = 0;
        int consecutiveLagDNet = 0;
        float consecutiveLagMinFrame = float.MaxValue;

        foreach ((DemoCommand command, NetMessageReadResult result) in Walk(bytes, state))
        {
            if (command.Type != DemoCommandType.Packet)
            {
                continue;
            }

            packetIndex++;
            foreach (SetPauseMessage pauseMessage in result.Messages.OfType<SetPauseMessage>())
            {
                output.WriteLine($"  PAUSE packet {packetIndex} demo tick {command.Tick}: SetPause({pauseMessage.Paused}), first message {(result.Messages.Count > 0 ? result.Messages[0].Type : null)}");
            }

            NetTickMessage? tick = result.Messages.Count > 0 ? result.Messages[0] as NetTickMessage : null;
            if (tick is null)
            {
                continue;
            }

            int dDemo = previousDemo == int.MinValue ? 0 : command.Tick - previousDemo;
            int dNet = previousNet == int.MinValue ? 0 : tick.Tick - previousNet;
            bool pause = result.Messages.Any(message => message is SetPauseMessage);

            // Every row for the first 200 packets (what the test samples); after that only rows where the two clocks
            // did not advance together, or a pause arrived.
            if (packetIndex < 200 || dDemo != dNet || pause)
            {
                string types = string.Join(',', result.Messages.Select(m => m switch
                {
                    SetPauseMessage p => $"SetPause({p.Paused})",
                    _ => m.Type.ToString(),
                }).Distinct());
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {packetIndex,4}  {command.Tick,8}  {tick.Tick,7}  {tick.Tick - command.Tick,6}  {dDemo,5}  {dNet,4}  " +
                    $"hostframe {tick.HostFrameTimeSeconds * 1000,6:F1} ms  {types}"));
            }

            // The corpus test's own statistic: first 200 packets, split where the demo clock jumps more than 32.
            if (packetIndex < 200)
            {
                if (previousDemo != int.MinValue && dDemo > 32)
                {
                    runs.Add([]);
                }

                runs[^1].Add(tick.Tick - command.Tick);

                // The proposed classifier: a host frame over 100 ms also starts a new run.
                if (previousDemo != int.MinValue && (dDemo > 32 || tick.HostFrameTimeSeconds > 0.1f))
                {
                    clampedRuns.Add([]);
                }

                clampedRuns[^1].Add(tick.Tick - command.Tick);

                // The narrower form: break only where the frame was over 100 ms AND the demo clock outran the
                // server's by more than the usual one-tick jitter in that packet.
                if (previousDemo != int.MinValue && (dDemo > 32 || (tick.HostFrameTimeSeconds > 0.1f && dDemo - dNet >= 2)))
                {
                    divergedRuns.Add([]);
                }

                divergedRuns[^1].Add(tick.Tick - command.Tick);
            }

            if (previousDemo != int.MinValue && packetIndex == previousIndex + 1 && dDemo - dNet >= 3)
            {
                consecutiveLagDNet = Math.Max(consecutiveLagDNet, dNet);
                consecutiveLagMinFrame = Math.Min(consecutiveLagMinFrame, tick.HostFrameTimeSeconds * 1000);
                consecutiveLag++;
            }

            previousIndex = packetIndex;

            if (previousDemo != int.MinValue && dDemo <= 32)
            {
                // Lagging = the demo clock ran at least 3 ticks ahead of the server's within one packet.
                List<(int DDemo, int DNet, float Frame)> bucket = dDemo == dNet ? level : other;
                (dDemo - dNet >= 3 ? lagging : bucket).Add((dDemo, dNet, tick.HostFrameTimeSeconds * 1000));
            }

            previousDemo = command.Tick;
            previousNet = tick.Tick;
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  SUMMARY {Path.GetFileName(path)}: in-step packets {level.Count} (host frame mean {Mean(level, x => x.Frame):F1} ms, max {Max(level, x => x.Frame):F1}); " +
            $"lagging packets {lagging.Count} (dNet max {Max(lagging, x => x.DNet):F0}, dDemo max {Max(lagging, x => x.DDemo):F0}, " +
            $"host frame mean {Mean(lagging, x => x.Frame):F1} ms, min {(lagging.Count == 0 ? 0 : lagging.Min(x => x.Frame)):F1}, max {Max(lagging, x => x.Frame):F1}); " +
            $"total server-behind-demo drift in lagging packets {lagging.Sum(x => x.DDemo - x.DNet)} ticks; other {other.Count}"));
        List<int> longest = runs.OrderByDescending(run => run.Count).First();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  TEST-STATISTIC {Path.GetFileName(path)}: longest run {longest.Count} packets, spread {(longest.Count == 0 ? 0 : longest.Max() - longest.Min())}, gaps {runs.Count - 1}"));
        List<int> clampedLongest = clampedRuns.OrderByDescending(run => run.Count).First();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  WITH-FRAME-SPLIT {Path.GetFileName(path)}: longest run {clampedLongest.Count} packets, spread {(clampedLongest.Count == 0 ? 0 : clampedLongest.Max() - clampedLongest.Min())}, runs {clampedRuns.Count}; " +
            $"consecutive-packet lag rows {consecutiveLag}, their dNet max {consecutiveLagDNet}, host frame min {(consecutiveLag == 0 ? 0 : consecutiveLagMinFrame):F1} ms"));
        List<int> divergedLongest = divergedRuns.OrderByDescending(run => run.Count).First();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  WITH-DIVERGED-SPLIT {Path.GetFileName(path)}: longest run {divergedLongest.Count} packets, spread {(divergedLongest.Count == 0 ? 0 : divergedLongest.Max() - divergedLongest.Min())}, runs {divergedRuns.Count}"));
    }

    private static double Mean(List<(int DDemo, int DNet, float Frame)> rows, Func<(int DDemo, int DNet, float Frame), double> value) =>
        rows.Count == 0 ? 0 : rows.Average(value);

    private static double Max(List<(int DDemo, int DNet, float Frame)> rows, Func<(int DDemo, int DNet, float Frame), double> value) =>
        rows.Count == 0 ? 0 : rows.Max(value);

    private static void Speex(TextWriter output, string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        NetDecodeState state = new() { NetworkProtocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol };
        string? codec = null;
        int quality = -1;
        List<(int Tick, int Client, byte[] Body)> packets = [];

        foreach ((DemoCommand command, NetMessageReadResult result) in Walk(bytes, state))
        {
            foreach (INetMessage message in result.Messages)
            {
                if (message is VoiceInitMessage init)
                {
                    codec = init.Codec;
                    quality = init.Quality;
                }
                else if (message is VoiceDataMessage voice && voice.BodyBits > 0)
                {
                    packets.Add((command.Tick, voice.Client, voice.Body.ToArray()));
                }
            }
        }

        if (codec != "vaudio_speex" || packets.Count == 0)
        {
            output.WriteLine($"== {Path.GetFileName(path)}: codec {codec ?? "none"}, {packets.Count} voice packets - skipped");
            return;
        }

        output.WriteLine($"== {Path.GetFileName(path)}: {codec} q{quality}, {packets.Count} packets");

        Dictionary<int, SpeexVoiceDecoder> perClient = [];
        using SpeexVoiceDecoder shared = new();
        int frames = 0, silentShared = 0, silentPerClient = 0, silentFresh = 0, zeroBytes = 0, steam = 0;
        Dictionary<string, int> silentHeads = new(StringComparer.Ordinal);
        Dictionary<string, int> audibleHeads = new(StringComparer.Ordinal);
        StringBuilder pattern = new();
        HashSet<string> distinctFrames = new(StringComparer.Ordinal);
        long crossings = 0;
        int peak = 0;
        int lastTick = int.MinValue;
        int runStart = 0, runFrames = 0, runSilent = 0, runIndex = 0;

        try
        {
            foreach ((int tick, int client, byte[] body) in packets)
            {
                if (SteamVoicePayload.TryDecode(body, out _))
                {
                    steam++;
                    continue;
                }

                if (lastTick != int.MinValue && tick - lastTick > 10)
                {
                    FlushRun(output, runIndex++, runStart, lastTick, runFrames, runSilent, pattern);
                    runFrames = runSilent = 0;
                    pattern.Clear();
                }

                if (runFrames == 0)
                {
                    runStart = tick;
                }

                lastTick = tick;

                if (!perClient.TryGetValue(client, out SpeexVoiceDecoder? mine))
                {
                    mine = new SpeexVoiceDecoder();
                    perClient[client] = mine;
                }

                using SpeexVoiceDecoder fresh = new();
                pattern.Append('|');

                for (int at = 0; at + SpeexFrameBytes <= body.Length; at += SpeexFrameBytes)
                {
                    ReadOnlySpan<byte> frame = body.AsSpan(at, SpeexFrameBytes);
                    short[] pcm = shared.Decode(frame);
                    bool silent = IsSilent(pcm);
                    distinctFrames.Add(Convert.ToHexString(frame));

                    if (!silent)
                    {
                        crossings += ZeroCrossings(pcm);
                        peak = Math.Max(peak, pcm.Max(sample => Math.Abs((int)sample)));
                    }

                    silentPerClient += IsSilent(mine.Decode(frame)) ? 1 : 0;
                    silentFresh += IsSilent(fresh.Decode(frame)) ? 1 : 0;
                    frames++;
                    runFrames++;
                    string head = Convert.ToHexString(frame[..4]);

                    if (frame.IndexOfAnyExcept((byte)0) < 0)
                    {
                        zeroBytes++;
                    }

                    if (silent)
                    {
                        silentShared++;
                        runSilent++;
                        silentHeads[head] = silentHeads.GetValueOrDefault(head) + 1;
                    }
                    else
                    {
                        audibleHeads[head] = audibleHeads.GetValueOrDefault(head) + 1;
                    }

                    pattern.Append(silent ? 'S' : '.');
                }
            }

            FlushRun(output, runIndex, runStart, lastTick, runFrames, runSilent, pattern);
        }
        finally
        {
            foreach (SpeexVoiceDecoder decoder in perClient.Values)
            {
                decoder.Dispose();
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  frames {frames}; silent: one decoder per demo {silentShared}, per client {silentPerClient}, fresh per packet {silentFresh}; " +
            $"all-zero encoded frames {zeroBytes}; clients [{string.Join(',', packets.Select(p => p.Client).Distinct())}]; steam packets {steam}; " +
            $"packet sizes [{string.Join(',', packets.GroupBy(p => p.Body.Length).Select(g => $"{g.Key}x{g.Count()}"))}]"));
        int audible = frames - silentShared;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  distinct encoded frames {distinctFrames.Count}; audible frames: peak |sample| {peak}, " +
            $"zero crossings per 160-sample frame {(audible == 0 ? 0 : (double)crossings / audible):F1} " +
            $"(a 440 Hz tone at 8 kHz is 17.6, 660 Hz is 26.4)"));
        if (distinctFrames.Count == 1)
        {
            output.WriteLine($"  the one encoded frame: {distinctFrames.First()}");
        }

        output.WriteLine($"  silent frame heads (top 6):{string.Join(' ', silentHeads.OrderByDescending(e => e.Value).Take(6).Select(e => $"{e.Key}x{e.Value}"))}");
        output.WriteLine($"  audible frame heads (top 6): {string.Join(' ', audibleHeads.OrderByDescending(e => e.Value).Take(6).Select(e => $"{e.Key}x{e.Value}"))}");
    }

    private static void FlushRun(TextWriter output, int index, int start, int end, int frames, int silent, StringBuilder pattern)
    {
        string text = pattern.ToString();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  run {index}: ticks {start}..{end}, {frames} frames, {silent} silent  {(text.Length > 400 ? text[..400] + "..." : text)}"));
    }

    private static bool IsSilent(short[] pcm) => Array.TrueForAll(pcm, sample => sample == 0);

    /// <summary>
    /// The encoder side, as a control for the decoder: libspeex narrowband at quality 5 (what every specimen's
    /// svc_VoiceInit declares) fed digital silence and then a 440 Hz tone, each frame decoded back.
    /// </summary>
    private static void SpeexEncodeControl(TextWriter output)
    {
        nint library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "speex.dll"));
        GetModeFn getMode = Export<GetModeFn>(library, "speex_lib_get_mode");
        EncoderInitFn init = Export<EncoderInitFn>(library, "speex_encoder_init");
        EncoderCtlFn ctl = Export<EncoderCtlFn>(library, "speex_encoder_ctl");
        EncodeFn encode = Export<EncodeFn>(library, "speex_encode_int");
        BitsFn bitsInit = Export<BitsFn>(library, "speex_bits_init");
        BitsFn bitsReset = Export<BitsFn>(library, "speex_bits_reset");
        BitsWriteFn bitsWrite = Export<BitsWriteFn>(library, "speex_bits_write");

        foreach (string input in new[] { "silence", "tone440" })
        {
            nint encoder = init(getMode(0));
            int quality = 5;
            _ = ctl(encoder, 4, ref quality); // SPEEX_SET_QUALITY
            byte[] bits = new byte[64]; // a SpeexBits (48 bytes on x64); it points at its own native buffer, so the array may move
            bitsInit(bits);
            using SpeexVoiceDecoder decoder = new();
            short[] pcm = new short[160];
            byte[] frame = new byte[SpeexFrameBytes];
            int silent = 0;
            int peak = 0;
            long crossings = 0;
            HashSet<string> distinct = new(StringComparer.Ordinal);

            for (int n = 0; n < 50; n++)
            {
                for (int i = 0; i < pcm.Length; i++)
                {
                    pcm[i] = input == "silence" ? (short)0 : (short)Math.Round(16383 * Math.Sin(2 * Math.PI * 440 * ((n * 160) + i) / 8000.0));
                }

                bitsReset(bits);
                _ = encode(encoder, pcm, bits);
                _ = bitsWrite(bits, frame, frame.Length);

                short[] decoded = decoder.Decode(frame);
                silent += IsSilent(decoded) ? 1 : 0;
                crossings += ZeroCrossings(decoded);
                peak = Math.Max(peak, decoded.Max(sample => Math.Abs((int)sample)));
                distinct.Add(Convert.ToHexString(frame));

                if (n is < 3 or 49)
                {
                    output.WriteLine($"  {input} frame {n}: {Convert.ToHexString(frame)}");
                }
            }

            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {input}: 50 frames, {silent} decode all-zero, {distinct.Count} distinct, zero crossings per frame {crossings / 50.0:F1}, peak |sample| {peak} (input peak 16383)"));
        }
    }

    private static T Export<T>(nint library, string name)
        where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint GetModeFn(int mode);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint EncoderInitFn(nint mode);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EncoderCtlFn(nint state, int request, ref int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EncodeFn(nint state, short[] pcm, byte[] bits);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BitsFn(byte[] bits);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int BitsWriteFn(byte[] bits, byte[] buffer, int max);

    private static int ZeroCrossings(short[] pcm)
    {
        int count = 0;
        for (int i = 1; i < pcm.Length; i++)
        {
            if ((pcm[i - 1] < 0) != (pcm[i] < 0))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The corpus round-trip test's walk, verbatim in intent: one message at a time, a write state that sees what the read state saw.</summary>
    private static void Events(TextWriter output, string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        ushort protocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol;
        NetDecodeState readState = new() { NetworkProtocol = protocol };
        NetDecodeState writeState = new() { NetworkProtocol = protocol };
        int checkedMessages = 0;
        int listCount = 0;
        Dictionary<string, int> mismatches = new(StringComparer.Ordinal);
        Dictionary<string, int> printed = new(StringComparer.Ordinal);

        // The whole demo, not the corpus test's first 1,500 commands: this reports every mismatch, not the first.
        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            byte[] payload = command.Payload.ToArray();
            NetMessageReadResult result = NetMessageReader.Read(payload, readState);
            writeState.MessageTypeBits = readState.MessageTypeBits;

            for (int i = 0; i < result.Messages.Count; i++)
            {
                INetMessage message = result.Messages[i];
                int start = result.MessageStartBits[i];
                int end = i + 1 < result.Messages.Count ? result.MessageStartBits[i + 1] : result.BitsConsumed;

                if (NetMessageWriter.CanWrite(message))
                {
                    BitWriter writer = new();
                    bool wrote = NetMessageWriter.TryWrite(writer, message, writeState);
                    checkedMessages++;
                    bool same = wrote && writer.BitCount == end - start &&
                        BitsAt(payload, start, end - start).AsSpan().SequenceEqual(BitsAt(writer.Build(), 0, end - start));

                    if (!same)
                    {
                        string key = message is GameEventMessage ge ? $"GameEvent {ge.Name}" : message.Type.ToString();
                        mismatches[key] = mismatches.GetValueOrDefault(key) + 1;

                        if (printed.GetValueOrDefault(key) < 3)
                        {
                            printed[key] = printed.GetValueOrDefault(key) + 1;
                            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                                $"  {Path.GetFileName(path)} tick {command.Tick} {key} at bit {start}: read {end - start}, wrote {(wrote ? writer.BitCount : -1)}"));

                            if (message is GameEventMessage gameEvent)
                            {
                                GameEventDefinition readDef = readState.EventDefinitions[gameEvent.EventId];
                                GameEventDefinition writeDef = writeState.EventDefinitions[gameEvent.EventId];
                                output.WriteLine($"    id {gameEvent.EventId} bodyBits {gameEvent.BodyBits}; read-side def '{readDef.Name}' [{string.Join(", ", readDef.Fields.Select(f => $"{f.Name}:{f.Type}"))}]");
                                output.WriteLine($"    write-side def '{writeDef.Name}' [{string.Join(", ", writeDef.Fields.Select(f => $"{f.Name}:{f.Type}"))}]");
                                output.WriteLine($"    values {{{string.Join(", ", gameEvent.Values.Select(v => $"{v.Key}={Show(v.Value)}"))}}}");
                                output.WriteLine($"    wire bits {Hex(payload, start, end - start)}");
                            }
                        }
                    }
                }

                if (message is ServerInfoMessage info)
                {
                    writeState.ServerInfo = info;
                }
                else if (message is GameEventListMessage list)
                {
                    listCount++;
                    writeState.AddEventDefinitions(list.Definitions);
                }
                else if (message is CreateStringTableMessage table)
                {
                    writeState.AddStringTable(table.Name, table.MaxEntries);
                }
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"== {Path.GetFileName(path)}: {checkedMessages} writable messages checked, {listCount} event lists, mismatches: " +
            $"{(mismatches.Count == 0 ? "none" : string.Join("; ", mismatches.Select(e => $"{e.Key} x{e.Value}")))}"));
    }

    private static string Show(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"({s.Length})",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    private static string Hex(byte[] source, int startBit, int bits) => Convert.ToHexString(BitsAt(source, startBit, bits));

    private static byte[] BitsAt(byte[] source, int startBit, int bits)
    {
        BitWriter writer = new();
        for (int i = 0; i < bits; i++)
        {
            int bit = startBit + i;
            writer.Write((uint)((source[bit / 8] >> (bit % 8)) & 1), 1);
        }

        return writer.Build();
    }
}
