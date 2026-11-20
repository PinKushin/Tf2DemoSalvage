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
    /// <param name="owners">
    /// Each item's owner as the last frame held him: a live item whose owner is dormant — not among
    /// <paramref name="players"/> (B725) — is decided from that record, not dropped (B69 review B). Null keeps none.
    /// </param>
    /// <param name="asleep">The dormant players' records, whose life a dormant owner's item is decided with; null for none.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Only an item with systems**: <c>m_bHasParticleSystems</c> (<c>econ_entity.cpp:236-252</c>) makes
    /// <c>UpdateParticleSystems</c> return for one without (<c>:1224-1227</c>). *Not carried:* the engine decides that once,
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
        EconParticleOwners? owners = null,
        IReadOnlyList<SceneDormantPlayer>? asleep = null)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(dormant);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(into);

        owners?.Begin();

        foreach (SceneProp prop in props)
        {
            if (prop.ItemDefinitionIndex is not { } definition || prop.OwnedBy is not { } owner)
            {
                continue;
            }

            ScenePlayer player;
            SceneItem item;
            IReadOnlyList<EconParticle> systems;
            bool alive;

            if (Held(players, owner, prop.EntityIndex) is var (live, carried))
            {
                player = live;
                item = carried;
                alive = live.IsAlive;
                systems = EconParticleSystems.Of(
                    schema,
                    definition,
                    prop.TeamNumber ?? player.Team ?? 0,
                    AttributeHooks.RoundFloatToInt(hooks.OnItem(player, item, "set_attached_particle_static", 0f)),
                    AttributeHooks.RoundFloatToInt(hooks.OnItem(player, item, "set_attached_particle", 0f)),
                    AttributeHooks.RoundFloatToInt(hooks.OnItem(player, item, "throwable_particle_trail_only", 0f)) != 0);
            }

            // A dormant owner is not among the players, while his live item stays: the client keeps it, and decides it with
            // him dormant (econ_entity.cpp:1224-1262) — a weapon only on its own update (tf_weaponbase.cpp:3525). Only the
            // SAME item, by serial and definition; its systems and his conditions as last held, his life as his dormant
            // record has it (B69 review B).
            else if (dormant(owner) && owners is not null && owners.Last(prop) is { } last)
            {
                player = last.Owner;
                item = last.Item;
                alive = AliveOf(asleep, owner) ?? last.Owner.IsAlive;
                systems = last.Systems;
            }
            else
            {
                continue;
            }

            if (systems.Count == 0)
            {
                continue;
            }

            owners?.Keep(prop, player, item, systems);

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
                OwnerAlive: alive,
                HeldInLocalViewModel: inViewModel && alive,
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

            // `m_hViewmodelAttachment` (`UpdateAttachmentModels`, econ_entity.cpp:1135-1178): an attach-to-hands item whose
            // owner is not drawn and whose `AttachmentModelsShouldBeVisible` holds — the base's `true` for any econ entity
            // (econ_entity.h:99), TF's ACTIVE weapon (tf_weaponbase.h:588). Its model is `GetPlayerDisplayModel( iClass,
            // team )` (:1167). *Interpolated:* only the local player's view model is modelled; `attach_to_hands_vm_only`, a
            // weapon repurposed for a taunt and the per-team model are not read.
            bool hasHands = localInEyes && (!weapon || prop.WeaponState == EntityState.WeaponActive) && schema.AttachesToHands(definition);

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

                // Placed by the one hands model posed here, the active weapon's.
                AttachmentModel: weapon && hasHands,
                HandsModel: hasHands ? schema.ModelFor(definition, player.PlayerClass ?? 0) : null));
        }
    }

    /// <summary>A dormant player's <c>IsAlive()</c> as his record holds it, or null with no record.</summary>
    private static bool? AliveOf(IReadOnlyList<SceneDormantPlayer>? asleep, int owner)
    {
        if (asleep is null)
        {
            return null;
        }

        for (int each = 0; each < asleep.Count; each++)
        {
            if (asleep[each].EntityIndex == owner)
            {
                return asleep[each].IsAlive;
            }
        }

        return null;
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

/// <summary>
/// Each econ item's owner as the last frame held him, so an item whose owner goes dormant is still decided (B69 review B).
/// </summary>
/// <remarks>
/// **One entity, not one slot**: a record is answered only for the item the slot held then, its serial and definition the
/// same — a new serial is a new object (<c>C_BaseEntity::Init</c>, c_baseentity.cpp:1088-1125), made <c>NOT_VISIBLE</c>
/// (econ_entity.cpp:1022-1027) with nothing of the last one's. Forgotten with the level (<see cref="Clear"/>).
/// *Interpolated:* a seek is ours (D221); a forward seek decides a dormant owner's item from his record before it.
/// </remarks>
public sealed class EconParticleOwners
{
    private Dictionary<int, Record> _last = [];
    private Dictionary<int, Record> _now = [];

    /// <summary>Forgets every owner, as a level's end destroys every entity.</summary>
    public void Clear()
    {
        _last.Clear();
        _now.Clear();
    }

    /// <summary>Starts a frame: what the last one kept becomes what is asked.</summary>
    internal void Begin()
    {
        (_last, _now) = (_now, _last);
        _now.Clear();
    }

    /// <summary>Keeps an item's owner, its record of the item and its systems for the next frame.</summary>
    internal void Keep(SceneProp prop, ScenePlayer owner, SceneItem item, IReadOnlyList<EconParticle> systems) =>
        _now[prop.EntityIndex] = new Record(prop.SerialNumber, prop.ItemDefinitionIndex, owner, item, systems);

    /// <summary>The last frame's record for this same item, or null.</summary>
    internal Record? Last(SceneProp prop) =>
        _last.TryGetValue(prop.EntityIndex, out Record held) && held.Serial == prop.SerialNumber &&
        held.Definition == prop.ItemDefinitionIndex
            ? held
            : null;

    /// <summary>One kept item.</summary>
    internal readonly record struct Record(
        int Serial, int? Definition, ScenePlayer Owner, SceneItem Item, IReadOnlyList<EconParticle> Systems);
}
