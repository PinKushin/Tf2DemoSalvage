using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>One ConVar as the engine declares it: a name, a default, and who may change it.</summary>
/// <param name="Name">The engine's own name, exactly as it appears on the wire.</param>
/// <param name="Default">Valve's default, as the STRING the declaration carries.</param>
/// <param name="Replicated">Whether <c>FCVAR_REPLICATED</c> is set — a server may enforce it.</param>
/// <param name="Cheat">Whether <c>FCVAR_CHEAT</c> is set — a player cannot change it themselves.</param>
/// <param name="UserInfo">
/// Whether <c>FCVAR_USERINFO</c> is set — the RECORDER's value travels in the <c>userinfo</c> string
/// table, so a demo carries it even though no server sent it.
/// </param>
/// <remarks>
/// **The default is a string because Valve's is a string.** `ConVar( "sv_maxspeed", "320", … )`
/// takes a literal, and the engine parses it on demand — so a declaration that stored `320f` would
/// have made a decision the engine leaves open, and would be unable to carry `sv_downloadurl`.
/// <see cref="Number"/> is the parse, at the point of use.
/// </remarks>
public sealed record EngineConVar(
    string Name, string Default, bool Replicated, bool Cheat, bool UserInfo = false)
{
    /// <summary>The default as a number, for the ones that are numbers.</summary>
    /// <exception cref="FormatException">The default is not numeric, so a caller asked wrongly.</exception>
    /// <remarks>
    /// **Throws rather than returning zero.** A zero `sv_maxspeed` is a camera that does not move
    /// and a reason nobody can find; the memory directory calls this the sentinel trap. Invariant
    /// culture because a ConVar's default is engine text, not the reader's locale — `"0.1"` is
    /// `snd_mixahead` on every machine.
    /// </remarks>
    public float Number => float.Parse(Default, CultureInfo.InvariantCulture);
}

/// <summary>
/// What Valve declares for the ConVars this viewer depends on, rather than the numbers it used to
/// bake.
/// </summary>
/// <remarks>
/// **D106: nothing is hardcoded that Valve does not hardcode.** The owner, auditing the convar
/// surface under D104:
///
/// > *"the vast majority of the cvars are going to be constants that never change, but i still dont
/// > want anything hardcoded that valve does not hard code, because doing so makes the rendering
/// > engine we are using less agnostic"*
///
/// and, on the form that takes:
///
/// > *"baked default is never the right answer i dont think, at least not if its not a baked default
/// > valve has"*
///
/// **A default is right as a VALUE and wrong as a `const float`.** Valve wrote three things — a
/// name, a default, and the ability for a server to change it. Copying only the number discards the
/// two that make it portable across the nineteen years of builds this project reads, and it fails
/// silently: a server that raised `sv_maxspeed` sends the new value, this project decodes it, and a
/// baked constant ignores it.
///
/// **Every entry here was read from two sources thirteen years apart** — `source-sdk-2013` and the
/// installed build's own `tf/cvarlist.log` — and `MovementConVarConformanceTests` checks both on
/// every run. They agree today; the test is what notices if a TF2 update moves one.
///
/// **What this deliberately is not.** It is not a registry of everything TF2 ships (that is
/// `cvarlist.log`, 3,668 entries, and `docs/CVAR-COVERAGE.md` is the map of it). It holds only the
/// ConVars whose VALUE this project depends on, which is the set that was invisible to
/// `CvarNameConformanceTests` — that test measures names this viewer answers to, and a convar we
/// silently depend on never appears in it.
/// </remarks>
public static class EngineConVars
{
    /// <summary>
    /// The movement and spectator speeds, all replicated, all cheat-protected.
    /// </summary>
    /// <remarks>
    /// **The three `cl_` speeds are the surprise.** Despite the prefix they are
    /// `FCVAR_REPLICATED | FCVAR_CHEAT` — server-controlled, and a player cannot change them
    /// without `sv_cheats`. So for these the watcher's own config does not enter the answer at all:
    /// `FCVAR_REPLICATED` is documented in `iconvar.h` as *"server setting enforced on clients"*,
    /// and *"if a change is requested it must come from the console"*.
    ///
    /// **`in_main.cpp` declares each of them twice**, and the first is Counter-Strike's. Lines
    /// 70–73 are the `CSTRIKE_DLL` branch — 400, `FCVAR_CHEAT`, not replicated — and 76–79 are the
    /// `#else` that TF2 compiles. A grep that takes the first match gets the wrong game.
    /// </remarks>
    private static readonly EngineConVar[] Declarations =
    [
        // src/game/client/in_main.cpp:76-79, the non-CSTRIKE branch.
        new("cl_forwardspeed", "450", Replicated: true, Cheat: true),
        new("cl_backspeed", "450", Replicated: true, Cheat: true),
        new("cl_sidespeed", "450", Replicated: true, Cheat: true),
        new("cl_upspeed", "320", Replicated: true, Cheat: true),

        // src/game/shared/movevars_shared.cpp:47-52.
        new("sv_maxspeed", "320", Replicated: true, Cheat: false),
        new("sv_specspeed", "3", Replicated: true, Cheat: false),
        new("sv_specaccelerate", "5", Replicated: true, Cheat: false),
        new("sv_specnoclip", "1", Replicated: true, Cheat: false),

        // **The interpolation set, and the effective amount is none of them on its own.**
        // `GetClientInterpAmount()` in src/game/client/cdll_bounded_cvars.cpp:126 is
        // `MAX( cl_interp, cl_interp_ratio / cl_updaterate )`, and each half is separately clamped
        // by the server's two ratio bounds. Declared together because a reader that takes one and
        // not the others gets a plausible number and the wrong one — see
        // InterpolationConVarConformanceTests, which writes the formula down without implementing
        // it. Nothing consumes these yet, deliberately.
        new("cl_interp", "0.1", Replicated: false, Cheat: false, UserInfo: true),
        new("cl_interp_ratio", "2.0", Replicated: false, Cheat: false, UserInfo: true),
        new("cl_updaterate", "20", Replicated: false, Cheat: false, UserInfo: true),
        new("sv_client_min_interp_ratio", "1", Replicated: true, Cheat: false),
        new("sv_client_max_interp_ratio", "5", Replicated: true, Cheat: false),

        // **The sound curve's parameters** (`SoundGain`). All four read out of `engine.dll`'s own
        // ConVar registrations, which is the only source that has them: the engine-side sound
        // cvars are absent from the whole SDK checkout.
        new("snd_refdist", "36", Replicated: false, Cheat: true),
        new("snd_refdb", "60", Replicated: false, Cheat: true),
        new("snd_gain", "1", Replicated: false, Cheat: true),
        new("snd_gain_min", "0.01", Replicated: false, Cheat: true),

        // Replicated, declared, and read by nothing yet — the shape is the point (D106).
        // `sv_downloadurl` is the one with a use waiting: a demo that carries it names where its
        // own map came from, which `MapDownloader` currently substitutes a public mirror for.
        new("sv_cheats", "0", Replicated: true, Cheat: false),
        new("host_timescale", "1", Replicated: true, Cheat: false),
        new("sv_downloadurl", "0", Replicated: true, Cheat: false),

        // Client-only: the watcher's, so the source is their config and the fallback is this.
        // src/game/client/vgui_fpspanel.cpp:28, clientleafsystem.cpp:32, c_baseplayer.cpp:118.
        new("cl_showpos", "0", Replicated: false, Cheat: false),
        new("cl_drawleaf", "-1", Replicated: false, Cheat: true),
        new("cl_first_person_uses_world_model", "0", Replicated: false, Cheat: false),
    ];

