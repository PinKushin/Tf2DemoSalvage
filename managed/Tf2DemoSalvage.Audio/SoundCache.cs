using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

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
/// <param name="OffFrame">First plays loaded through <see cref="SoundCache.OffFrame"/> instead, outside any frame (finding 90).</param>
public readonly record struct SoundCacheHeld(int Sounds, long WaveBytes, long Mp3Bytes, int Misses, int OffFrame = 0);

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

        // A load still in flight belongs to the level that asked; its result is dropped when it arrives.
        _generation++;
        _pending.Clear();
        _listed.Clear();
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

        Drain();

        if (_cache.TryGetValue(key, out SoundSample? cached))
        {
            if (_sizes.TryGetValue(key, out (LinkedListNode<string> Node, long Bytes, bool Wave) held))
            {
                _recency.Remove(held.Node);
                _recency.AddFirst(held.Node);
            }

            return cached;
        }

        if (Read is not { } read)
        {
            _cache[key] = null;

            return null;
        }

        // **A first play in the frame is loaded off it** (B568, finding 90), when the viewer gives a place to load. The
        // engine's mixer, for an async-load source or under `snd_async_fullyasync`, does not mix a sound whose data has not
        // arrived; it kicks the load and answers "not ready" (engine.dll FUN_18004bc10). This answers null and
        // `Loading` answers true until the load is taken in; `SoundPresenter` and the soundscape then start the sound late,
        // as the engine's waiting channel does (FUN_18003f2c0, finding 90).
        if (!_precaching && OffFrame is { } offFrame)
        {
            if (_pending.Add(key))
            {
                int generation = _generation;
                offFrame(() => _arrived.Enqueue((generation, LoadCaught(name, key, read))));
            }

            return null;
        }

        // **Timed because this is a file read and a decode on the caller**: in play, inside the frame, which is what the
        // STALL line below reports. The read and the decode are timed apart (finding 87): the ~395 ms stalls were the read.
        Loaded loaded = Load(name, key, read);

        // A miss is a decode in the frame, so a name the install cannot open is none (finding 80).
        if (loaded.Opened && !_precaching)
        {
            _held = _held with { Misses = _held.Misses + 1 };
        }

        Keep(loaded);

        double readSeconds = (loaded.DecodedAt - loaded.ReadAt) / (double)Stopwatch.Frequency;

        if (readSeconds > StallSeconds && !_precaching)
        {
            // Stryker disable all : the String mutator wraps the interpolated literal in a ternary
            // that cannot bind to string.Create's interpolated-string handler (CS1620), and Safe
            // Mode then drops every mutation in this method — B410.
            _audio.LogWarning(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"STALL reading '{name}' took {readSeconds * 1000d:0} ms " +
                    $"(read {(loaded.OpenedAt - loaded.ReadAt) * 1000d / Stopwatch.Frequency:0.0} ms, decode {(loaded.DecodedAt - loaded.OpenedAt) * 1000d / Stopwatch.Frequency:0.0} ms; " +
                    $"{loaded.Sample?.FrameCount ?? 0} frames); this frame is a freeze"));

            // Stryker restore all
        }

        return loaded.Sample;
    }

    /// <summary>Where a first play during playback is loaded; null loads it on the caller, inside the frame.</summary>
    /// <remarks>
    /// The work handed here only reads and decodes, touching nothing of the cache; its result is taken in on the owner's
    /// thread at the next <see cref="Sample"/>, so the cache stays single-threaded. The read path must be safe to call
    /// from another thread (`GameArchives`, `PakFile` and `VpkArchive` read immutable state and open a stream per read).
    /// </remarks>
    public Action<Action>? OffFrame { get; init; }

    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _listed = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentQueue<(int Generation, Loaded Loaded)> _arrived = new();
    private int _generation;

    /// <summary>One read and decode, with when each half ended; touches nothing shared.</summary>
    private readonly record struct Loaded(
        string Name, string Key, string Path, bool Opened, SoundSample? Sample, long Bytes, bool Wave, string? Refusal,
        long ReadAt, long OpenedAt, long DecodedAt);

    private static Loaded Load(string name, string key, Func<string, byte[]?> read)
    {
        long readAt = Stopwatch.GetTimestamp();

        // **The soundchars come off first.** A precached name carries Valve's prefixes — ')' for spatialised, '#' for a
        // stream, '*' and the rest — and they are instructions to the engine rather than part of the path.
        SoundName parsed = SoundName.Parse(name);

        (byte[] Bytes, string Path)? openedResult = SoundFile.Open("sound/" + parsed.Path, read);
        long openedAt = Stopwatch.GetTimestamp();

        if (openedResult is not { } opened)
        {
            return new Loaded(name, key, parsed.Path, Opened: false, null, 0, false, null, readAt, openedAt, openedAt);
        }

        SoundSampleResult result = SoundSampleReader.Read(opened.Bytes);
        long decodedAt = Stopwatch.GetTimestamp();

        SoundSample? sample = result.Sample is { } fresh ? fresh with { Key = key } : null;
        long bytes = sample is { } decoded
            ? ((long)decoded.Samples.Length * sizeof(float)) + ((long)decoded.Pcm16.Length * sizeof(short))
            : 0;
        bool wave = opened.Bytes.Length >= 4 && opened.Bytes[0] == (byte)'R' && opened.Bytes[1] == (byte)'I';

        return new Loaded(
            name, key, parsed.Path, Opened: true, sample, bytes, wave, result.Succeeded ? null : $"{opened.Path}: {result.Refusal}",
            readAt, openedAt, decodedAt);
    }

    /// <summary><see cref="Load"/> off the owner's thread, where an exception would end the process rather than reach a caller.</summary>
    /// <remarks>
    /// **Every exception, deliberately, and only here.** On the caller a failure reaches `DemoSounds.Precache`'s narrow
    /// catch; on a pool thread nothing is above this, and an unhandled exception there ends the process. So it is caught,
    /// carried back as a refusal naming the sound (logged by <see cref="Keep"/>), and the name is held as failed: not
    /// pending, and not asked for again.
    /// </remarks>
