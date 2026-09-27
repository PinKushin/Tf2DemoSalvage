using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `CTFHudPlayerHealth::OnThink` (tf_hud_playerstatus.cpp:942): the condition icons — `m_vecBuffInfo`'s buff table
/// (:663–688, updated :969–983) and the six bleed/debuff panels (:986–1004).
/// </summary>
public sealed class TfHudPlayerHealthConditionIconsConformanceTests
{
    private static HudState Playing(PlayerConditions conditions, int team = 2) =>
        new(InGame: true, HasLocalPlayer: true, HideHud: 0, Health: 100, Alive: true, MaxHealth: 100, Team: team, Conditions: conditions, CurTime: 1f);

    private static TfHudPlayerHealth Panel() => new(null, "HudPlayerHealth");

    private static VguiImagePanel Image(TfHudPlayerHealth health, string name) =>
        (VguiImagePanel)health.FindChildByName(name)!;

    [Test]
    public void Think_Bleeding_ShowsTheBleedImageTintedByColorFade()
    {
        TfHudPlayerHealth health = Panel();
        PlayerConditions bleeding = new(Cond: 1 << 25, Ex: 0, Ex2: 0, Ex3: 0, Ex4: 0);

        health.Think(Playing(bleeding) with { RealTime = 0f });

        VguiImagePanel bleed = Image(health, "PlayerStatusBleedImage");
        bleed.Visible.ShouldBeTrue();
        bleed.DrawColor.ShouldBe(((byte)160, (byte)0, (byte)0, (byte)255));
    }

    [Test]
    public void Think_NoConditions_EveryIconIsHidden()
    {
        TfHudPlayerHealth health = Panel();

        health.Think(Playing(default));

        Image(health, "PlayerStatusBleedImage").Visible.ShouldBeFalse();
        Image(health, "PlayerStatus_RuneKing").Visible.ShouldBeFalse();
        Image(health, "PlayerStatus_MedicUberBulletResistImage").Visible.ShouldBeFalse();
    }

    [Test]
    public void Think_UberAndSmallBulletResistTogether_OnlyTheFirstInTableDraws()
    {
        TfHudPlayerHealth health = Panel();

        // TF_COND_MEDIGUN_UBER_BULLET_RESIST (58) and TF_COND_MEDIGUN_SMALL_BULLET_RESIST (61) — same buff class.
        PlayerConditions both = new(Cond: 0, Ex: (1 << (58 - 32)) | (1 << (61 - 32)), Ex2: 0, Ex3: 0, Ex4: 0);

        health.Think(Playing(both));

        Image(health, "PlayerStatus_MedicUberBulletResistImage").Visible.ShouldBeTrue("the vaccinator's own row comes first in m_vecBuffInfo");
        Image(health, "PlayerStatus_MedicSmallBulletResistImage").Visible.ShouldBeFalse("its class is already drawn");
    }

    [Test]
    public void Think_RuneKingOnBlueTeam_UsesTheSameImageAsRed()
    {
        TfHudPlayerHealth health = Panel();

        // TF_COND_RUNE_KING = 109, in Ex3 (96..127): bit 109 - 96 = 13.
        PlayerConditions runeKing = new(Cond: 0, Ex: 0, Ex2: 0, Ex3: 1 << 13, Ex4: 0);

        health.Think(Playing(runeKing, team: 3));

        VguiImagePanel image = Image(health, "PlayerStatus_RuneKing");
        image.Visible.ShouldBeTrue();
    }

    [Test]
    public void Think_MarkedForDeathSilentAndPasstimePenalty_ShareOnePanel()
    {
        TfHudPlayerHealth health = Panel();

        // TF_COND_PASSTIME_PENALTY_DEBUFF = 119, in Ex3 (96..127): bit 119 - 96 = 23.
        PlayerConditions passtime = new(Cond: 0, Ex: 0, Ex2: 0, Ex3: 1 << 23, Ex4: 0);

        health.Think(Playing(passtime));

        Image(health, "PlayerStatusMarkedForDeathSilentImage").Visible.ShouldBeTrue(
            "TF_COND_PASSTIME_PENALTY_DEBUFF targets the silent marked-for-death panel — Valve's own row, tf_hud_playerstatus.cpp:1001");
    }
}
