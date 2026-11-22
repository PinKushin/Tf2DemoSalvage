using System;
using System.Collections.Generic;
using System.IO;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Working out every model a demo will show, before anything is drawn.
/// </summary>
/// <remarks>
/// **This was <c>MainForm.DemoModelPaths</c> and <c>WornModelPaths</c>** (B188, D90) — a question
/// about a demo and an install, asked from a window, and therefore untested.
///
/// **What is NOT covered here, said rather than left to look covered: the three timeline sources.**
/// Props, viewmodels and held weapons all need a `DemoTimeline`, whose only public factory reads a
/// demo file — so a device-free test cannot reach them, and they are covered end to end by the
/// corpus suites instead. What IS here is the source that needs no demo at all, and it is the one
/// that fails silently.
/// </remarks>
public sealed class DemoModelsTests
{
    // **A roster case was written here and removed, and the reason is worth keeping.** It asserted
    // that `Needed` returns the nine class models with no demo open, using the real locator — and it
    // FAILED, because `Tf2ConfigFiles.DefaultGameFolder` only looks under Program Files (x86) while
    // this machine keeps TF2 on another drive. The code was right; the test was measuring the
    // ENVIRONMENT.
    //
    // Pointing it at a better path would have hidden that rather than fixed it: a deterministic
    // suite that passes or fails on where somebody installed a game is not deterministic. The roster
    // needs an install, so it belongs with the suites that skip without one, alongside the three
    // timeline sources named above.

    [Test]
    public void Sprites_ARopeThatChangesMaterial_BacksOnlyTheMaterialItHeldAsARope()
    {
        // B548: a cable that is a rope under `cable/a.vmt`, then under `cable/b.vmt` is no rope. Both sprites load;
        // only the one held at a rope keyframe gets its `_back` anti-aliasing material.
        Core.Scene.ScenePropTrack cable = new(7, "cable/a.vmt");
        cable.Add(0, new Core.Scene.ScenePose
        {
            Rope = new Core.Scene.SceneRope(
                StartPoint: 1, StartAttachment: 0, EndPoint: 2, EndAttachment: 0, Slack: 120, Length: 300,
                LockedPoints: 3, Flags: 104, Segments: 10, ConstrainBetweenEndpoints: false, Subdiv: 2,
                TextureScale: 1f, Width: 2f, ScrollSpeed: 0f),
        });
        cable.Stamp(0);
        cable.Add(10, new Core.Scene.ScenePose { X = 1f });
        cable.Follow("cable/b.vmt");
        cable.Stamp(10);

        HashSet<string> sprites = DemoModels.Sprites(Core.Scene.DemoTimeline.ForTracks([cable]));

        sprites.ShouldContain("cable/a.vmt");
        sprites.ShouldContain("cable/b.vmt");
        sprites.ShouldContain(EntityRopes.BackMaterialPath("cable/a.vmt"));
        sprites.ShouldNotContain(EntityRopes.BackMaterialPath("cable/b.vmt"));
    }

    [Test]
    public void Precache_WithNoDemoOpen_DoesNothingRatherThanThrowing()
    {
        // **The guard moved here from the call site** (B208). "There is no demo" and "the game
        // content is not open" are both statements about whether precaching is possible, and
        // leaving them in the window means every future caller rediscovers the same two
        // preconditions — which is how one of them eventually gets forgotten.
        RecordingLogger log = new();

        DemoModels.Precache(
            new EntityModelSet(new RecordingLoggerFactory()), timeline: null, game: null, log);

        log.Lines.ShouldBeEmpty("nothing was precached, so nothing should be reported");
    }

    [Test]
    public void Precache_WithAnInstallButNoDemo_StillDoesNothing()
    {
        // **The control on the guard above**, and it separates the two conditions. With only one
        // case, a `Precache` that checked the game and ignored the timeline would pass identically.
        RecordingLogger log = new();
        GameContent game = GameContent.Open(folder: null, new RecordingLoggerFactory());

        DemoModels.Precache(
            new EntityModelSet(new RecordingLoggerFactory()), timeline: null, game, log);

        log.Lines.ShouldBeEmpty();
    }

    [Test]
    public void Precache_WithoutAModelSet_Refuses()
    {
        Should.Throw<ArgumentNullException>(() => DemoModels.Precache(
            models: null!, timeline: null, game: null, new RecordingLogger()));
    }

    [Test]
    public void Needed_WithNoInstall_IsEmptyRatherThanThrowing()
    {
        // **The control, and the ordinary case on a machine with no TF2.** An empty set means
        // nothing packs, which is correct: there is nothing to pack from.
        GameContent game = GameContent.Open(folder: null, new RecordingLoggerFactory());

        DemoModels.Needed(timeline: null, game).ShouldBeEmpty();
    }

    [Test]
    public void Needed_IsCaseInsensitive()
    {
        // A demo names models as the server sent them and the archives are case-insensitive, so a
        // set that treated `Models/Player/Scout.mdl` and `models/player/scout.mdl` as two would pack
        // the same geometry twice and grow the vertex buffer for nothing.
        HashSet<string> needed = DemoModels.Needed(timeline: null, Empty());

        needed.Comparer.ShouldBe(StringComparer.OrdinalIgnoreCase);
    }

