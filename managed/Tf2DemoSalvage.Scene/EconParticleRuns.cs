using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>One econ entity as a frame decides it, for <see cref="EconParticleRuns"/> (B69 g).</summary>
/// <param name="Entity">The item's entity.</param>
/// <param name="Wearable">A wearable, which re-decides every frame; else a weapon, which re-decides on its own updates.</param>
/// <param name="Received">The item's last update at or before the frame — <c>SceneProp.Received</c>.</param>
/// <param name="Dormant">
/// The item's own <c>IsDormant()</c>, its entity out of the snapshot: entering it forces <see cref="EconParticleState.NotVisible"/>.
/// </param>
/// <param name="State">What <see cref="EconParticleVisibility.State"/> answers for it this frame.</param>
/// <param name="Systems">Its systems, <see cref="EconParticleSystems.Of"/>.</param>
/// <param name="Holder">The item and its owners, for <see cref="EconParticleSystems.Create"/>.</param>
/// <param name="SmokeStyleOff">Its style's <c>UseSmokeParticleEffect()</c> is false (<c>econ_entity.cpp:1556-1576</c>).</param>
/// <param name="ExtraWearable">A weapon's live <c>m_hExtraWearable</c>, or null.</param>
/// <param name="ExtraWearableViewModel">A weapon's live <c>m_hExtraWearableViewModel</c>, or null.</param>
public readonly record struct EconParticleItem(
    int Entity,
    bool Wearable,
    int? Received,
    bool Dormant,
    EconParticleState State,
    IReadOnlyList<EconParticle> Systems,
    EconParticleHolder Holder,
    bool SmokeStyleOff = false,
    EconExtraWearable? ExtraWearable = null,
    EconExtraWearable? ExtraWearableViewModel = null);

/// <summary>A weapon's extra wearable as a frame finds it (B69 part 4).</summary>
/// <param name="Entity">The wearable's entity.</param>
/// <param name="Dormant">Its own <c>IsDormant()</c>.</param>
public readonly record struct EconExtraWearable(int Entity, bool Dormant);

/// <summary>One effect an item's systems created, and the tick it is stepped from (B69 g).</summary>
/// <param name="Entity">The item.</param>
/// <param name="Effect">What was created, and on what.</param>
/// <param name="From">The tick it was created, or the window's start when a seek met it already running.</param>
/// <param name="Owner">
/// The entity whose particle property holds it: the item, its extra wearable, the local view model
/// (<see cref="ViewmodelScene.ArmsEntityIndex"/>) or the view-model extra wearable (<c>econ_entity.cpp:1606-1639</c>).
/// </param>
public readonly record struct EconParticleRun(int Entity, EconParticleCreate Effect, int From, int Owner);

/// <summary>
/// Each econ entity's <c>m_nParticleSystemsCreated</c> and what it created — <c>CEconEntity::SetParticleSystemsVisible</c>
/// (<c>econ_entity.cpp:1510-1587</c>) stepped frame by frame (B69 g).
/// </summary>
/// <remarks>
/// **When it re-decides.** A wearable every frame, its <c>ClientThink</c> (<c>econ_wearable.cpp:428-433</c>); a weapon only
/// in its own <c>OnDataChanged</c> (<c>tf_weaponbase.cpp:3525</c>), so a weapon whose owner dies keeps its effects until
/// the weapon itself is next updated. Entering dormancy forces <see cref="EconParticleState.NotVisible"/>
/// (<c>CEconEntity::SetDormant</c>, <c>:995-1001</c>). Creation sets <see cref="EconParticleState.NotVisible"/>
/// (<c>:1022-1027</c>), so the first decision that is not that is a change.
///
/// **An unchanged state does nothing** (<c>:1512-1537</c>): the effects run on. A change stops every system's effects at
/// once (<c>StopParticlesNamed( name, true )</c>, removed instantly, <c>:1695-1720</c>) and creates afresh, and a create is
/// also the <c>EF_BONEMERGE_FASTCULL</c> clear (<c>:1733-1740</c>), kept as <see cref="ClearedAt"/>.
///
/// **A seek is ours** (D221): a landing, or a clock gone back, forgets every state and decides afresh, an effect met
/// running starting <see cref="ReplayTicks"/> back as the overheal pluses do
/// (<c>docs/memory/a-parity-rule-may-bound-the-cost.md</c>), and its clear dated at the landing.
///
/// **A weapon's extra wearables** (B69 part 4; the demo names them, <c>DT_TFWeaponBase.m_hExtraWearable</c> and
/// <c>m_hExtraWearableViewModel</c>): they own the weapon's effects (<c>:1606-1624</c>), and an unchanged state writes itself
/// into each and re-creates when one differed (<c>:1514-1532</c>) — <c>bDirty</c> assigned, not or-ed, so the view-model
/// wearable's answer overwrites the world one's. A changed state does not write them. A wearable's state is its own
/// <c>m_nParticleSystemsCreated</c>, the record an item of its own would use: <c>NOT_VISIBLE</c> when it is made
/// (<c>:1022-1027</c>) and when it goes dormant (<c>:994-1001</c>). A stop reaches only the owners it names
/// (<c>:1695-1720</c>), so an effect on an owner the weapon no longer names runs on; one whose wearable is gone ends with it
/// (<c>:946-989</c>, <c>particle_property.cpp:69-74</c> — removed here, where the engine lets an awake one fade).
/// *Not carried:* an effect on a dormant extra wearable keeps emitting, where <c>OwnerSetDormantTo</c> stops its emission
/// (<c>particle_property.cpp:465-475</c>).
/// </remarks>
public sealed class EconParticleRuns
{
    /// <summary>How far back a seek starts an effect met running: two seconds.</summary>
    public const int ReplayTicks = 132;

