using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The local player's screen overlay: one slot, `view->SetScreenOverlayMaterial`, written by the condition hooks
/// `CTFPlayerShared::OnConditionAdded` / `OnConditionRemoved` run (tf_player_shared.cpp:1571, :1892), in the order
/// `SyncConditions` walks the bits (:1504 — ascending, an add or a remove per bit, word by word).
/// </summary>
/// <remarks>
/// Each `OnAdd*` overwrites the slot; each `OnRemove*` clears it only when the slot holds ITS material ("only remove
/// the overlay if it is urine", :3631). So the overlay is history, not a function of the conditions now.
/// </remarks>
public sealed class ScreenOverlayConformanceTests
{
    private const int Red = 2;
    private const int Blue = 3;

    [Test]
    public void Step_UrineAdded_IsJarate()
    {
        // OnAddUrine (:3602): TF_SCREEN_OVERLAY_MATERIAL_URINE "effects/jarate_overlay" (:228).
        ScreenOverlay.Step(null, Conds(), Conds(24), Red).ShouldBe("effects/jarate_overlay");
    }

    [Test]
    public void Step_InvulnerableAdded_IsTheTeamsInvuln()
    {
        // OnAddInvulnerable (:3438-3461): RED → invuln_overlay_red, BLUE and default → invuln_overlay_blue.
        ScreenOverlay.Step(null, Conds(), Conds(5), Red).ShouldBe("effects/invuln_overlay_red");
        ScreenOverlay.Step(null, Conds(), Conds(52), Blue).ShouldBe("effects/invuln_overlay_blue");
        ScreenOverlay.Step(null, Conds(), Conds(57), 1).ShouldBe("effects/invuln_overlay_blue");
    }

    [Test]
    public void Step_UberResistAndMegaHealAdded_AreTheTeamsInvuln()
    {
        // AddUberScreenEffect (:4684) for 58-60, 67-69 and feign death 13; OnAddMegaHeal (:6385) for 28.
        foreach (int condition in new[] { 13, 28, 58, 59, 60, 67, 68, 69 })
        {
            ScreenOverlay.Step(null, Conds(), Conds(condition), Red).ShouldBe("effects/invuln_overlay_red");
            ScreenOverlay.Step(null, Conds(), Conds(condition), 0).ShouldBe("effects/invuln_overlay_blue");
        }
    }

    [Test]
    public void Step_BurningAdded_IsImCookin()
    {
        // OnAddBurning (:7294, set at :7311) sets TF_SCREEN_OVERLAY_MATERIAL_BURNING "effects/imcookin" (:223).
        ScreenOverlay.Step("effects/jarate_overlay", Conds(24), Conds(22, 24), Red).ShouldBe("effects/imcookin");
    }

    [Test]
    public void Step_TwoAddedInOneUpdate_TheHigherBitWins()
    {
        // Urine 24 then bleeding 25, ascending.
        ScreenOverlay.Step(null, Conds(), Conds(24, 25), Red).ShouldBe("effects/bleed_overlay");
    }

    [Test]
    public void Step_TheShownOneRemovedWhileAnotherHolds_IsNone()
    {
        // OnRemoveBleeding (:4141) clears its own, and nothing re-adds the urine still held.
        ScreenOverlay.Step("effects/bleed_overlay", Conds(24, 25), Conds(24), Red).ShouldBeNull();
    }

    [Test]
    public void Step_AnotherRemoved_KeepsTheShownOne()
    {
        ScreenOverlay.Step("effects/bleed_overlay", Conds(24, 25), Conds(25), Red).ShouldBe("effects/bleed_overlay");
    }

    [Test]
    public void Step_PlagueRemoved_ClearsTheBleed()
    {
        // OnRemovePlague (:5846) compares against TF_SCREEN_OVERLAY_MATERIAL_BLEED.
        ScreenOverlay.Step("effects/bleed_overlay", Conds(112), Conds(), Red).ShouldBeNull();
    }

    [Test]
    public void Step_SwimmingCurseRemoved_ClearsJarate()
    {
        // TF_SCREEN_OVERLAY_MATERIAL_SWIMMING_CURSE is also "effects/jarate_overlay" (:231), so its removal clears urine's.
        ScreenOverlay.Step("effects/jarate_overlay", Conds(24, 86), Conds(24), Red).ShouldBeNull();
    }

    [Test]
    public void Step_SpyCloaks_ShowsNoOverlay()
    {
        // OnAddStealthed (:6983) sets the stealth overlay only under InCond( TF_COND_STEALTHED_USER_BUFF ).
        ScreenOverlay.Step(null, Conds(), Conds(4), Red).ShouldBeNull();
        ScreenOverlay.Step(null, Conds(), Conds(64), Red).ShouldBe("effects/stealth_overlay");
        ScreenOverlay.Step(null, Conds(64), Conds(4, 64), Red).ShouldBe("effects/stealth_overlay");
    }

    [Test]
    public void Step_StealthRemovedWhileFading_KeepsIt()
    {
        // OnRemoveStealthed (:7076) clears only when !InCond( TF_COND_STEALTHED_USER_BUFF_FADING ).
        ScreenOverlay.Step("effects/stealth_overlay", Conds(64, 66), Conds(66), Red).ShouldBe("effects/stealth_overlay");
        ScreenOverlay.Step("effects/stealth_overlay", Conds(64), Conds(), Red).ShouldBeNull();
    }

    [Test]
    public void Step_MilkAdded_ChangesNothing()
    {
        // OnAddMadMilk's overlay is commented out (:3834-3838).
        ScreenOverlay.Step("effects/jarate_overlay", Conds(24), Conds(24, 27), Red).ShouldBe("effects/jarate_overlay");
    }

    [Test]
    public void Step_GasAndInterception_AreTheirOwn()
    {
        // OnAddCondGas (:7678) and OnAddPasstimeInterception (:5733).
        ScreenOverlay.Step(null, Conds(), Conds(123), Red).ShouldBe("effects/gas_overlay");
        ScreenOverlay.Step(null, Conds(), Conds(106), Red).ShouldBe("effects/dodge_overlay");
        ScreenOverlay.Step("effects/gas_overlay", Conds(123), Conds(), Red).ShouldBeNull();
    }

    [Test]
    public void Step_InvulnRemovedWhileTheOtherTeamsShows_ClearsIt()
    {
        // Every invuln remover accepts either team's material.
        ScreenOverlay.Step("effects/invuln_overlay_blue", Conds(5), Conds(), Red).ShouldBeNull();
    }

    private static PlayerConditions Conds(params int[] conditions)
    {
        int[] words = new int[5];

        foreach (int condition in conditions)
        {
            words[condition / 32] |= 1 << (condition % 32);
        }

        return new PlayerConditions(words[0], words[1], words[2], words[3], words[4]);
    }
}
