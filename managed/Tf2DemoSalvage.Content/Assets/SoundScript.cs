using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>A value a soundscript may state as a single number or as a range.</summary>
/// <param name="Low">The low end, or the only value.</param>
/// <param name="High">The high end; equal to <paramref name="Low"/> when one number was given.</param>
/// <remarks>
/// **Ranges are ordinary in shipped scripts, not an exotic case.** <c>"pitch" "90, 110"</c> means the
/// engine picks per play, which is what stops a repeated sound sounding mechanical. A reader taking
/// the first number gets a plausible sound with no variation — audible only by comparison, which is
/// the hardest kind of difference to notice.
/// </remarks>
public readonly record struct SoundRange(float Low, float High)
{
    /// <summary>Whether the script gave a range rather than one value.</summary>
    public bool Varies => High > Low;

    /// <summary>The midpoint, for a caller that does not want to choose.</summary>
    public float Middle => (Low + High) / 2f;
}

/// <summary>One entry of a soundscript: what to play and how.</summary>
/// <param name="Name">The script name, as <c>svc_Sounds</c> or game code refers to it.</param>
/// <param name="Channel">Which channel it occupies; <c>CHAN_AUTO</c> when unstated.</param>
/// <param name="Volume">Volume, 0 to 1. <c>VOL_NORM</c> is 1.</param>
/// <param name="Pitch">Pitch percentage; <c>PITCH_NORM</c> is 100.</param>
/// <param name="SoundLevel">The <c>soundlevel_t</c> that decides attenuation.</param>
/// <param name="Waves">
/// Every wave the entry names. One for a plain <c>wave</c>, several for an <c>rndwave</c> block.
/// </param>
/// <remarks>
/// **The defaults are Valve's, from <c>CSoundParameters</c>'s constructor** in
/// <c>public/SoundEmitterSystem/isoundemittersystembase.h</c> — channel <c>CHAN_AUTO</c>, volume
/// <c>VOL_NORM</c> (1), pitch <c>PITCH_NORM</c> (100), soundlevel <c>SNDLVL_NORM</c> (75). Most
/// entries state only some of these, so a wrong default is a wrong sound on many entries rather than
/// on an edge case.
/// </remarks>
public readonly record struct SoundScriptEntry(
    string Name,
    int Channel,
    SoundRange Volume,
    SoundRange Pitch,
    int SoundLevel,
    IReadOnlyList<string> Waves);

/// <summary>
/// Reads a <c>scripts/game_sounds*.txt</c> soundscript.
/// </summary>
/// <remarks>
/// **A demo names a sound two different ways and only one of them is a file.** `svc_Sounds` carries
/// an index into <c>soundprecache</c>, and what that resolves to may be a path — or a SCRIPT NAME
/// like <c>FX_RicochetSound.Ricochet</c>, which is an entry in one of these files carrying the
/// channel, volume, pitch, soundlevel and one or more waves.
///
/// **The shipped scripts document their own syntax**, which is where the symbolic values here come
/// from rather than from a wiki — `game_sounds_weapons.txt` opens with the channel list and the
/// legacy attenuation constants, including Valve's own warning:
///
/// <code>
/// // DON'T USE THESE - USE SNDLVL_ INSTEAD!!!
/// //	ATTN_NONE		0.0f
/// //	ATTN_NORM		0.8f
/// </code>
///
/// That `ATTN_NORM 0.8` is an independent confirmation of `SNDLVL_TO_ATTN(75) = 0.8` from
/// <c>soundflags.h</c> — two shipped sources agreeing, which is worth more than either alone.
///
/// **Wave names carry sound characters here too.** Shipped entries include
/// <c>"wave" "&gt;weapons/fx/nearmiss/bulletLtoR08.wav"</c>, so
/// <see cref="Tf2DemoSalvage.Core.Net.SoundName"/>'s prefix handling applies to soundscript waves
/// exactly as it does to precached names. They are kept verbatim here and split at the point of use.
/// </remarks>
public static class SoundScript
{
    /// <summary><c>VOL_NORM</c>.</summary>
    private const float NormalVolume = 1f;

    /// <summary><c>PITCH_NORM</c>.</summary>
    private const int NormalPitch = 100;

    /// <summary><c>SNDLVL_NORM</c>.</summary>
    private const int NormalSoundLevel = 75;

    /// <summary><c>CHAN_AUTO</c>.</summary>
    private const int AutoChannel = 0;

