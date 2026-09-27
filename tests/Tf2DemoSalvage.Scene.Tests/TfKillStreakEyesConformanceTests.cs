using System.Numerics;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`C_TFPlayer::UpdateKillStreakEffects` (c_tf_player.cpp:10414-10610) as `ClientKillStreakBuffThink` drives it.</summary>
public sealed class TfKillStreakEyesConformanceTests
{
    private const string Schema = """
        "items_game"
        {
            "attribute_controlled_attached_particles"
            {
                "killstreak"
                {
                    "2002" { "system" "killstreak_t1_lvl1" }
                    "22002" { "system" "killstreak_t1_lvl2" }
                    "2003" { "system" "killstreak_t2_teamcolor_red" }
                    "2004" { "system" "killstreak_t2_teamcolor_blue" }
                }
            }
        }
        """;

    private static readonly ItemSchema Items = ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));

    private static readonly SceneItem Weapon = new(30, "CTFRocketLauncher", 18, new EconAttributeWire([], [], HasValidItemId: false), IsWeapon: true);

    private static ScenePlayer Player(int team, int streak) =>
        new(1, 0f, 0f, 0f, Team: team, Health: 100, PlayerClass: 3, ActiveWeapon: 30) { Items = [Weapon], KillStreak = streak };

    private static HudState State(ScenePlayer player) => new(true, true, 0, 100, true, 100, 150, 1f, player.Team ?? 0, LocalIndex: 1, Players: [player]);

    /// <summary>`killstreak_effect` and `killstreak_idleeffect` on the held weapon.</summary>
    private static System.Func<ScenePlayer, SceneItem, string, float, float> Hook(int effect, int color) =>
        (_, _, attributeClass, value) => attributeClass switch
        {
            "killstreak_effect" => effect,
            "killstreak_idleeffect" => color,
            _ => value,
        };

    [Test]
    public void Think_FiveKills_NamesTheEffectWithTheSheenColors()
    {
        TfKillStreakEyes eyes = new();
        ScenePlayer red = Player(2, 5);

        eyes.Think(red, State(red), Items, Hook(2002, 2));

        eyes.EffectName.ShouldBe("killstreak_t1_lvl1");
        eyes.Color1.ShouldBe(new Vector3(1f, 237f / 255f, 138f / 255f), "g_KillStreakEffectsBase[2] color 1 (:340)");
        eyes.Color2.ShouldBe(new Vector3(1f, 213f / 255f, 65f / 255f));
    }

    [Test]
    public void Think_FourKills_HasColorsButNoEffect()
    {
        TfKillStreakEyes eyes = new();
        ScenePlayer red = Player(2, 4);

        eyes.Think(red, State(red), Items, Hook(2002, 2));

        eyes.EffectName.ShouldBeNull("tf_killstreakeyes_minkills 5 (:10515)");
        eyes.Color1.ShouldNotBe(Vector3.Zero);
    }

    [Test]
    public void Think_TenKills_TakesTheHighGlowIndex()
    {
        TfKillStreakEyes eyes = new();
        ScenePlayer red = Player(2, 10);

        eyes.Think(red, State(red), Items, Hook(2002, 2));

        eyes.EffectName.ShouldBe("killstreak_t1_lvl2", "index + 20000 at tf_killstreakeyes_maxkills (:10540-10543)");
    }

    [Test]
    public void Think_BlueWithARedTeamColorEffect_SwapsToTheBlueSystemAndTable()
    {
        TfKillStreakEyes eyes = new();
        ScenePlayer blue = Player(3, 6);

        eyes.Think(blue, State(blue), Items, Hook(2003, 1));

        eyes.EffectName.ShouldBe("killstreak_t2_teamcolor_blue", "(:10550-10554)");
        eyes.Color1.ShouldBe(new Vector3(0f, 92f / 255f, 1f), "g_KillStreakEffectsBlue[1] (:352)");
    }

    [Test]
    public void Think_NoKillstreakAttributes_ClearsTheEffectAndColorOne()
    {
        TfKillStreakEyes eyes = new();
        ScenePlayer red = Player(2, 5);

        eyes.Think(red, State(red), Items, Hook(2002, 2));
        eyes.Think(red with { KillStreak = 6 }, State(red), Items, Hook(0, 2));

        eyes.EffectName.ShouldBeNull();
        eyes.Color1.ShouldBe(Vector3.Zero, "(:10478)");
    }

    [Test]
    public void DemomanEyeEffectName_ByHeads_IsTheEyelanderLevel()
    {
        TfKillStreakEyes.DemomanEyeEffectName(0).ShouldBeNull();
        TfKillStreakEyes.DemomanEyeEffectName(2).ShouldBe("eye_powerup_green_lvl_2");
        TfKillStreakEyes.DemomanEyeEffectName(9).ShouldBe("eye_powerup_green_lvl_4");
    }
}
