using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Audio;

/// <summary>A value a soundscript may state as a single number or as a range: the two bounds <c>RandomFloat</c> is drawn between.</summary>
/// <param name="Low">The stored start, or the only value.</param>
/// <param name="High">
/// The stored start plus the stored range — NOT necessarily above <paramref name="Low"/>, and for a descending
/// pair wrapped through the storage type (B487, <see cref="SoundScript"/>).
/// </param>
/// <remarks>
/// **Ranges are ordinary in shipped scripts, not an exotic case.** <c>"pitch" "90, 110"</c> means the
/// engine picks per play, which is what stops a repeated sound sounding mechanical. A reader taking
/// the first number gets a plausible sound with no variation — audible only by comparison, which is
/// the hardest kind of difference to notice.
/// </remarks>
public readonly record struct SoundRange(float Low, float High)
{
    /// <summary>Whether the script gave a range rather than one value.</summary>
    public bool Varies => MathF.Abs(High - Low) > 0f;

    /// <summary>The midpoint, for a caller that does not want to choose.</summary>
    public float Middle => (Low + High) / 2f;
}

/// <summary>One entry of a soundscript: what to play and how.</summary>
/// <param name="Name">The script name, as <c>svc_Sounds</c> or game code refers to it.</param>
/// <param name="Channel">Which channel it occupies; <c>CHAN_AUTO</c> when unstated.</param>
/// <param name="Volume">Volume, 0 to 1. <c>VOL_NORM</c> is 1.</param>
/// <param name="Pitch">Pitch percentage; <c>PITCH_NORM</c> is 100.</param>
/// <param name="SoundLevel">The <c>soundlevel_t</c> that decides attenuation, drawn per play like the pitch.</param>
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
    SoundRange SoundLevel,
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
    // **Valve's defaults, from `CSoundParameters`' constructor**, public because a caller resolving
    // a raw path needs the same values a script-less entry would get. Two copies of "the default
    // volume is 1" would be two places for a wrong sound to come from.

    /// <summary><c>VOL_NORM</c>.</summary>
    public const float NormalVolume = 1f;

    /// <summary><c>PITCH_NORM</c>.</summary>
    public const int NormalPitch = 100;

    /// <summary>A pitch as a playback rate: <see cref="NormalPitch"/> is 1.</summary>
    /// <param name="pitch">The pitch percentage.</param>
    /// <returns>The rate, or 1 for a pitch of zero or below, which no sink can play.</returns>
    public static float Rate(int pitch) => pitch > 0 ? pitch / (float)NormalPitch : 1f;

    /// <summary><c>SNDLVL_NORM</c>.</summary>
    public const int NormalSoundLevel = 75;

    /// <summary>What marks a soundlevel as a name: <c>SNDLVL_PREFIX</c> (<c>SoundParametersInternal.cpp:179</c>).</summary>
    private const string SoundLevelPrefix = "SNDLVL_";

    /// <summary>The highest number <c>TextToSoundLevel</c> takes from a name, <c>SNDLVL_180dB</c>.</summary>
    private const int MostSoundLevel = 180;

    /// <summary><c>CHAN_AUTO</c>.</summary>
    public const int AutoChannel = 0;

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
        SoundRange volume = Normal.Volume;
        SoundRange pitch = Normal.Pitch;
        SoundRange soundLevel = Normal.SoundLevel;
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
            volume = Normal.Volume;
            pitch = Normal.Pitch;
            soundLevel = Normal.SoundLevel;
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
                    volume = Volume(value);
                    break;

                case "PITCH" when value is not null:
                    pitch = Pitch(value);
                    break;

                case "SOUNDLEVEL" when value is not null:
                    soundLevel = SoundLevelRange(value);
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

        // `int sndlvl = atoi( val ); if ( sndlvl > 0 && sndlvl <= 180 ) return sndlvl;` — else `SNDLVL_NORM`
        // (`SoundParametersInternal.cpp:200-212`). A number past 180 or at zero is the default, not itself.
        if (text.StartsWith(SoundLevelPrefix, StringComparison.OrdinalIgnoreCase))
        {
            int dB = CStdlib.Atoi(text.AsSpan(SoundLevelPrefix.Length));

            return dB is > 0 and <= MostSoundLevel ? dB : NormalSoundLevel;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int plain)
            ? plain
            : NormalSoundLevel;
    }

    /// <summary>What an entry, or a raw path with no entry, plays at when it states nothing: <c>CSoundParameters</c>' constructor.</summary>
    public static (SoundRange Volume, SoundRange Pitch, SoundRange SoundLevel) Normal { get; } =
        (new(NormalVolume, NormalVolume), new(NormalPitch, NormalPitch), new(NormalSoundLevel, NormalSoundLevel));

    // **B487: `CSoundParametersInternal::*FromString` (`SoundParametersInternal.cpp:498-550`), the storage narrowing
    // settled in the x64 `soundemittersystem.dll`.** A name, else `FromInterval( ReadInterval( sz ) )` — atof of the
    // first two comma tokens as start and range, never ordered and never refused — narrowed into the field's type:
    // pitch `uint8` and soundlevel `uint16` through `cvttss2si` (FUN_180007150, FUN_1800072b0), volume through
    // `ConvertFloatTo16bits` (FUN_180006ee0). `GetParametersForSound` (FUN_180003370) then draws
    // `RandomFloat( start, start + range )` over the stored values, so a SoundRange holds exactly those two bounds.
    // Names compare the raw value, as `Q_strcasecmp` does: no trimming.

    /// <summary><c>VolumeFromString</c>: <c>VOL_NORM</c>, else the interval as two halves.</summary>
    internal static SoundRange Volume(string value)
    {
        if (value.Equals("VOL_NORM", StringComparison.OrdinalIgnoreCase))
        {
            return Normal.Volume;
        }

        Interval read = Interval.Read(value);
        float start = Half(read.Start);

        return new SoundRange(start, start + Half(read.Range));
    }

    /// <summary><c>PitchFromString</c>: the three names (0x64, 0x5f, 0x78 in the disassembly), else the interval as bytes.</summary>
    internal static SoundRange Pitch(string value)
    {
        int? named = value.ToUpperInvariant() switch
        {
            "PITCH_NORM" => NormalPitch,
            "PITCH_LOW" => 95,
            "PITCH_HIGH" => 120,
            _ => null,
        };

        if (named is { } pitch)
        {
            return new SoundRange(pitch, pitch);
        }

        Interval read = Interval.Read(value);
        byte start = unchecked((byte)CStdlib.Truncate(read.Start));

        return new SoundRange(start, start + unchecked((byte)CStdlib.Truncate(read.Range)));
    }

    /// <summary><c>SoundLevelFromString</c>: a <c>SNDLVL_</c> name through <see cref="SoundLevel"/>, else the interval as <c>uint16</c>s.</summary>
    internal static SoundRange SoundLevelRange(string value)
    {
        if (value.StartsWith(SoundLevelPrefix, StringComparison.OrdinalIgnoreCase))
        {
            int level = SoundLevel(value);

            return new SoundRange(level, level);
        }

        Interval read = Interval.Read(value);
        ushort start = unchecked((ushort)CStdlib.Truncate(read.Start));

        return new SoundRange(start, start + unchecked((ushort)CStdlib.Truncate(read.Range)));
    }

    /// <summary>
    /// A float stored in Valve's <c>float16</c> and read back: <c>ConvertFloatTo16bits</c> then
    /// <c>Convert16bitFloatTo32bits</c> (<c>mathlib/compressed_vector.h:368-495</c>).
    /// </summary>
    /// <remarks>
    /// **Not <see cref="System.Half"/>, which rounds**: Valve's conversion TRUNCATES the mantissa, flushes a float
    /// denormal and NaN to zero, and saturates at 65504 where IEEE would give infinity.
    /// </remarks>
    internal static float Half(float value)
    {
        const float MostHalf = 65504f;

        float clamped = Math.Clamp(value, -MostHalf, MostHalf);
        uint bits = BitConverter.SingleToUInt32Bits(clamped);
        uint sign = bits & 0x8000_0000u;
        int biased = (int)((bits >> 23) & 0xFF);
        uint mantissa = bits & 0x7F_FFFFu;

        if (biased == 0 || float.IsNaN(value))
        {
            return BitConverter.UInt32BitsToSingle(sign);
        }

        int exponent = biased - 127;

        if (exponent < -14)
        {
            // The half denormal branch: `exp_val = -14 - exponent`, kept only below 11, then widened back as
            // mantissa / 1024 * 2^-14.
            int shift = -14 - exponent;
            int halfMantissa = shift is > 0 and < 11 ? (1 << (10 - shift)) + (int)(mantissa >> (13 + shift)) : 0;
            float magnitude = halfMantissa / 1024f * (1f / 16384f);

            return sign != 0 ? -magnitude : magnitude;
        }

        // Inside the clamp a normal float's exponent is at most 15, so the regular branch: ten mantissa bits kept.
        return BitConverter.UInt32BitsToSingle(sign | (bits & 0x7F80_0000u) | (mantissa & 0x7F_E000u));
    }
}
