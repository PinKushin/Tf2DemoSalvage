using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Audio;

/// <summary>
/// Every soundscape the client knows, in the order that gives each one its index.
/// </summary>
/// <remarks>
/// **A demo carries an INDEX, not a name**, so this list's order is the whole feature. The engine
/// puts the current soundscape into the player's audio params as
/// <c>CNetworkVar( int, soundscapeIndex )</c> (<c>playernet_vars.h:118</c>), networked through
/// <c>DT_Local</c>, and the client resolves it by position in the list it built at load.
///
/// **The order is defined by the manifest, and both sides build it identically.**
/// <c>C_SoundscapeSystem::Init</c> (<c>c_soundscape.cpp:300</c>) and
/// <c>CSoundscapeSystem::Init</c> (<c>soundscape_system.cpp:129</c>) are the same routine: walk
/// <c>scripts/soundscapes_manifest.txt</c> in order, and for each <c>"file"</c> it names, append
/// every top-level section of that file in order. The map's own
/// <c>scripts/soundscapes_&lt;map&gt;.txt</c> is appended last, and only if the manifest did not
/// already list it.
///
/// **Verified against a running client rather than inferred from the SDK**, which matters because
/// the failure mode is playing the WRONG ambience rather than none — a plausible sound instead of
/// an error. `cl_soundscape_printdebuginfo` on TF2 lists 153 soundscapes, and this reconstruction
/// produces the same 153 with the same name at every index: 0 is `tf2.respawn_room`, 152 is
/// `Lair.Cap3Vista`. The owner also ran `soundscape_dumpclient` while standing in cp_process's
/// respawn room and the client reported index 0, which is independently the entry this list puts
/// there.
/// </remarks>
public sealed class SoundscapeCatalog
{
    /// <summary>The manifest, from <c>SOUNDSCAPE_MANIFEST_FILE</c>.</summary>
    private const string ManifestPath = "scripts/soundscapes_manifest.txt";

    private readonly List<Soundscape> _soundscapes;

    private SoundscapeCatalog(List<Soundscape> soundscapes) => _soundscapes = soundscapes;

    /// <summary>A catalog of exactly these soundscapes, for testing without a game install.</summary>
    internal static SoundscapeCatalog ForSoundscapes(List<Soundscape> soundscapes) =>
        new(soundscapes);

