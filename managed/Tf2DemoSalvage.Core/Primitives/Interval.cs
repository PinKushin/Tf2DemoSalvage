using System;

namespace Tf2DemoSalvage.Core.Primitives;

/// <summary>Valve's <c>interval_t</c>: a value written as one number or as a range, drawn when it is used.</summary>
/// <param name="Start">The first number.</param>
/// <param name="Range">The second number minus the first, or 0 when there was one — and NEGATIVE for a descending pair.</param>
/// <remarks>
/// **Published source, <c>game/shared/interval.cpp:21-59</c>**, which soundscapes read every variable value through
/// (B462), and game_sounds scripts too before their storage narrows it (B487, <c>SoundScript</c>). It is <c>atof</c> of
/// <c>strtok</c> tokens — a word is zero and <c>"110,90"</c> is a start of 110 and a range of −20.
///
/// *Not reproduced:* <c>ReadInterval</c> copies into a 128-byte buffer first, so a value past 127 characters is cut.
/// </remarks>
public readonly record struct Interval(float Start, float Range)
{
    /// <summary><c>ReadInterval</c>: the first two comma-separated tokens, through <c>atof</c>.</summary>
    /// <param name="text">The text as the script wrote it.</param>
    /// <returns>The interval; zero and zero when no token is present.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <remarks>
    /// <c>strtok( tempString, "," )</c> never answers an empty token, so leading and doubled commas vanish — which
    /// <see cref="StringSplitOptions.RemoveEmptyEntries"/> reproduces exactly.
    /// </remarks>
    public static Interval Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string[] tokens = text.Split(',', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            return default;
        }

        // `tmp.start = atof( token )` narrows on assignment; `tmp.range = atof( token ) - tmp.start` subtracts in double —
        // the float start promoted — and narrows only the difference (`interval.cpp:34,38`).
        float start = (float)CStdlib.Atof(tokens[0]);

        return new Interval(start, tokens.Length > 1 ? (float)(CStdlib.Atof(tokens[1]) - start) : 0f);
    }

    /// <summary><c>RandomInterval</c>: the start, plus one draw over the range when there is one.</summary>
    /// <param name="random">The stream to draw from.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is null.</exception>
    /// <remarks><c>if ( interval.range != 0 )</c> guards the draw, so a fixed value leaves the stream where it was.</remarks>
    public float Random(UniformRandomStream random)
    {
        ArgumentNullException.ThrowIfNull(random);

        return Range != 0f ? Start + random.RandomFloat(0f, Range) : Start;
    }
}
