using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>What `spy_invis` and `invis` bind per entity: `MomentScene.CloakOf` and `CloakBind.For`.</summary>
public sealed class CloakBindTests
{
    private const int Spy = 8;
    private const int Red = 2;
    private const int Blue = 3;
    private const int Recorder = 1;
    private const int Cloaker = 5;

    [Test]
    public void CloakOf_AFullyCloakedEnemysBody_IsUndrawn()
    {
        // GetEffectiveInvisibilityLevel 1 for an enemy → C_TFPlayer::DrawModel returns 0 (c_tf_player.cpp:6938).
        CloakBind bind = MomentScene.CloakOf(Body(), [Viewer(), Cloaked(1f, enemy: true)], Recorder, 10f, Recorder);

        bind.ShouldBe(new CloakBind(1f, 1f, CloakPass.TeamTint(Blue), Undrawn: true));
    }

    [Test]
    public void CloakOf_AFullyCloakedTeammatesBody_IsCappedAndDrawn()
    {
        CloakBind bind = MomentScene.CloakOf(Body(), [Viewer(), Cloaked(1f, enemy: false)], Recorder, 10f, Recorder);

        bind.ShouldBe(new CloakBind(0.95f, 0.95f, CloakPass.TeamTint(Blue), Undrawn: false));
    }

    [Test]
    public void CloakOf_AWornHat_TakesItsWearersLevelAndNoTint()
    {
        SceneProp hat = new(40, "models/hat.mdl", SceneModelKind.Studio, new ScenePose(), AttachedTo: Cloaker);

        MomentScene.CloakOf(hat, [Viewer(), Cloaked(1f, enemy: true)], Recorder, 10f, Recorder)
            .ShouldBe(new CloakBind(1f, 1f));
    }

    [Test]
    public void CloakOf_TheRecordersOwnViewmodel_TakesTheViewmodelRemap()
    {
        // Half cloaked: effective 0.5 (capped only past 0.95), invis 0.22 + 0.28 × 0.5 for the local player.
        ScenePlayer recorder = Cloaked(0.5f, enemy: false) with { EntityIndex = Recorder };
        SceneProp arms = new(4096, "models/arms.mdl", SceneModelKind.Studio, new ScenePose(), FirstPerson: true);

        CloakBind bind = MomentScene.CloakOf(arms, [recorder], Recorder, 10f, Recorder);

        bind.SpyInvis.ShouldBe(0.5f);
        bind.Invis.ShouldBe(0.36f, 1e-6f);
        bind.PlayerTint.ShouldBeNull();
    }

    [Test]
    public void CloakOf_NobodysProp_IsNotCloaked()
    {
        SceneProp crate = new(50, "models/crate.mdl", SceneModelKind.Studio, new ScenePose());

        MomentScene.CloakOf(crate, [Viewer(), Cloaked(1f, enemy: true)], Recorder, 10f, Recorder).ShouldBe(default);
    }

    [Test]
    public void CloakOf_AMovingMotionCloakSpyOnAnEmptyMeter_FadesBySpeed()
    {
        // InvisibilityThink under m_bMotionCloak with m_flCloakMeter 0 (tf_player_shared.cpp:8017-8024):
        // RemapVal( 150², 0, 300², 1, 0.5 ) = 0.875 — for an enemy who is NOT the recorder.
        ScenePlayer spy = Cloaked(1f, enemy: true) with { MaxSpeed = 300f, CloakMeter = 0f, Velocity = (150f, 0f, 0f) };

        MomentScene.CloakOf(Body(), [Viewer(), spy], Recorder, 10f, Recorder, motionCloak: _ => true).SpyInvis.ShouldBe(0.875f);
        MomentScene.CloakOf(Body(), [Viewer(), spy], Recorder, 10f, Recorder, motionCloak: _ => false)
            .ShouldBe(new CloakBind(1f, 1f, CloakPass.TeamTint(Blue), Undrawn: true), "the control: an ordinary watch");
    }

