using System;
using System.Globalization;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// The one value this project needs out of a player class script.
/// </summary>
/// <remarks>
/// A class script is KeyValues, the same brace-and-quoted-pairs text as a VMT or a BSP entity
/// lump. <c>tf_classdata.cpp</c> reads dozens of keys from it — speed, health, sounds, weapon
/// slots — and this reads one, because that is all a viewer needs to draw somebody.
///
/// **Deliberately not a general KeyValues parser.** One already exists for materials, and the
/// class scripts nest deeply enough that a shared reader would have to grow a tree API to serve
/// both. Finding a single top-level key is a smaller and more testable problem than parsing a
/// format nothing else here needs in full.
/// </remarks>
internal static class ClassScript
{
    /// <summary>The <c>model</c> key's value.</summary>
    /// <param name="script">The script's bytes.</param>
    /// <returns>The model path, or <c>null</c> when the script names none.</returns>
    /// <remarks>
    /// **Backslashes are normalised to forward.** Valve's own data mixes them —
    /// <c>models\player\scout.mdl</c> appears in scripts while the VPK indexes forward slashes —
    /// and a lookup with the wrong separator finds nothing and reports nothing.
    ///
    /// Only the first match is taken. A key repeated later in a KeyValues block is an override in
    /// some readers and ignored in others; the engine's own parser answers with the first, which
    /// is what matters for agreeing with it.
    /// </remarks>
    public static string? Model(ReadOnlySpan<byte> script)
    {
        // The scan itself lives in ScriptKeyValue, because the weapon scripts need the same thing
        // for their WeaponType and one copy of the quoting rules is enough.
        return ScriptKeyValue.First(script, "model")?.Replace('\\', '/');
    }

    /// <summary>Whether this class refuses the air-walk animation.</summary>
    /// <param name="script">The script's bytes.</param>
    /// <returns>True when the class never air-walks.</returns>
    /// <remarks>
    /// <c>m_bDontDoAirwalk = ( pKeyValuesData->GetInt( "DontDoAirwalk", 0 ) &gt; 0 )</c>,
    /// <c>tf_classdata.cpp:187</c>. A class that does air-walk plays <c>ACT_MP_AIRWALK</c> while
    /// rising fast instead of the jump, so this decides which of two animations a rocket-jumping
    /// player is drawn with.
    ///
    /// **Greater than zero, not merely non-zero**, which is the engine's own test — a negative
    /// value would read as "does air-walk" there and must here too.
    ///
    /// Absent means false: <c>GetInt</c>'s default is 0, so a script that never mentions the key
    /// describes a class that air-walks.
    /// </remarks>
    public static bool DontDoAirwalk(ReadOnlySpan<byte> script) =>
        ScriptKeyValue.First(script, "DontDoAirwalk") is { } value &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int set) &&
        set > 0;

    /// <summary>Whether this class skips the landing gesture after a jump.</summary>
    /// <param name="script">The decrypted class script.</param>
    /// <returns>Whether the script sets <c>DontDoNewJump</c>.</returns>
    /// <remarks>
    /// **<c>tf_classdata.cpp:188</c>**, read the same way as its neighbour:
    /// <c>m_bDontDoNewJump = ( pKeyValuesData-&gt;GetInt( "DontDoNewJump", 0 ) &gt; 0 )</c>.
    ///
    /// **It gates the LANDING gesture, not the jump.** `CTFPlayerAnimState::HandleJumping` runs
    /// `if ( bNewJump ) RestartGesture( GESTURE_SLOT_JUMP, ACT_MP_JUMP_LAND )`
    /// (`tf_playeranimstate.cpp:1507`), where `bNewJump` is this flag inverted — so a class that
    /// sets it jumps normally and never plays a landing.
    ///
    /// Absent means false, as with air-walk: `GetInt`'s default is 0, so a script that omits the
    /// key describes a class that does land.
    /// </remarks>
    public static bool DontDoNewJump(ReadOnlySpan<byte> script) =>
        ScriptKeyValue.First(script, "DontDoNewJump") is { } value &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int set) &&
        set > 0;

    /// <summary>The first-person hands this class holds its weapons with.</summary>
    /// <param name="script">The decrypted class script.</param>
    /// <returns>The model path, forward-slashed, or <c>null</c> when the script omits the key.</returns>
    /// <remarks>
    /// <c>model_hands</c>, which <c>tf_classdata.cpp:149</c> reads into
    /// <c>m_szHandModelName</c> and <c>CTFPlayerClassShared::GetHandModelName</c> returns.
    ///
    /// **It decides whether a first-person weapon is one model or two.**
    /// <c>CTFWeaponBase::GetViewModel</c> returns THIS instead of the weapon's own viewmodel when
    /// the item attaches to hands, and the weapon then arrives as a separate attachment. When it
    /// does not, the weapon's <c>v_</c> model is the whole viewmodel and already contains hands.
    /// </remarks>
    public static string? Hands(ReadOnlySpan<byte> script) =>
        ScriptKeyValue.First(script, "model_hands")?.Replace('\\', '/');
}