    /// <summary>The channels, as `game_sounds_weapons.txt` lists them in its own header.</summary>
    private static readonly Dictionary<string, int> Channels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CHAN_AUTO"] = 0,
        ["CHAN_WEAPON"] = 1,
        ["CHAN_VOICE"] = 2,
        ["CHAN_ITEM"] = 3,
        ["CHAN_BODY"] = 4,
        ["CHAN_STREAM"] = 5,
        ["CHAN_STATIC"] = 6,
        ["CHAN_VOICE2"] = 7,
        ["CHAN_VOICE_BASE"] = 8,
    };

    /// <summary>Reads every entry in one soundscript file.</summary>
    /// <param name="text">The file's bytes.</param>
    /// <returns>The entries, keyed by name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <remarks>
    /// **Built on <see cref="KeyValuesReader"/> rather than a second parser.** These files are
    /// ordinary KeyValues and the repository already has a reader with its own tests; a bespoke one
    /// here would be a second place for comment and quoting rules to be got wrong.
    ///
    /// A malformed or unreadable entry is skipped rather than throwing. These files come from the
    /// user's install and a third-party map may ship its own, so one bad entry must not cost the
    /// other nine hundred.
    /// </remarks>
    public static IReadOnlyDictionary<string, SoundScriptEntry> Read(ReadOnlyMemory<byte> text)
    {
        Dictionary<string, SoundScriptEntry> entries = new(StringComparer.OrdinalIgnoreCase);

        string? name = null;
        int channel = AutoChannel;
        SoundRange volume = new(NormalVolume, NormalVolume);
        SoundRange pitch = new(NormalPitch, NormalPitch);
        int soundLevel = NormalSoundLevel;
        List<string> waves = [];

        void Flush()
        {
            if (name is { Length: > 0 } && waves.Count > 0)
            {
                entries[name] = new SoundScriptEntry(
                    name, channel, volume, pitch, soundLevel, waves);
            }

            name = null;
            channel = AutoChannel;
            volume = new SoundRange(NormalVolume, NormalVolume);
            pitch = new SoundRange(NormalPitch, NormalPitch);
            soundLevel = NormalSoundLevel;
            waves = [];
        }

        KeyValuesReader.Read(text.Span, (key, value, depth) =>
        {
            // Depth 0 is the entry name — a key with no value, opening a block. Reaching one means
            // the previous entry is complete.
            if (depth == 0)
            {
                Flush();

                if (value is null)
                {
                    name = key;
                }

                return true;
            }

            // Inside an entry, or inside its rndwave block. `rndwave` itself has no value and needs
            // no handling: its children are `wave` keys, which are collected the same way a single
            // `wave` is, so a plain wave and a random set differ only in count.
            switch (key.ToUpperInvariant())
            {
                case "CHANNEL" when value is not null:
                    channel = Channel(value);
                    break;

                case "VOLUME" when value is not null:
                    volume = Range(value, NormalVolume, "VOL_NORM");
                    break;

                case "PITCH" when value is not null:
                    pitch = Range(value, NormalPitch, "PITCH_NORM");
                    break;

                case "SOUNDLEVEL" when value is not null:
                    soundLevel = SoundLevel(value);
                    break;

                case "WAVE" when value is not null:
                    waves.Add(value);
                    break;

                default:
                    break;
            }

            return true;
        });

        Flush();

        return entries;
    }

    /// <summary>Resolves a channel, symbolic or numeric.</summary>
    /// <remarks>
    /// The shipped header says outright that both forms occur: *"these can be set with `channel`
    /// `2` or `channel` `chan_voice`"*. Handling only one silently mis-channels every entry using
    /// the other.
    /// </remarks>
    internal static int Channel(string value)
    {
        string text = value.Trim();

        if (Channels.TryGetValue(text, out int known))
        {
            return known;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? number
            : AutoChannel;
    }

    /// <summary>Resolves a <c>soundlevel_t</c>, named or numeric.</summary>
    /// <remarks>
    /// **The names are a pattern rather than a table, which is why this parses rather than looks
    /// up.** `soundflags.h` declares `SNDLVL_20dB` through `SNDLVL_180dB` at their own values, so
    /// the number is in the name — plus a handful of aliases that are not:
    /// <c>SNDLVL_NORM</c> 75, <c>SNDLVL_IDLE</c> 60, <c>SNDLVL_STATIC</c> 66,
    /// <c>SNDLVL_TALKING</c> 80, <c>SNDLVL_GUNFIRE</c> 140, <c>SNDLVL_NONE</c> 0.
    ///
    /// Several values have two names, so this direction is a function and the reverse is not.
    /// </remarks>
    internal static int SoundLevel(string value)
    {
        string text = value.Trim();

        switch (text.ToUpperInvariant())
        {
            case "SNDLVL_NONE": return 0;
            case "SNDLVL_IDLE": return 60;
            case "SNDLVL_STATIC": return 66;
            case "SNDLVL_NORM": return 75;
            case "SNDLVL_TALKING": return 80;
            case "SNDLVL_GUNFIRE": return 140;
            default: break;
        }

        if (text.StartsWith("SNDLVL_", StringComparison.OrdinalIgnoreCase))
        {
            string digits = text[7..].TrimEnd('B', 'b', 'D', 'd');

            return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dB)
                ? dB
                : NormalSoundLevel;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int plain)
            ? plain
            : NormalSoundLevel;
    }

    /// <summary>Parses a value that may be one number, a range, or a named constant.</summary>
    private static SoundRange Range(string value, float fallback, string normalName)
    {
        string text = value.Trim();

        if (text.Equals(normalName, StringComparison.OrdinalIgnoreCase))
        {
            return new SoundRange(fallback, fallback);
        }

        int comma = text.IndexOf(',', StringComparison.Ordinal);

        if (comma < 0)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float one)
                ? new SoundRange(one, one)
                : new SoundRange(fallback, fallback);
        }

        bool low = float.TryParse(
            text[..comma], NumberStyles.Float, CultureInfo.InvariantCulture, out float lowValue);

        bool high = float.TryParse(
            text[(comma + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out float highValue);

        return low && high
            ? new SoundRange(Math.Min(lowValue, highValue), Math.Max(lowValue, highValue))
            : new SoundRange(fallback, fallback);
    }
}
