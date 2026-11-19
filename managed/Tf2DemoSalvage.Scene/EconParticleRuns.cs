using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene;

/// <summary>One econ entity as a frame decides it, for <see cref="EconParticleRuns"/> (B69 g).</summary>
/// <param name="Entity">The item's entity.</param>
/// <param name="Wearable">A wearable, which re-decides every frame; else a weapon, which re-decides on its own updates.</param>
/// <param name="Received">The item's last update at or before the frame — <c>SceneProp.Received</c>.</param>
/// <param name="Dormant">The item's <c>IsDormant()</c>: entering it forces <see cref="EconParticleState.NotVisible"/>.</param>
/// <param name="State">What <see cref="EconParticleVisibility.State"/> answers for it this frame.</param>
/// <param name="Systems">Its systems, <see cref="EconParticleSystems.Of"/>.</param>
/// <param name="Holder">The item and its owners, for <see cref="EconParticleSystems.Create"/>.</param>
/// <param name="SmokeStyleOff">Its style's <c>UseSmokeParticleEffect()</c> is false (<c>econ_entity.cpp:1556-1576</c>).</param>
public readonly record struct EconParticleItem(
    int Entity,
    bool Wearable,
    int? Received,
    bool Dormant,
    EconParticleState State,
    IReadOnlyList<EconParticle> Systems,
    EconParticleHolder Holder,
    bool SmokeStyleOff = false);

/// <summary>One effect an item's systems created, and the tick it is stepped from (B69 g).</summary>
/// <param name="Entity">The item.</param>
/// <param name="Effect">What was created, and on what.</param>
/// <param name="From">The tick it was created, or the window's start when a seek met it already running.</param>
public readonly record struct EconParticleRun(int Entity, EconParticleCreate Effect, int From);

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
/// *Not built:* a weapon's extra wearables, whose created state the weapon sets and whose difference makes it re-create
/// (<c>:1514-1532</c>); the demo never names them to this class.
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
    /// <param name="hasAttachment">Whether an item's effect owner — its view model when the flag says so — has the attachment.</param>
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
            _seen.Add(item.Entity);

            bool first = !_created.TryGetValue(item.Entity, out Created? created);

            if (created is null)
            {
                created = new Created();
                _created[item.Entity] = created;
            }

            bool rethinks = first || item.Wearable || item.Received != created.Received;
            created.Received = item.Received;

            EconParticleState state = created.State;

            if (item.Dormant)
            {
                state = EconParticleState.NotVisible;
            }
            else if (rethinks)
            {
                state = item.State;
            }

            if (state != created.State)
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
        created.Runs.Clear();
        created.State = state;

        int entity = item.Entity;

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

            if (EconParticleSystems.Create(system, visible, item.Holder, found, (viewModel, name) => hasAttachment(entity, viewModel, name))
                is { } effect)
            {
                created.Runs.Add(new EconParticleRun(entity, effect, from));
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
