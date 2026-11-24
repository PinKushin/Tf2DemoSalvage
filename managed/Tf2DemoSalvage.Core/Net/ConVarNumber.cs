using System;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>A ConVar's number from the string it holds, as `ConVar::InternalSetValue` makes it (B823).</summary>
/// <remarks>
/// <code>
/// // src/tier1/convar.cpp:791-802
/// fNewValue = ( float )atof( value );
/// m_fValue = fNewValue;
/// m_nValue = ( int )( fNewValue );
/// </code>
/// **The one place a ConVar's text becomes a number**, so every reader — the viewer's settings, the console's `toggle`,
/// `incrementvar`, `multvar` and `sv_cheats`, a server's `net_SetConVar`, the HUD's `ConVarRef`, the holidays, the
/// sidecar — agrees with `GetFloat` and `GetInt`. A `TryParse` that refuses `1 2` sets nothing where TF2 sets 1; `atof`
/// takes the longest numeric prefix and is 0 for none. Clamping to declared bounds is the caller's, since `ClampValue`
/// runs between the first two lines above.
/// </remarks>
public static class ConVarNumber
{
    /// <summary>`GetFloat`: <c>(float)atof(value)</c>.</summary>
    /// <param name="value">The ConVar's string.</param>
    /// <returns>The float.</returns>
    public static float GetFloat(string value) => (float)CStdlib.Atof(value);

    /// <summary>`GetInt`: <c>(int)(float)atof(value)</c>, truncated as MSVC's <c>cvttss2si</c> does.</summary>
    /// <param name="value">The ConVar's string.</param>
    /// <returns>The int; <see cref="int.MinValue"/> for infinity, NaN or past an int's range.</returns>
    public static int GetInt(string value) => Truncate(GetFloat(value));

    /// <summary>`GetBool() { return !!GetInt(); }` (`convar.h:520`): `0.5` false, `nan` true.</summary>
    /// <param name="value">The ConVar's string.</param>
    /// <returns>Whether <see cref="GetInt"/> is not zero.</returns>
    public static bool GetBool(string value) => GetInt(value) != 0;

    /// <summary>Whether <c>atof</c> finds any number at the start of the text.</summary>
    /// <param name="value">The text.</param>
    /// <returns>True when <c>strtod</c>'s end pointer moves.</returns>
    public static bool ReadsANumber(string value)
    {
        _ = CStdlib.Atof(value, out int used);

        return used > 0;
    }

    /// <summary>Whether the text is one number to <c>atof</c>, then at most a C float suffix, then only C white space.</summary>
    /// <param name="value">The text.</param>
    /// <returns>
    /// True for <c>16</c>, <c>0x10</c>, <c>inf</c>, <c>0.1f</c> and <c>1L</c>; false for <c>255 255 255</c>, <c>1 x</c>,
    /// <c>2fort</c>, <c>infantry</c> and <c>nano</c>.
    /// </returns>
    /// <remarks>
    /// B823 reviews 4 and 5 (Fable): `0.1f` is the number <see cref="EngineConVar.Number"/> reads, 0.1, so a caller asking
    /// whether a default is numeric must agree; a suffix is the one C allows a float literal (`f`, `F`, `l`, `L`), directly
    /// after the number. Anything else after it — a second number, a word, a suffix after a space — makes the text a string.
    /// </remarks>
    public static bool HoldsOneNumber(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _ = CStdlib.Atof(value, out int used);

        if (used == 0)
        {
            return false;
        }

        ReadOnlySpan<char> rest = value.AsSpan(used);

        if (rest.Length > 0 && rest[0] is 'f' or 'F' or 'l' or 'L')
        {
            rest = rest[1..];
        }

        return rest.Trim([' ', '\t', '\n', '\v', '\f', '\r']).IsEmpty;
    }

    /// <summary>`m_nValue = ( int )( fNewValue )` on a float already made — a clamped one, say.</summary>
    /// <param name="value">The ConVar's float.</param>
    /// <returns>The int, as <see cref="GetInt"/> gives it.</returns>
    public static int Truncate(float value) => CStdlib.Truncate(value);
}