    [Test]
    public void Worn_WithNoDemoOpen_IsEmpty()
    {
        // Nothing is worn by nobody. Empty rather than null, so the caller hands it straight on.
        DemoModels.Worn(timeline: null, Empty()).ShouldBeEmpty();
    }

    [Test]
    public void Needed_WithNoInstallSupplied_Refuses()
    {
        // A real object rather than null (D83): the install always answers, even when it is empty.
        Should.Throw<ArgumentNullException>(() => DemoModels.Needed(timeline: null, game: null!));
    }

    [Test]
    public void Worn_WithNoInstallSupplied_Refuses()
    {
        Should.Throw<ArgumentNullException>(() => DemoModels.Worn(timeline: null, game: null!));
    }

    /// <remarks>
    /// **A `.phy` this project cannot parse costs the model its gib list, not the load** (B405, D32) — the contract
    /// `BreakPiecesOf` states and keeps by catching `InvalidDataException`. A file too short for `phyheader_t` was
    /// refused with `InvalidOperationException`, which went straight past that catch. **The control is the
    /// well-formed `.phy` in the same folder**, which must yield its one piece: without it, a folder the install
    /// never read would pass this test as well as a refusal that was caught.
    /// </remarks>
    [Test]
    public void BreakPiecesOf_APhyTooShortForItsHeader_IsEmptyRatherThanThrowing()
    {
        // A tf folder with no gameinfo.txt is read as loose files, which is what lets a test put a .phy in one.
        DirectoryInfo tf = Directory.CreateTempSubdirectory("tf2demosalvage-phy-");

        try
        {
            string models = Directory.CreateDirectory(Path.Combine(tf.FullName, "models")).FullName;

            File.WriteAllBytes(Path.Combine(models, "truncated.phy"), [0x10, 0, 0, 0]);
            File.WriteAllBytes(Path.Combine(models, "gibbed.phy"), Gibbed());

            GameContent game = GameContent.Open(tf.FullName, new RecordingLoggerFactory());

            DemoModels.BreakPiecesOf("models/gibbed.mdl", game).Count.ShouldBe(1, "the control: the folder is read");
            DemoModels.BreakPiecesOf("models/truncated.mdl", game).ShouldBeEmpty();
        }
        finally
        {
            tf.Delete(recursive: true);
        }
    }

    /// <remarks>
    /// **A robot's model and its pieces are loaded and packed, alike** (B763, B195): a player track names no model and
    /// the roster is the class scripts', so nothing else brings in the custom model `SetCustomModel` precaches
    /// (`tf_playerclass_shared.cpp:95-101`) or the break list `InitPlayerGibs` builds from it (`c_tf_player.cpp:7362`).
    /// Unlisted, a robot draws nothing for ever. The install is loose files: one class script, which makes it an install,
    /// and the robot's `.phy`. A demo with no custom model is the control.
    /// </remarks>
    [TestCase("models/bots/spy/bot_spy.mdl", true, TestName = "NeededAndToPack_ADemoWithARobot_HoldItsModelAndPieces")]
    [TestCase("", false, TestName = "NeededAndToPack_ADemoWithNoRobot_HoldNeither")]
    public void NeededAndToPack_ACustomModel_IsInBoth(string custom, bool held)
    {
        DirectoryInfo tf = Directory.CreateTempSubdirectory("tf2demosalvage-robot-");

        try
        {
            string scripts = Directory.CreateDirectory(Path.Combine(tf.FullName, "scripts", "playerclasses")).FullName;
            string bots = Directory.CreateDirectory(Path.Combine(tf.FullName, "models", "bots", "spy")).FullName;

            File.WriteAllText(Path.Combine(scripts, "spy.txt"), "\"PlayerClass\"\n{\n  \"model\" \"models/player/spy.mdl\"\n}\n");
            File.WriteAllBytes(Path.Combine(bots, "bot_spy.phy"), Breaking(@"bots\gibs\spybot_gib_head"));

            GameContent game = GameContent.Open(tf.FullName, new RecordingLoggerFactory());
            Core.Scene.DemoTimeline timeline = Core.Scene.DemoTimeline.Build(Core.Tests.SyntheticPlayer.DemoOfACorpse(8, custom));
            string piece = Content.Assets.PhysicsModel.GibPath(@"bots\gibs\spybot_gib_head");

            foreach (HashSet<string> set in (HashSet<string>[])[DemoModels.Needed(timeline, game), DemoModels.ToPack(timeline, game)])
            {
                set.ShouldContain("models/player/spy.mdl", "the control: the roster is read");
                set.ShouldNotContain(PlayerProps.GhostBlu, "no ghost in the demo, so none loaded (B767)");
                (set.Contains("models/bots/spy/bot_spy.mdl"), set.Contains(piece)).ShouldBe((held, held));
            }
        }
        finally
        {
            tf.Delete(recursive: true);
        }
    }

