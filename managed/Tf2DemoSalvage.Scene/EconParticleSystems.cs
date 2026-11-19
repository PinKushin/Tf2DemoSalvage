using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>One <c>attachedparticlesystem_t</c> an econ item carries, whichever source declared it (B69).</summary>
/// <param name="System"><c>pszSystemName</c>.</param>
/// <param name="CustomType"><c>iCustomType</c>: non-zero is a weapon's own system, skipped by the visibility walk.</param>
/// <param name="FollowRootBone"><c>bFollowRootBone</c>.</param>
/// <param name="DrawInViewModel"><c>bDrawInViewModel</c>.</param>
/// <param name="UseSuffixName"><c>bUseSuffixName</c>.</param>
/// <param name="ControlPoints"><c>pszControlPoints[0..6]</c>, the first being the attachment.</param>
public readonly record struct EconParticle(
    string System, int CustomType, bool FollowRootBone, bool DrawInViewModel, bool UseSuffixName, IReadOnlyList<string?> ControlPoints);

/// <summary><c>CEconEntity::GetEconParticleSystems</c> (<c>econ_entity.cpp:1419-1505</c>).</summary>
public static class EconParticleSystems
{
    /// <summary><c>TF_TEAM_RED</c>.</summary>
    private const int RedTeam = 2;

    /// <summary><c>TF_TEAM_BLUE</c>.</summary>
    private const int BlueTeam = 3;

    private const string RedColour = "_teamcolor_red";
    private const string BlueColour = "_teamcolor_blue";

    /// <summary>An item's particle systems, in the engine's order.</summary>
    /// <param name="schema"><c>items_game.txt</c>.</param>
    /// <param name="definitionIndex">The item view's definition, or null for an entity with no item.</param>
    /// <param name="team">The entity's <c>GetTeamNumber()</c>.</param>
    /// <param name="staticEffect"><c>CALL_ATTRIB_HOOK_INT( …, set_attached_particle_static )</c>, from 0.</param>
    /// <param name="dynamicEffect"><c>CALL_ATTRIB_HOOK_INT( …, set_attached_particle )</c>, from 0.</param>
    /// <param name="throwableTrailOnly"><c>CALL_ATTRIB_HOOK_INT( …, throwable_particle_trail_only )</c> is non-zero.</param>
    /// <returns>The systems, any without a name dropped (<c>:1496-1504</c>).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> is null.</exception>
    /// <remarks>
    /// **No quality sparkle, by the engine's own rule.** <c>GetQualityParticleType</c> (<c>econ_item_view.cpp:1076-1088</c>)
    /// returns 0 when <c>GetSOCData()</c> is null, and on a demo client it is: a networked view has no
    /// <c>m_pNonSOEconItem</c>, and the inventory of the item's account (<c>:839-857</c>) is not one a demo viewer holds.
    /// The networked <c>m_iEntityQuality</c> is not what it reads.
    /// </remarks>
    public static IReadOnlyList<EconParticle> Of(
        ItemSchema schema, int? definitionIndex, int team, int staticEffect, int dynamicEffect, bool throwableTrailOnly)
    {
        ArgumentNullException.ThrowIfNull(schema);

        List<EconParticle> systems = [];

        if (definitionIndex is { } definition)
        {
            // `GetNumAttachedParticles( GetTeamNumber() )` and `GetAttachedParticleData` (:1430-1434).
            foreach (AttachedParticle own in schema.AttachedParticlesFor(definition, team))
            {
                if (!string.IsNullOrEmpty(own.System))
                {
                    systems.Add(new EconParticle(own.System, own.CustomType, own.FollowRootBone, false, false, [own.Attachment]));
                }
            }
        }

        // `set_attached_particle_static` (:1449-1454).
        if (staticEffect > 0)
        {
            Add(systems, schema.AttributeControlledParticleSystem(staticEffect));
        }

        // `set_attached_particle`, unless `throwable_particle_trail_only` (:1457-1492).
        if (dynamicEffect > 0 && !throwableTrailOnly && schema.AttributeControlledParticleSystem(dynamicEffect) is { } dynamic)
        {
            Add(systems, TeamColoured(schema, dynamic, team));
        }

        return systems;
    }

    /// <summary>The TF team-colour swap (<c>:1472-1485</c>): `V_stristr` to find, `V_StrSubst` to replace, then a lookup by name.</summary>
    private static AttributeParticleSystem? TeamColoured(ItemSchema schema, AttributeParticleSystem system, int team)
    {
        string name = system.SystemName ?? string.Empty;

        if (team == BlueTeam && name.Contains(RedColour, StringComparison.OrdinalIgnoreCase))
        {
            return schema.FindAttributeControlledParticleSystem(name.Replace(RedColour, BlueColour, StringComparison.Ordinal));
        }

        if (team == RedTeam && name.Contains(BlueColour, StringComparison.OrdinalIgnoreCase))
        {
            return schema.FindAttributeControlledParticleSystem(name.Replace(BlueColour, RedColour, StringComparison.Ordinal));
        }

        return system;
    }

    /// <summary>Adds one attribute-controlled system, dropping a missing or nameless one (<c>:1496-1504</c>).</summary>
    private static void Add(List<EconParticle> systems, AttributeParticleSystem? system)
    {
        if (system is { SystemName: { Length: > 0 } name })
        {
            systems.Add(new EconParticle(name, 0, system.FollowRootBone, system.DrawInViewModel, system.UseSuffixName, system.ControlPoints));
        }
    }
}
