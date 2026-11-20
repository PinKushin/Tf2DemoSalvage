using System;
using System.Collections.Generic;
using System.Linq;

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
/// <param name="AttachmentModel">
/// It has a hands model, <c>m_hViewmodelAttachment</c> (<c>UpdateAttachmentModels</c>, econ_entity.cpp:1135-1178): its
/// attachments are asked of <see cref="ViewmodelScene.WeaponEntityIndex"/> (:1967-1984).
/// </param>
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
    EconExtraWearable? ExtraWearableViewModel = null,
    bool AttachmentModel = false);

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
/// <param name="Index">The entry's place in the item's systems, which with the item and the start names the effect.</param>
/// <param name="Released">
/// Its owner — an extra wearable — went this frame, releasing it (<c>StopEmission( NULL, false, true )</c>,
/// particle_property.cpp:69-74, :251-281): answered this one frame more, then dropped.
/// </param>
/// <param name="AttachmentModel">
/// The item this frame has a hands model, <c>m_hViewmodelAttachment</c> (<see cref="EconParticleItem.AttachmentModel"/>): the
/// item's own attachments are that model's.
/// </param>
public readonly record struct EconParticleRun(
    int Entity, EconParticleCreate Effect, int From, int Owner, int Index = 0, bool Released = false, bool AttachmentModel = false)
{
    /// <summary>
    /// The entity an attachment of <paramref name="owner"/> is asked of: the item's hands model
    /// (<see cref="ViewmodelScene.WeaponEntityIndex"/>) when it has one — <c>CEconEntity::LookupAttachment</c> and
    /// <c>GetAttachment</c> answer from <c>m_hViewmodelAttachment</c> (econ_entity.cpp:1967-1984) — else the owner itself.
    /// </summary>
    /// <param name="owner">The entity whose attachment is asked.</param>
    /// <returns>The entity to ask.</returns>
    public int AttachmentsOf(int owner) => AttachmentEntity(Entity, AttachmentModel, owner);

    /// <summary>
    /// A seek met it running: <see cref="From"/> is the replay window's start (<see cref="EconParticleRuns.ReplayTicks"/>
    /// back), not its create, so its owner's dormancy holds from there as playing through showed it (B69 review).
    /// </summary>
    public bool MetRunning { get; init; }

    /// <summary><see cref="AttachmentsOf"/> for an item not yet a run.</summary>
    /// <param name="item">The item.</param>
    /// <param name="attachmentModel">Whether it has a hands model this frame.</param>
    /// <param name="owner">The entity whose attachment is asked.</param>
    /// <returns>The entity to ask.</returns>
    public static int AttachmentEntity(int item, bool attachmentModel, int owner) =>
        attachmentModel && owner == item ? ViewmodelScene.WeaponEntityIndex : owner;
}

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
/// **An unchanged state does nothing** (<c>:1512-1537</c>): the effects run on. A change walks the entries, each stopping
/// every effect of its name on the owners — removed instantly, whichever item made it, an earlier entry's included — and
/// then creating (<c>StopParticlesNamed( name, true )</c> or, with a found attachment, <c>StopParticlesWithNameAndAttachment</c>,
/// <c>:1695-1720</c>; B69 sixth pass, b). A create is also the <c>EF_BONEMERGE_FASTCULL</c> clear (<c>:1733-1740</c>), kept as
/// <see cref="ClearedAt"/>.
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
/// (<c>:1695-1720</c>), so an effect on an owner the weapon no longer names runs on; one whose wearable is gone is released with it
/// (<c>:946-989</c>, <c>particle_property.cpp:69-74</c>, <see cref="EconParticleRun.Released"/>): faded when awake, removed
/// when asleep.
/// An effect on a dormant extra wearable stops emitting until it wakes (<c>OwnerSetDormantTo</c>,
/// <c>particle_property.cpp:465-475</c>): the burst asks its owner's dormancy, <c>BurstEffects.AddEconParticles</c>.
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

    /// <summary>
    /// The engine's <c>static char pszTempNameVM[256]</c> (econ_entity.cpp:1659): global, kept across every item, frame and
    /// level, as the engine's is (B69 review). Empty until a view-model entry writes it.
    /// </summary>
    private string _viewModelBuffer = string.Empty;

    /// <summary>The tick an entity's fast-cull bit was last cleared by a create, or null.</summary>
    /// <param name="entity">The item.</param>
    /// <returns>The tick, which a receive of <c>m_fEffects</c> at or after undoes (<c>SceneProp.EffectsReceived</c>).</returns>
    public int? ClearedAt(int entity) => _created.TryGetValue(entity, out Created? created) ? created.ClearedAt : null;

    /// <summary>Forgets every entity, as a level's end destroys them: the next <see cref="At"/> decides afresh.</summary>
    public void Clear()
    {
        _created.Clear();
        _runs.Clear();
        _tick = null;
    }

    /// <summary>Steps every item to a tick and answers the effects running.</summary>
    /// <param name="tick">The tick shown.</param>
    /// <param name="landed">Whether the frame landed by a seek.</param>
    /// <param name="items">Every econ entity of the frame; one missing from it is gone, and its effects with it.</param>
    /// <param name="found"><c>FindParticleSystem( name ) != NULL</c>.</param>
    /// <param name="attachmentIndex">
    /// An entity's <c>LookupAttachment</c>, one-based and 0 for none: <c>(owner, viewModel, name)</c>, the owner resolved —
    /// the item, an extra wearable, or <see cref="ViewmodelScene.ArmsEntityIndex"/> — and the flag saying whether it stands
    /// for the view model.
    /// </param>
    /// <returns>The effects, valid until the next call.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public IReadOnlyList<EconParticleRun> At(
        int tick,
        bool landed,
        IReadOnlyList<EconParticleItem> items,
        Func<string, bool> found,
        Func<int, bool, string, int> attachmentIndex)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(attachmentIndex);

        bool restart = landed || _tick is not { } last || tick < last;

        // **A release's one frame more is a simulated frame** (B69 review): a paused repaint decides again at the same tick
        // (B744), where the engine's frame time is 0 and nothing moves on. Every other step below is idempotent at one tick.
        bool advanced = _tick != tick;

        if (restart)
        {
            _created.Clear();
        }

        _tick = tick;
        _seen.Clear();

        // Indexed, not `foreach` over the interface, which boxes an enumerator each frame (B69 review).
        for (int each = 0; each < items.Count; each++)
        {
            EconParticleItem item = items[each];
            bool first = !_created.ContainsKey(item.Entity);
            Created created = Record(item.Entity);
            Created? world = Extra(item.ExtraWearable);
            Created? viewModel = Extra(item.ExtraWearableViewModel);

            // An extra wearable that is gone releases the effects it owned (`Release`, :946-989, and its particle property's
            // destructor, particle_property.cpp:69-74): `StopEmission( NULL, false, true )`, which fades an awake effect and
            // removes an asleep one (:251-281) — the particle system's to tell, so the run is answered one frame more as
            // released, and dropped the tick after.
            if (advanced)
            {
                created.Runs.RemoveAll(static run => run.Released);
            }

            for (int at = 0; at < created.Runs.Count; at++)
            {
                EconParticleRun run = created.Runs[at];

                if (run.Owner != item.Entity && run.Owner != ViewmodelScene.ArmsEntityIndex &&
                    run.Owner != item.ExtraWearable?.Entity && run.Owner != item.ExtraWearableViewModel?.Entity)
                {
                    created.Runs[at] = run with { Released = true };
                }
            }

            created.Last = item;
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
                Recreate(created, item, state, restart ? tick - ReplayTicks : tick, tick, found, attachmentIndex);
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
            Release(_created[entity], tick, found, attachmentIndex);
        }

        foreach (int entity in _gone)
        {
            _created.Remove(entity);
        }

        _runs.Clear();

        for (int each = 0; each < items.Count; each++)
        {
            // Whether the item's attachments are its hands model's is asked per call (`GetAttachment`, :1978-1984), so it is
            // this frame's, not the create's.
            bool attachmentModel = items[each].AttachmentModel;

            foreach (EconParticleRun run in _created[items[each].Entity].Runs)
            {
                _runs.Add(run with { AttachmentModel = attachmentModel });
            }
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

        return Unculls(prop, clearedAt, tick) ? prop with { BoneMergeFastCull = false } : prop;
    }

    /// <summary>Whether <see cref="FastCull"/> takes the bit off, without the copy.</summary>
    /// <param name="prop">The prop as the timeline built it.</param>
    /// <param name="clearedAt"><see cref="ClearedAt"/> for it.</param>
    /// <param name="tick">The tick shown.</param>
    /// <returns>True when a clear holds for it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prop"/> is null.</exception>
    public static bool Unculls(SceneProp prop, int? clearedAt, int tick)
    {
        ArgumentNullException.ThrowIfNull(prop);

        return prop.BoneMergeFastCull && clearedAt is { } cleared && cleared <= tick && cleared >= (prop.EffectsReceived ?? int.MinValue);
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

        bool first = !_created.ContainsKey(live.Entity);
        Created created = Record(live.Entity);

        // Only on GOING dormant, `!IsDormant() && bDormant` (:997): held dormant, the weapon's write of its state stands
        // (:1514-1532) and no later update re-creates (B69 review). A record first made here starts as it is found.
        if (live.Dormant && !first && !created.Dormant && created.State != EconParticleState.NotVisible)
        {
            created.Runs.Clear();
            created.State = EconParticleState.NotVisible;
        }

        created.Dormant = live.Dormant;

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

    /// <summary>
    /// The state change (<c>:1539-1586</c>): each entry stops every effect of its name on the owners it names — whichever
    /// item made it, and an earlier entry's of this same pass too (<c>:1695-1720</c>) — then creates what this state makes.
    /// </summary>
    private void Recreate(
        Created created,
        in EconParticleItem item,
        EconParticleState state,
        int from,
        int clearedAt,
        Func<string, bool> found,
        Func<int, bool, string, int> lookupAttachment)
    {
        int entity = item.Entity;
        bool attachmentModel = item.AttachmentModel;

        // The item's own `LookupAttachment` answers from its hands model when it has one (econ_entity.cpp:1967-1973), for its
        // world effect's point and its control points 1..6 alike.
        Func<int, bool, string, int> attachmentIndex = (owner, viewModel, name) =>
            lookupAttachment(EconParticleRun.AttachmentEntity(entity, attachmentModel, owner), viewModel, name);

        // `pEffectOwnerWM` and `pEffectOwnerVM` (:1606-1624): the extra wearables when named, else the item and — for a
        // weapon only — the local view model.
        int worldOwner = item.ExtraWearable?.Entity ?? entity;
        int? viewModelOwner = item.ExtraWearableViewModel?.Entity ?? (item.Holder.IsWeapon ? ViewmodelScene.ArmsEntityIndex : null);

        created.State = state;

        // The holder's flags are the item's handles, so there is one answer to "has it one".
        EconParticleHolder holder = item.Holder with
        {
            ExtraWearable = item.ExtraWearable is not null,
            ExtraWearableViewModel = item.ExtraWearableViewModel is not null,
            HasViewModelOwner = item.Holder.HasViewModelOwner || item.ExtraWearableViewModel is not null,
        };

        for (int index = 0; index < item.Systems.Count; index++)
        {
            EconParticle system = item.Systems[index];

            // "Ignore custom particles. Weapons handle them in custom fashions." (:1546-1548)
            if (system.CustomType != 0)
            {
                continue;
            }

            bool visible = state != EconParticleState.NotVisible &&
                !(state == EconParticleState.Visible && item.SmokeStyleOff &&
                  string.Equals(system.System, PipeSmoke, StringComparison.Ordinal));

            // `static char pszTempNameVM[256]` (:1659): written by a view-model entry, read stale by any other (:1672-1716).
            string? written = holder.HasLocalPlayer ? EconParticleSystems.ViewModelBuffer(system, holder.ParticleSuffix, found) : null;
            string? stale = written is null ? _viewModelBuffer : null;
            _viewModelBuffer = written ?? _viewModelBuffer;

            (EconParticleStop? stop, EconParticleCreate? made) = EconParticleSystems.Update(
                system,
                visible,
                holder,
                found,
                (viewModel, name) => attachmentIndex(OwnerOf(viewModel), viewModel, name) > 0,
                name => attachmentIndex(entity, false, name) > 0);

            // On `pEffectOwnerWM`, and on `pEffectOwnerVM` when the weapon has one — its own `_vm` name first (:1695-1720) —
            // by the ONE index looked up on `pEffectOwner` (:1647-1651), whichever owner it then stops on.
            if (stop is not null)
            {
                int? at = stop.Attachment is { } named ? attachmentIndex(OwnerOf(stop.ViewModel), stop.ViewModel, named) : null;

                Stop(worldOwner, stop.Name, at);

                if (holder.HasViewModelOwner && viewModelOwner is { } onViewModel)
                {
                    // `bHasUniqueVMEffect` starts true (:1672): a plain entry stops whatever name the buffer last held.
                    if ((stop.UniqueViewModelName ?? stale) is { Length: > 0 } unique)
                    {
                        Stop(onViewModel, unique, at);
                    }

                    Stop(onViewModel, stop.Name, at);
                }
            }

            if (made is { } effect)
            {
                int owner = OwnerOf(effect.OnViewModel);

                // Stopped as it is made: the failed point's `StopParticlesNamed( name )` on the owner's property, not forced —
                // every running effect of the name there stops to fade (particle_property.cpp:608-617, :359-386).
                if (effect.StoppedAsMade)
                {
                    ReleaseNamed(owner, effect.System);
                }

                created.Runs.Add(
                    new EconParticleRun(entity, effect with { AttachmentIndices = IndicesOf(effect, owner, entity, attachmentIndex) }, from, owner, index)
                    {
                        MetRunning = from < clearedAt,
                    });
                created.ClearedAt = clearedAt;
            }
        }

        // `pEffectOwner`: the view model (or its extra wearable) for `bIsVM`, else the world owner (:1623-1638).
        int OwnerOf(bool viewModel) => viewModel ? viewModelOwner ?? entity : worldOwner;
    }

    /// <summary>
    /// <c>CEconEntity::Release</c> for an item gone from the frame (econ_entity.cpp:945-981): its own systems stopped as
    /// <c>SetParticleSystemsVisible( NOT_VISIBLE )</c> stops them — by name, on its owners, whichever item made them — then, for
    /// a weapon, "always kill all effects for VM": <c>pEffectOwnerVM-&gt;ParticleProp()-&gt;StopEmission( NULL, false, true )</c>
    /// releases every effect on its owner's view model (or its view-model extra wearable), the active weapon's included. Its own
    /// world owner's release reaches only its own effects, which go with its record.
    /// </summary>
    /// <remarks>
    /// *Interpolated:* <c>GetPlayerOwner()-&gt;GetViewModel()</c> is the one view model modelled here
    /// (<see cref="ViewmodelScene.ArmsEntityIndex"/>), taken as this weapon's when its holder has it as a view-model owner — the
    /// local player in his own eyes, the only owner whose view model holds an econ effect here.
    /// </remarks>
    private void Release(Created created, int tick, Func<string, bool> found, Func<int, bool, string, int> attachmentIndex)
    {
        if (created.Last is not { } item)
        {
            return;
        }

        if (created.State != EconParticleState.NotVisible)
        {
            Recreate(created, item, EconParticleState.NotVisible, tick, tick, found, attachmentIndex);
        }

        int? viewModel = item.ExtraWearableViewModel?.Entity ??
            (item.Holder.IsWeapon && item.Holder.HasViewModelOwner ? ViewmodelScene.ArmsEntityIndex : null);

        if (viewModel is not { } owner)
        {
            return;
        }

        foreach (Created other in _created.Values)
        {
            for (int at = 0; at < other.Runs.Count; at++)
            {
                if (other.Runs[at] is { Released: false } run && run.Owner == owner)
                {
                    other.Runs[at] = run with { Released = true };
                }
            }
        }
    }

    /// <summary>
    /// Each control point's <c>iAttachmentPoint</c> (particle_property.cpp:168-218): control point 0's looked up on the effect's
    /// owner, 1..6 on the item (<c>AddControlPoint( pEffect, i, this, … )</c>, econ_entity.cpp:1762-1770); one not found is
    /// <c>INVALID_PARTICLE_ATTACHMENT</c>, which no stop by attachment matches, and is left out.
    /// </summary>
    private static List<int> IndicesOf(EconParticleCreate effect, int owner, int item, Func<int, bool, string, int> attachmentIndex)
    {
        List<int> indices = [];

        if (effect.Attachment is { } attachment && attachmentIndex(owner, effect.OnViewModel, attachment) is > 0 and var found)
        {
            indices.Add(found);
        }

        foreach ((int _, string name) in effect.ControlPoints)
        {
            if (attachmentIndex(item, false, name) is > 0 and var point)
            {
                indices.Add(point);
            }
        }

        return indices;
    }

    /// <summary>
    /// <c>StopParticlesNamed( name, true )</c> (particle_property.cpp:359-386) on one owner, or with an attachment index
    /// <c>StopParticlesWithNameAndAttachment</c> (:389-422), which takes only an effect with a control point whose
    /// <c>iAttachmentPoint</c> is that index — an index, so one looked up on another model can miss or hit by number (B69 b).
    /// Every item's effects on that owner are its effects.
    /// </summary>
    private void Stop(int owner, string name, int? attachment)
    {
        foreach (Created created in _created.Values)
        {
            // A released effect left every particle property with its owner.
            created.Runs.RemoveAll(run =>
                !run.Released &&
                run.Owner == owner &&
                string.Equals(run.Effect.System, name, StringComparison.Ordinal) &&
                (attachment is not { } index || run.Effect.AttachmentIndices.Contains(index)));
        }
    }

    /// <summary>
    /// <c>StopParticlesNamed( name )</c> without <c>bForceRemoveInstantly</c> on one owner (particle_property.cpp:359-386): every
    /// running effect of the name there stops emitting and fades — <see cref="EconParticleRun.Released"/>, answered one tick
    /// more. *Interpolated:* a released run is also removed when asleep, which this stop does not do to an asleep system.
    /// </summary>
    private void ReleaseNamed(int owner, string name)
    {
        foreach (Created created in _created.Values)
        {
            for (int at = 0; at < created.Runs.Count; at++)
            {
                if (created.Runs[at] is { Released: false, Effect.StoppedAsMade: false } run &&
                    run.Owner == owner &&
                    string.Equals(run.Effect.System, name, StringComparison.Ordinal))
                {
                    created.Runs[at] = run with { Released = true };
                }
            }
        }
    }

    /// <summary>One entity's state, its effects and its last clear.</summary>
    private sealed class Created
    {
        public EconParticleState State { get; set; } = EconParticleState.NotVisible;

        public int? Received { get; set; }

        public int? ClearedAt { get; set; }

        /// <summary>An extra wearable's <c>IsDormant()</c> as the last frame found it, so going dormant is a transition.</summary>
        public bool Dormant { get; set; }

        /// <summary>The item as the last frame that held it decided it; null for an extra wearable's record.</summary>
        public EconParticleItem? Last { get; set; }

        public List<EconParticleRun> Runs { get; } = [];
    }
}
