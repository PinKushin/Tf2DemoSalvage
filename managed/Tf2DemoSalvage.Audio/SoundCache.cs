using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Audio;

/// <summary>What a precache produced, which is two numbers because they answer different things.</summary>
/// <param name="Named">How many sounds were asked for.</param>
/// <param name="Decoded">How many of them the install could supply.</param>
/// <param name="Seconds">How long it took.</param>
/// <remarks>
/// The gap between the first two is how many sounds are missing, which is the difference between
/// "the precache worked" and "the precache found nothing to do".
/// </remarks>
public readonly record struct PrecacheResult(int Named, int Decoded, double Seconds);

/// <summary>What the cache holds, by the container each sound came from, and how many decodes playback paid for.</summary>
/// <param name="Sounds">Sounds held.</param>
/// <param name="WaveBytes">Bytes of decoded samples from WAV files.</param>
/// <param name="Mp3Bytes">Bytes of decoded samples from MP3 files.</param>
/// <param name="Misses">Decodes outside a precache, each one inside the frame that asked (finding 80).</param>
public readonly record struct SoundCacheHeld(int Sounds, long WaveBytes, long Mp3Bytes, int Misses);

/// <summary>Decoded sound samples, kept so a name is read and decoded once.</summary>
/// <remarks>
/// **The engine owns the sample cache, and the client reaches it through an interface.**
/// <c>IEngineSound</c> declares <c>PrecacheSound( const char *pSample, bool bPreload, bool
/// bIsUISound )</c>, <c>IsSoundPrecached</c> and <c>PrefetchSound</c> together at
/// <c>src/public/engine/IEngineSound.h:89-91</c>, and game code asks for them rather than holding
/// samples itself — <c>CBaseEntity::PrecacheSound</c> is one line, <c>enginesound-&gt;PrecacheSound(
/// name, true )</c> (<c>SoundEmitterSystem.cpp:1507</c>).
///
/// Ours was <c>MainForm.Sample</c> with three fields beside it, handed to
/// <see cref="SoundscapeSystem"/> and the sound presenter as a delegate (B188, D90).
///
/// **Why a cache at all is the interesting part, and it is a measured one.** A decode is a VPK read
/// plus a full decode of what may be an MP3, and it lands on the thread that draws — so the first
/// play of every new sound in a match is a freeze wherever in playback that sound happens to occur.
/// <see cref="Precache"/> exists to move all of them before playback rather than during it, which is
/// what <c>bPreload: true</c> means in the call above.
/// </remarks>
public sealed class SoundCache
{
    private readonly Dictionary<string, SoundSample?> _cache =
        new(StringComparer.Ordinal);

    private readonly ILogger _audio;
    private int _unopened;
    private bool _precaching;

    /// <summary>How long a single decode may take before it is worth reporting, in seconds.</summary>
    /// <remarks>
    /// **Its own threshold rather than the frame loop's, even though the numbers agree.** A
    /// constant carries no scope: this one is applied to one decode blocking the thread that draws,
    /// and the viewer's frame threshold is applied to a whole frame. Sharing the symbol would tie
    /// two independent judgements together, and changing either would silently move the other.
    /// </remarks>
    public const double StallSeconds = 0.03;

    /// <summary>The most decoded sample bytes held at once; past it the least recently used sound is let go (finding 80).</summary>
    /// <remarks>
    /// **Interpolated: the engine's figure is not in the SDK.** Chosen conservatively, twice what the f12 demo holds once its
    /// WAVs are 16-bit (about 260 MB), so an ordinary match evicts nothing and plays exactly as before; it bounds a demo that
    /// would otherwise keep every sound it ever heard. A sound evicted and asked for again is decoded again, in the frame, and
    /// counted in <see cref="SoundCacheHeld.Misses"/>.
    /// </remarks>
    public const long CapacityBytes = 512L * 1024 * 1024;

    private readonly long _capacity;
    private readonly LinkedList<string> _recency = [];
    private readonly Dictionary<string, (LinkedListNode<string> Node, long Bytes, bool Wave)> _sizes = new(StringComparer.Ordinal);

