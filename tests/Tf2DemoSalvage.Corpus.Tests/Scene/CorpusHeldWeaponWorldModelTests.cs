using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.SdkReference;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace rather than to the
// helper class — the same reason `CorpusItemAnimSlotTests` beside it gives.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// On a real recording, a held weapon is DRAWN with the world model the engine gives it — its item's `model_world` first
/// (RISKS B105).
/// </summary>
/// <remarks>
/// **The assertion on the output**: `MomentScene.Build` — the call the viewer makes each frame — over the props and players
/// the timeline holds at the tick, with the installed game's resolver, read back out of its draw list. The rule and its
/// citations are `WeaponWorldModelConformanceTests`; `MomentSceneTests` pins the wiring on a synthetic Hot Hand.
///
/// **The specimens are the census's** (`item-props 933,1080,1102,1181,735,736,810,831` over gcor and lcor, 2026-09-30).
/// Of the four items that declare a `model_world`, three are held somewhere. **The Hot Hand is the one whose answer
/// changes**: a pyro holds it in `tf2-2026-pub-pov-clean` for 148 ticks from 5133, the wire already naming its
/// `model_world` — the server precaches `GetWorldModel()` into `m_iWorldModelIndex` — and the port drew its first-person
/// `c_slapping_glove.mdl` over it. The Festive Sapper and the Snack Attack are `CTFWeaponSapper`s, whose
/// `C_TFWeaponSapper::GetWorldModel` skips the builder's own to reach the item's (c_tf_weapon_builder.cpp:456-460); each
/// names its `model_player` again, so their draw was right before the rule and these rows pin that it stays so. Every
/// other held sapper is the stock one, item 735, a `CTFWeaponBuilder` through the `weapon_sapper` prefab, which the engine
/// draws as `c_sapper.mdl` by either route: its item's `model_player` for another player's builder, which has no object
/// type to ask (:406-419), and `scripts/objects.txt`'s `Playermodel` for the owner's, which names the same file. The wire
/// agrees at every sapper moment, so those cannot tell the item from the wire; what they guard is the draw list — a
/// weapon draws its item's world model, not "" and not nothing. Each moment is the first the census found in hand.
///
/// **Each demo gets its own timeline, walked in tick order, rather than `TimelineCache`'s.** `PropsAt` advances a sample it
/// keeps between calls (`_sampledTo`, B259), so two tests sampling one shared timeline at once read each other's state: run
/// as four parallel cases on the cached z1800, three found no sapper at a tick where the census, and each case alone,
/// found it.
/// </remarks>
public sealed class CorpusHeldWeaponWorldModelTests
{
    /// <summary>`TF_CLASS_PYRO`.</summary>
    private const int Pyro = 7;

    /// <summary>`TF_CLASS_SPY`.</summary>
    private const int Spy = 8;

    private const string StockSapperModel = "models/weapons/c_models/c_sapper/c_sapper.mdl";

    /// <summary>tf2-2026-pub-pov-clean (lcor): a pyro holding the Hot Hand (1181), which draws `w_slapping_glove.mdl`.</summary>
    /// <remarks>The recorder is a soldier (player 9); the pyro is player 17, seen in the third person.</remarks>
    [Test]
    public void Build_APyroHoldingTheHotHandInThePubPov_DrawsItsModelWorld()
    {
        DrawnAt(
            "tf2-2026-pub-pov-clean",
            Pyro,
            [(1030, 5133, 1181, "CTFSlap", "models/weapons/c_models/c_slapping_glove/w_slapping_glove.mdl")]);
    }

    /// <summary>koth_ashville (lcor): a Snack Attack (1102) and a Festive Sapper (1080), each on its `model_world`.</summary>
    [Test]
    public void Build_SpiesHoldingModelWorldSappersInAshville_DrawTheirModelWorld()
    {
        DrawnAt(
            "demostf-koth_ashville_final2-1491186",
            Spy,
            [
                (495, 562, 1102, "CTFWeaponSapper", "models/weapons/c_models/c_breadmonster_sapper/c_breadmonster_sapper.mdl"),
                (391, 35938, 1080, "CTFWeaponSapper", "models/weapons/c_models/c_sapper/c_sapper_xmas.mdl"),
            ]);
    }

    /// <summary>z1800 (gcor): the four stock sappers, 735, its census found in hand.</summary>
    [Test]
    public void Build_SpiesHoldingTheStockSapperInZ1800_DrawItsModelPlayer()
    {
        DrawnAt(
            "z1800",
            Spy,
            [
                (752, 4070, 735, "CTFWeaponBuilder", StockSapperModel),
                (673, 17953, 735, "CTFWeaponBuilder", StockSapperModel),
                (778, 25134, 735, "CTFWeaponBuilder", StockSapperModel),
                (672, 37574, 735, "CTFWeaponBuilder", StockSapperModel),
            ]);
    }

    /// <summary>Builds the scene at each moment, in tick order, and reads back the weapon's drawn model.</summary>
    private static void DrawnAt(
        string demo, int holderClass, (int Entity, int Tick, int Item, string ClassName, string Model)[] moments)
    {
        string path = Corpus.Demo(demo);
        string root = GameInstall.Require();
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        MomentScene scene = new(new EntityModelSet(), new ViewmodelScene(), NullLogger.Instance)
        {
            Weapons = GameContent.Open(root, NullLoggerFactory.Instance).Weapons,
        };

        List<SceneProp> props = [];
        List<ScenePlayer> players = [];

        foreach ((int entity, int tick, int item, string className, string model) in moments)
        {
            timeline.PropsAt(tick, props);
            timeline.PlayersAt(tick, players);

            // The controls: the census's weapon, in its holder's hand, at this tick. Without them a model read off some
            // other prop — or off none — would pass for the one this is about.
            SceneProp weapon = props.Single(prop => prop.EntityIndex == entity);
            weapon.ItemDefinitionIndex.ShouldBe(item);
            weapon.ClassName.ShouldBe(className);
            weapon.WeaponState.ShouldBe(EntityState.WeaponActive, "held, so C_BaseCombatWeapon::ShouldDraw keeps it");
            players.Single(player => player.EntityIndex == (weapon.AttachedTo ?? weapon.OwnedBy)).PlayerClass
                .ShouldBe(holderClass);

            scene.Build(
                players,
                props,
                new MomentInfo(
                    Tick: tick,
                    CurrentTick: tick,
                    FirstPerson: false,
                    Followed: null,
                    EyeCamera: null,
                    IntervalPerTick: timeline.IntervalPerTick,
                    ViewmodelFieldOfView: 54f));

            scene.Drawn.Single(prop => prop.EntityIndex == entity).ModelPath
                .ShouldBe(model, $"{demo}: entity {entity} at tick {tick}");
        }
    }
}