    [Test]
    public void CloakOf_TheRecordersDryMotionCloak_PinsHisWeaponsAtPointThree()
    {
        // CInvisProxy, local player: HasMotionCloak() && GetSpyCloakMeter() <= 0 → 0.3 (tf_viewmodel.cpp:594-598).
        ScenePlayer recorder = Cloaked(1f, enemy: false) with { EntityIndex = Recorder, MaxSpeed = 300f, CloakMeter = 0f };
        SceneProp arms = new(4096, "models/arms.mdl", SceneModelKind.Studio, new ScenePose(), FirstPerson: true);

        MomentScene.CloakOf(arms, [recorder], Recorder, 10f, Recorder, motionCloak: _ => true).Invis.ShouldBe(0.3f);
        MomentScene.CloakOf(arms, [recorder with { CloakMeter = 40f }], Recorder, 10f, Recorder, motionCloak: _ => true)
            .Invis.ShouldBe(0.5f, "a meter with charge is the ordinary remap of 1");
    }

    [Test]
    public void CloakOf_AnEnemyInHightowersStealthSpell_IsCappedAndDrawn()
    {
        // TF_COND_STEALTHED_USER_BUFF (64) on Hightower (scenario 4): bLimitedInvis (c_tf_player.cpp:6849-6850).
        ScenePlayer buffed = Cloaked(1f, enemy: true) with { PlayerClass = 1, Conditions = new PlayerConditions(0, 0, 1, 0, 0) };

        MomentScene.CloakOf(Body(), [Viewer(), buffed], Recorder, 10f, Recorder, halloweenScenario: 4)
            .ShouldBe(new CloakBind(0.95f, 0.95f, CloakPass.TeamTint(Blue), Undrawn: false));
        MomentScene.CloakOf(Body(), [Viewer(), buffed], Recorder, 10f, Recorder, halloweenScenario: 0).Undrawn
            .ShouldBeTrue("the control: off Hightower an enemy reaches 1");
    }

    [Test]
    public void CloakOf_ACloakedCorpse_TakesItsOwnPercentAndNoTint()
    {
        // spy_invis: a C_TFRagdoll that IsCloaked writes its own GetPercentInvisible and no tint (c_tf_player.cpp:1720-1725);
        // invis leaves it (tf_viewmodel.cpp:565-572). No teammate cap.
        SceneProp corpse = new(2048, "models/player/scout.mdl", SceneModelKind.Studio, new ScenePose(), CorpseInvisibility: 0.4f);

        MomentScene.CloakOf(corpse, [Viewer()], Recorder, 10f, Recorder).ShouldBe(new CloakBind(0.4f, 0.4f));
    }

    [Test]
    public void For_SpyInvisThenInvis_TheLastWritesTheFactorAndSpyInvisTheTint()
    {
        // spy_red.vmt runs "spy_invis" then "invis" — last wins; only spy_invis writes $cloakColorTint.
        CloakBind bind = new(0.95f, 0.36f, (1f, 0.5f, 0.4f));
        CloakPass pass = new((1f, 1f, 1f), 0.1f, 0f);

        bind.For(pass, [new MaterialProxy("spy_invis"), new MaterialProxy("invis")]).ShouldBe((0.36f, (1f, 0.5f, 0.4f)));
        bind.For(pass, [new MaterialProxy("invis"), new MaterialProxy("spy_invis")]).ShouldBe((0.95f, (1f, 0.5f, 0.4f)));
        bind.For(pass, [new MaterialProxy("invis")]).ShouldBe((0.36f, (1f, 1f, 1f)));
        bind.For(pass, []).ShouldBe((0f, (1f, 1f, 1f)));
    }

    private static SceneProp Body() => new(Cloaker, "models/player/spy.mdl", SceneModelKind.Studio, new ScenePose());

    private static ScenePlayer Viewer() => new(Recorder, 0f, 0f, 0f, Team: Red, Health: 125, PlayerClass: 1);

    /// <summary>A spy fully stealthed, or half way in with 0.5 s to go.</summary>
    private static ScenePlayer Cloaked(float percent, bool enemy) =>
        new(Cloaker, 0f, 0f, 0f, Team: Blue, Health: 125, PlayerClass: Spy, Conditions: new PlayerConditions(1 << 4, 0, 0, 0, 0), IsEnemy: enemy)
        {
            // Stealthed with the change complete at 10 + ( 1 − percent ): 1 − ( complete − 10 ).
            InvisChangeCompleteTime = 10f + (1f - percent),
        };
}