    /// <summary>Creates an empty cache.</summary>
    /// <param name="audio">Where misses and slow decodes are reported.</param>
    /// <param name="capacityBytes">The most decoded bytes held at once; <see cref="CapacityBytes"/> unless a test says otherwise.</param>
    /// <exception cref="ArgumentNullException"><paramref name="audio"/> is null.</exception>
    public SoundCache(ILogger audio, long capacityBytes = CapacityBytes)
    {
        ArgumentNullException.ThrowIfNull(audio);

        _audio = audio;
        _capacity = capacityBytes;
    }

    /// <summary>Where files come from, set when the game's archives are opened.</summary>
    /// <remarks>
    /// Null until then, and a viewer with no TF2 install keeps it null for its whole life — so
    /// every lookup answers silence rather than each caller checking first.
    /// </remarks>
    public Func<string, byte[]?>? Read { get; set; }

    /// <summary>Starts a level: reads through <paramref name="read"/> from now on, and forgets every earlier decode.</summary>
    /// <param name="read">The level's search path — its pakfile ahead of the install (B485).</param>
    /// <exception cref="ArgumentNullException"><paramref name="read"/> is null.</exception>
    /// <remarks>
    /// **Forgets, because a decode is only true of the level that read it.** A map's pakfile can shadow a stock path, so
    /// a sample decoded on one map is the wrong answer on the next — and a miss cached on a map without the file would
    /// hide it on one that ships it.
    /// </remarks>
    public void Level(Func<string, byte[]?> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        Read = read;
        _cache.Clear();
        _recency.Clear();
        _sizes.Clear();
        _unopened = 0;
        _held = default;
    }

    /// <summary>How many distinct names the install could not supply.</summary>
    public int Unopened => _unopened;

    /// <summary>What is held now, and the decodes playback paid for since the level began (finding 80).</summary>
    public SoundCacheHeld Held => _held;

    private SoundCacheHeld _held;

    /// <summary>Decodes a named sound, once.</summary>
    /// <param name="name">The name as the demo carries it, soundchars and all.</param>
    /// <returns>The sample, or null when it could not be opened or decoded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public SoundSample? Sample(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Keyed as `S_FindName` keys a `CSfxTable` (B545): two names the engine resolves to one sfx are one sample here,
        // so a stop matching the sample matches exactly what `S_AlterChannel` matches.
        string key = SoundName.SfxKey(name);

        if (_cache.TryGetValue(key, out SoundSample? cached))
        {
            if (_sizes.TryGetValue(key, out (LinkedListNode<string> Node, long Bytes, bool Wave) held))
            {
                _recency.Remove(held.Node);
                _recency.AddFirst(held.Node);
            }

            return cached;
        }

        SoundSample? sample = null;

        // **Timed because this is a file read and a decode INSIDE the frame.** A sound reaches here
        // once, the first time it plays — so every new sound in a match pays a VPK read plus a full
        // decode on the UI thread, and a voice line is an MP3. That is a "once per sound" cost
        // wearing the clothes of a cache, and it lands wherever in playback the sound happens to
        // first occur.
        long readAt = Stopwatch.GetTimestamp();

