using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// What the server the demo was recorded against had its ConVars set to, falling back to Valve's
/// declared defaults.
/// </summary>
/// <remarks>
/// **D106's second half.** `EngineConVars` says what Valve declares; this says what the recording
/// actually ran under. Both are needed, and this one outranks the other, because
/// <c>FCVAR_REPLICATED</c> is documented in `public/tier1/iconvar.h` as a *"server setting enforced
/// on clients"*:
///
/// > At signon, the values of all such ConVars are sent from the server to the client … If a value
/// > is changed while a server is active, it's replicated to all connected clients.
///
/// So the precedence is not a preference this project picked. The server's value REPLACES the
/// client's, and for the movement speeds it is stronger still: they are also `FCVAR_CHEAT`, so a
/// player could not have changed them without `sv_cheats` and the watcher's own config has no
/// business in the answer at all.
///
/// **`NET_SetConVar` was decoded and round-tripped long before anything read it.** The message,
/// the writer and the assembly all handled it; no consumer existed. A server that raised
/// `sv_maxspeed` therefore sent the value, this project decoded it correctly, and every reader used
/// a baked constant instead — the value arrives, is right, and is ignored.
///
/// **Why this is not merely tidiness.** The owner: *"the cvars can change by server, some mods will
/// change move speed and all the other settings for the most part, like jailbreak. the only mods we
/// might currently work with are DM and MGE, because those keep most things constant, but jump,
/// surf, and other mods might not run right."* A vanilla competitive server already sends forty
/// values without touching movement; a jump or surf server moves movement itself.
///
/// **Undeclared names are kept rather than dropped.** A real match demo sends forty and this
/// project declares eight. Refusing the rest would throw on an ordinary demo; discarding them would
/// lose the record of what the server was, which is the evidence the mod question needs.
/// </remarks>
public sealed class ServerConVars
{
    private readonly Dictionary<string, string> _replicated = new(StringComparer.Ordinal);

    /// <summary>Parsed values, so a per-frame reader pays a lookup rather than a parse.</summary>
    /// <remarks>
    /// **This is what Valve's `ConVar` already is.** `FullNoClipMove` calls
    /// `sv_maxspeed.GetFloat()` on every move, which reads a float the ConVar cached when its value
    /// was set — the engine parses on assignment, not on use. Reading per frame is therefore parity
    /// rather than waste, and the cache is what makes the two the same shape.
    ///
    /// Cleared on <see cref="Apply"/> rather than updated, because a message carries a handful of
    /// names and rebuilding a handful of floats lazily is cheaper than reasoning about which
    /// entries a partial invalidation may have missed. It happens at signon and on a change, not
    /// per frame.
    /// </remarks>
    private readonly Dictionary<string, float> _numbers = new(StringComparer.Ordinal);

    /// <summary>Applies one <c>svc_SetConVar</c>, as the client would at signon or mid-match.</summary>
    /// <param name="message">The decoded message.</param>
    /// <remarks>
    /// **Later wins**, which is the engine's own behaviour rather than a convenience: a second
    /// message for a name is sent exactly when the value has moved, so keeping the first would show
    /// the wrong half of the demo.
    /// </remarks>
    public void Apply(SetConVarMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        foreach (KeyValuePair<string, string> variable in message.Variables)
        {
            _replicated[variable.Key] = variable.Value;
        }

        _numbers.Clear();
    }

    /// <summary>The value in force, as text.</summary>
    /// <param name="name">The ConVar's engine name.</param>
    /// <returns>
    /// The server's value if it sent one, else Valve's declared default, else null when the name is
    /// neither declared here nor sent.
    /// </returns>
    /// <remarks>
    /// Null rather than empty for the third case, because empty is a legitimate value for a string
    /// ConVar — `sv_downloadurl` is empty by default, and conflating "no such ConVar" with "set to
    /// nothing" is the sentinel trap this project keeps finding.
    /// </remarks>
    public string? Value(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_replicated.TryGetValue(name, out string? sent))
        {
            return sent;
        }

        return EngineConVars.TryByName(name, out EngineConVar? declared) ? declared.Default : null;
    }

    /// <summary>The value in force, as a number.</summary>
    /// <param name="name">The ConVar's engine name.</param>
    /// <returns>The server's value if it sent one, else Valve's declared default.</returns>
    /// <exception cref="KeyNotFoundException">Nothing declares that name, so there is no default.</exception>
    /// <exception cref="FormatException">The value in force is not numeric.</exception>
    /// <remarks>
    /// **Throws in both failure cases rather than returning zero.** A zero `sv_maxspeed` is a camera
    /// that will not move, reported as a symptom with no cause; the engine would refuse the
    /// assignment outright rather than accept it as nought.
    /// </remarks>
    public float Number(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_numbers.TryGetValue(name, out float cached))
        {
            return cached;
        }

        // Asked first so an undeclared name fails as "nobody declared this" rather than as a parse
        // error about the value a server happened to send for it.
        EngineConVar declared = EngineConVars.ByName(name);

        string inForce = _replicated.TryGetValue(name, out string? sent) ? sent : declared.Default;

        if (!float.TryParse(inForce, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            throw new FormatException($"the server set {name} to '{inForce}', which is not a number");
        }

        _numbers[name] = value;

        return value;
    }

    /// <summary>Which declared ConVars this server actually moved off Valve's default.</summary>
    /// <remarks>
    /// **Only the declared ones, and only genuine changes.** A competitive server sends forty values
    /// and most of them match the defaults — reporting those would make every demo look like a mod.
    /// What this answers is the owner's open question: whether a given recording ran under altered
    /// movement, which is what decides whether replaying it faithfully needs more than the defaults.
    ///
    /// Ordinal comparison of the strings rather than of parsed numbers, deliberately: `"320"` and
    /// `"320.0"` are the same speed and a server that sent the second DID retype the value, which is
    /// worth seeing. A false positive here costs a log line; a false negative hides a mod.
    /// </remarks>
    public IReadOnlyList<string> Changed
    {
        get
        {
            List<string> moved = [];

            foreach (EngineConVar declared in EngineConVars.All)
            {
                if (_replicated.TryGetValue(declared.Name, out string? sent) &&
                    !string.Equals(sent, declared.Default, StringComparison.Ordinal))
                {
                    moved.Add(declared.Name);
                }
            }

            return moved;
        }
    }
}