    /// <remarks>
    /// **A demo with a ghost in it loads and packs both ghosts** (B767): `ValidateModelIndex` draws a player in
    /// `TF_COND_HALLOWEEN_GHOST_MODE` as one of them (`c_tf_player.cpp:9004-9014`), and the engine precaches them only for
    /// the holiday that brings ghosts (`tf_player.cpp:2893-2897`). The same player out of ghost mode is the control.
    /// </remarks>
    [TestCase(true, TestName = "NeededAndToPack_ADemoWithAGhost_HoldBothGhosts")]
    [TestCase(false, TestName = "NeededAndToPack_ADemoWithNoGhost_HoldNeither")]
    public void NeededAndToPack_AGhost_BringsInTheGhostModels(bool ghost)
    {
        DirectoryInfo tf = Directory.CreateTempSubdirectory("tf2demosalvage-ghost-");

        try
        {
            string scripts = Directory.CreateDirectory(Path.Combine(tf.FullName, "scripts", "playerclasses")).FullName;
            string halloween = Directory.CreateDirectory(Path.Combine(tf.FullName, "models", "props_halloween")).FullName;

            File.WriteAllText(Path.Combine(scripts, "soldier.txt"), "\"PlayerClass\"\n{\n  \"model\" \"models/player/soldier.mdl\"\n}\n");

            // A stand-in break list on the RED ghost: a ghost's pieces are what it breaks into (B766).
            File.WriteAllBytes(Path.Combine(halloween, "ghost_no_hat_red.phy"), Breaking(@"props_halloween\ghost_gib"));
            File.WriteAllBytes(Path.Combine(halloween, "ghost_no_hat.phy"), Breaking(@"props_halloween\blu_ghost_gib"));

            GameContent game = GameContent.Open(tf.FullName, new RecordingLoggerFactory());
            Core.Scene.DemoTimeline timeline = Core.Scene.DemoTimeline.Build(Core.Tests.SyntheticPlayer.DemoOfGestures(
                0.015f, Core.Scene.SceneTeams.Red, 3, rules: null, alwaysLoser: false,
                new Core.Tests.SyntheticPlayer.GestureSnapshot(100, 0f, 0)
                {
                    PlayerCondEx2 = ghost ? 1 << (Core.Scene.PlayerConditions.HalloweenGhostMode - 64) : 0,
                }));
            string piece = Content.Assets.PhysicsModel.GibPath(@"props_halloween\ghost_gib");
            string bluPiece = Content.Assets.PhysicsModel.GibPath(@"props_halloween\blu_ghost_gib");

            foreach (HashSet<string> set in (HashSet<string>[])[DemoModels.Needed(timeline, game), DemoModels.ToPack(timeline, game)])
            {
                set.ShouldContain("models/player/soldier.mdl", "the control: the roster is read");
                (set.Contains(PlayerProps.GhostBlu), set.Contains(PlayerProps.GhostRed), set.Contains(piece), set.Contains(bluPiece))
                    .ShouldBe((ghost, ghost, ghost, ghost));
            }
        }
        finally
        {
            tf.Delete(recursive: true);
        }
    }

    /// <remarks>
    /// **The dispenser a crouched spy is drawn as is loaded and packed with any install** (B767): `CObjectDispenser::Precache`
    /// precaches it at level load (`tf_obj_dispenser.cpp:488`), and a demo where nobody builds one has no track naming it.
    /// No install is the control: there is nothing to load from.
    /// </remarks>
    [TestCase(true, TestName = "NeededAndToPack_WithAnInstall_HoldTheDispenser")]
    [TestCase(false, TestName = "NeededAndToPack_WithNoInstall_HoldNoDispenser")]
    public void NeededAndToPack_TheDispenserDisguise_IsLoadedWithAnInstall(bool install)
    {
        DirectoryInfo tf = Directory.CreateTempSubdirectory("tf2demosalvage-dispenser-");

        try
        {
            // The dispenser's pieces too (`PrecacheGibsForModel`, `:489`), which a spy gibbed as it throws (B766).
            string buildables = Directory.CreateDirectory(Path.Combine(tf.FullName, "models", "buildables")).FullName;
            File.WriteAllBytes(Path.Combine(buildables, "dispenser_light.phy"), Breaking(@"buildables\gibs\dispenser_gib1"));
            string piece = Content.Assets.PhysicsModel.GibPath(@"buildables\gibs\dispenser_gib1");

            if (install)
            {
                string scripts = Directory.CreateDirectory(Path.Combine(tf.FullName, "scripts", "playerclasses")).FullName;
                File.WriteAllText(Path.Combine(scripts, "spy.txt"), "\"PlayerClass\"\n{\n  \"model\" \"models/player/spy.mdl\"\n}\n");
            }

            GameContent game = GameContent.Open(tf.FullName, new RecordingLoggerFactory());

            foreach (HashSet<string> set in (HashSet<string>[])[DemoModels.Needed(null, game), DemoModels.ToPack(null, game)])
            {
                (set.Contains(Disguise.DispenserModel), set.Contains(piece)).ShouldBe((install, install));
            }
        }
        finally
        {
            tf.Delete(recursive: true);
        }
    }

