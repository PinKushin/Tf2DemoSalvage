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
        new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>Creates an empty cache.</summary>
    /// <param name="audio">Where misses and slow decodes are reported.</param>
    /// <exception cref="ArgumentNullException"><paramref name="audio"/> is null.</exception>
    public SoundCache(ILogger audio)
    {
        ArgumentNullException.ThrowIfNull(audio);

        _audio = audio;
    }

    /// <summary>Where files come from, set when the game's archives are opened.</summary>
    /// <remarks>
    /// Null until then, and a viewer with no TF2 install keeps it null for its whole life — so
    /// every lookup answers silence rather than each caller checking first.
    /// </remarks>
    public Func<string, byte[]?>? Read { get; set; }

    /// <summary>How many distinct names the install could not supply.</summary>
    public int Unopened => _unopened;

    /// <summary>Decodes a named sound, once.</summary>
    /// <param name="name">The name as the demo carries it, soundchars and all.</param>
    /// <returns>The sample, or null when it could not be opened or decoded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public SoundSample? Sample(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_cache.TryGetValue(name, out SoundSample? cached))
        {
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
                SoundSampleResult result = SoundSampleReader.Read(opened.Bytes);

                sample = result.Sample;

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

        _cache[name] = sample;

        double readSeconds = (Stopwatch.GetTimestamp() - readAt) / (double)Stopwatch.Frequency;

        if (readSeconds > StallSeconds && !_precaching)
        {
            _audio.LogWarning(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"STALL decoding '{name}' took {readSeconds * 1000d:0} ms " +
                    $"({sample?.FrameCount ?? 0} frames); this frame is a freeze"));
        }

        return sample;
    }

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