    /// <summary>Every wave any soundscape in this catalog can loop.</summary>
    /// <returns>Wave paths, without duplicates and in no particular order.</returns>
    /// <remarks>
    /// **The half of the sound precache the timeline cannot name.** A demo's sound list carries what
    /// the server told the client to play; a soundscape's loops come from the MAP's
    /// <c>env_soundscape</c> entities by way of <c>scripts/soundscapes.txt</c>, so they appear in no
    /// demo message at all. Precaching only the timeline's sounds therefore left these to decode on
    /// first play — measured 2026-08-25, `ambient/indoors.wav` cost 103 ms in one frame, which is
    /// the largest single stall left after the timeline precache landed.
    ///
    /// **Every soundscape, not just the ones this recording enters.** Which soundscape a player is
    /// in is a runtime fact that changes as they walk, and a seek can land anywhere; the catalog is
    /// small and a wave is decoded once, so loading the lot is cheaper than being clever about it.
    /// </remarks>
    public IEnumerable<string> WaveNames()
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (Soundscape soundscape in _soundscapes)
        {
            foreach (SoundscapeSound sound in soundscape.Looping)
            {
                if (sound.Wave is { Length: > 0 } && seen.Add(sound.Wave))
                {
                    yield return sound.Wave;
                }
            }
        }
    }

    /// <summary>Every soundscape, indexed exactly as the wire indexes them.</summary>
    public IReadOnlyList<Soundscape> Soundscapes => _soundscapes;

    /// <summary>How many the client would have loaded.</summary>
    public int Count => _soundscapes.Count;

    /// <summary>The soundscape an index names, or <c>null</c> when it names none.</summary>
    /// <param name="index">The index as the demo carries it.</param>
    /// <returns>The soundscape, or <c>null</c>.</returns>
    /// <remarks>
    /// **-1 is the engine's "no soundscape" and is not an error.**
    /// <c>CEnvSoundscape</c> initialises <c>m_soundscapeIndex</c> to -1
    /// (<c>soundscape.cpp:105</c>), so a player who has not entered one carries it. Answering null
    /// for anything out of range covers that and a genuinely bad index with the same, correct,
    /// "play nothing".
    /// </remarks>
    public Soundscape? At(int index) =>
        index >= 0 && index < _soundscapes.Count ? _soundscapes[index] : null;

    /// <summary>Builds the list the client would have built.</summary>
    /// <param name="read">Opens a game file by path, or answers null when absent.</param>
    /// <param name="mapName">
    /// The map, without path or extension, so its own soundscape file can be appended the way the
    /// engine appends it. Null skips that step, which is correct for every TF2 map that has none.
    /// </param>
    /// <returns>The catalog, empty when the manifest cannot be read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="read"/> is null.</exception>
    /// <remarks>
    /// **Empty rather than throwing when the manifest is missing**, matching how every other game
    /// asset is treated here: a viewer with no TF2 install draws and plays what it can. The engine
    /// calls `Error()` there, but it cannot run without its own game files and this can.
    /// </remarks>
    public static SoundscapeCatalog Load(Func<string, byte[]?> read, string? mapName = null)
    {
        ArgumentNullException.ThrowIfNull(read);

        List<Soundscape> soundscapes = [];

        if (read(ManifestPath) is not { } manifest)
        {
            return new SoundscapeCatalog(soundscapes);
        }

        string? mapFile = mapName is { Length: > 0 }
            ? $"scripts/soundscapes_{mapName}.txt"
            : null;

        bool mapFileListed = false;

        foreach (string path in Listed(manifest))
        {
            if (mapFile is not null &&
                path.Equals(mapFile, StringComparison.OrdinalIgnoreCase))
            {
                mapFileListed = true;
            }

            // **A named file that will not open is SKIPPED, not fatal, and this is the one place
            // that can silently shift every index after it.** The engine would have loaded it, so a
            // missing file means our list is shorter than the client's and every later index points
            // one entry too early. Nothing here can detect that; what stops it mattering is that
            // the manifest and its files ship together in the same VPK.
            if (read(path) is { } script)
            {
                Append(soundscapes, script);
            }
        }

        // The map's own file goes last, and only when the manifest did not already name it — the
        // engine's own condition, `if ( mapSoundscapeFilename && filesystem->FileExists( ... ) )`.
        if (mapFile is not null && !mapFileListed && read(mapFile) is { } mapScript)
        {
            Append(soundscapes, mapScript);
        }

        return new SoundscapeCatalog(soundscapes);
    }

    /// <summary>Appends one file's top-level sections, in order.</summary>
    /// <remarks>
    /// The engine's rule is `if ( pKeys->GetFirstSubKey() )` — a top-level key that opens a block is
    /// a soundscape, and one that carries a value is not. That is exactly a depth-zero key with a
    /// null value from <see cref="KeyValuesReader"/>.
    /// </remarks>
    private static void Append(List<Soundscape> soundscapes, ReadOnlyMemory<byte> script)
    {
        string? name = null;
        int dsp = 0;
        List<SoundscapeSound> looping = [];
        List<string> other = [];

        // The block currently being filled, and the fields gathered for it so far.
        string? rule = null;
        string wave = string.Empty;
        float volume = 1f;
        int pitch = 100;
        int? position = null;
        float? attenuation = null;

        void CloseRule()
        {
            if (rule is null)
            {
                return;
            }

            if (rule.Equals("playlooping", StringComparison.OrdinalIgnoreCase) &&
                wave.Length > 0)
            {
                looping.Add(new SoundscapeSound(wave, volume, pitch, position, attenuation));
            }
            else if (!rule.Equals("playlooping", StringComparison.OrdinalIgnoreCase))
            {
                other.Add(rule);
            }

            rule = null;
            wave = string.Empty;
            volume = 1f;
            pitch = 100;
            position = null;
            attenuation = null;
        }

        void CloseSoundscape()
        {
            CloseRule();

            if (name is not null)
            {
                soundscapes.Add(new Soundscape(name, dsp, looping, other));
            }

            name = null;
            dsp = 0;
            looping = [];
            other = [];
        }

        KeyValuesReader.Read(script.Span, (key, value, depth) =>
        {
            switch (depth)
            {
                case 0 when value is null:
                    // A new top-level section: the previous one is complete.
                    CloseSoundscape();
                    name = key;
                    break;

                case 1 when value is null:
                    // A rule block — playlooping, playrandom, playsoundscape.
                    CloseRule();
                    rule = key;
                    break;

                case 1:
                    if (key.Equals("dsp", StringComparison.OrdinalIgnoreCase))
                    {
                        dsp = Number(value, 0);
                    }

                    break;

                case 2 when value is not null:
                    Field(key, value);
                    break;

                default:
                    break;
            }

            return true;
        });

        CloseSoundscape();

        void Field(string key, string value)
        {
            if (key.Equals("wave", StringComparison.OrdinalIgnoreCase))
            {
                wave = value;
            }
            else if (key.Equals("volume", StringComparison.OrdinalIgnoreCase))
            {
                volume = Decimal(value, 1f);
            }
            else if (key.Equals("pitch", StringComparison.OrdinalIgnoreCase))
            {
                pitch = Number(value, 100);
            }
            else if (key.Equals("position", StringComparison.OrdinalIgnoreCase))
            {
                position = Number(value, 0);
            }
            else if (key.Equals("attenuation", StringComparison.OrdinalIgnoreCase))
            {
                attenuation = Decimal(value, 1f);
            }
        }
    }

    /// <summary>The script paths a manifest names, in order.</summary>
    /// <remarks>
    /// The engine accepts only <c>"file"</c> here and warns on anything else. Comments are the
    /// reader's business and it drops them, which keeps anything Valve commented out of the list —
    /// and that matters more here than for sound scripts, because a spurious entry would shift
    /// every index after it.
    /// </remarks>
    private static List<string> Listed(ReadOnlyMemory<byte> manifest)
    {
        List<string> paths = [];

        KeyValuesReader.Read(manifest.Span, (key, value, _) =>
        {
            if (value is { Length: > 0 } &&
                key.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(value);
            }

            return true;
        });

        return paths;
    }

    private static int Number(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int read)
            ? read
            : fallback;

    /// <summary>
    /// A decimal as the scripts write it — <c>".6"</c> and <c>".30"</c> with no leading zero.
    /// </summary>
    /// <remarks>
    /// Invariant culture, because a machine whose decimal separator is a comma would otherwise read
    /// <c>".75"</c> as 75 and play the sound a hundred times too loud.
    /// </remarks>
    private static float Decimal(string? value, float fallback) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float read)
            ? read
            : fallback;
}