    /// <remarks>
    /// **The HUD's layout models are on the level's preload** (B779 review 19, B812). The engine's panels load their models
    /// from the install on demand (basemodelpanel.cpp:407, mdlpanel.cpp:185); ours read the preload (`MapAssets.Geometry`, a
    /// lookup over `DemoModels.Needed`), so a model only a HUD layout names — the match doors, an attached model (a synthetic
    /// `versus_doors_lock.mdl`: the install's layout attaches none), the round sign, one of them inside the layout's
    /// `if_match` block — must be on it. Built the production way: an install folder, its
    /// `HudMatchStatus.res`, the two lists, and a model set whose loader answers only the preload's paths, as
    /// `MapAssets.Geometry` does; the doors then load with nothing else having asked. The control: a path no layout names
    /// is not on the list and does not load.
    /// </remarks>
    [Test]
    public void NeededAndToPack_AnInstallsHudLayout_HoldItsPanelModelsForTheLevelsLoad()
    {
        const string doors = "models/vgui/versus_doors.mdl";
        const string lockModel = "models/vgui/versus_doors_lock.mdl";
        const string sign = "models/props_ui/banner.mdl";
        DirectoryInfo tf = Directory.CreateTempSubdirectory("tf2demosalvage-hudmodels-");

        try
        {
            string scripts = Directory.CreateDirectory(Path.Combine(tf.FullName, "scripts", "playerclasses")).FullName;
            File.WriteAllText(Path.Combine(scripts, "spy.txt"), "\"PlayerClass\"\n{\n  \"model\" \"models/player/spy.mdl\"\n}\n");
            string ui = Directory.CreateDirectory(Path.Combine(tf.FullName, "resource", "UI")).FullName;
            File.WriteAllText(Path.Combine(ui, "HudMatchStatus.res"), $$"""
                "Resource/UI/HudMatchStatus.res"
                {
                    "MatchDoors" { "ControlName" "CModelPanel" "model" { "modelname" "{{doors}}" "attached_model" { "modelname" "{{lockModel}}" } } }
                    "if_match" { "RoundSignModel" { "ControlName" "CModelPanel" "model" { "modelname" "{{sign}}" } } }
                }
                """);
            GameContent game = GameContent.Open(tf.FullName, new RecordingLoggerFactory());

            foreach (HashSet<string> set in (HashSet<string>[])[DemoModels.Needed(null, game), DemoModels.ToPack(null, game)])
            {
                (set.Contains(doors), set.Contains(lockModel), set.Contains(sign)).ShouldBe((true, true, true));
            }

            HashSet<string> preload = DemoModels.Needed(null, game);
            PropModels.ModelFrames frames = new(
                [[new PropVertex(0f, 0f, 0f, 0f, 0f, MaterialIndex: 1) { Weights = (1f, 0f, 0f), Bones = (0, 0, 0) }]],
                new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
                [0],
                [true],
                Skinned: SyntheticSkinnedModel.WithBones("root"));
            EntityModelSet models = new() { Geometry = path => preload.Contains(path) ? frames : null };

            models.LoadMdl("models/vgui/not_named.mdl").ShouldBeNull("the control: a path no layout names");
            models.LoadMdl(doors).ShouldNotBeNull();
        }
        finally
        {
            tf.Delete(recursive: true);
        }
    }

    /// <remarks>
    /// **A carried item's panel models are on the preload** (B779 review 19): the class panel attaches
    /// `TfPlayerModelPanel.DisplayModel` for its class and team, with the item's world attachments, and the item panels its
    /// display model with no team — for an item the player may never draw (`HoldFirstValidItem`), so no held-weapon walk names
    /// it. Here a scout's scattergun, one world attachment and one view attachment: the class's model and the world
    /// attachment are listed, the view attachment (never drawn by a panel) is not.
    /// </remarks>
    [Test]
    public void AddItemModels_AWeaponWithAttachments_ListsTheWorldAttachmentAlone()
    {
        Content.Assets.ItemSchema schema = Content.Assets.ItemSchema.Read(System.Text.Encoding.UTF8.GetBytes("""
            "items_game"
            {
                "items"
                {
                    "13"
                    {
                        "item_slot" "primary" "used_by_classes" { "scout" "1" } "model_player" "models/weapons/w_scattergun.mdl"
                        "visuals" { "attached_models" { "0" { "model_display_flags" "1" "model" "models/weapons/scattergun_world_bit.mdl" } "1" { "model_display_flags" "2" "model" "models/weapons/scattergun_view_bit.mdl" } } }
                    }
                }
            }
            """));
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

        DemoModels.AddItemModels(new Hud.TfItemView(13, 6, new Dictionary<int, Core.Scene.EconAttributeValue>()), schema, paths);

        paths.ShouldBe(["models/weapons/w_scattergun.mdl", "models/weapons/scattergun_world_bit.mdl"], ignoreOrder: true);
    }

