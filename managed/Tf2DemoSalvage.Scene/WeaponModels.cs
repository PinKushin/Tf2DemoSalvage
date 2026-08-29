using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>Which model is in a player's hands, from the game's own item schema.</summary>
/// <remarks>
/// **This was <c>MainForm.WeaponModelFor</c>, <c>WeaponModel</c> and <c>ItemDefinitions</c>, with
/// the two fields that cached the schema** (B188, D90). None of it is window work — it is two
/// lookups into <c>items_game.txt</c> — and none of it had a test, because reaching it meant
/// constructing a form.
///
/// **The client builds what the demo omits, which is why this exists at all.** Nothing on the wire
/// carries a weapon's model path; the demo carries an item index, and the schema turns that into a
/// model exactly as the game does.
///
/// **Two routes, and the second is needed more often than it looks.** Measured on z1800, 22 of 56
/// held weapons never send <c>m_iItemDefinitionIndex</c> at all, so the weapon's own class is used
/// to find the stock item instead. Together they answered for 56 of 56.
/// </remarks>
public sealed class WeaponModels
{
    private readonly Func<string, byte[]?> _read;
    private readonly ILogger _render;

    private ItemSchema? _schema;
    private bool _missing;

    /// <summary>Creates a resolver over an install.</summary>
    /// <param name="read">Opens a content path, answering null when it is absent.</param>
    /// <param name="render">Where a missing schema is reported.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public WeaponModels(Func<string, byte[]?> read, ILogger render)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(render);

        _read = read;
        _render = render;
    }

    /// <summary>A resolver for a viewer with no game installed, which answers nothing.</summary>
    /// <param name="render">Where the absence is reported.</param>
    /// <returns>A resolver that always answers null.</returns>
    /// <remarks>
    /// **A real object rather than a null field** (D83), so the scene asks the same question whether
    /// or not TF2 is present.
    /// </remarks>
    public static WeaponModels None(ILogger render) => new(_ => null, render);

    /// <summary>The model of the weapon a player is holding, or null.</summary>
    /// <param name="holder">The player, at whichever tick they were read.</param>
    /// <returns>The model path, or null when neither route answers.</returns>
    /// <remarks>
    /// Shared by the draw path and by the load set, deliberately: the set decides which models are
    /// packed and the draw path decides which is shown, so a disagreement between them is a weapon
    /// that resolves and cannot be drawn — which is exactly the failure this feature already had
    /// once, from the other direction.
    ///
    /// **No null guard, because <see cref="ScenePlayer"/> is a record STRUCT.** One was written here
    /// and the analyzer rejected it as a no-op (CA2264) — the same shape
    /// <c>docs/memory/nullable-pattern-on-a-struct-is-dead-code.md</c> records, where a guard
    /// compiles and can never fire.
    /// </remarks>
    public string? For(ScenePlayer holder) =>
        For(holder.WeaponItem, holder.WeaponClass, holder.PlayerClass);

    /// <summary>The display model for a weapon named by its item, its class, or both.</summary>
    /// <param name="weaponItem">The item definition index, when the weapon carries one.</param>
    /// <param name="weaponClass">Its entity class, for the stock route.</param>
    /// <param name="forClass">Which player class is holding it; models differ per class.</param>
    /// <returns>The display model, or <c>null</c> when neither route names one.</returns>
    /// <remarks>
    /// **Split out so the VIEWMODEL can ask about its own weapon** (B222). `DT_BaseViewModel`
    /// networks `m_hWeapon`, which is the engine's answer to what is in this hand; the player's
    /// `m_hActiveWeapon` is a reconstruction of the same thing and can disagree with it. Both routes
    /// end here, because the model itself comes from the item schema either way —
    /// `pItem->GetPlayerDisplayModel( iClass, team )`, `econ_entity.cpp:1167`.
    /// </remarks>
    public string? For(int? weaponItem, string? weaponClass, int? forClass)
    {
        if (Schema() is not { } schema)
        {
            return null;
        }

        int playerClass = forClass ?? 0;

        // **The item first, because it is what the player actually equipped.** The class route only
        // knows the stock version, so preferring it would draw a stock rocket launcher for every
        // unusual and reskin in the game.
        if (weaponItem is { } item &&
            schema.ModelFor(item, playerClass) is { Length: > 0 } named)
        {
            return named;
        }

        if (weaponClass is null)
        {
            return null;
        }

        foreach (string candidate in WeaponScriptName.Candidates(weaponClass, forClass))
        {
            if (schema.ModelForClass(candidate, playerClass) is { Length: > 0 } stock)
            {
                return stock;
            }
        }

        return null;
    }

    /// <summary>Every weapon model any player holds at any point in a demo.</summary>
    /// <param name="timeline">The decoded demo.</param>
    /// <returns>Distinct model paths, in the order they are first seen.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeline"/> is null.</exception>
    /// <remarks>
    /// **Resolved up front for the same reason the class models are.** A player switches weapon
    /// constantly, and a set built from what is held right now is missing whatever they draw next —
    /// which does not fail loudly, it just leaves an empty hand.
    ///
    /// **Distinct PAIRS rather than distinct players**, so a whole match resolves to a few dozen
    /// models rather than one lookup per player per frame.
    ///
    /// Shares <see cref="For(ScenePlayer)"/> with the draw path deliberately: the set decides which models are
    /// packed and the draw path decides which is shown, so a disagreement between them is a weapon
    /// that resolves and cannot be drawn.
    /// </remarks>
    public IEnumerable<string> AllIn(DemoTimeline timeline)
    {
        // **Checked here rather than in the iterator below, which is not a style rule.** An
        // iterator's body does not run until something enumerates it, so a guard inside one throws
        // at the `foreach` rather than at the call — pointing at the consumer instead of the caller
        // that passed null.
        ArgumentNullException.ThrowIfNull(timeline);

        return Resolve(timeline);
    }

    /// <summary>The walk itself, once the argument is known good.</summary>
    private IEnumerable<string> Resolve(DemoTimeline timeline)
    {
        if (Schema() is null)
        {
            yield break;
        }

        HashSet<(int? Item, string? Weapon, int Class)> seen = [];

        foreach (TimelineFrame frame in timeline.Frames)
        {
            foreach (ScenePlayer player in frame.Players)
            {
                if (player.ActiveWeapon is null ||
                    !seen.Add((player.WeaponItem, player.WeaponClass, player.PlayerClass ?? 0)))
                {
                    continue;
                }

                if (For(player) is { Length: > 0 } model)
                {
                    yield return model;
                }
            }
        }
    }

    /// <summary>TF2's item schema, read from the installed game once.</summary>
    /// <remarks>
    /// Null when the game is not installed or the file is not where it should be, which is the same
    /// condition every other asset lookup here already tolerates — the viewer draws what it can find
    /// and says what it could not.
    /// </remarks>
    private ItemSchema? Schema()
    {
        if (_schema is not null || _missing)
        {
            return _schema;
        }

        if (_read(SchemaPath) is not { } bytes)
        {
            // Recorded so the eight-megabyte read is not attempted every frame, and reported once so
            // a viewer with no weapons in hand says why.
            _missing = true;
            _render.LogWarning(
                "{Message}", "no items_game.txt, so no weapon models in first person");

            return null;
        }

        _schema = ItemSchema.Read(bytes);
        _render.LogInformation("{Message}", "item schema read");

        return _schema;
    }

    /// <summary>Where the game keeps its item definitions.</summary>
    private const string SchemaPath = "scripts/items/items_game.txt";
}
