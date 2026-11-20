using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene;

/// <summary>What one frame decides about each econ entity, as <see cref="EconParticleRuns"/> reads it (B69 g).</summary>
/// <remarks>
/// Every input is something the frame already decided: <c>ShouldDraw</c> is whether the item survived the visibility rules
/// (<see cref="WeaponVisibility"/>, <see cref="WeaponWearableVisibility"/>, <see cref="FirstPersonVisibility"/>, the
/// disguise, ghost and vision rules); the owner's stealth, disguise and life are his conditions; dormancy is his.
///
/// **The local player is the engine's**: the recorder. The view model branches (<c>VISIBLE_VM</c>, econ_entity.cpp:1244-1253;
/// the effect owner, :1288-1301) need him to own the item and be watched in first person, <c>!ShouldDrawLocalPlayer()</c>;
/// anyone else watched in first person is only not drawn as a world model (<c>ShouldDrawThisPlayer</c>,
/// c_baseplayer.cpp:1992-2011).
///
/// **An item's dormancy is its own** (B69 part 5): <c>CEconEntity::SetDormant</c> is called for the item's own entity leaving
/// the snapshot (<c>econ_entity.cpp:994-1001</c>, <c>c_baseentity.cpp:4015-4039</c>). The server tests a carried item's own
/// edict (<c>FL_EDICT_PVSCHECK</c>, <c>basecombatweapon.cpp:132-145</c>; a wearable has no override), and SourceTV always
/// sends an entity with a move parent (<c>baseentity.cpp:4116-4124</c>), so a wearer out of view can keep a live item. An item
/// with no player owner is left out, as <see cref="EconParticleSystems.Create"/> creates nothing for one.
/// </remarks>
public static class EconParticleItems
{
    /// <summary>
    /// <c>C_BasePlayer::GetLocalPlayer()</c> is never null in a demo: a SourceTV recording's is its own spectator
    /// (<c>docs/memory/sourcetv-has-a-local-player.md</c>), so the engine's <c>pLocalPlayer</c> guards pass on both kinds.
    /// </summary>
    private const bool HasLocalPlayer = true;

    /// <summary>The frame's econ entities with particle systems.</summary>
    /// <param name="props">Every prop of the moment, drawn or not.</param>
    /// <param name="drawn">Whether a prop's <c>ShouldDraw()</c> holds — it survived the frame's visibility rules.</param>
    /// <param name="players">The moment's players.</param>
    /// <param name="dormant">Whether an entity — an item, or its owner — is dormant.</param>
    /// <param name="local">The local player's entity — the recorder — or null for SourceTV's spectator, who owns nothing.</param>
    /// <param name="inEyes">The player the view is in the eyes of, or null.</param>
    /// <param name="viewModelDraws">The local view model's <c>ShouldDraw()</c>.</param>
    /// <param name="schema"><c>items_game.txt</c>.</param>
    /// <param name="hooks">The attribute hooks over it.</param>
    /// <param name="into">Where the items go, after those already there.</param>
    /// <param name="held">
    /// The items as the last frame decided them, by entity: a live item whose owner is dormant — not among
    /// <paramref name="players"/> (B725) — is carried from here, not dropped (B69 review). Null keeps none.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Only an item with systems**: <c>m_bHasParticleSystems</c> (<c>econ_entity.cpp:236-252</c>) makes
    /// <c>UpdateParticleSystems</c> return for one without (<c>:1222-1225</c>). *Not carried:* the engine decides that once,
    /// at creation, with the team then; this asks the team now.
    /// </remarks>
    public static void Of(
        IReadOnlyList<SceneProp> props,
        Func<SceneProp, bool> drawn,
        IReadOnlyList<ScenePlayer> players,
        Func<int, bool> dormant,
        int? local,
        int? inEyes,
        bool viewModelDraws,
        ItemSchema schema,
        AttributeHooks hooks,
        ICollection<EconParticleItem> into,
        IReadOnlyDictionary<int, EconParticleItem>? held = null)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(dormant);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(into);