#pragma warning disable CA1031 // A pool thread has no caller to rethrow to: uncaught, any exception ends the process (above).
    private static Loaded LoadCaught(string name, string key, Func<string, byte[]?> read)
    {
        try
        {
            return Load(name, key, read);
        }
        catch (Exception failure)
        {
            long now = Stopwatch.GetTimestamp();

            return new Loaded(
                name, key, name, Opened: true, null, 0, false,
                $"loading '{name}' off the frame failed: {failure.GetType().Name}: {failure.Message}", now, now, now);
        }
    }
#pragma warning restore CA1031

    /// <summary>Whether a load of <paramref name="name"/> handed to <see cref="OffFrame"/> has not been taken in yet.</summary>
    /// <param name="name">The name as the demo carries it.</param>
    /// <returns>True while the sound is on its way; false once it arrived, failed, or was never asked for.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public bool Loading(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Drain();

        return _pending.Contains(SoundName.SfxKey(name));
    }

    /// <summary>Takes in a load: the cache entry, the cap, and what is worth saying about it.</summary>
    private void Keep(Loaded loaded)
    {
        _cache[loaded.Key] = loaded.Sample;

        if (loaded.Sample is not null)
        {
            Hold(loaded.Key, loaded.Bytes, loaded.Wave);
        }

        if (loaded.Refusal is { } refusal)
        {
            _audio.LogWarning("{Message}", refusal);
        }

        if (!loaded.Opened)
        {
            // **Counted and named, once per sound rather than once per play**: the cache means a name reaches here once,
            // so this lists what the install could not supply. A warning, not Debug: at most once per name (B191).
            _unopened++;

            _audio.LogWarning(
                "{Message}",
                $"could not open '{loaded.Path}' (from '{loaded.Name}'); " +
                $"{_unopened.ToString(CultureInfo.InvariantCulture)} unopened so far");
        }
    }

    /// <summary>Takes in every load finished off the frame for this level; one finished for an earlier level is dropped.</summary>
    private void Drain()
    {
        while (_arrived.TryDequeue(out (int Generation, Loaded Loaded) arrived))
        {
            if (arrived.Generation != _generation)
            {
                continue;
            }

            Loaded loaded = arrived.Loaded;
            _pending.Remove(loaded.Key);

            // A precache decoded it while this load was in flight: held already, so holding it again would count it twice.
            if (_cache.ContainsKey(loaded.Key))
            {
                continue;
            }

            Keep(loaded);

            // A failure or a missing file was reported by Keep; only an arrival is counted and named here.
            if (loaded.Sample is not { } arrivedSample)
            {
                continue;
            }

            _held = _held with { OffFrame = _held.OffFrame + 1 };

            // Stryker disable all : the String mutator cannot bind to string.Create's handler (CS1620) — B410.
            _audio.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"loaded off the frame '{loaded.Name}' (read {(loaded.OpenedAt - loaded.ReadAt) * 1000d / Stopwatch.Frequency:0.0} ms, " +
                    $"decode {(loaded.DecodedAt - loaded.OpenedAt) * 1000d / Stopwatch.Frequency:0.0} ms; {arrivedSample.FrameCount} frames)"));

            // Stryker restore all
        }
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
                // **A name an earlier precache of this level listed is skipped** (B570): past the cap it may have been
                // evicted, and the second precache runs on the UI thread, so asking again re-decoded it there. Evicted
                // and asked for in play, it loads like any first play.
                if (!_listed.Add(SoundName.SfxKey(name)))
                {
                    continue;
                }

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
