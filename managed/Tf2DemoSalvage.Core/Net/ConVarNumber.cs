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
/// `incrementvar`, `multvar` and `sv_cheats`, a server's `net_SetConVar` — agrees with `GetFloat` and `GetInt`. A
/// `TryParse` that refuses `1 2` sets nothing where TF2 sets 1; `atof` takes the longest numeric prefix and is 0 for none.
/// Clamping to declared bounds is the caller's, since `ClampValue` runs between the two lines above.
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
    public static int GetInt(string value) => CStdlib.Truncate(GetFloat(value));
}
