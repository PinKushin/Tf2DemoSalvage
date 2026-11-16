using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>A war painted item drawn in its paint (B754): the material it is forced to, and the composite its proxy binds.</summary>
public sealed partial class WeaponModels
{
    /// <summary>`TEAM_UNASSIGNED`, which the proxy composites as red (c_tf_player.cpp:3654).</summary>
    private const int Unassigned = 0;

    private const int Red = 2;

    private readonly Dictionary<(uint Kit, int Item, int Wear), PaintKitNode?> _recipes = [];

    /// <summary>
    /// The material this item's model is drawn with in place of its own, or null: `CEconEntity::OnDataChanged`'s
    /// `m_MaterialOverrides[ team ]` (econ_entity.cpp:1046-1056), read back by `GetEconWeaponMaterialOverride(
    /// GetTeamNumber() )` for the world model (c_baseanimating.cpp:3430) and by `DrawOverriddenViewmodel` for the view
    /// model (econ_entity.cpp:1903-1912).
    /// </summary>
    /// <param name="prop">The item's prop.</param>
    /// <param name="team">The entity's team, which picks the definition's visuals block.</param>
    /// <returns>A VMT path with its extension, as the override table keys it, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prop"/> is null.</exception>
    /// <remarks>
    /// **The paint kit's material stomps every team's** ("if we have paintkit material override, stomp all material
    /// override", :1045); without one, the definition's `material_override` for the team.
    ///
    /// *Not reproduced, and why:* a taunting owner whose taunt has a prop ignores the override (c_baseanimating.cpp:3398-
    /// 3416), but it reads the owner's `Inventory()->GetItemInLoadout`, which a demo watcher's client holds for nobody
    /// but the watcher — so it finds no taunt item and never ignores. Interpolated from the inventory code, not measured.
    /// </remarks>
    public string? MaterialOverrideFor(SceneProp prop, int? team)
    {
        ArgumentNullException.ThrowIfNull(prop);

        if (prop.ItemDefinitionIndex is not { } item || Schema() is not { } schema)
        {
            return null;
        }

        string? material = Painting(schema, AttributesFor(prop)) is { } painting && PaintKits is { } kits
            ? PaintKitRecipe.MaterialOverride(kits, painting.Kit, item)
            : null;

        material ??= schema.MaterialOverride(item, team ?? Unassigned);
        return material is null ? null : material + ".vmt";
    }

    /// <summary>
    /// The composite a `WeaponSkin` proxy on this item's material binds, or null for an item with no paint kit or no
    /// recipe for it (`CWeaponSkinProxy::OnBind`, c_tf_player.cpp:3612-3631).
    /// </summary>
    /// <param name="prop">The item's prop.</param>
    /// <param name="fullSize">Whether the item is the local player's: the composite is then not dropped two mips.</param>
    /// <returns>The request, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prop"/> is null.</exception>
    /// <remarks>
    /// **The seed is 0 without a custom one, and that is every watcher's answer**: `GetOriginalID()` is the item's SOC data's
    /// (econ_item_view.h:208), found only in an inventory the client holds (econ_item_view.cpp:839-858), and a demo watcher's
    /// client holds only the watcher's own. Read from source, not measured; the watcher's own items are the exception it
    /// leaves.
    /// </remarks>
    public WeaponSkin? WeaponSkinFor(SceneProp prop, bool fullSize)
    {
        ArgumentNullException.ThrowIfNull(prop);

        if (prop.ItemDefinitionIndex is not { } item || Schema() is not { } schema || PaintKits is not { } kits
            || Painting(schema, AttributesFor(prop)) is not { } painting
            || Recipe(schema, kits, painting.Kit, item, painting.Wear) is not { } recipe)
        {
            return null;
        }

        int team = prop.Econ?.TeamNumber ?? Red;
        string name = string.Create(
            CultureInfo.InvariantCulture, $"{item}_{painting.Kit}_wear_{painting.Wear:00}");

        return new WeaponSkin(name, recipe, team == Unassigned ? Red : team, painting.Seed, fullSize);
    }