    /// <remarks>
    /// **Each class's stock weapon is on the preload** (B779 review 19): the class panel holds the class's base primary,
    /// else secondary, when nothing carried can be held (`HoldFirstValidItem`, tf_playermodelpanel.cpp:290-298), and a weapon
    /// nobody carries is named by no walk of the demo — measured missing on a 2015 POV demo, the spy's revolver. Here an
    /// install whose schema has the scout's base scattergun (a primary), the spy's base revolver (the spy has no primary, so
    /// the secondary arm, :294-296), and a non-base pistol. The control: the non-base item is not listed.
    /// </remarks>
    [Test]
    public void NeededAndToPack_AnInstallsSchema_HoldEachClasssStockWeapon()
    {
        const string stock = "models/weapons/c_models/c_scattergun.mdl";
        const string other = "models/weapons/c_models/c_pistol.mdl";
        const string secondaryOnly = "models/weapons/c_models/c_revolver.mdl";
        DirectoryInfo tf = Directory.CreateTempSubdirectory("tf2demosalvage-stockitems-");

        try
        {
            string scripts = Directory.CreateDirectory(Path.Combine(tf.FullName, "scripts", "playerclasses")).FullName;
            File.WriteAllText(Path.Combine(scripts, "scout.txt"), "\"PlayerClass\"\n{\n  \"model\" \"models/player/scout.mdl\"\n}\n");
            string items = Directory.CreateDirectory(Path.Combine(tf.FullName, "scripts", "items")).FullName;
            File.WriteAllText(Path.Combine(items, "items_game.txt"), $$"""
                "items_game"
                {
                    "items"
                    {
                        "13" { "item_slot" "primary" "baseitem" "1" "used_by_classes" { "scout" "1" } "model_player" "{{stock}}" }
                        "23" { "item_slot" "secondary" "used_by_classes" { "scout" "1" } "model_player" "{{other}}" }
                        "24" { "item_slot" "secondary" "baseitem" "1" "used_by_classes" { "spy" "1" } "model_player" "{{secondaryOnly}}" }
                    }
                }
                """);
            GameContent game = GameContent.Open(tf.FullName, new RecordingLoggerFactory());

            foreach (HashSet<string> set in (HashSet<string>[])[DemoModels.Needed(null, game), DemoModels.ToPack(null, game)])
            {
                (set.Contains(stock), set.Contains(secondaryOnly), set.Contains(other)).ShouldBe((true, true, false));
            }
        }
        finally
        {
            tf.Delete(recursive: true);
        }
    }

    /// <summary>A carried, non-base item's model: in no list but by the carried-item walk.</summary>
    private const string CarriedModel = "models/weapons/c_carried.mdl";

    /// <summary>An install with a schema naming a base scattergun and a non-base carried item (30), and optionally a class script.</summary>
    private static DirectoryInfo CarryingInstall(bool classScripts)
    {
        DirectoryInfo tf = Directory.CreateTempSubdirectory("tf2demosalvage-carried-");

        if (classScripts)
        {
            string scripts = Directory.CreateDirectory(Path.Combine(tf.FullName, "scripts", "playerclasses")).FullName;
            File.WriteAllText(Path.Combine(scripts, "scout.txt"), "\"PlayerClass\"\n{\n  \"model\" \"models/player/scout.mdl\"\n}\n");
        }

        string items = Directory.CreateDirectory(Path.Combine(tf.FullName, "scripts", "items")).FullName;
        File.WriteAllText(Path.Combine(items, "items_game.txt"), $$"""
            "items_game"
            {
                "items"
                {
                    "13" { "item_slot" "primary" "baseitem" "1" "used_by_classes" { "scout" "1" } "model_player" "models/weapons/c_scattergun.mdl" }
                    "30" { "item_slot" "secondary" "used_by_classes" { "scout" "1" } "model_player" "{{CarriedModel}}" }
                }
            }
            """);
        return tf;
    }

    /// <summary>A timeline whose one frame has a scout carrying item 30.</summary>
    private static Core.Scene.DemoTimeline Carrying() =>
        Core.Scene.DemoTimeline.ForTracks(
            [],
            [
                new Core.Scene.TimelineFrame(
                    0,
                    [
                        new Core.Scene.ScenePlayer(1, 0f, 0f, 0f, Team: 2, Health: 125, PlayerClass: 1)
                        {
                            Items = [new(40, "CTFPistol", 30, new Core.Scene.EconAttributeWire([], [], HasValidItemId: false), IsWeapon: true)],
                        },
                    ]),
            ]);