    private static readonly Dictionary<string, EngineConVar> ByEngineName =
        BuildIndex(Declarations);

    /// <summary>Every ConVar this project depends on the value of.</summary>
    public static IReadOnlyList<EngineConVar> All => Declarations;

    /// <summary>The declaration for one ConVar.</summary>
    /// <param name="name">The engine's name for it.</param>
    /// <returns>What Valve declares.</returns>
    /// <exception cref="KeyNotFoundException">Nothing here declares that name.</exception>
    /// <remarks>
    /// **Throws for an unknown name rather than inventing a declaration.** A caller asking for a
    /// ConVar nobody declared has a typo or a missing entry, and both are defects; handing back an
    /// empty default would turn either into a plausible number somewhere far away.
    ///
    /// Ordinal comparison, because ConVar names are engine identifiers rather than words — the same
    /// reason the wire compares them byte for byte.
    /// </remarks>
    public static EngineConVar ByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return ByEngineName.TryGetValue(name, out EngineConVar? found)
            ? found
            : throw new KeyNotFoundException(
                $"no engine ConVar named '{name}' is declared here; add it to EngineConVars with " +
                "its default read from the SDK and from cvarlist.log");
    }

    /// <summary>The declaration for one ConVar, when there is one.</summary>
    /// <param name="name">The engine's name for it.</param>
    /// <param name="declared">What Valve declares, or null.</param>
    /// <returns>Whether anything here declares that name.</returns>
    /// <remarks>
    /// **For the caller that has a name off the wire rather than one it chose.** A demo sends
    /// whatever the server changed — forty values on a real match, against the eight declared here —
    /// so asking about an arbitrary name is ordinary rather than a defect, and
    /// <see cref="ByName"/>'s throw would be wrong for it.
    /// </remarks>
    public static bool TryByName(
        string name, [NotNullWhen(true)] out EngineConVar? declared)
    {
        ArgumentNullException.ThrowIfNull(name);

        return ByEngineName.TryGetValue(name, out declared);
    }

    private static Dictionary<string, EngineConVar> BuildIndex(EngineConVar[] declarations)
    {
        Dictionary<string, EngineConVar> index = new(declarations.Length, StringComparer.Ordinal);

        foreach (EngineConVar declaration in declarations)
        {
            index.Add(declaration.Name, declaration);
        }

        return index;
    }
}
