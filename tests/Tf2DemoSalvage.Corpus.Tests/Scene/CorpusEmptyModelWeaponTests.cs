using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.SdkReference;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace rather than to the
// helper class — the same reason `CorpusItemAnimSlotTests` beside it gives.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// On a real recording, a weapon whose item's `model_player` is empty is RESOLVED to no model (B105's open item).
/// </summary>
/// <remarks>
/// **The assertion on the output of the step**: `MomentScene.Build` runs `WeaponPropModels.Resolve` handed the installed
/// game's `WeaponModels.For` and `WorldDisplayModel` over the props the timeline holds at the tick, and this reads back the
/// model it gave the prop. The rule and its citations are `WeaponWorldModelConformanceTests`.
///
/// **The specimen is the census's only one** (`item-props` probe over gcor and lcor, 2026-09-30). The items are common —
/// the Duel MiniGame on most modern matches, fists and spellbooks from 2009 on — but everywhere else the wire names no
/// model for them. Here an engineer's Basic Spellbook is a prop for the recording's first 54 ticks with
/// `c_engineer_arms.mdl` on the wire: `m_nModelIndex`, his hands, with no world index sent. **It is holstered throughout**
/// (`m_iState` 0), so `WeaponVisibility` hides it before and after and nothing on screen changes; what changes is the
/// model a HELD spellbook would draw. It is lcor, so the gate's gcor-only run skips it.
/// </remarks>
public sealed class CorpusEmptyModelWeaponTests
{
    /// <summary>The Basic Spellbook, whose `model_player` is `halloween2013_spellbook`'s "".</summary>
    private const int BasicSpellbook = 1070;

    [Test]
    public void Resolve_TheSpellbookAnEngineerCarries_NamesNoModel()
    {
        string path = Corpus.Demo("20150119_2240_cp_process_final_(ovo)_blu");
        string root = GameInstall.Require();
        DemoTimeline timeline = TimelineCache.For(path);
        GameContent game = GameContent.Open(root, NullLoggerFactory.Instance);

        ScenePropTrack spellbook = timeline.Props.Single(track => track.ItemDefinitionIndex == BasicSpellbook);

        List<SceneProp> props = [];
        List<ScenePlayer> players = [];
        timeline.PropsAt(spellbook.FirstTick, props);
        timeline.PlayersAt(spellbook.FirstTick, players);

        int at = props.FindIndex(prop => prop.EntityIndex == spellbook.EntityIndex);

        // The controls: a prop at that tick, a combat weapon, and a model on the wire. Without all three a blank answer
        // below would pass while measuring nothing.
        at.ShouldBeGreaterThanOrEqualTo(0, "the census found it a prop at its first tick");
        props[at].WeaponState.ShouldNotBeNull("a combat weapon, which the client re-derives from its item");
        props[at].ModelPath.ShouldBe("models/weapons/c_models/c_engineer_arms.mdl", "the wire's model: his hands");

        new WeaponPropModels().Resolve(props, players, game.Weapons.For, game.Weapons.WorldDisplayModel);

        props[at].ModelPath.ShouldBe(string.Empty, "the item's \"\" is the world model, and it indexes none");
    }
}
