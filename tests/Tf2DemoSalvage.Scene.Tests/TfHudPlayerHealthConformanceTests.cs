using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFHudPlayerStatus`, `CTFHudPlayerHealth` and `CTFHealthPanel` (game/client/tf/tf_hud_playerstatus.cpp).</summary>
/// <remarks>
/// A synthetic `HudPlayerHealth.res` places the bonus image at 10,10 40×40 and the numbers as `%Health%`/`%MaxHealth%`
/// labels; `HUDDeathWarning` is 255 0 0 255. `HealthBonusPosAdj` is 25 and `HealthDeathWarning` 0.49 by default.
/// </remarks>
public sealed class TfHudPlayerHealthConformanceTests
{
    private const string Res = """
        "Resource/UI/HudPlayerHealth.res"
        {
            "PlayerStatusHealthImage" { "xpos" "0" "ypos" "0" "wide" "40" "tall" "40" }
            "PlayerStatusHealthBonusImage" { "xpos" "10" "ypos" "10" "wide" "40" "tall" "40" "visible" "0" }
            "PlayerStatusHealthValue" { "ControlName" "CExLabel" "labelText" "%Health%" }
            "PlayerStatusMaxHealthValue" { "ControlName" "CExLabel" "labelText" "%MaxHealth%" }
        }
        """;

    [Test]
    public void Paint_PartHealth_FillsTheCrossFromTheBottom()
    {
        TextRecorder surface = new();
        TfHealthPanel panel = new(null, "Cross") { Wide = 40, Tall = 40, Health = 0.6f, FgColor = (255, 255, 255, 255) };

        panel.Paint(surface, Context());

        surface.Calls.ShouldContain("texture hud/health_color");
        // `h * ( 1.0f - m_flHealth )` in float: 40 × 0.39999998, kept, not rounded to 16.
        surface.SubRects.ShouldBe(["0 15.999999 40 40 uv 0 0.39999998 1 1"]);
    }

    [Test]
    public void Paint_NoHealth_IsTheDeadCrossInWhite()
    {
        TextRecorder surface = new();
        TfHealthPanel panel = new(null, "Cross") { Wide = 40, Tall = 40, Health = 0f, FgColor = (1, 2, 3, 4) };

        panel.Paint(surface, Context());

        surface.Calls.ShouldBe(["texture hud/health_dead", "color 255 255 255 255", "quad 0 0 40 40"]);
    }

    [Test]
    public void SetHealth_Numbers_ShowTheMaximumOnlyWhenFiveIsMissing()
    {
        (TfHudPlayerHealth health, VguiLabel value, VguiLabel max) = Built();

        health.SetHealth(120, 125, 185);
        (value.Text, max.Text).ShouldBe(("120", "125"));
        FindChild(health, "BuildingStatusHealthImageBG").Visible.ShouldBeFalse("m_bBuilding: a player, not a building");

        health.SetHealth(121, 125, 185);
        (value.Text, max.Text).ShouldBe(("121", string.Empty));

        health.SetHealth(0, 125, 185);
        (value.Text, max.Text).ShouldBe((string.Empty, string.Empty));
        health.HealthImageBackground.Visible.ShouldBeFalse();
    }

    [Test]
    public void SetHealth_Overheal_GrowsTheBonusImageByTheShareOfTheBoost()
    {
        (TfHudPlayerHealth health, _, _) = Built();

        // (160 − 125) / (185 − 125) = 0.583; × 25 = 14.58 → 15.
        health.SetHealth(160, 125, 185);

        VguiImagePanel bonus = health.HealthBonusImage;
        bonus.Visible.ShouldBeTrue();
        (bonus.X, bonus.Y, bonus.Wide, bonus.Tall).ShouldBe((10 - 15, 10 - 15, 40 + 30, 40 + 30));
        bonus.DrawColor.ShouldBe(((byte)255, (byte)255, (byte)255, (byte)255));
    }

    [Test]
    public void SetHealth_UnderTheWarning_TintsTheCrossAndGrowsTheGlow()
    {
        (TfHudPlayerHealth health, _, _) = Built();

        // 125 × 0.49 = 61.25; (61.25 − 40) / 61.25 = 0.3469; × 25 = 8.67 → 9.
        health.SetHealth(40, 125, 185);

        health.HealthBonusImage.Wide.ShouldBe(40 + 18);
        health.HealthBonusImage.DrawColor.ShouldBe(((byte)255, (byte)0, (byte)0, (byte)255));
        health.HealthImage.FgColor.ShouldBe(((byte)255, (byte)0, (byte)0, (byte)255));

        health.SetHealth(100, 125, 185);

        health.HealthBonusImage.Visible.ShouldBeFalse();
        (health.HealthBonusImage.X, health.HealthBonusImage.Wide).ShouldBe((10, 40), "back to its own bounds");
        health.HealthImage.FgColor.ShouldBe(((byte)255, (byte)255, (byte)255, (byte)255));
    }

    [Test]
    public void Think_WithinFiftyMilliseconds_DoesNotReadAgain()
    {
        (TfHudPlayerHealth health, VguiLabel value, _) = Built();
        HudState state = new(true, true, 0, 100, true, 125, 185, CurTime: 10f);

        health.Think(state);
        health.Think(state with { Health = 90, CurTime = 10.04f });
        value.Text.ShouldBe("100");

        health.Think(state with { Health = 90, CurTime = 10.06f });
        value.Text.ShouldBe("90");
    }

    [Test]
    public void PlayerStatus_IsHiddenByHealthAndDeath()
    {
        HudViewport viewport = new();
        TfHudPlayerStatus status = new(viewport);
        HudState alive = new(true, true, 0, 100, true, 125, 185, 1f);

        viewport.Think(alive);
        status.Visible.ShouldBeTrue();

        viewport.Think(alive with { Health = 0, Alive = false });
        status.Visible.ShouldBeFalse();
    }

    private static (TfHudPlayerHealth Health, VguiLabel Value, VguiLabel Max) Built()
    {
        VguiContext context = Context();
        TfHudPlayerHealth health = new(null, "HudPlayerHealth");

        health.PerformApplySchemeSettings(context);

        VguiLabel value = (VguiLabel)FindChild(health, "PlayerStatusHealthValue");
        VguiLabel max = (VguiLabel)FindChild(health, "PlayerStatusMaxHealthValue");

        return (health, value, max);
    }

    private static VguiPanel FindChild(VguiPanel panel, string name)
    {
        foreach (VguiPanel child in panel.Children)
        {
            if (child.Name == name)
            {
                return child;
            }
        }

        throw new KeyNotFoundException(name);
    }

    private static VguiContext Context()
    {
        KeyValuesTree scheme = KeyValuesTree.Load(
            Encoding.UTF8.GetBytes("""Scheme { Colors { "HUDDeathWarning" "255 0 0 255" } Borders { } Fonts { } }"""), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        Dictionary<string, byte[]> files = new() { ["resource/UI/HudPlayerHealth.res"] = Encoding.UTF8.GetBytes(Res) };

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = new TextRecorder(),
            Read = files.GetValueOrDefault,
        };
    }
}
