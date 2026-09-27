using System;
using System.Globalization;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// `ConVarRef` as the HUD reads one (src/public/tier1/convar.h:506-590): one lookup by name instead of a
/// <see cref="HudState"/> field per console variable.
/// </summary>
/// <param name="Find">
/// `g_pCVar->FindVar( name )->GetString()` (convar.cpp:1269): the value in force — for a replicated var the server's, off
/// the demo — or null when nothing set it, which falls to Valve's declared default in <see cref="EngineConVars"/>.
/// </param>
public readonly record struct HudConVars(Func<string, string?>? Find)
{
    /// <summary>`CEmptyConVar() : ConVar( "", "0" )` (convar.cpp:1246): what a ref to an unregistered name reads.</summary>
    private const string EmptyConVarValue = "0";

    /// <summary>`GetString()`.</summary>
    /// <param name="name">The ConVar's engine name.</param>
    /// <returns>The value in force, else the declared default, else "0".</returns>
    public string GetString(string name) =>
        Find?.Invoke(name) ??
        (EngineConVars.TryByName(name, out EngineConVar? declared) ? declared.Default : EmptyConVarValue);

    /// <summary>`GetFloat()`: `m_fValue = ( float )atof( value )` (convar.cpp:792).</summary>
    /// <param name="name">The ConVar's engine name.</param>
    /// <returns>The value as a float, 0 when it is not a number.</returns>
    // ponytail: whole-string parse, not atof's leading-prefix parse ("3abc" reads 0, atof 3); no HUD var carries such text.
    public float GetFloat(string name) =>
        float.TryParse(GetString(name), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0f;

    /// <summary>`GetInt()`: `m_nValue = ( int )( fNewValue )` (convar.cpp:802) — the float, truncated.</summary>
    /// <param name="name">The ConVar's engine name.</param>
    /// <returns>The value as an int.</returns>
    public int GetInt(string name) => (int)GetFloat(name);

    /// <summary>`GetBool() { return !!GetInt(); }` (convar.h:520).</summary>
    /// <param name="name">The ConVar's engine name.</param>
    /// <returns>Whether the int value is non-zero.</returns>
    public bool GetBool(string name) => GetInt(name) != 0;
}
