using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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

/// <summary>The names <c>UpdateSingleParticleSystem</c> settles on for one entry (B69 e).</summary>
/// <param name="System"><c>pszSystemName</c>: what is created, the <c>_vm</c> one on the view model.</param>
/// <param name="ViewModel"><c>pszTempNameVM</c>: the view model's name, the entry's own when the level has no <c>_vm</c>.</param>
/// <param name="UniqueViewModel"><c>bHasUniqueVMEffect</c>.</param>
public readonly record struct EconParticleName(string System, string ViewModel, bool UniqueViewModel);

/// <summary><c>CEconEntity::GetEconParticleSystems</c> (<c>econ_entity.cpp:1419-1505</c>).</summary>
public static partial class EconParticleSystems
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

        // **Made once per answer, not per item per frame** (B69 review L): the systems are a function of the schema and these
        // five inputs, so a repeat is the list already made. Concurrent: corpus tests share a schema across threads.
        return Made.GetValue(schema, static _ => new ConcurrentDictionary<SystemsKey, EconParticle[]>()).GetOrAdd(
            new SystemsKey(definitionIndex, team, staticEffect, dynamicEffect, throwableTrailOnly),
            static (key, schema) => Build(schema, key),
            schema);
    }

    /// <summary>Each schema's answers by their inputs; weak, so a closed schema takes its answers with it.</summary>
    private static readonly ConditionalWeakTable<ItemSchema, ConcurrentDictionary<SystemsKey, EconParticle[]>> Made = new();

    /// <summary><see cref="Of"/>'s inputs beside the schema.</summary>
    private readonly record struct SystemsKey(int? Definition, int Team, int Static, int Dynamic, bool TrailOnly);

    /// <summary><see cref="Of"/>'s answer, made.</summary>
    private static EconParticle[] Build(ItemSchema schema, SystemsKey key)
    {
        (int? definitionIndex, int team, int staticEffect, int dynamicEffect, bool throwableTrailOnly) = key;
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

        return [.. systems];
    }

    /// <summary>
    /// <c>UpdateSingleParticleSystem</c>'s name for one entry (<c>econ_entity.cpp:1659-1693</c>): the weapon's
    /// <c>particle_suffix</c> appended when the entry asks for it, the view model's <c>_vm</c> system when the level has one,
    /// and null when the level has not loaded the name it ends on — the engine's <c>return</c> before any stop or create.
    /// </summary>
    /// <param name="system">The entry, from <see cref="Of"/>.</param>
    /// <param name="particleSuffix">The item definition's <c>GetParticleSuffix()</c>, or null.</param>
    /// <param name="viewModel"><c>bIsVM</c>: the effect goes on the local player's view model.</param>
    /// <param name="found"><c>g_pParticleSystemMgr-&gt;FindParticleSystem( name ) != NULL</c> over the level's systems.</param>
    /// <returns>The names, or null when the system is not loaded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="found"/> is null.</exception>
    /// <remarks>
    /// The engine builds the names in two <c>static char[256]</c> buffers, so an entry that does not draw in the view model
    /// leaves <c>pszTempNameVM</c> holding the previous call's name: that stale stop is <see cref="ViewModelBuffer"/> and
    /// <c>EconParticleRuns</c>' to carry (B69 review). *Not carried:* a name past 255 characters is truncated.
    /// </remarks>
    public static EconParticleName? Resolve(EconParticle system, string? particleSuffix, bool viewModel, Func<string, bool> found)
    {
        ArgumentNullException.ThrowIfNull(found);

        string name = NameOf(system, particleSuffix);
        string viewModelName = name;
        bool unique = true;

        if (ViewModelBuffer(system, particleSuffix, found) is { } written)
        {
            viewModelName = written;
            unique = !string.Equals(written, name, StringComparison.Ordinal);

            if (viewModel)
            {
                name = viewModelName;
            }
        }

        // "Check that the effect is valid" (:1690-1692).
        return found(name) ? new EconParticleName(name, viewModelName, unique) : null;
    }

    /// <summary>
    /// What an entry writes into the engine's <c>static char pszTempNameVM[256]</c> (econ_entity.cpp:1659, :1672-1683): for a
    /// <c>bDrawInViewModel</c> entry its <c>_vm</c> name, or its plain name when the level has no <c>_vm</c> system; null for any
    /// other entry, which writes nothing and leaves the previous call's name there.
    /// </summary>
    /// <param name="system">The entry.</param>
    /// <param name="particleSuffix">The item definition's <c>GetParticleSuffix()</c>, or null.</param>
    /// <param name="found"><c>FindParticleSystem( name ) != NULL</c>.</param>
    /// <returns>The name written, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="found"/> is null.</exception>
    public static string? ViewModelBuffer(EconParticle system, string? particleSuffix, Func<string, bool> found)
    {
        ArgumentNullException.ThrowIfNull(found);

        if (!system.DrawInViewModel)
        {
            return null;
        }

        string name = NameOf(system, particleSuffix);

        // "VM doesn't exist so fall back to regular" (:1678-1683).
        return found(name + "_vm") ? name + "_vm" : name;
    }

    /// <summary>The entry's name with the weapon's <c>particle_suffix</c> when it asks for it (:1663-1670).</summary>
    private static string NameOf(EconParticle system, string? particleSuffix) =>
        system.UseSuffixName && particleSuffix is not null ? $"{system.System}_{particleSuffix}" : system.System;

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
