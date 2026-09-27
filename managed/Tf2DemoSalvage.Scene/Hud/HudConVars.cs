using System;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// `ConVarRef` as the HUD reads one (src/public/tier1/convar.h:506-590): one lookup by name instead of a
/// <see cref="HudState"/> field per console variable.
/// </summary>
/// <param name="Find">The demo's replicated values (the server's), or null where it sent none.</param>
/// <param name="Client">The watcher's own values — their TF2 configs, then this viewer's settings — or null where unset.</param>
public readonly record struct HudConVars(Func<string, string?>? Find, Func<string, string?>? Client = null)
{
    /// <summary>`CEmptyConVar() : ConVar( "", "0" )` (convar.cpp:1246): what a ref to an unregistered name reads.</summary>
    private const string EmptyConVarValue = "0";

    /// <summary>`GetString()`: a `FCVAR_REPLICATED` var holds the server's value, any other the client's own.</summary>
    /// <param name="name">The ConVar's engine name.</param>
    /// <returns>The value in force, else the declared default, else "0".</returns>
    public string GetString(string name)
    {
        if (!EngineConVars.TryByName(name, out EngineConVar? declared))
        {
            return Find?.Invoke(name) ?? Client?.Invoke(name) ?? EmptyConVarValue;
        }

        return (declared.Replicated ? Find?.Invoke(name) : Client?.Invoke(name)) ?? declared.Default;
    }

    /// <summary>`GetFloat()`: `m_fValue = ( float )atof( value )` (convar.cpp:792).</summary>
    /// <param name="name">The ConVar's engine name.</param>
    /// <returns>The value as a float, 0 when it is not a number.</returns>
    public float GetFloat(string name) => PanelLayout.Atof(GetString(name));

    /// <summary>`GetInt()`: `m_nValue = ( int )( fNewValue )` (convar.cpp:802) — the float, truncated.</summary>
    /// <param name="name">The ConVar's engine name.</param>
    /// <returns>The value as an int.</returns>
    public int GetInt(string name) => (int)GetFloat(name);

    /// <summary>`GetBool() { return !!GetInt(); }` (convar.h:520).</summary>
    /// <param name="name">The ConVar's engine name.</param>
    /// <returns>Whether the int value is non-zero.</returns>
    public bool GetBool(string name) => GetInt(name) != 0;
}
