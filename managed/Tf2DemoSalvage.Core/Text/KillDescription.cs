using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// Turns the numeric qualifiers on a death event into words.
/// </summary>
/// <remarks>
/// **Every field on <c>player_death</c> was already decoded and printed; none of it was
/// interpreted.** A death rendered as <c>customkill=1 damagebits=34603010 death_flags=0</c>, which
/// is the raw truth and tells a reader nothing. <c>customkill=1</c> is a headshot, and that is the
/// single most interesting thing about the kill.
///
/// **Only a named subset is translated, deliberately.** TF2 declares **87** custom kill values, most
/// of them individual taunt kills that change with every update. Transcribing all of them here would
/// be a maintenance burden buying almost nothing, and would go stale silently — the failure mode
/// this project keeps finding elsewhere.
///
/// So: **name the values that change how a kill reads, and pass the rest through as numbers.** A
/// number is honest, where a wrong name is not, and nothing branches on the result — it is text for
/// a person. That is not the "fallbacks do not make guesses safe" hazard, because no premise about
/// meaning is being smuggled in; an unnamed value is reported as unnamed.
///
/// Values are pinned against the SDK by the conformance suite rather than trusted here.
/// </remarks>
public static class KillDescription
{
    /// <summary>Custom kill values worth naming, from <c>ETFDmgCustom</c>.</summary>
    /// <remarks>
    /// The head of the enumeration, plus the handful further down a reader would recognise.
    /// Deliberately not all 87.
    ///
    /// **These were transcribed wrongly on the first attempt and the conformance test caught it
    /// before it shipped.** `TF_DMG_CUSTOM_BURNING_FLARE` was written as 5 by counting lines in a
    /// filtered grep; it is 8. Five is `TF_DMG_CUSTOM_MINIGUN`, so a kill feed would have called
    /// every minigun kill a flare — a wrong name, confidently printed, which is exactly the failure
    /// the numeric passthrough exists to avoid and which transcription reintroduced.
    ///
    /// Worth noting the trap that caused it: <c>TF_DMG_WRENCH_FIX</c> sits at 4 **without the
    /// `CUSTOM_` infix**, so a grep for `TF_DMG_CUSTOM_` skips it and every value after it shifts by
    /// one. Third variety of prefix trap in this project, after a duration among the death flags and
    /// a substring match inside ordinary words.
    /// </remarks>
    private static readonly Dictionary<int, string> NamedKills = new()
    {
        [1] = "headshot",
        [2] = "backstab",
        [3] = "burning",
        [5] = "minigun",
        [6] = "suicide",
        [8] = "burning flare",
    };

    /// <summary>Death flag bits, from the <c>TF_DEATH_*</c> defines in <c>tf_shareddefs.h</c>.</summary>
    /// <remarks>
    /// Eleven single-bit values. <c>TF_DEATH_ANIMATION_TIME</c> is NOT among them despite sharing
    /// the prefix — it is a duration in seconds, and a prefix is a naming convention rather than a
    /// category.
    /// </remarks>
    private static readonly (int Bit, string Name)[] DeathFlagNames =
    [
        (0x0001, "domination"),
        (0x0002, "assister domination"),
        (0x0004, "revenge"),
        (0x0008, "assister revenge"),
        (0x0010, "first blood"),
        (0x0020, "feign death"),
        (0x0040, "interrupted"),
        (0x0080, "gibbed"),
        (0x0100, "purgatory"),
        (0x0200, "miniboss"),
        (0x0400, "australium"),
    ];

    /// <summary>Damage bits worth naming, with TF2's meanings where it overrides the engine's.</summary>
    /// <remarks>
    /// **TF2 aliases thirteen engine bits to different meanings** (`tf_shareddefs.h:1162-1175`), so
    /// the base-game names are wrong in a TF2 demo. `DMG_CRITICAL` is `DMG_ACID`,
    /// `DMG_USE_HITLOCATIONS` is `DMG_AIRBOAT`, `DMG_MELEE` is `DMG_BLAST_SURFACE`, `DMG_IGNITE` is
    /// `DMG_PLASMA`. A decoder using the engine's names prints "acid" and "airboat" for an ordinary
    /// critical headshot.
    ///
    /// **Two aliases are genuinely ambiguous and keep the engine meaning here.**
    /// `DMG_IGNORE_MAXHEALTH` is `DMG_BULLET` and `DMG_IGNORE_DEBUFFS` is `DMG_SLASH`, so bit 1
    /// means both "shot" and "ignore max health" with nothing in the word to separate them. The
    /// damage KIND is the useful half for a reader; the modifier is not recoverable from this field
    /// alone, so it is not guessed at.
    /// </remarks>
    private static readonly (int Bit, string Name)[] DamageNames =
    [
        (1 << 0, "crush"),
        (1 << 1, "bullet"),
        (1 << 2, "slash"),
        (1 << 3, "burn"),
        (1 << 5, "fall"),
        (1 << 6, "blast"),
        (1 << 7, "club"),
        (1 << 8, "shock"),
        (1 << 10, "radius max"),
        (1 << 14, "drown"),
        (1 << 17, "no close distance mod"),
        (1 << 18, "half falloff"),
        (1 << 20, "critical"),
        (1 << 21, "use distance mod"),
        (1 << 24, "ignite"),
        (1 << 25, "hit locations"),
        (1 << 26, "not counted toward crit rate"),
        (1 << 27, "melee"),
        (1 << 28, "direct"),
        (1 << 29, "buckshot"),
    ];