    /// <summary>
    /// Every material a painted item in the demo can be forced to, and every texture its composites read, for loading
    /// before playback as every other asset is (D86).
    /// </summary>
    /// <param name="timeline">The decoded demo.</param>
    /// <returns>VMT paths with their extension, and texture names under `materials/`.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeline"/> is null.</exception>
    /// <remarks>
    /// **Every team, because the definition's override is per team** and a texture's red or blue variant is per team; a
    /// recipe's texture names do not depend on the seed, since <c>TextureCompositor.TextureNames</c> lists every sticker.
    /// </remarks>
    public (IReadOnlyList<string> Materials, IReadOnlyList<string> Textures) PaintedIn(DemoTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        HashSet<string> materials = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> textures = new(StringComparer.OrdinalIgnoreCase);

        if (Schema() is not { } schema)
        {
            return ([], []);
        }

        HashSet<(int, EconAttributeWire?)> asked = [];

        foreach (ScenePropTrack track in timeline.Props)
        {
            if (track.ItemDefinitionIndex is not { } item || !asked.Add((item, track.Econ)))
            {
                continue;
            }

            foreach (int team in (int[])[Unassigned, Red, 3])
            {
                if (schema.MaterialOverride(item, team) is { } own)
                {
                    materials.Add(own + ".vmt");
                }
            }

            if (Painting(schema, ResolveAttributes(item, track.Econ)) is not { } painting || PaintKits is not { } kits)
            {
                continue;
            }

            if (PaintKitRecipe.MaterialOverride(kits, painting.Kit, item) is { } painted)
            {
                materials.Add(painted + ".vmt");
            }

            if (Recipe(schema, kits, painting.Kit, item, painting.Wear) is { } recipe
                && TextureCompositor.Create(recipe, 1, 1, Red, painting.Seed) is { } compositor)
            {
                textures.UnionWith(compositor.TextureNames());
            }
        }

        return ([.. materials], [.. textures]);
    }

    /// <summary>`GetItemPaintKitDefinitionKV`, built once per kit, remapped item and wear.</summary>
    private PaintKitNode? Recipe(ItemSchema schema, PaintKitDefinitions kits, uint kit, int item, int wear)
    {
        int remapped = schema.RemappedDefinitionIndex(item);

        if (!_recipes.TryGetValue((kit, remapped, wear), out PaintKitNode? recipe))
        {
            recipe = PaintKitRecipe.Build(kits, kit, remapped, wear);
            _recipes[(kit, remapped, wear)] = recipe;
        }

        return recipe;
    }

    /// <summary>An item's paint kit, wear category and seed, or null for an item with no `paintkit_proto_def_index`.</summary>
    /// <remarks>
    /// `GetPaintKitDefIndex` (econ_item_interface.h:501-516); `GetPaintKitWear` (econ_item_interface.cpp:457-481): the
    /// item's `set_item_texture_wear`, else `texture_wear_default`, else 0 for an item with a kit; `EconWear_ToIntCategory`
    /// (econ_item_constants.cpp:210-235); the seed from `custom_paintkit_seed_lo`/`_hi` when both are present (c_tf_player.cpp
    /// :3636-3651). Each is the FIRST of its definition, as `FindAttribute` stops at the first (econ_item_interface.h:320-337).
    /// </remarks>
    private static (uint Kit, int Wear, ulong Seed)? Painting(ItemSchema schema, ResolvedAttributes attributes)
    {
        if (First(schema, attributes, "paintkit_proto_def_index") is not { } kit)
        {
            return null;
        }

        float wear = (First(schema, attributes, "set_item_texture_wear") ?? First(schema, attributes, "texture_wear_default"))?.Value ?? 0f;
        ulong seed = 0;

        if (First(schema, attributes, "custom_paintkit_seed_lo") is { } low && First(schema, attributes, "custom_paintkit_seed_hi") is { } high)
        {
            seed = ((ulong)unchecked((uint)high.RawBits) << 32) | unchecked((uint)low.RawBits);
        }

        return (unchecked((uint)kit.RawBits), WearCategory(wear), seed);
    }

    /// <summary>`EconWear_ToIntCategory` (econ_item_constants.cpp:210-235): 1 to 5 by fifths, 3 above 1.</summary>
    /// <param name="wear">The wear, 0 to 1.</param>
    /// <returns>The category.</returns>
    public static int WearCategory(float wear) => wear switch
    {
        <= 0.2f => 1,
        <= 0.4f => 2,
        <= 0.6f => 3,
        <= 0.8f => 4,
        <= 1.0f => 5,
        _ => 3,
    };

    private static EconAttributeValue? First(ItemSchema schema, ResolvedAttributes attributes, string name)
    {
        if (schema.AttributeDefinitionIndex(name) is not { } definition)
        {
            return null;
        }

        foreach (EconAttributeValue attribute in attributes)
        {
            if (attribute.DefinitionIndex == definition)
            {
                return attribute;
            }
        }

        return null;
    }
}
