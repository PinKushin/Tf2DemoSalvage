using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A real point-of-view demo gives the spectator GUI's item panel what `UpdateItemPanel` reads (B756).</summary>
/// <remarks>
/// `rgl-pug-2026-08-10-pov`: the recorder is dead and spectating slugmundo at tick 24150, where the viewer drew
/// "slugmundo is carrying:" over his Wrap Assassin. TF2 drew the same panel over the same player at demoui tick 23632. lcor
/// and the game; skips without either.
/// </remarks>
public sealed class CorpusSpectatorItemPanelTests
{
    [Test]
    public void For_ARecorderSpectatingATeammate_ShowsTheGuiAndGivesTheTargetsItems()
    {
        string? schemaPath = SdkReference.GameInstall.Find("scripts/items/items_game.txt");

        if (schemaPath is null || !System.IO.File.Exists(schemaPath))
        {
            Assert.Ignore("the game is not present");
        }

        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("rgl-pug-2026-08-10-pov"));
        HudState state = HudStates.For(timeline, 24150);
        Content.Assets.ItemSchema schema = Content.Assets.ItemSchema.Read(System.IO.File.ReadAllBytes(schemaPath));

        TfSpectatorGui.IsShown(state).ShouldBeTrue("a dead recorder's spectator GUI is up");
        state.ObserverTarget.ShouldNotBe(state.LocalIndex);

        ScenePlayer target = state.Player(state.ObserverTarget).ShouldNotBeNull("the spectated entity is a player");

        // `GetInspectItem` (c_tf_player.cpp:10257) walks the target's weapons and wearables, so they must arrive with
        // definitions the schema knows.
        target.Items.ShouldNotBeNull().Count(item => item.DefinitionIndex is { } definition && schema.HasDefinition(definition))
            .ShouldBeGreaterThan(0);
        state.Names.ShouldNotBeNull()[target.EntityIndex].ShouldBe("slugmundo");
    }
}
