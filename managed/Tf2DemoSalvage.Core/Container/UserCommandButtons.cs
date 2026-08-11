using System;
using System.Globalization;
using System.Text;

namespace Tf2DemoSalvage.Core.Container;

/// <summary>
/// Names the bits of a <c>CUserCmd</c>'s <c>buttons</c> field.
/// </summary>
/// <remarks>
/// Read from Valve's <c>game/shared/in_buttons.h</c>, which names bits 0 to 24 and stops. The
/// field is thirty-two bits wide, so seven of them have no published name — bit 25 is
/// <c>IN_ATTACK3</c> in the live game, added for TF2's third weapon slot, and it is absent from
/// the SDK snapshot. Naming it from memory would be a guess dressed as a fact, so anything
/// unnamed is reported as its own hexadecimal value instead.
/// </remarks>
public static class UserCommandButtons
{
    /// <summary>What a zero field is called, so an idle tick does not render as blank.</summary>
    private const string None = "none";

    /// <summary>
    /// Bit 0 upward, exactly as <c>in_buttons.h</c> declares them. Position in this array *is*
    /// the bit number, so nothing may be reordered or removed.
    /// </summary>
    private static readonly string[] Names =
    [
        "IN_ATTACK", "IN_JUMP", "IN_DUCK", "IN_FORWARD",
        "IN_BACK", "IN_USE", "IN_CANCEL", "IN_LEFT",
        "IN_RIGHT", "IN_MOVELEFT", "IN_MOVERIGHT", "IN_ATTACK2",
        "IN_RUN", "IN_RELOAD", "IN_ALT1", "IN_ALT2",
        "IN_SCORE", "IN_SPEED", "IN_WALK", "IN_ZOOM",
        "IN_WEAPON1", "IN_WEAPON2", "IN_BULLRUSH", "IN_GRENADE1",
        "IN_GRENADE2",
    ];

    /// <summary>Renders a button field as names, lowest bit first.</summary>
    /// <param name="buttons">The raw thirty-two bit field.</param>
    /// <returns>
    /// Names joined by <c>|</c>, with any bits the header does not name appended as a single
    /// hexadecimal residual, or <see cref="None"/> when nothing is held.
    /// </returns>
    public static string Describe(uint buttons)
    {
        if (buttons == 0)
        {
            return None;
        }

        StringBuilder held = new();
        uint residual = buttons;

        for (int bit = 0; bit < Names.Length; bit++)
        {
            uint mask = 1u << bit;

            if ((buttons & mask) == 0)
            {
                continue;
            }

            if (held.Length > 0)
            {
                held.Append('|');
            }

            held.Append(Names[bit]);
            residual &= ~mask;
        }

        if (residual != 0)
        {
            if (held.Length > 0)
            {
                held.Append('|');
            }

            held.Append(string.Create(CultureInfo.InvariantCulture, $"0x{residual:X8}"));
        }

        return held.ToString();
    }
}