    /// <remarks>
    /// **One carried-item walk serves both of B195's lists** (B779 review 21): `Needed` at the map read and `ToPack` at the
    /// precache each walked every frame, about 0.5 s of f12's load. Here a timeline whose scout carries a non-base item —
    /// its model is in no list but by the walk — and an install with its schema: both lists hold the carried model, the map
    /// read's list walks and holds its result, and the precache's list gets that same result. The controls: nothing is held
    /// before the first list; another timeline under the same schema, and another schema, each walk again (review 21 item 9).
    /// </remarks>
    [Test]
    public void NeededThenToPack_OneTimelineWithACarriedItem_ShareOneCarriedWalk()
    {
        DirectoryInfo tf = CarryingInstall(classScripts: true);

        try
        {
            GameContent game = GameContent.Open(tf.FullName, new RecordingLoggerFactory());
            Core.Scene.DemoTimeline timeline = Carrying();
            DemoModels.CarriedWalked(timeline).ShouldBeNull("the control: nothing walked yet");

            HashSet<string> needed = DemoModels.Needed(timeline, game);
            IReadOnlySet<string> walked = DemoModels.CarriedWalked(timeline).ShouldNotBeNull();
            HashSet<string> toPack = DemoModels.ToPack(timeline, game);

            (needed.Contains(CarriedModel), toPack.Contains(CarriedModel), walked.Contains(CarriedModel)).ShouldBe((true, true, true));
            DemoModels.CarriedWalked(timeline).ShouldBeSameAs(walked);
            DemoModels.CarriedPanelModels(Carrying(), game.Weapons.Items.ShouldNotBeNull())
                .ShouldNotBeSameAs(walked, "the control: another timeline under the same schema walks again");
            DemoModels.CarriedPanelModels(timeline, CarriedSchema()).ShouldNotBeSameAs(walked, "the control: another schema walks again");
        }
        finally
        {
            tf.Delete(recursive: true);
        }
    }

    /// <remarks>
    /// **The panel models are not under the level-load guard** (B779 review 21 item 3): `Needed` and `ToPack` add the
    /// arrows and the dispenser only when the install names class models, and the panel models used to ride inside that
    /// guard. The engine's panels have no such condition. Here an install with a schema and no class scripts: both lists
    /// still hold the carried item's model and the class's stock weapon. The control: neither lists a class model.
    /// </remarks>
    [Test]
    public void NeededAndToPack_AnInstallWithNoClassScripts_StillListThePanelModels()
    {
        DirectoryInfo tf = CarryingInstall(classScripts: false);

        try
        {
            GameContent game = GameContent.Open(tf.FullName, new RecordingLoggerFactory());
            Core.Scene.DemoTimeline timeline = Carrying();

            foreach (HashSet<string> set in (HashSet<string>[])[DemoModels.Needed(timeline, game), DemoModels.ToPack(timeline, game)])
            {
                (set.Contains(CarriedModel), set.Contains("models/weapons/c_scattergun.mdl"), set.Contains("models/player/scout.mdl"))
                    .ShouldBe((true, true, false));
            }
        }
        finally
        {
            tf.Delete(recursive: true);
        }
    }

    /// <remarks>
    /// **One level's two lists read one HUD view** (B779 review 22 item 4; B195): the load fixes the HUD as it begins
    /// (`GameContent.BeginLevelLoad`), so a HUD changed between `Needed` at the map read and `ToPack` at the precache does
    /// not split them. Here a custom HUD chosen as the load begins and stock chosen before either list: both list the
    /// custom HUD's doors and not the stock ones. The control: with no load begun, the lists read the HUD as it is now.
    /// </remarks>
    [Test]
    public void NeededAndToPack_AHudChangedDuringALoad_ReadTheHudTheLoadBeganWith()
    {
        DirectoryInfo tf = Directory.CreateTempSubdirectory("tf2demosalvage-hudsnapshot-");

        try
        {
            const string stockDoors = "models/vgui/versus_doors.mdl";
            const string customDoors = "models/custom/chosen_doors.mdl";
            string chosen = Path.Combine(tf.FullName, "custom", "chosen_hud");
            WriteDoors(Path.Combine(tf.FullName, "resource", "UI"), stockDoors);
            WriteDoors(Path.Combine(chosen, "resource", "UI"), customDoors);
            GameContent game = GameContent.Open(tf.FullName, new RecordingLoggerFactory());

            game.ChosenHud = null;
            DemoModels.Needed(null, game).Contains(stockDoors).ShouldBeTrue("the control: no load begun reads the HUD now");

            game.ChosenHud = chosen;
            game.BeginLevelLoad();
            game.ChosenHud = null;

            foreach (HashSet<string> set in (HashSet<string>[])[DemoModels.Needed(null, game), DemoModels.ToPack(null, game)])
            {
                (set.Contains(customDoors), set.Contains(stockDoors)).ShouldBe((true, false));
            }
        }
        finally
        {
            tf.Delete(recursive: true);
        }
    }

    /// <summary>A `HudMatchStatus.res` in that `resource/UI` folder whose doors are this model.</summary>
    private static void WriteDoors(string ui, string doors)
    {
        Directory.CreateDirectory(ui);
        File.WriteAllText(
            Path.Combine(ui, "HudMatchStatus.res"),
            $$"""
            "Resource/UI/HudMatchStatus.res" { "MatchDoors" { "model" { "modelname" "{{doors}}" } } }
            """);
    }