        if (Read is { } read)
        {
            // **The soundchars come off first.** A precached name carries Valve's prefixes — ')'
            // for spatialised, '#' for a stream, '*' and the rest — and they are instructions to
            // the engine rather than part of the path. Left on, every one of them is a file that
            // does not exist.
            SoundName parsed = SoundName.Parse(name);

            if (SoundFile.Open("sound/" + parsed.Path, read) is { } opened)
            {
                // A miss is a decode in the frame, so a name the install cannot open is none (finding 80).
                if (!_precaching)
                {
                    _held = _held with { Misses = _held.Misses + 1 };
                }

                SoundSampleResult result = SoundSampleReader.Read(opened.Bytes);

                sample = result.Sample is { } fresh ? fresh with { Key = key } : null;

                if (sample is { } decoded)
                {
                    long bytes = ((long)decoded.Samples.Length * sizeof(float)) + ((long)decoded.Pcm16.Length * sizeof(short));
                    bool wave = opened.Bytes.Length >= 4 && opened.Bytes[0] == (byte)'R' && opened.Bytes[1] == (byte)'I';
                    Hold(key, bytes, wave);
                }

                if (!result.Succeeded)
                {
                    _audio.LogWarning("{Message}", $"{opened.Path}: {result.Refusal}");
                }
            }
            else
            {
                // **Counted and named, once per sound rather than once per play.** The cache means
                // a name reaches here exactly once, so this is a list of what the install could not
                // supply rather than a stream of the same failure. Silence with no explanation is
                // the outcome this exists to prevent.
                //
                // Stays a warning rather than dropping to Debug with the per-frame lines: it fires
                // at most once per distinct name and names a real gap in the install, which is the
                // opposite of the per-frame volume B191 was about.
                _unopened++;

                _audio.LogWarning(
                    "{Message}",
                    $"could not open '{parsed.Path}' (from '{name}'); " +
                    $"{_unopened.ToString(CultureInfo.InvariantCulture)} unopened so far");
            }
        }

        _cache[key] = sample;

        double readSeconds = (Stopwatch.GetTimestamp() - readAt) / (double)Stopwatch.Frequency;

        if (readSeconds > StallSeconds && !_precaching)
        {
            // Stryker disable all : the String mutator wraps the interpolated literal in a ternary
            // that cannot bind to string.Create's interpolated-string handler (CS1620), and Safe
            // Mode then drops every mutation in this method — B410.
            _audio.LogWarning(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"STALL decoding '{name}' took {readSeconds * 1000d:0} ms " +
                    $"({sample?.FrameCount ?? 0} frames); this frame is a freeze"));

            // Stryker restore all
        }

        return sample;
    }

    /// <summary>Counts a decoded sound as held, then lets the least recently used go until the cap holds again.</summary>
    private void Hold(string key, long bytes, bool wave)
    {
        _sizes[key] = (_recency.AddFirst(key), bytes, wave);
        Count(bytes, wave, sign: 1);

        while (_held.WaveBytes + _held.Mp3Bytes > _capacity && _recency.Last is { } oldest && oldest.Value != key)
        {
            (_, long gone, bool goneWave) = _sizes[oldest.Value];
            _recency.RemoveLast();
            _sizes.Remove(oldest.Value);
            _cache.Remove(oldest.Value);
            Count(gone, goneWave, sign: -1);
        }
    }

    private void Count(long bytes, bool wave, int sign) =>
        _held = _held with
        {
            Sounds = _held.Sounds + sign,
            WaveBytes = _held.WaveBytes + (wave ? sign * bytes : 0),
            Mp3Bytes = _held.Mp3Bytes + (wave ? 0 : sign * bytes),
        };

    /// <summary>Decodes a whole list of names ahead of playback.</summary>
    /// <param name="names">Every sound worth having ready.</param>
    /// <returns>What was asked for, what arrived, and how long it took.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="names"/> is null.</exception>
    /// <remarks>
    /// **This is <c>bPreload: true</c>, and it was measured before it was written.** Of eleven slow
    /// frames on cp_process, six were dominated by the sound step at 27-91 ms while posing and
    /// drawing sat at 1.7-2.6 ms — and only ONE decode logged a stall of its own, because three
    /// sub-threshold decodes in one frame never cross a per-decode threshold. An instrument watching
    /// single decodes reported almost nothing while the frames were visibly freezing (B191).
    ///
    /// The per-decode stall warning is suppressed while this runs, because its text asserts "this
    /// frame is a freeze" and during a precache there is no frame — every decode here is
    /// deliberately outside one.
    /// </remarks>
    public PrecacheResult Precache(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        long decodedAt = Stopwatch.GetTimestamp();
        int named = 0;
        int decoded = 0;

        _precaching = true;

        try
        {
            foreach (string name in names)
            {
                named++;

                if (Sample(name) is not null)
                {
                    decoded++;
                }
            }
        }
        finally
        {
            _precaching = false;
        }

        return new PrecacheResult(
            named, decoded, (Stopwatch.GetTimestamp() - decodedAt) / (double)Stopwatch.Frequency);
    }
}
