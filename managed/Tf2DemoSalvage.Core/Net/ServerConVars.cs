using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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
    /// <summary>One server's settings, complete and never edited after it is published.</summary>
    /// <param name="Text">What the server sent, verbatim.</param>
    /// <param name="Numbers">
    /// The same values as the engine reads them: <c>atof</c> of the text, so the number that leads it or zero, clamped to
    /// a declared bound (<c>ConVar::InternalSetValue</c>, <c>convar.cpp:769-808</c>; B775).
    /// </param>
    /// <remarks>
    /// **Read-only types rather than `Dictionary`, so the fault that caused this class to be
    /// rewritten cannot be reintroduced without a compile error.** The whole defect was one
    /// assignment on the read path; typed this way, that assignment does not build. A convention
    /// would not have caught it — the previous version was written by someone who knew the rule.
    /// </remarks>
    private sealed record Settings(
        IReadOnlyDictionary<string, string> Text,
        IReadOnlyDictionary<string, float> Numbers);

    /// <summary>The settings in force, replaced wholesale on <see cref="Apply"/>.</summary>
    /// <remarks>
    /// **A published snapshot rather than a mutable map, because the readers are on another
    /// thread.** The demo is decoded off the UI thread and `svc_SetConVar` arrives with it, while
    /// the free camera reads `sv_maxspeed` every frame on the UI thread. A `Dictionary` written
    /// in place while another thread reads it is undefined, and it does not fail politely: measured
    /// 2026-08-27, the viewer suite threw *"Operations that change non-concurrent collections must
    /// have exclusive access … corrupted its state"* out of a per-frame speed lookup.
    ///
    /// Swapping a whole immutable snapshot means a reader sees either the settings before a message
    /// or the settings after it, never a half-applied mixture, with no lock on the read path at all.
    /// `Apply` allocates; it runs at signon and on a change, not per frame.
    /// </remarks>
    private volatile Settings _state = new(
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, float>(StringComparer.Ordinal));

    /// <summary>Applies one <c>svc_SetConVar</c>, as the client would at signon or mid-match.</summary>
    /// <param name="message">The decoded message.</param>
    /// <remarks>
    /// **Later wins**, which is the engine's own behaviour rather than a convenience: a second
    /// message for a name is sent exactly when the value has moved, so keeping the first would show
    /// the wrong half of the demo.
    ///
    /// **The parse happens here, which is where Valve's happens.** `ConVar::InternalSetValue`
    /// converts to a float on assignment and stashes it in `m_fValue`, so `sv_maxspeed.GetFloat()`
    /// in `FullNoClipMove` reads a field rather than parsing per move. An earlier version of this
    /// class memoised lazily on the read instead — same answers, but a write on the read path, which
    /// is both a departure from the engine's shape and the race described on <see cref="_state"/>.
    /// </remarks>
    public void Apply(SetConVarMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Settings current = _state;

        Dictionary<string, string> text = new(current.Text, StringComparer.Ordinal);
        Dictionary<string, float> numbers = new(current.Numbers, StringComparer.Ordinal);

        foreach (KeyValuePair<string, string> variable in message.Variables)
        {
            text[variable.Key] = variable.Value;

            // `fNewValue = ( float )atof( value ); if ( ClampValue( fNewValue ) ) ...` (`convar.cpp:791-797`): the engine
            // never refuses a value, it reads what number leads the text and clamps it to the declared bounds.
            float parsed = ConVarNumber.GetFloat(variable.Value);

            if (EngineConVars.TryByName(variable.Key, out EngineConVar? declared) && Clamped(declared, parsed) is { } clamped)
            {
                // `Q_snprintf( tempVal, sizeof(tempVal), "%f", fNewValue ); val = tempVal;` (`convar.cpp:794-797`): a
                // clamped value's text is the clamped number, six decimals.
                parsed = clamped;
                text[variable.Key] = parsed.ToString("F6", CultureInfo.InvariantCulture);
            }

            numbers[variable.Key] = parsed;
        }

        // Published only once both maps are complete, so no reader can see one without the other.
        _state = new Settings(text, numbers);
    }

    /// <summary>`ConVar::ClampValue`: the minimum, then the maximum, each only where declared; null when neither moved it.</summary>
    private static float? Clamped(EngineConVar declared, float value)
    {
        if (declared.Min is { } min && value < min)
        {
            return min;
        }

        if (declared.Max is { } max && value > max)
        {
            return max;
        }

        return null;
    }

    /// <summary>The settings in force now, kept apart from later <see cref="Apply"/> calls.</summary>
    /// <returns>A copy sharing this one's immutable snapshot.</returns>
    public ServerConVars Snapshot() => new() { _state = _state };

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

        if (_state.Text.TryGetValue(name, out string? sent))
        {
            return sent;
        }

        return EngineConVars.TryByName(name, out EngineConVar? declared) ? declared.Default : null;
    }

    /// <summary>The value in force, as a number.</summary>
    /// <param name="name">The ConVar's engine name.</param>
    /// <returns>The server's value if it sent one, else Valve's declared default.</returns>
    /// <exception cref="KeyNotFoundException">Nothing declares that name, so there is no default.</exception>
    /// <remarks>
    /// **A sent value is never refused** (B775): the engine reads text that is no number as zero, and a zero `sv_maxspeed`
    /// stops TF2's own roaming camera too (`hltvcamera.cpp:372`). An earlier version threw here, on the belief that the
    /// engine refuses such an assignment; `ConVar::InternalSetValue` does not.
    /// </remarks>
    public float Number(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Asked first so an undeclared name fails as "nobody declared this" rather than as a parse
        // error about the value a server happened to send for it.
        EngineConVar declared = EngineConVars.ByName(name);

        return _state.Numbers.TryGetValue(name, out float sent) ? sent : declared.Number;
    }

    /// <summary>The value in force as `GetInt` reads it: <c>m_nValue = ( int )( fNewValue )</c> (`convar.cpp:802`).</summary>
    /// <param name="name">The ConVar's engine name.</param>
    /// <returns>The float of <see cref="Number"/>, truncated as MSVC's <c>cvttss2si</c> does (<see cref="ConVarNumber"/>).</returns>
    public int GetInt(string name) => ConVarNumber.Truncate(Number(name));

    /// <summary>`GetBool() { return !!GetInt(); }` (`convar.h:520`): so `0.5` is false and `nan` true (B823 review 2).</summary>
    /// <param name="name">The ConVar's engine name.</param>
    /// <returns>Whether <see cref="GetInt"/> is not zero.</returns>
    public bool GetBool(string name) => GetInt(name) != 0;

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
            Settings current = _state;

            return
            [
                .. EngineConVars.All
                    .Where(declared =>
                        current.Text.TryGetValue(declared.Name, out string? sent) &&
                        !string.Equals(sent, declared.Default, StringComparison.Ordinal))
                    .Select(declared => declared.Name),
            ];
        }
    }
}