    /// <summary>Describes the damage word on a death or hurt event.</summary>
    /// <param name="damageBits">The event's <c>damagebits</c> field.</param>
    /// <returns>A comma-separated list of the bits set, or <c>null</c> when none are.</returns>
    /// <remarks>
    /// Named in TF2's terms, not the engine's — see <see cref="DamageNames"/>. Unknown bits are
    /// reported as their mask so a future flag is visible rather than dropped.
    /// </remarks>
    public static string? DamageTypes(int damageBits)
    {
        if (damageBits == 0)
        {
            return null;
        }

        StringBuilder described = new();
        int remaining = damageBits;

        foreach ((int bit, string name) in DamageNames)
        {
            if ((damageBits & bit) == 0)
            {
                continue;
            }

            Append(described, name);
            remaining &= ~bit;
        }

        for (int bit = 1; bit != 0 && remaining != 0; bit <<= 1)
        {
            if ((remaining & bit) == 0)
            {
                continue;
            }

            Append(described, string.Create(CultureInfo.InvariantCulture, $"bit 0x{bit:X8}"));
            remaining &= ~bit;
        }

        return described.ToString();
    }

    /// <summary>Describes how a kill was made, when it was anything but ordinary.</summary>
    /// <param name="customKill">The event's <c>customkill</c> field.</param>
    /// <returns>
    /// A word for a named kind, <c>custom N</c> for an unnamed one, or <c>null</c> for an ordinary
    /// kill.
    /// </returns>
    /// <remarks>
    /// **Null rather than "none" for the ordinary case**, so the caller decides how an absent
    /// qualifier reads — most kills are ordinary and a line reading "none" every time is noise.
    /// </remarks>
    public static string? CustomKill(int customKill)
    {
        // TF_DMG_CUSTOM_NONE. Zero means "nothing special", not "unknown".
        if (customKill == 0)
        {
            return null;
        }

        return NamedKills.TryGetValue(customKill, out string? name)
            ? name
            : string.Create(CultureInfo.InvariantCulture, $"custom {customKill}");
    }

    /// <summary>Describes the death flag word.</summary>
    /// <param name="deathFlags">The event's <c>death_flags</c> field.</param>
    /// <returns>A comma-separated list of set flags, or <c>null</c> when none are set.</returns>
    /// <remarks>
    /// **A bit field, so more than one can be set and the description says so.** A kill can be a
    /// domination AND a first blood; anything treating this word as an enumeration reports one of
    /// them and loses the other.
    ///
    /// **Unknown bits are reported rather than dropped.** A future TF2 flag would otherwise be
    /// invisible here rather than merely unnamed, which is the difference between a reader knowing
    /// there is something to look up and not.
    /// </remarks>
    public static string? DeathFlags(int deathFlags)
    {
        if (deathFlags == 0)
        {
            return null;
        }

        StringBuilder described = new();
        int remaining = deathFlags;

        foreach ((int bit, string name) in DeathFlagNames)
        {
            if ((deathFlags & bit) == 0)
            {
                continue;
            }

            Append(described, name);
            remaining &= ~bit;
        }

        // Whatever is left is a bit this project does not know about. Reported one bit at a time so
        // the number in the text is the bit itself rather than a sum a reader has to decompose.
        for (int bit = 1; bit != 0 && remaining != 0; bit <<= 1)
        {
            if ((remaining & bit) == 0)
            {
                continue;
            }

            Append(described, string.Create(CultureInfo.InvariantCulture, $"flag 0x{bit:X4}"));
            remaining &= ~bit;
        }

        return described.ToString();
    }

    private static void Append(StringBuilder described, string name)
    {
        if (described.Length > 0)
        {
            described.Append(", ");
        }

        described.Append(name);
    }
}
