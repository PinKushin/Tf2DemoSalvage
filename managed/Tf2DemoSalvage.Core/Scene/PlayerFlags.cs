using System;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>`m_fFlags` bits whose position moved between builds, read off the demo's own schema.</summary>
/// <remarks>
/// `SendPropInt( SENDINFO( m_fFlags ), PLAYER_FLAG_BITS, SPROP_UNSIGNED )`: the orangebox `const.h` lists nine player bits
/// with `FL_FROZEN (1&lt;&lt;5)`; the multiplayer list that inserted `FL_ANIMDUCKING (1&lt;&lt;2)` lists eleven, with
/// `FL_FROZEN (1&lt;&lt;6)` (source-sdk-2013 const.h:148-163). *Measured* (`schema` probe): 9 bits in the 2007, 2008 and
/// 2009 specimens, 11 in 2011 and 2013, 32 in 2026 — so the width tells which list a demo was written with.
/// </remarks>
public static class PlayerFlags
{
    /// <summary>`FL_FROZEN` in the list with `FL_ANIMDUCKING`.</summary>
    public const int FrozenCurrent = 1 << 6;

    /// <summary>`FL_FROZEN` in the orangebox list.</summary>
    public const int FrozenOrangeBox = 1 << 5;

    /// <summary>`FL_FROZEN` for the layout the schema's `DT_BasePlayer.m_fFlags` width declares.</summary>
    /// <param name="schema">The demo's schema.</param>
    /// <returns>The bit; the current one when no player table declares the flags.</returns>
    public static int Frozen(DemoSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        foreach (SendTable table in schema.Tables)
        {
            if (table.Name != "DT_BasePlayer")
            {
                continue;
            }

            foreach (SendProperty property in table.Properties)
            {
                if (property.Name == "m_fFlags")
                {
                    return property.BitCount <= OrangeBoxPlayerFlagBits ? FrozenOrangeBox : FrozenCurrent;
                }
            }
        }

        return FrozenCurrent;
    }

    /// <summary>The orangebox `PLAYER_FLAG_BITS`.</summary>
    private const int OrangeBoxPlayerFlagBits = 9;
}
