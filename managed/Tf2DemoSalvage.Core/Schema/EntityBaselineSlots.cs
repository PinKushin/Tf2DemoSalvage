using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>
/// The two per-entity baseline arrays a <c>svc_PacketEntities</c> snapshot names and rebuilds.
/// </summary>
/// <remarks>
/// **This is what <c>baseline</c> and <c>update_baseline</c> are for.** Both fields have been
/// decoded and round-tripped since the container work and consumed by nothing, which is a whole
/// mechanism the recording uses and this project ignored. Counted on `cp_fulgur`:
///
/// | flags | snapshots |
/// |---|---|
/// | `baseline=0 updatebaseline=0` | 12,340 |
/// | `baseline=0 updatebaseline=1` | 1,169 |
/// | `baseline=1 updatebaseline=0` | 12,798 |
/// | `baseline=1 updatebaseline=1` | 1,171 |
///
/// **The point of the pair is that a re-entering entity is described against ITSELF.** An entity
/// entering the potentially visible set is a delta against a baseline, and if that baseline is the
/// entity's own last known state then two properties can describe it completely. Read against the
/// CLASS baseline instead — one representative entity's state, shared by every entity of that
/// class — those same two properties leave the entity wearing a stranger's model and a stranger's
/// position. Measured: `cp_fulgur`'s BLU spawn door came out as `resupply_locker.mdl` at
/// `prop_locker_blu_5`'s world origin.
///
/// **Two arrays rather than one, because the server cannot know when the client got the update.**
/// The server keeps sending against the slot it last had acknowledged and writes the new state into
/// the other one; <c>clc_BaselineAck</c> is how the client says it has caught up. A demo never
/// carries the ack, but it does carry the alternation, so a reader that honours the named slot
/// tracks the server exactly.
///
/// **Its own class rather than fields on <see cref="EntityDecoder"/>**, because this is a distinct
/// question — "what did this entity look like last time the server checkpointed it" — with its own
/// rules about class identity and full updates. The decoder asks it and does not own the rules.
///
/// **The engine's networking source is closed** (`source-sdk-2013/src/engine` ships only `audio`;
/// the SDK carries no more than the <c>CLC_BaselineAck</c> declaration at
/// <c>public/inetmsghandler.h:99</c>), so the rules below are cross-checked against
/// <a href="https://github.com/demostf/parser">demostf/parser</a> — read, not ported.
/// `ParserState::get_baseline` (`src/demo/parser/state.rs:153`) and the `updated_base_line` block
/// (`src/demo/parser/state.rs:271`) state them.
/// </remarks>
internal sealed class EntityBaselineSlots
{
    /// <summary>What one entity looked like when a snapshot last checkpointed it.</summary>
    /// <param name="ClassId">
    /// The class it was then. Kept because an entity SLOT is reissued: a stored baseline can
    /// belong to the previous occupant, and merging that would hand the newcomer a stranger's
    /// state through exactly the door <c>EntityStateTable</c> already closed for serials.
    /// </param>
    /// <param name="Properties">Its full state, baseline and update already merged.</param>
    private readonly record struct Stored(
        int ClassId, IReadOnlyList<DecodedProperty> Properties);

    /// <summary>Slot 0 and slot 1, each keyed by entity index.</summary>
    private readonly Dictionary<int, Stored>[] _slots = [[], []];

    /// <summary>The baseline an entering entity deltas against, when one applies.</summary>
    /// <param name="slot">Which array the snapshot named — <c>false</c> is 0, <c>true</c> is 1.</param>
    /// <param name="entityIndex">The entity's slot in the entity table.</param>
    /// <param name="classId">The class the update says the entity is.</param>
    /// <param name="isDelta">Whether the snapshot is a delta.</param>
    /// <returns>The stored state, or <c>null</c> when the class baseline is what applies.</returns>
    /// <remarks>
    /// Three conditions, all from the same line of the reference:
    /// <c>Some(baseline) if baseline.server_class == class_id &amp;&amp; is_delta</c>.
    ///
    /// **The full-update condition is the one that is easy to drop.** A full snapshot is the server
    /// saying "forget what you had", so an entity in one is described against its class baseline
    /// however much this holds. Honouring a stored baseline there would merge state the server has
    /// already assumed the client discarded.
    /// </remarks>
    public IReadOnlyList<DecodedProperty>? For(
        bool slot, int entityIndex, int classId, bool isDelta)
    {
        if (!isDelta || !_slots[slot ? 1 : 0].TryGetValue(entityIndex, out Stored stored))
        {
            return null;
        }

        return stored.ClassId == classId ? stored.Properties : null;
    }

    /// <summary>Rebuilds the other array from this snapshot, as <c>update_baseline</c> asks.</summary>
    /// <param name="slot">The array the snapshot named; the OTHER one is rebuilt.</param>
    /// <param name="isDelta">Whether the snapshot is a delta.</param>
    /// <param name="entities">Everything the snapshot described, in order.</param>
    /// <remarks>
    /// **The whole array is copied across before anything is written into it**, which is the step
    /// that is invisible until it is missing. Without it, one snapshot's entities would erase every
    /// other entity's stored state — and since the named index alternates, nothing would survive
    /// two snapshots and the mechanism would do worse than nothing.
    ///
    /// **Entering entities only.** A delta describes a change to something already on screen; it is
    /// not a checkpoint of what the entity is.
    ///
    /// **What is stored is the MERGED state**, not the update that produced it. Storing the update
    /// alone would make each baseline as sparse as the snapshot behind it, so a value would survive
    /// exactly one alternation and then vanish — which is a slow leak rather than a visible break.
    /// </remarks>
    public void Update(bool slot, bool isDelta, IReadOnlyList<DecodedEntity> entities)
    {
        Dictionary<int, Stored> from = _slots[slot ? 1 : 0];
        Dictionary<int, Stored> into = _slots[slot ? 0 : 1];

        into.Clear();

        foreach (KeyValuePair<int, Stored> carried in from)
        {
            into[carried.Key] = carried.Value;
        }

        foreach (DecodedEntity entity in entities)
        {
            if (entity.UpdateType != EntityUpdateType.Enter)
            {
                continue;
            }

            IReadOnlyList<DecodedProperty> merged =
                For(slot, entity.EntityIndex, entity.ClassId, isDelta) is { } had
                    ? BaselineMerge.Overlay(had, entity.Properties)
                    : entity.Properties;

            into[entity.EntityIndex] = new Stored(entity.ClassId, merged);
        }
    }
}
