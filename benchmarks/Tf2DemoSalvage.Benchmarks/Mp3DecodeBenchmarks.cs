using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using BenchmarkDotNet.Attributes;

using NLayer;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Benchmarks;

/// <summary>
/// How long NLayer takes to decode a real TF2 voice line to PCM.
/// </summary>
/// <remarks>
/// **This exists to decide whether the question of a faster decoder needs asking at all.** The
/// choice is between NLayer (managed), Media Foundation through COM (Windows only), and a portable
/// C decoder such as dr_mp3 behind a C ABI. The last two are real work — a COM source reader, or a
/// native build step in a project that already fights one for celt and speex — so the cheap move is
/// to measure the easy option first and find out whether the others are worth building.
///
/// `CLAUDE.md` is explicit that native code needs profiling to justify rather than assumption, and
/// that C# comes first for everything including the performance-sensitive parts.
///
/// **What matters is not throughput.** TF2's voice lines are one to three seconds of mono 44.1 kHz,
/// and a match fires a handful a second at most; no plausible decoder is short of throughput for
/// that. What could hurt is LATENCY at the moment a sound starts — a hitch when a line begins — and
/// that is what the per-clip figure below measures.
///
/// It is also the number that decides whether caching is enough on its own: a decoded voice line is
/// replayed constantly, so if the first decode is cheap the decoder stops mattering after it.
///
/// **Reads from the user's installed game**, like everything else here; nothing is committed and
/// the benchmark skips when TF2 is absent rather than measuring an empty array.
/// </remarks>
[MemoryDiagnoser]
public class Mp3DecodeBenchmarks
{
    /// <summary>The Steam libraries a TF2 install is looked for in.</summary>
    private static readonly string[] LibraryRoots =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common\Team Fortress 2\tf",
        @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
        @"D:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
    ];

    private byte[] _mp3 = [];
    private float[] _samples = [];

    /// <summary>
    /// Which clip to decode: a short response, a long announcer line, and a music track.
    /// </summary>
    /// <remarks>
    /// Three lengths on purpose. A decoder's fixed cost per file and its cost per sample are
    /// different quantities, and one clip cannot separate them — which matters here because the
    /// worry is a hitch at sound START, where the fixed cost dominates.
    /// </remarks>
    [Params("short", "long", "music")]
    public string Clip { get; set; } = string.Empty;

    [GlobalSetup]
    public void Load()
    {
        string tf = GameFolder
            ?? throw new InvalidOperationException(
                "Team Fortress 2 is not installed. This benchmark reads real voice lines out of "
                + "the game's VPKs; measuring anything else would not answer the question.");

        List<(string Path, long Size)> candidates = [];

        foreach (string name in new[] { "tf2_sound_vo_english_dir.vpk", "tf2_sound_misc_dir.vpk" })
        {
            string directory = Path.Combine(tf, name);

            if (!File.Exists(directory))
            {
                continue;
            }

            VpkArchive archive = VpkArchive.Open(directory);

            foreach (string path in archive.Paths)
            {
                if (path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) &&
                    archive.TryFind(path, out VpkEntry entry))
                {
                    candidates.Add((path, entry.Size));
                }
            }

            // Chosen by SIZE rather than by name, so the three clips are genuinely different
            // lengths rather than three files that happen to sound different.
            candidates.Sort((left, right) => left.Size.CompareTo(right.Size));

            string wanted = Clip switch
            {
                "short" => candidates[candidates.Count / 10].Path,
                "long" => candidates[candidates.Count * 9 / 10].Path,
                _ => candidates[^1].Path,
            };

            _mp3 = archive.ReadFile(wanted)
                ?? throw new InvalidOperationException($"{wanted} could not be read.");

            // Sized once, outside the measured region: allocating a fresh buffer per iteration
            // would put the allocator in the measurement rather than the decoder.
            _samples = new float[Math.Max(1, _mp3.Length * 64)];

            return;
        }

        throw new InvalidOperationException("no sound VPK was found.");
    }

    /// <summary>Decodes the whole clip to float PCM, which is what a mixer consumes.</summary>
    [Benchmark]
    public int DecodeWhole()
    {
        using MemoryStream stream = new(_mp3, writable: false);
        using MpegFile file = new(stream);

        int total = 0;
        int read;

        while ((read = file.ReadSamples(_samples, 0, _samples.Length)) > 0)
        {
            total += read;
        }

        return total;
    }

    /// <summary>Opening the file and decoding only the first buffer — the start-of-sound cost.</summary>
    /// <remarks>
    /// **The latency figure, and the one that decides whether a decoder can hitch.** A mixer that
    /// streams a sound only needs the first buffer before it can begin; everything after it is
    /// hidden behind playback. So this separates "can it start in time" from "can it keep up",
    /// which the whole-clip number conflates.
    /// </remarks>
    [Benchmark]
    public int DecodeFirstBuffer()
    {
        using MemoryStream stream = new(_mp3, writable: false);
        using MpegFile file = new(stream);

        return file.ReadSamples(_samples, 0, Math.Min(4096, _samples.Length));
    }

    /// <summary>Where the game is, when it is installed.</summary>
    private static string? GameFolder
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("TF2_FOLDER");

            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            {
                return configured;
            }

            return LibraryRoots.FirstOrDefault(
                root => File.Exists(Path.Combine(root, "tf2_textures_dir.vpk")));
        }
    }
}
