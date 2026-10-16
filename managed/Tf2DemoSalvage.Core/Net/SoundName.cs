using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// A precached sound name split into the file path and the prefix characters that lead it.
/// </summary>
/// <param name="Path">The name with any leading sound characters removed.</param>
/// <param name="Characters">Those characters, in the order they appeared.</param>
/// <remarks>
/// **A precached sound name is not always a path**, and treating it as one fails as SILENCE — which
/// on a sound feature is indistinguishable from not having implemented that sound yet.
/// <c>public/soundchars.h</c> declares ten characters that may lead a name, and
/// <c>PSkipSoundChars</c> skips them before the remainder is opened as a file:
///
/// <code>
/// #define CHAR_STREAM       '*'   // streaming wav data
/// #define CHAR_USERVOX      '?'   // user realtime voice data
/// #define CHAR_SENTENCE     '!'   // sentence wav
/// #define CHAR_DRYMIX       '#'   // bypasses dsp fx
/// #define CHAR_DOPPLER      '>'   // doppler encoded stereo
/// #define CHAR_DIRECTIONAL  '&lt;'   // stereo with a direction cone
/// #define CHAR_DISTVARIANT  '^'   // distance variant stereo (left close, right far)
/// #define CHAR_OMNI         '@'   // non-directional
/// #define CHAR_SPATIALSTEREO ')'  // spatialised stereo
/// #define CHAR_FAST_PITCH   '}'   // low quality, non-interpolated pitch shift
/// </code>
///
/// **Measured on the committed corpus before this type was written**, by <c>SoundCharProbe</c>:
/// 34,436 precached names across ten demos, **1,971 of them — 5.7% — carrying a prefix**, led by
/// <c>)</c> at 1,783. Roughly one sound in eighteen. Not a corner case, and the reason this exists
/// as its own type rather than as a <c>TrimStart</c> at one call site.
///
/// **The characters are RETAINED, not merely stripped.** Each is an instruction about how the sound
/// is played — <c>*</c> streams it rather than loading it whole, <c>#</c> takes it out of the DSP
/// chain, <c>)</c> spatialises a stereo file — so discarding them keeps the path and loses the
/// behaviour, which is the half-fix that looks finished.
///
/// **Leading only.** The same byte later in a name is part of the path, so this skips a prefix
/// rather than stripping a character set; a <c>Trim</c> or <c>Replace</c> would quietly corrupt any
/// name containing one.
/// </remarks>
public readonly record struct SoundName(string Path, IReadOnlyList<char> Characters)
{
    /// <summary>Valve's ten, from <c>soundchars.h</c>.</summary>
    /// <remarks>
    /// Ordered as the header declares them so the two can be read side by side.
    /// <c>SoundCharConformanceTests</c> parses the header and asserts every one of them is here,
    /// with ordinary path characters as the control — so a character Valve adds fails rather than
    /// being silently skipped.
    /// </remarks>
    private static ReadOnlySpan<char> All => "*?!#><^@)}";

    /// <summary>Is this one of the characters that may lead a sound name?</summary>
    /// <param name="character">A character.</param>
    /// <returns><c>true</c> when it is one of Valve's ten.</returns>
    public static bool IsSoundChar(char character) =>
        All.Contains(character);

    /// <summary>Splits a precached name into its path and its leading characters.</summary>
    /// <param name="precached">The name exactly as the <c>soundprecache</c> table carries it.</param>
    /// <returns>The split name; an empty path when the name is empty or entirely prefixes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="precached"/> is null.</exception>
    /// <remarks>
    /// **Transcribed from the FUNCTION, not from the comment beside it.** Valve's comment says the
    /// characters appear "as one of 1st 2 chars", which reads as a limit of two;
    /// <c>PSkipSoundChars</c> loops until the character is not one, with no limit. The loop is what
    /// runs, so the loop is what is reproduced — see
    /// <c>docs/memory/read-the-encoder-not-the-decoder.md</c>.
    ///
    /// An all-prefix or empty name yields an empty path rather than throwing. A demo comes from a
    /// stranger, so its string tables are untrusted input (D32), and a sound that cannot be opened
    /// is the correct answer where an exception would take down the whole pass.
    /// </remarks>
    public static SoundName Parse(string precached)
    {
        ArgumentNullException.ThrowIfNull(precached);

        int at = 0;

        while (at < precached.Length && IsSoundChar(precached[at]))
        {
            at++;
        }

        if (at == 0)
        {
            return new SoundName(precached, []);
        }

        return new SoundName(precached[at..], precached[..at].ToCharArray());
    }

    /// <summary>Whether two names are one <c>CSfxTable</c> to the engine, which is how `S_AlterChannel` matches (<see cref="SfxKey"/>).</summary>
    /// <param name="first">A name.</param>
    /// <param name="second">Another.</param>
    /// <returns>True when their keys are equal.</returns>
    public static bool SameSfx(string first, string second) =>
        string.Equals(SfxKey(first), SfxKey(second), StringComparison.Ordinal);

    /// <summary>The key two names share exactly when the engine resolves them to one <c>CSfxTable</c>.</summary>
    /// <param name="name">A sound name as played or stopped, sound characters included.</param>
    /// <returns>The name lower-cased with its separators fixed, its sound characters kept.</returns>
    /// <remarks>
    /// `S_AlterChannel` (engine.dll FUN_18002aa20) matches a playing channel by its <c>CSfxTable</c> POINTER, and
    /// `S_FindName` (FUN_18002b290) looks a name up by the file system's file-name handle of the WHOLE name, sound
    /// characters included — so <c>#a.wav</c> and <c>a.wav</c> are two sounds. That handle is
    /// <c>CUtlFilenameSymbolTable::FindOrAddFileName</c> (`utlsymbol.cpp:312`): <c>Q_RemoveDotSlashes</c> then
    /// <c>Q_strlower</c>. The handle call is engine.dll's file-system vtable +0x118, *interpolated* to be that
    /// function from the SDK. ponytail: <c>..</c> segments are not collapsed (`strtools.cpp:2409`); no sound name
    /// has been seen carrying one.
    /// </remarks>
    public static string SfxKey(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // V_FixSlashes to the Windows separator, then V_RemoveDotSlashes (`strtools.cpp:2315`).
        string fixedSlashes = name.Replace('/', '\\');
        System.Text.StringBuilder key = new(fixedSlashes.Length);

        foreach (char character in fixedSlashes)
        {
            // Double slashes collapse from the second character on (`:2324`).
            if (character == '\\' && key.Length > 1 && key[^1] == '\\')
            {
                continue;
            }

            // Q_strlower is the C runtime's tolower: ASCII only, so no culture applies.
            key.Append(character is >= 'A' and <= 'Z' ? (char)(character + ('a' - 'A')) : character);

            // "\.\" becomes "\" (`:2341`).
            if (key.Length >= 3 && key[^1] == '\\' && key[^2] == '.' && key[^3] == '\\')
            {
                key.Length -= 2;
            }
        }

        // A leading ".\" goes (`:2365`, `:2403`).
        if (key.Length > 1 && key[0] == '.' && key[1] == '\\')
        {
            key.Remove(0, 2);
        }

        return key.ToString();
    }
}