    /// <summary>The style rule's system (<c>:1570</c>).</summary>
    private const string PipeSmoke = "drg_pipe_smoke";

    private readonly Dictionary<int, Created> _created = [];
    private readonly HashSet<int> _seen = [];
    private readonly List<int> _gone = [];
    private readonly List<EconParticleRun> _runs = [];
    private int? _tick;

    /// <summary>The tick an entity's fast-cull bit was last cleared by a create, or null.</summary>
    /// <param name="entity">The item.</param>
    /// <returns>The tick, which a receive of <c>m_fEffects</c> at or after undoes (<c>SceneProp.EffectsReceived</c>).</returns>
    public int? ClearedAt(int entity) => _created.TryGetValue(entity, out Created? created) ? created.ClearedAt : null;

    /// <summary>Steps every item to a tick and answers the effects running.</summary>
    /// <param name="tick">The tick shown.</param>
    /// <param name="landed">Whether the frame landed by a seek.</param>
    /// <param name="items">Every econ entity of the frame; one missing from it is gone, and its effects with it.</param>
    /// <param name="found"><c>FindParticleSystem( name ) != NULL</c>.</param>
    /// <param name="hasAttachment">
    /// Whether an entity has the attachment: <c>(owner, viewModel, name)</c>, the owner resolved — the item, an extra wearable,
    /// or <see cref="ViewmodelScene.ArmsEntityIndex"/> — and the flag saying whether it stands for the view model.
    /// </param>
    /// <returns>The effects, valid until the next call.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public IReadOnlyList<EconParticleRun> At(
        int tick,
        bool landed,
        IReadOnlyList<EconParticleItem> items,
        Func<string, bool> found,
        Func<int, bool, string, bool> hasAttachment)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(hasAttachment);

        bool restart = landed || _tick is not { } last || tick < last;

        if (restart)
        {
            _created.Clear();
        }

        _tick = tick;
        _seen.Clear();

        foreach (EconParticleItem item in items)
        {
            bool first = !_created.ContainsKey(item.Entity);
            Created created = Record(item.Entity);
            Created? world = Extra(item.ExtraWearable);
            Created? viewModel = Extra(item.ExtraWearableViewModel);

            // An extra wearable that is gone takes the effects it owned (`Release`, :946-989).
            created.Runs.RemoveAll(run =>
                run.Owner != item.Entity && run.Owner != ViewmodelScene.ArmsEntityIndex &&
                run.Owner != item.ExtraWearable?.Entity && run.Owner != item.ExtraWearableViewModel?.Entity);

            bool rethinks = first || item.Wearable || item.Received != created.Received;
            created.Received = item.Received;

            // Entering dormancy: `SetParticleSystemsVisible( NOT_VISIBLE )` only from another state (:997-1000).
            EconParticleState? asked = null;

            if (item.Dormant)
            {
                asked = created.State != EconParticleState.NotVisible ? EconParticleState.NotVisible : null;
            }
            else if (rethinks)
            {
                asked = item.State;
            }

            if (asked is { } state && Changes(created, state, world, viewModel))
            {
                Recreate(created, item, state, restart ? tick - ReplayTicks : tick, tick, found, hasAttachment);
            }
        }

        _gone.Clear();

        foreach (int entity in _created.Keys)
        {
            if (!_seen.Contains(entity))
            {
                _gone.Add(entity);
            }
        }

        foreach (int entity in _gone)
        {
            _created.Remove(entity);
        }

        _runs.Clear();

        foreach (EconParticleItem item in items)
        {
            _runs.AddRange(_created[item.Entity].Runs);
        }

        return _runs;
    }

    /// <summary>
    /// A prop with <c>EF_BONEMERGE_FASTCULL</c> as the client holds it (B69 h): cleared by a create
    /// (<c>RemoveEffects</c>, <c>econ_entity.cpp:1736</c>) and restored by the next update carrying <c>m_fEffects</c>, which
    /// writes the server's flags back (<see cref="SceneProp.EffectsReceived"/>). The same tick is the clear's: a receive's
    /// <c>PostDataUpdate</c> runs before its <c>OnDataChanged</c> re-decides.
    /// </summary>
    /// <param name="prop">The prop as the timeline built it.</param>
    /// <param name="clearedAt"><see cref="ClearedAt"/> for it.</param>
    /// <param name="tick">The tick shown; a clear after it is a state the timeline has not reached, and holds nothing.</param>
    /// <returns>The prop, or a copy without the bit.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prop"/> is null.</exception>
    public static SceneProp FastCull(SceneProp prop, int? clearedAt, int tick)
    {
        ArgumentNullException.ThrowIfNull(prop);

        return prop.BoneMergeFastCull && clearedAt is { } cleared && cleared <= tick && cleared >= (prop.EffectsReceived ?? int.MinValue)
            ? prop with { BoneMergeFastCull = false }
            : prop;
    }

    /// <summary>An entity's record, made <c>NOT_VISIBLE</c> on first sight (<c>DATA_UPDATE_CREATED</c>, :1022-1027); seen this frame.</summary>
    private Created Record(int entity)
    {
        _seen.Add(entity);

        if (!_created.TryGetValue(entity, out Created? created))
        {
            created = new Created();
            _created[entity] = created;
        }

        return created;
    }

    /// <summary>An extra wearable's record, its dormancy written in (<c>SetDormant</c>, :994-1001), or null for none.</summary>
    private Created? Extra(EconExtraWearable? wearable)
    {
        if (wearable is not { } live)
        {
            return null;
        }

        Created created = Record(live.Entity);

        if (live.Dormant && created.State != EconParticleState.NotVisible)
        {
            created.Runs.Clear();
            created.State = EconParticleState.NotVisible;
        }

        return created;
    }

    /// <summary>
    /// <c>SetParticleSystemsVisible</c>'s early return (<c>:1512-1537</c>): an unchanged state writes itself into the extra
    /// wearables and goes on only when one differed, the view-model one's answer overwriting the world one's.
    /// </summary>
    private static bool Changes(Created created, EconParticleState state, Created? world, Created? viewModel)
    {
        if (state != created.State)
        {
            return true;
        }

        bool dirty = false;

        if (world is not null)
        {
            dirty = world.State != state;
            world.State = state;
        }

        if (viewModel is not null)
        {
            dirty = viewModel.State != state;
            viewModel.State = state;
        }

        return dirty;
    }

    /// <summary>The state change: stop what was made, create what this state makes (<c>:1539-1586</c>).</summary>
    private static void Recreate(
        Created created,
        in EconParticleItem item,
        EconParticleState state,
        int from,
        int clearedAt,
        Func<string, bool> found,
        Func<int, bool, string, bool> hasAttachment)
    {
        int entity = item.Entity;

        // `pEffectOwnerWM` and `pEffectOwnerVM` (:1606-1624): the extra wearables when named, else the item and — for a
        // weapon only — the local view model.
        int worldOwner = item.ExtraWearable?.Entity ?? entity;
        int? viewModelOwner = item.ExtraWearableViewModel?.Entity ?? (item.Holder.IsWeapon ? ViewmodelScene.ArmsEntityIndex : null);

        // The stops reach those owners only (:1695-1720).
        created.Runs.RemoveAll(run => run.Owner == worldOwner || run.Owner == viewModelOwner);
        created.State = state;

        // The holder's flags are the item's handles, so there is one answer to "has it one".
        EconParticleHolder holder = item.Holder with
        {
            ExtraWearable = item.ExtraWearable is not null,
            ExtraWearableViewModel = item.ExtraWearableViewModel is not null,
            HasViewModelOwner = item.Holder.HasViewModelOwner || item.ExtraWearableViewModel is not null,
        };

        foreach (EconParticle system in item.Systems)
        {
            // "Ignore custom particles. Weapons handle them in custom fashions." (:1546-1548)
            if (system.CustomType != 0)
            {
                continue;
            }

            bool visible = state != EconParticleState.NotVisible &&
                !(state == EconParticleState.Visible && item.SmokeStyleOff &&
                  string.Equals(system.System, PipeSmoke, StringComparison.Ordinal));

            if (EconParticleSystems.Create(
                    system,
                    visible,
                    holder,
                    found,
                    (viewModel, name) => hasAttachment(viewModel ? viewModelOwner ?? entity : worldOwner, viewModel, name),
                    name => hasAttachment(entity, false, name)) is { } effect)
            {
                created.Runs.Add(new EconParticleRun(entity, effect, from, effect.OnViewModel ? viewModelOwner ?? entity : worldOwner));
                created.ClearedAt = clearedAt;
            }
        }
    }

    /// <summary>One entity's state, its effects and its last clear.</summary>
    private sealed class Created
    {
        public EconParticleState State { get; set; } = EconParticleState.NotVisible;

        public int? Received { get; set; }

        public int? ClearedAt { get; set; }

        public List<EconParticleRun> Runs { get; } = [];
    }
}
