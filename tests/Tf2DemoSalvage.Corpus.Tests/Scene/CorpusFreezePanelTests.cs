using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A real point-of-view demo's freeze cam drives the freeze panel's events, through the production chain (B748).
/// </summary>
/// <remarks>
/// **The output-level assertion** (<c>docs/memory/output-level-assertion-or-it-is-not-done.md</c>): the HUD's state for
/// each tick, as <see cref="HudStates.For"/> builds it from the timeline, through <see cref="FreezeEvents"/>. The recorder
/// of `etf2l-12025-pov-2020-07-21` dies at tick 2008 and is in the freeze cam from about 2023 to 2095. lcor; skips
/// without it.
/// </remarks>
public sealed class CorpusFreezePanelTests
{
    private const string PovDemo = "etf2l-12025-pov-2020-07-21";

    [Test]
    public void Advance_ARealFreezeCam_ShowsAPlayerKillerThenStartsThenHides()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(PovDemo));
        FreezeEvents events = new();
        List<(int Tick, SceneGameEvent Event, HudState State)> fired = [];

        for (int tick = 1990; tick <= 2130; tick++)
        {
            HudState state = HudStates.For(timeline, tick);

            fired.AddRange(events.Advance(state, reset: tick == 1990).Select(e => (tick, e, state)));
        }

        fired.Select(f => f.Event.Name).ShouldBe(["show_freezepanel", "freezecam_started", "hide_freezepanel"]);

        // The killer the card names is a player at that tick: the observer target `PostDataUpdate` reads (:905-909).
        (int showTick, SceneGameEvent shown, HudState at) = fired[0];
        at.Player(shown.GetInt("killer")).ShouldNotBeNull($"the killer named at tick {showTick} is a player");

        // `spec_freeze_traveltime` after the entry, and not a frame sooner.
        (int startTick, _, HudState started) = fired[1];
        (started.CurTime - at.CurTime).ShouldBeGreaterThanOrEqualTo(ObserverCamera.FreezeTravelTime);
        (HudStates.For(timeline, startTick - 1).CurTime - at.CurTime).ShouldBeLessThan(ObserverCamera.FreezeTravelTime);
    }

    [Test]
    public void Show_ARealKillersStrangeWarPaintedWeapon_GivesTheItemPanelItsItemAndStatClockLine()
    {
        // `rgl-pug-2026-08-10-pov`: the freeze cam from 32774 names entity 6, holding a strange, war-painted Upgradeable
        // Stickybomb Launcher (item 207). TF2 showed it in the item panel with "Hale's Own Stat Clock Attached" (B751),
        // which is `Attrib_stattrakmodule` in the strange color (econ_item_description.cpp:1148-1151).
        string? schemaPath = SdkReference.GameInstall.Find("scripts/items/items_game.txt");

        if (schemaPath is null || !System.IO.File.Exists(schemaPath))
        {
            Assert.Ignore("the game is not present");
        }

        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("rgl-pug-2026-08-10-pov"));
        HudState state = HudStates.For(timeline, 32800);
        ScenePlayer killer = state.Player(state.ObserverTarget).ShouldNotBeNull("the freeze cam's target is a player");
        SceneItem weapon = killer.Items.ShouldNotBeNull().Single(item => item.IsWeapon && item.EntityIndex == killer.ActiveWeapon);
        Content.Assets.ItemSchema schema = Content.Assets.ItemSchema.Read(System.IO.File.ReadAllBytes(schemaPath));

        (weapon.DefinitionIndex, weapon.Quality).ShouldBe((207, 11), "its networked quality is strange, not normal (tf_hud_freezepanel.cpp:450)");

        // The game's own strings, so the line can be read as TF2 wrote it.
        string? english = SdkReference.GameInstall.Find("resource/tf_english.txt");

        if (english is null || !System.IO.File.Exists(english))
        {
            Assert.Ignore("the game's tf_english.txt is not present");
        }

        VguiLocalize localize = new("english");

        localize.AddFile(english, path => System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null).ShouldBeTrue();

        TfItemView item = TfItemView.From(weapon, schema).ShouldNotBeNull();
        IReadOnlyList<TfDescriptionLine> lines = TfItemDescription.Generate(item, new TfItemDescriptionContext { Schema = schema, Find = localize.Find });

        lines.ShouldContain(new TfDescriptionLine("Hale's Own Stat Clock Attached", TfItemDescription.ColorStrange, TfItemDescription.FlagMisc));
    }

    [Test]
    public void Show_ARealKillersWarPaintedWeapon_NamesItsKitGradeAndCollection()
    {
        // The same launcher (B754): its kit from the shipped `proto_defs.vpd` before the name (econ_item_description.cpp:
        // 991-1011), the grade of its War Paint tool's collection rarity (`GetRarity`, econ_item_interface.cpp:384-400), and
        // that collection listed with the tool checked (:2810-2973). *Read from the game's own data; the strings are not
        // compared with a TF2 capture.*
        if (SdkReference.GameInstall.Root is not { } game)
        {
            Assert.Ignore("the game is not present");
            return;
        }

        Content.Assets.GameArchives archives = Content.Assets.GameArchives.Open(game).WithoutCustom();

        if (archives.Read("scripts/items/items_game.txt") is not { } itemsGame
            || archives.Read(Content.Assets.PaintKitDefinitions.FilePath) is not { } protoDefs)
        {
            Assert.Ignore("the game's item schema or proto definitions are not present");
            return;
        }

        Content.Assets.ItemSchema schema = Content.Assets.ItemSchema.Read(itemsGame);
        Content.Assets.PaintKitDefinitions kits = Content.Assets.PaintKitDefinitions.Read(protoDefs);
        VguiLocalize localize = new("english");
        localize.AddFile("resource/tf_%language%.txt", archives.Read).ShouldBeTrue();
        localize.AddFile("resource/tf_proto_obj_defs_%language%.txt", archives.Read).ShouldBeTrue();

        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("rgl-pug-2026-08-10-pov"));
        HudState state = HudStates.For(timeline, 32800);
        ScenePlayer killer = state.Player(state.ObserverTarget).ShouldNotBeNull("the freeze cam's target is a player");
        SceneItem weapon = killer.Items.ShouldNotBeNull().Single(item => item.IsWeapon && item.EntityIndex == killer.ActiveWeapon);
        TfItemView item = TfItemView.From(weapon, schema).ShouldNotBeNull();

        IReadOnlyList<TfDescriptionLine> lines = TfItemDescription.Generate(
            item, new TfItemDescriptionContext { Schema = schema, Find = localize.Find, PaintKitToken = kits.DescriptionToken });

        // Kit "Horror Holiday", whose War Paint tool sits in the Scream Fortress X collection's "mercenary" block.
        lines[0].Text.ShouldBe("Hale's Own Specialized Killstreak Horror Holiday Stickybomb Launcher");
        lines.ShouldContain(line => line.Text == "Mercenary Grade Stickybomb Launcher (Minimal Wear)");
        lines.ShouldContain(line => line.Text == "Scream Fortress X Collection" && (line.MetaType & TfItemDescription.FlagCollectionName) != 0);
        lines.ShouldContain(line => line.Text.EndsWith("Horror Holiday War Paint", System.StringComparison.Ordinal)
            && (line.MetaType & TfItemDescription.FlagCollectionCurrentItem) != 0);
    }

    [Test]
    public void PlayersAt_APointOfViewRecorder_CarriesHisDominatingMeArray()
    {
        // The nemesis input is local data, sent to the recorder alone: present for him on a POV demo (B748).
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(PovDemo));
        int recorder = timeline.RecorderEntityIndex.ShouldNotBeNull();

        timeline.PlayersAt(2050).Single(player => player.EntityIndex == recorder).DominatingMe.ShouldNotBeNull();
        timeline.PlayersAt(2050).Where(player => player.EntityIndex != recorder).ShouldAllBe(player => player.DominatingMe == null);
    }
}
