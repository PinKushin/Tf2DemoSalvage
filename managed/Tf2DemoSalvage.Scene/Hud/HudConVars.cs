using System;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// `ConVarRef` as the HUD reads one (src/public/tier1/convar.h:506-590): one lookup by name instead of a
/// <see cref="HudState"/> field per console variable.
/// </summary>
/// <param name="Find">The demo's replicated values (the server's), or null where it sent none.</param>
/// <param name="Client">The watcher's own values — their TF2 configs, then this viewer's settings — or null where unset.</param>
/// <param name="ClientAt">
/// The watcher's values as one demo tick's `Cbuf_Execute` left them, or null where that tick reads <paramref name="Client"/>
/// (B839): a frame that plays several ticks runs the console before each, and a game event of an earlier tick is heard
/// before a later tick's run changes anything.
/// </param>
/// <param name="FindAt">
/// The server's values as of one demo tick, every `net_SetConVar` read by then (B843), or null where that tick reads
/// <paramref name="Find"/>: the client applies the message as its packet is parsed. By tick, not by stream position: an
/// event read before a set in the same packet hears the set too (the recorded bound, B843).
/// </param>
public readonly record struct HudConVars(
    Func<string, string?>? Find,
    Func<string, string?>? Client = null,
    Func<int, Func<string, string?>?>? ClientAt = null,
    Func<int, Func<string, string?>?>? FindAt = null)
{
    /// <summary>The values as a demo tick reads them: the server's (<see cref="ServerAt"/>) and <see cref="ClientAt"/>'s for it.</summary>
    /// <param name="tick">The demo tick whose packet is read.</param>
    /// <returns>The ConVars that tick reads.</returns>
    public HudConVars At(int tick)
    {
        HudConVars server = ServerAt(tick);

        return ClientAt?.Invoke(tick) is { } client ? server with { Client = client } : server;
    }

    /// <summary>The values with the server's as a demo tick reads them (<see cref="FindAt"/>), the client's unchanged.</summary>
    /// <param name="tick">The demo tick whose packet is read.</param>
    /// <returns>The ConVars with that tick's server values.</returns>
    /// <remarks>A replay after a seek reads every packet to the tick (`demo_gototick`), so its events hear this too (B843).</remarks>
    public HudConVars ServerAt(int tick) => FindAt?.Invoke(tick) is { } find ? this with { Find = find } : this;

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
    public float GetFloat(string name) => ConVarNumber.GetFloat(GetString(name));

    /// <summary>`GetInt()`: `m_nValue = ( int )( fNewValue )` (convar.cpp:802) — the float, truncated as MSVC does.</summary>
    /// <param name="name">The ConVar's engine name.</param>
    /// <returns>The value as an int; <see cref="int.MinValue"/> for NaN, as `cvttss2si` gives (B823 review 1).</returns>
    public int GetInt(string name) => ConVarNumber.GetInt(GetString(name));

    /// <summary>`GetBool() { return !!GetInt(); }` (convar.h:520).</summary>
    /// <param name="name">The ConVar's engine name.</param>
    /// <returns>Whether the int value is non-zero.</returns>
    public bool GetBool(string name) => ConVarNumber.GetBool(GetString(name));
}
