using System;
using System.Collections.Generic;
using System.Text;

namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// Renders one <c>player_death</c> the way the game's own kill feed reads.
/// </summary>
/// <remarks>
/// **Shape: attacker [weapon] victim**, with qualifiers in parentheses after. That is the game's
/// layout, and matching it means a reader does not have to learn a second convention to read a
/// match.
///
/// Everything here comes from fields the decoder already produced. The work is entirely in deciding
/// what to say when a field is missing — which is most of what makes a feed readable, and all of
/// what makes it honest.
///
/// **Three cases where the obvious rendering would assert something the demo does not say:**
///
/// - **No attacker at all.** A fall or a trigger kills with no killer. Rendered as the victim dying,
///   not attributed to anyone.
/// - **Attacker is the victim.** A suicide. Rendering "medic [world] medic" reads as someone else's
///   kill of a similarly-named player.
/// - **An empty assister.** The field is present and blank far more often than it is filled, so a
///   naive "(assist )" would appear on most lines.
/// </remarks>
public static class KillFeed
{
    /// <summary>Renders a death event's fields as a single line.</summary>
    /// <param name="fields">The event's decoded fields.</param>
    /// <returns>A line in the game's kill feed shape.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fields"/> is null.</exception>
    public static string Line(IReadOnlyList<KeyValuePair<string, object?>> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        string victim = Text(fields, "userid");
        string attacker = Text(fields, "attacker");
        string weapon = Text(fields, "weapon");
        string assister = Text(fields, "assister");

        StringBuilder line = new();

        // **Suicide first, because it is the case the general form gets wrong**, not because it is
        // common. The attacker field is filled in with the victim, so every later branch would
        // treat it as an ordinary kill by a player of the same name.
        bool suicide = attacker.Length > 0 && string.Equals(attacker, victim, StringComparison.Ordinal);

        if (attacker.Length == 0)
        {
            // No killer. The demo does not say who did it, so neither does this.
            line.Append(victim).Append(" died");
        }
        else if (suicide)
        {
            line.Append(victim);
        }
        else
        {
            line.Append(attacker);
        }

        if (weapon.Length > 0)
        {
            line.Append(" [").Append(weapon).Append(']');
        }

        if (attacker.Length > 0 && !suicide)
        {
            line.Append(' ').Append(victim);
        }

        List<string> notes = [];

        if (suicide)
        {
            notes.Add("suicide");
        }

        if (Number(fields, "customkill") is { } customKill &&
            KillDescription.CustomKill(customKill) is { } named)
        {
            notes.Add(named);
        }

        if (Number(fields, "death_flags") is { } deathFlags &&
            KillDescription.DeathFlags(deathFlags) is { } flagged)
        {
            notes.Add(flagged);
        }

        // **An assister of -1 is nobody, and the field is always present.** So "was there an
        // assist" cannot be answered by presence, only by value — and the value meaning nobody is
        // not zero, because zero is a legitimate user id.
        //
        // Found by reading 407 real kills, most of which rendered "(assist -1)". A resolved player
        // never renders as a bare negative number, so testing the rendered text is safe here.
        if (assister.Length > 0 && !IsAbsentReference(assister))
        {
            notes.Add($"assist {assister}");
        }

        if (notes.Count > 0)
        {
            line.Append(" (").AppendJoin(", ", notes).Append(')');
        }

        return line.ToString();
    }

    /// <summary>Whether a rendered player reference is the "nobody" sentinel.</summary>
    /// <remarks>
    /// A negative user id. Checked on the rendered text because a resolved player renders as
    /// <c>name(id)</c> and an unresolved one as the bare number, so a leading minus can only be the
    /// sentinel.
    /// </remarks>
    private static bool IsAbsentReference(string rendered) =>
        rendered.StartsWith('-') && int.TryParse(rendered, out int id) && id < 0;

    /// <summary>A field's value as text, or empty when it is absent or blank.</summary>
    private static string Text(IReadOnlyList<KeyValuePair<string, object?>> fields, string key)
    {
        foreach (KeyValuePair<string, object?> field in fields)
        {
            if (string.Equals(field.Key, key, StringComparison.Ordinal))
            {
                return field.Value?.ToString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    /// <summary>A field's value as a number, whatever integral width it was sent as.</summary>
    /// <remarks>
    /// **Every width, because the event definition chooses it** — <c>customkill</c> is a byte and
    /// <c>death_flags</c> a short. Matching on <c>int</c> alone silently matches neither, which
    /// shipped once already in the dumper's annotation.
    /// </remarks>
    private static int? Number(IReadOnlyList<KeyValuePair<string, object?>> fields, string key)
    {
        foreach (KeyValuePair<string, object?> field in fields)
        {
            if (!string.Equals(field.Key, key, StringComparison.Ordinal))
            {
                continue;
            }

            return field.Value switch
            {
                int whole => whole,
                short small => small,
                byte tiny => tiny,
                _ => null,
            };
        }

        return null;
    }
}