    /// <remarks>
    /// **The preload reads the HUD the viewer draws** (B779 review 20 item 1): the chosen HUD over the stock files
    /// (`GameContent.HudArchives`), the view the HUD's own frame reads — not every `custom/` folder at once. An install with
    /// a stock layout and two custom HUDs, each naming its own doors: chosen, the first HUD's doors and not the other's;
    /// none chosen, the stock doors and neither custom's. The control: the custom HUDs are installed under `custom/`.
    /// </remarks>
    [Test]
    public void Needed_AChosenCustomHud_ListsThatHudsLayoutModelsAlone()
    {
        DirectoryInfo tf = Directory.CreateTempSubdirectory("tf2demosalvage-customhud-");

        try
        {
            string scripts = Directory.CreateDirectory(Path.Combine(tf.FullName, "scripts", "playerclasses")).FullName;
            File.WriteAllText(Path.Combine(scripts, "spy.txt"), "\"PlayerClass\"\n{\n  \"model\" \"models/player/spy.mdl\"\n}\n");
            WriteDoors(Path.Combine(tf.FullName, "resource", "UI"), "models/vgui/versus_doors.mdl");
            string chosen = Path.Combine(tf.FullName, "custom", "chosen_hud");
            WriteDoors(Path.Combine(chosen, "resource", "UI"), "models/custom/chosen_doors.mdl");
            WriteDoors(Path.Combine(tf.FullName, "custom", "other_hud", "resource", "UI"), "models/custom/other_doors.mdl");
            GameContent game = GameContent.Open(tf.FullName, new RecordingLoggerFactory());

            game.ChosenHud = chosen;
            HashSet<string> withChosen = DemoModels.Needed(null, game);
            game.ChosenHud = null;
            HashSet<string> stock = DemoModels.Needed(null, game);

            (withChosen.Contains("models/custom/chosen_doors.mdl"), withChosen.Contains("models/custom/other_doors.mdl"))
                .ShouldBe((true, false));
            (stock.Contains("models/vgui/versus_doors.mdl"), stock.Contains("models/custom/chosen_doors.mdl"),
                stock.Contains("models/custom/other_doors.mdl")).ShouldBe((true, false, false));
        }
        finally
        {
            tf.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A schema for the carried-item rows: a scout scattergun whose world model differs from its player model (the Hot Hand's
    /// shape), with a festive-only world attachment and an extra wearable; a per-class pistol; a paint-kit StatTrak weapon.
    /// </summary>
    private static Content.Assets.ItemSchema CarriedSchema() => Content.Assets.ItemSchema.Read(System.Text.Encoding.UTF8.GetBytes("""
        "items_game"
        {
            "items"
            {
                "13"
                {
                    "item_slot" "primary" "used_by_classes" { "scout" "1" }
                    "model_player" "models/weapons/c_scattergun.mdl" "model_world" "models/weapons/w_scattergun.mdl"
                    "extra_wearable" "models/weapons/scattergun_extra.mdl"
                    "visuals" { "attached_models_festive" { "0" { "model_display_flags" "1" "model" "models/weapons/festive_lights.mdl" } } }
                }
                "23"
                {
                    "item_slot" "secondary" "used_by_classes" { "scout" "1" "engineer" "1" }
                    "model_player_per_class" { "scout" "models/weapons/pistol_scout.mdl" "engineer" "models/weapons/pistol_engineer.mdl" }
                }
                "15" { "item_slot" "primary" "used_by_classes" { "heavy" "1" } "model_player" "models/weapons/minigun.mdl" "static_attrs" { "weapon_uses_stattrak_module" "models/weapons/stattrack.mdl" } }
                "18"
                {
                    "item_slot" "primary" "used_by_classes" { "soldier" "1" }
                    "model_player_per_class" { "soldier" "models/weapons/rocket_plain.mdl" }
                    "visuals" { "styles" { "0" { "model_player_per_class" { "soldier" "models/weapons/rocket_style.mdl" } } } }
                }
            }
            "attributes"
            {
                "542" { "name" "item style override" }
                "834" { "name" "paintkit_proto_def_index" "stored_as_integer" "1" }
                "2001" { "name" "weapon_uses_stattrak_module" }
            }
        }
        """));

    /// <remarks>
    /// **Every model a panel can ask for one item** (B779 review 20 items 2 and 7): the class panel's `EquipItem` model, which is
    /// the world model when the item has one, and the item panel's `GetPlayerDisplayModel( first class, 0 )`
    /// (item_model_panel.cpp:330-343) — two different paths for the Hot Hand's shape; the extra wearable
    /// (tf_playermodelpanel.cpp, `EquipAllWearables`); a festive-only world attachment, which `AttachedModelsFor` adds to the
    /// plain list under `is_festivized` (econ_entity.cpp:1107-1131), so asking with it lists both; each class's
    /// per-class model; a paint-kit weapon's StatTrak module.
    /// </remarks>
    [Test]
    public void AddItemModels_ItemsOfEachShape_ListEveryPanelModel()
    {
        Content.Assets.ItemSchema schema = CarriedSchema();
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

        DemoModels.AddItemModels(new Hud.TfItemView(13, 6, new Dictionary<int, Core.Scene.EconAttributeValue>()), schema, paths);
        DemoModels.AddItemModels(new Hud.TfItemView(23, 6, new Dictionary<int, Core.Scene.EconAttributeValue>()), schema, paths);
        DemoModels.AddItemModels(
            new Hud.TfItemView(15, 11, new Dictionary<int, Core.Scene.EconAttributeValue> { [834] = new(834, 350) }) { IteratesDefinition = true },
            schema,
            paths);

        paths.ShouldBe(
            [
                "models/weapons/w_scattergun.mdl", "models/weapons/c_scattergun.mdl", "models/weapons/scattergun_extra.mdl",
                "models/weapons/festive_lights.mdl", "models/weapons/pistol_scout.mdl", "models/weapons/pistol_engineer.mdl",
                "models/weapons/minigun.mdl", "models/weapons/stattrack.mdl",
            ],
            ignoreOrder: true);
    }

    /// <remarks>
    /// **What players carry, and a spy's disguise weapon** (B779 review 20 items 3 and 4): every frame's players' items and
    /// disguise weapons are walked, each instance once, and each model-deciding item once — keyed after its view is made, so
    /// two rows of one definition with different styles or paint kits are both resolved. Here one player carrying the
    /// scattergun and disguised with the pistol, then the same definition again with a paint kit.
    /// </remarks>
    [Test]
    public void AddCarriedItemModels_ItemsAndADisguiseWeapon_ListEachItemsModels()
    {
        Content.Assets.ItemSchema schema = CarriedSchema();
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        Core.Scene.SceneItem scattergun = new(40, "CTFScattergun", 13, new Core.Scene.EconAttributeWire([], [], HasValidItemId: false), IsWeapon: true);
        Core.Scene.SceneItem pistol = new(41, "CTFPistol", 23, new Core.Scene.EconAttributeWire([], [], HasValidItemId: false), IsWeapon: true);
        Core.Scene.SceneItem plainMinigun = new(42, "CTFMinigun", 15, new Core.Scene.EconAttributeWire([], [], HasValidItemId: false), IsWeapon: true);
        Core.Scene.SceneItem kitMinigun = new(
            43, "CTFMinigun", 15, new Core.Scene.EconAttributeWire([new(834, 350)], [], HasValidItemId: false), IsWeapon: true);
        Core.Scene.SceneItem plainRocket = new(44, "CTFRocketLauncher", 18, new Core.Scene.EconAttributeWire([], [], HasValidItemId: false), IsWeapon: true);
        Core.Scene.SceneItem styledRocket = new(
            45, "CTFRocketLauncher", 18,
            new Core.Scene.EconAttributeWire([new(542, BitConverter.SingleToInt32Bits(0f))], [], HasValidItemId: false),
            IsWeapon: true);

        DemoModels.AddCarriedItemModels(
            [
                new Core.Scene.ScenePlayer(1, 0f, 0f, 0f, Team: 2, Health: 100, PlayerClass: 8) { Items = [scattergun], DisguiseWeaponItem = pistol },
                new Core.Scene.ScenePlayer(2, 0f, 0f, 0f, Team: 3, Health: 100, PlayerClass: 6) { Items = [plainMinigun, kitMinigun] },
                new Core.Scene.ScenePlayer(3, 0f, 0f, 0f, Team: 2, Health: 100, PlayerClass: 3) { Items = [plainRocket, styledRocket] },
            ],
            schema,
            paths);

        paths.ShouldContain("models/weapons/c_scattergun.mdl");
        paths.ShouldContain("models/weapons/pistol_scout.mdl", "the disguise weapon");
        paths.ShouldContain("models/weapons/stattrack.mdl", "the paint-kit row, after a plain row of its definition");
        paths.ShouldContain("models/weapons/rocket_plain.mdl", "the control: the plain row of the styled definition");
        paths.ShouldContain("models/weapons/rocket_style.mdl", "the styled row (style 0, the first in the list), after a plain row of its definition");
    }

    /// <summary>A <c>.phy</c> with no solids and one <c>break</c> block naming one piece.</summary>
    private static byte[] Breaking(string piece) =>
    [
        .. BitConverter.GetBytes(16),
        .. BitConverter.GetBytes(0),
        .. BitConverter.GetBytes(0),
        .. BitConverter.GetBytes(0),
        .. System.Text.Encoding.ASCII.GetBytes($"break {{\n  \"model\" \"{piece}\"\n  \"fadetime\" \"10\"\n}}\n"),
    ];

    /// <summary>A <c>.phy</c> with no solids and one <c>break</c> block.</summary>
    private static byte[] Gibbed() =>
    [
        .. BitConverter.GetBytes(16),  // size, sizeof(phyheader_t)
        .. BitConverter.GetBytes(0),   // id
        .. BitConverter.GetBytes(0),   // solidCount
        .. BitConverter.GetBytes(0),   // checkSum
        .. System.Text.Encoding.ASCII.GetBytes(
            "break {\n  \"model\" \"player\\gibs\\medicgib001\"\n  \"health\" \"0\"\n  \"fadetime\" \"10\"\n}\n"),
    ];

    /// <summary>An install that is not there, which is the only kind a unit test should assume.</summary>
    private static GameContent Empty() =>
        GameContent.Open(folder: null, new RecordingLoggerFactory());
}