        foreach (SceneProp prop in props)
        {
            if (prop.ItemDefinitionIndex is not { } definition || prop.OwnedBy is not { } owner)
            {
                continue;
            }

            if (Held(players, owner, prop.EntityIndex) is not var (player, item))
            {
                // A dormant owner is not among the players, while his live item stays: the client keeps it, and its next
                // decision is made with the owner dormant, `NOT_VISIBLE` (econ_entity.cpp:1224-1262) — a weapon's only on its
                // own update (tf_weaponbase.cpp:3525). Its systems are the last frame's: his attributes left with him.
                if (dormant(owner) && held is not null && held.TryGetValue(prop.EntityIndex, out EconParticleItem last))
                {
                    into.Add(last with
                    {
                        Received = prop.Received,
                        Dormant = dormant(prop.EntityIndex),
                        State = EconParticleState.NotVisible,
                        ExtraWearable = Live(props, prop.ExtraWearable, dormant),
                        ExtraWearableViewModel = Live(props, prop.ExtraWearableViewModel, dormant),
                    });
                }

                continue;
            }

            IReadOnlyList<EconParticle> systems = EconParticleSystems.Of(
                schema,
                definition,
                prop.TeamNumber ?? player.Team ?? 0,
                AttributeHooks.RoundFloatToInt(hooks.OnItem(player, item, "set_attached_particle_static", 0f)),
                AttributeHooks.RoundFloatToInt(hooks.OnItem(player, item, "set_attached_particle", 0f)),
                AttributeHooks.RoundFloatToInt(hooks.OnItem(player, item, "throwable_particle_trail_only", 0f)) != 0);

            if (systems.Count == 0)
            {
                continue;
            }

            bool weapon = prop.WeaponState is not null;
            bool shouldDraw = drawn(prop);
            bool ownerDrawn = inEyes != owner;

            // `pLocalPlayer == GetOwnerEntity() && pLocalPlayer->GetViewModel() && !ShouldDrawLocalPlayer()`.
            bool localInEyes = local == owner && !ownerDrawn;

            // `GetViewModel()->GetWeapon() == this`: the view model holds the active weapon.
            bool inViewModel = weapon && localInEyes && prop.WeaponState == EntityState.WeaponActive;

            EconParticleView view = new(
                weapon ? EconParticleOwnerKind.Entity : EconParticleOwnerKind.Wearable,
                NoDraw: false,
                ShouldDraw: shouldDraw,
                HasOwner: true,
                Dormant: false,
                OwnerIsPlayer: true,
                OwnerDormant: dormant(owner),
                OwnerAlive: player.IsAlive,
                HeldInLocalViewModel: inViewModel && player.IsAlive,
                OwnerStealthed: player.Conditions.IsStealthed,
                OwnerDisguised: player.Conditions.Has(PlayerConditions.Disguised),
                DisguiseWeapon: prop.OfDisguise,
                HasLocalPlayer: HasLocalPlayer,
                EffectOwnerDraws: localInEyes ? viewModelDraws : shouldDraw,
                OwnerDrawnAsWorldModel: ownerDrawn,
                HiddenWorldTraveler: string.Equals(
                    schema.ItemDefinitionName(definition), EconParticleVisibility.WorldTraveler, StringComparison.Ordinal));

            EconParticleHolder holder = new(
                HasLocalPlayer: HasLocalPlayer,
                HasOwner: true,
                IsWeapon: weapon,
                ExtraWearable: false,
                ExtraWearableViewModel: false,
                OwnerDrawnAsWorldModel: ownerDrawn,
                InLocalViewModel: inViewModel,
                // `pEffectOwnerVM = pWeapon->GetPlayerOwner()->GetViewModel()` for ANY weapon, before the first-person test
                // (econ_entity.cpp:1606-1620): the stops reach it in third person too, which is how a first/third-person switch
                // removes the view-model effect (:1655-1720). *Interpolated:* only the local player's view model is modelled,
                // so another owner's is taken as none.
                HasViewModelOwner: weapon && local == owner,
                ParticleSuffix: schema.ParticleSuffix(definition));

            int? style = TfItemView.From(item, schema)?.Style(schema);

            into.Add(new EconParticleItem(
                prop.EntityIndex,
                Wearable: !weapon,
                prop.Received,
                Dormant: dormant(prop.EntityIndex),
                EconParticleVisibility.State(view),
                systems,
                holder,
                SmokeStyleOff: schema.StyleUsesSmokeParticleEffect(definition, style) == false,
                ExtraWearable: Live(props, prop.ExtraWearable, dormant),
                ExtraWearableViewModel: Live(props, prop.ExtraWearableViewModel, dormant),

                // `m_hViewmodelAttachment` (`UpdateAttachmentModels`, econ_entity.cpp:1135-1178): an attach-to-hands item whose
                // owner is not drawn, and — TF's `AttachmentModelsShouldBeVisible`, tf_weaponbase.h:588 — the ACTIVE weapon.
                // *Interpolated:* `attach_to_hands_vm_only` and a weapon repurposed for a taunt are not read.
                AttachmentModel: inViewModel && schema.AttachesToHands(definition)));
        }
    }

    /// <summary>
    /// A weapon's extra-wearable handle as <c>EHANDLE::Get</c> answers it — slot and serial — with that wearable's own dormancy;
    /// null when it names nothing in the moment (B69 part 4).
    /// </summary>
    /// <remarks>
    /// *Interpolated:* resolved against the moment's props, where the engine asks its whole entity list; an extra wearable is
    /// drawn with its weapon's owner, so one alive and not in the props is not expected.
    /// </remarks>
    private static EconExtraWearable? Live(IReadOnlyList<SceneProp> props, (int Entity, int Serial)? handle, Func<int, bool> dormant)
    {
        if (handle is not var (entity, serial))
        {
            return null;
        }

        foreach (SceneProp prop in props)
        {
            if (prop.EntityIndex == entity && prop.SerialNumber == serial)
            {
                return new EconExtraWearable(entity, dormant(entity));
            }
        }

        return null;
    }

    /// <summary>The owner and his record of the item, or null when the moment holds neither.</summary>
    private static (ScenePlayer Player, SceneItem Item)? Held(IReadOnlyList<ScenePlayer> players, int owner, int entity)
    {
        foreach (ScenePlayer player in players)
        {
            if (player.EntityIndex != owner)
            {
                continue;
            }

            foreach (SceneItem item in player.Items ?? [])
            {
                if (item.EntityIndex == entity)
                {
                    return (player, item);
                }
            }
        }

        return null;
    }
}
