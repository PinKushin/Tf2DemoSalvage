using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace rather than to the
// helper class — the same reason `CorpusItemAnimSlotTests` beside it gives.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The census's only empty-`model_player` weapon on the wire is not a prop at all once its class baseline is read (B452).
/// </summary>
/// <remarks>
/// **What this test used to assert, and why it was wrong.** The `item-props` census (2026-09-30) found an engineer's
/// Basic Spellbook (entity 1130) on `20150119_2240_cp_process_final_(ovo)_blu` as a prop for the recording's first 54
/// ticks with `c_engineer_arms.mdl` on the wire and `m_iState` 0, and this test ran `WeaponPropModels.Resolve` on it to
/// show an item whose `model_player` is "" resolves to no model. **Both observations were decoded against no
/// baseline.** `CTFSpellBook`'s instancebaseline (class 287) exists only in the demo's `dem_stringtables` block, which
/// nothing read until B452 (the `baseline` probe, reading the signon's tables, reports "NO ENTRY"; the
/// `stringtables-block` probe lists 21 baselines only the block carries). Read against it, the entity enters with
/// `m_fEffects 161` — `EF_BONEMERGE | EF_NODRAW | EF_BONEMERGE_FASTCULL` — and `m_iState 1`. No later update sends
/// `m_fEffects` for it in a way that clears the flag (`item-props`: present at 0 of 54 sampled ticks), so the engine's client, which deltas every ENTER against the class baseline
/// (`CL_CopyNewEntity`), holds `EF_NODRAW` for its whole life: nothing draws it. *Measured on the demo (trace,
/// `--entity-limit 3`); the baseline rule is the engine's, B452.*
///
/// So the rule `WeaponPropModels.Resolve` applies to an empty `model_player` has no real specimen left; it stays
/// covered by `WeaponWorldModelConformanceTests`. What a real recording proves is the answer below. It is lcor, so
/// the gate's gcor-only run skips it.
/// </remarks>
public sealed class CorpusEmptyModelWeaponTests
{
    /// <summary>The Basic Spellbook, whose `model_player` is `halloween2013_spellbook`'s "".</summary>
    private const int BasicSpellbook = 1070;

    [Test]
    public void PropsAt_TheSpellbookAnEngineerCarries_IsNotAPropItsClassBaselineHidesIt()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("20150119_2240_cp_process_final_(ovo)_blu"));

        ScenePropTrack spellbook = timeline.Props.Single(track => track.ItemDefinitionIndex == BasicSpellbook);

        List<SceneProp> props = [];
        timeline.PropsAt(spellbook.FirstTick, props);

        // The control: the same engineer's earphones, a wearable bone-merged to him, ARE a prop at that tick, so an
        // empty answer below is about the spellbook and not about the sample.
        props.ShouldContain(prop => prop.ModelPath == "models/player/items/engineer/engy_earphones.mdl");

        props.ShouldNotContain(prop => prop.EntityIndex == spellbook.EntityIndex, "its class baseline sets EF_NODRAW");
    }
}
