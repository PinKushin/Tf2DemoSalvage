using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>`CDecalEmitterSystem` (`game/shared/decals.cpp`): decal groups, and which one a game material asks for (B415).</summary>
/// <remarks>
/// **The client's decal names are groups, not files.** `CBaseEntity::DamageDecal` answers `"Impact.Concrete"` for a
/// bullet and `TranslateDecalForGameMaterial` swaps it for the surface's group through the `TranslationData` block of
/// `scripts/decals_subrect.txt`; `GetDecalIndexForName` then picks one of the group's files by weight. Both of the
/// engine's dictionaries are `CUtlDict`s, which compare without case.
///
/// **The pick is random in the engine and cannot be reproduced**: it draws from the client's global uniform stream,
/// whose state depends on everything the client drew before. The caller supplies the draw.
/// </remarks>
public sealed class DecalEmitters
{
    /// <summary>`DECAL_LIST_FILE`.</summary>
    public const string ScriptPath = "scripts/decals_subrect.txt";

    /// <summary>What `CBaseEntity::DamageDecal` answers for a bullet; "translated at a lower layer based on game material".</summary>
    public const string ImpactConcrete = "Impact.Concrete";

    /// <summary>`CHAR_TEX_CONCRETE`.</summary>
    private const char Concrete = 'C';

    /// <summary>`TRANSLATION_DATA_SECTION`.</summary>
    private const string TranslationSection = "TranslationData";

    private readonly Dictionary<string, List<(string File, float Weight)>> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _translation = new(StringComparer.OrdinalIgnoreCase);

    private DecalEmitters()
    {
    }

    /// <summary>Reads a decal list, as `LoadDecalsFromScript` does.</summary>
    /// <param name="script">The file's bytes.</param>
    /// <returns>The emitters; empty when the file holds nothing.</returns>
    public static DecalEmitters Parse(ReadOnlySpan<byte> script)
    {
        DecalEmitters emitters = new();
        List<(string Material, string Group)> pending = [];
        string? block = null;

        KeyValuesReader.Read(script, (key, value, depth) =>
        {
            if (depth == 0)
            {
                block = value is null ? key : null;
            }
            else if (depth == 1 && block is not null && value is not null)
            {
                if (block.Equals(TranslationSection, StringComparison.OrdinalIgnoreCase))
                {
                    pending.Add((key, value));
                }
                else
                {
                    // `decal.weight = sub->GetFloat()`: a value that is not a number reads as zero.
                    float weight = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float read)
                        ? read
                        : 0f;

                    if (!emitters._groups.TryGetValue(block, out List<(string File, float Weight)>? group))
                    {
                        emitters._groups[block] = group = [];
                    }

                    group.Add((key, weight));
                }
            }

            return true;
        });

        // The translation is resolved after every group is read, and an entry naming no group is dropped with a message.
        foreach ((string material, string group) in pending)
        {
            if (group.Length > 0 && emitters._groups.ContainsKey(group))
            {
                emitters._translation[material] = group;
            }
        }

        return emitters;
    }

    /// <summary>`TranslateDecalForGameMaterial`.</summary>
    /// <param name="decalName">The name the entity asked for.</param>
    /// <param name="gameMaterial">The struck surface's `game.material`.</param>
    /// <returns>The group to draw from; empty for none.</returns>
    public string Translate(string decalName, char gameMaterial)
    {
        ArgumentNullException.ThrowIfNull(decalName);

        if (gameMaterial == Concrete || !decalName.Equals(ImpactConcrete, StringComparison.OrdinalIgnoreCase))
        {
            return decalName;
        }

        if (gameMaterial == '-')
        {
            return string.Empty;
        }

        return _translation.TryGetValue(gameMaterial.ToString(), out string? group) ? group : decalName;
    }

    /// <summary>`GetDecalIndexForName`: one of a group's files, by weight.</summary>
    /// <param name="group">The group's name.</param>
    /// <param name="random">`random->RandomFloat( min, max )`.</param>
    /// <returns>The decal's file name, or null for an empty or unknown group.</returns>
    public string? Pick(string group, Func<float, float, float> random)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(random);

        if (!_groups.TryGetValue(group, out List<(string File, float Weight)>? entries))
        {
            return null;
        }

        float total = 0f;
        int slot = 0;

        for (int index = 0; index < entries.Count; index++)
        {
            float weight = entries[index].Weight;

            if (total == 0f)
            {
                slot = index;
            }

            total += weight;

            if (total == 0f || random(0f, total) < weight)
            {
                slot = index;
            }
        }

        return entries[slot].File;
    }
}
