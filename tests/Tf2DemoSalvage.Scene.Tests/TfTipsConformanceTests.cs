using System.Collections.Generic;

using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `CTFTips` (game/client/tf/tf_tips.cpp), B733: the tip a point-of-view recorder's spectator bar shows.
/// </summary>
/// <remarks>
/// Every expected value is read from the published client: `Init` (:31) counts each class's tips from `Tip_%d_Count`;
/// `GetNextClassTip` (:143) walks ONE index, `m_iCurrentClassTip`, for every class and for arena, wrapping it to the class
/// asked; with no class it is `GetRandomTip` (:99) — `RandomInt( 1, 100 )` (:112) against an abuse-tip chance of 33 under
/// twenty hours played and 3 from twenty (:105-110), then a second `RandomInt`: a class for the abuse tip (:114), or the
/// tip over all classes (:119).
/// The strings here are hand-written so each answer can be read off them.
/// </remarks>
public sealed class TfTipsConformanceTests
{
    private const int Scout = 1;
    private const int Soldier = 3;

    [Test]
    public void NextClassTip_ForOneClass_WalksItsTipsInOrderAndWraps()
    {
        // `m_iCurrentClassTip` starts at 0 and the tip is `#Tip_%d_%d` of index + 1 (:167-168), wrapped by `%=` (:161-164).
        TfTips tips = new(new UniformRandomStream());

        Next(tips, Soldier).ShouldBe("soldier one");
        Next(tips, Soldier).ShouldBe("soldier two");
        Next(tips, Soldier).ShouldBe("soldier one", "the third wraps to the first of two");
    }

    [Test]
    public void NextClassTip_AfterAClassChange_KeepsTheOneSharedIndex()
    {
        // One `m_iCurrentClassTip` for every class (tf_tips.h): after a soldier tip the scout gets his SECOND.
        TfTips tips = new(new UniformRandomStream());

        Next(tips, Soldier).ShouldBe("soldier one");
        Next(tips, Scout).ShouldBe("scout two");
    }

    [Test]
    public void NextClassTip_InArena_IsTheArenaTipWithItsKeysBound()
    {
        // `IsInArenaMode()` takes a chosen class to `GetArenaTip` (:151-154), whose `%binding%`s are replaced (:195).
        TfTips tips = new(new UniformRandomStream());

        Next(tips, Soldier, arena: true).ShouldBe("arena one, press SPACE");
        Next(tips, Scout, arena: true).ShouldBe("arena one, press SPACE", "one arena tip wraps to itself");
    }

    [Test]
    public void NextClassTip_ForAClassTip_BindsItsKeys()
    {
        // `GetTip` (:206): `UTIL_ReplaceKeyBindings` over `#Tip_%d_%d` (:211-213).
        Dictionary<string, string> strings = Strings();
        strings["Tip_3_1"] = "rocket jump with %jump% and %+attack%";
        TfTips tips = new(new UniformRandomStream());

        tips.NextClassTip(Soldier, false, strings.GetValueOrDefault, Keys().GetValueOrDefault).ShouldBe("rocket jump with SPACE and MOUSE1");
    }

    [Test]
    public void NextClassTip_ForAnUnchosenClassWithFewHoursPlayed_DrawsTheAbuseTipOnADrawOf33OrLess()
    {
        // `TF_CLASS_UNDEFINED` (0) is `GetRandomTip` (:148-149). Under twenty hours the chance is 33 (:107-110). The default
        // stream's first `RandomInt( 1, 100 )` is 17 — inside 33, so the abuse tip, with a second draw for its class.
        TfTips tips = new(new UniformRandomStream(), hoursPlayed: 0f);

        Next(tips, 0).ShouldBe("report with F7");
    }

    [Test]
    public void NextClassTip_ForAnUnchosenClassWithManyHoursPlayed_DrawsATipOverEveryClass()
    {
        // The control: twenty hours and over is a chance of 3 (:105), so the same first draw, 17, is not the abuse tip, and
        // the next — `RandomInt( 0, 4 - 1 )` (:119) over two scout and two soldier tips — is 3: past the scout's two
        // (3 - 2 = 1), none for class 2, so the soldier's index 1, his SECOND (:121-133). The two draws are the default-seeded stream's, which `UniformRandomStreamConformanceTests` pins to
        // vstdlib's own bits.
        TfTips tips = new(new UniformRandomStream(), hoursPlayed: 20f);

        Next(tips, 0).ShouldBe("soldier two");
    }

    [Test]
    public void NextClassTip_AfterAnAbuseTip_HasDrawnItsThrownAwayClass()
    {
        // The abuse branch draws `iClassUsed = RandomInt( 1, 9 )` (:114) that this caller never reads — but it moves the
        // stream. The default stream reads 17, 7, 50, 3: the abuse tip (17 <= 33, its class 7 discarded), then 50 is not,
        // and `RandomInt( 0, 3 )` is 3, the soldier's second. Without the discarded draw the same stream reads 100 then 1:
        // the scout's second.
        TfTips tips = new(new UniformRandomStream(), hoursPlayed: 0f);

        Next(tips, 0).ShouldBe("report with F7");
        Next(tips, 0).ShouldBe("soldier two");
    }

    [Test]
    public void NextClassTip_ForAnUnchosenClassWithNoTipsAtAll_IsEmptyAndDrawsOnlyTheChance()
    {
        // No `Tip_%d_Count`: the walk is `RandomInt( 0, -1 )`, which answers 0 without a draw, and finds no class. Only
        // the chance draw, 17, is taken — the control stream's second draw is the next one this stream gives.
        UniformRandomStream random = new();
        UniformRandomStream control = new();
        TfTips tips = new(random, hoursPlayed: 20f);

        tips.NextClassTip(0, false, new Dictionary<string, string>().GetValueOrDefault, null).ShouldBeEmpty();

        _ = control.RandomInt(1, 100);
        random.RandomInt(0, int.MaxValue - 1).ShouldBe(control.RandomInt(0, int.MaxValue - 1));
    }

    [Test]
    public void NextClassTip_ForAClassWithNoTips_IsEmptyAndLeavesTheIndex()
    {
        // `if ( 0 == iClassTipCount ) return L"";` (:158-159), before the index moves: the soldier's next is still his first.
        TfTips tips = new(new UniformRandomStream());

        Next(tips, 2).ShouldBeEmpty("the fixture gives the sniper no tips");
        Next(tips, Soldier).ShouldBe("soldier one");
    }

    [Test]
    public void NextClassTip_AnArenaWalk_AdvancesTheIndexTheClassesShare()
    {
        // `GetArenaTip` (:176) moves `m_iCurrentClassTip` (:197), the same member a class walk reads: after one arena tip
        // the soldier's next is his SECOND.
        Dictionary<string, string> strings = Strings();
        strings["Tip_arena_Count"] = "2";
        strings["Tip_arena_2"] = "arena two";
        TfTips tips = new(new UniformRandomStream());

        tips.NextClassTip(Soldier, true, strings.GetValueOrDefault, null).ShouldBe("arena one, press < NOT BOUND >");
        tips.NextClassTip(Soldier, false, strings.GetValueOrDefault, null).ShouldBe("soldier two");
        tips.NextClassTip(Soldier, true, strings.GetValueOrDefault, null).ShouldBe("arena one, press < NOT BOUND >", "2 wraps to 0 of 2");
    }

    [Test]
    public void NextClassTip_AClassAboveNine_IsARandomTip()
    {
        // `iClass > TF_LAST_NORMAL_CLASS` (:148) is `GetRandomTip`: with twenty hours the default stream's 17 is no abuse
        // tip and its next draw, 3, is the soldier's second — as for no class at all.
        TfTips tips = new(new UniformRandomStream(), hoursPlayed: 20f);

        Next(tips, 10).ShouldBe("soldier two");
    }

    [Test]
    public void NextClassTip_ATipTokenNotFound_IsEmptyAndStillAdvances()
    {
        // `GetTip` hands `Find`'s null to `UTIL_ReplaceKeyBindings`, which writes an empty string (cdll_util.cpp:872-875);
        // the index still moves (:168).
        Dictionary<string, string> strings = Strings();
        strings.Remove("Tip_3_1");
        TfTips tips = new(new UniformRandomStream());

        tips.NextClassTip(Soldier, false, strings.GetValueOrDefault, null).ShouldBeEmpty();
        tips.NextClassTip(Soldier, false, strings.GetValueOrDefault, null).ShouldBe("soldier two");
    }

    [Test]
    public void NextClassTip_BeforeTheStringsAreLoaded_CountsThemOnceTheyAre()
    {
        // `Init` runs as a game system after the localisation is loaded; a call with no table yet must not latch zero
        // counts for the rest of the session.
        TfTips tips = new(new UniformRandomStream());

        tips.NextClassTip(Soldier, false, null, null).ShouldBeEmpty();
        Next(tips, Soldier).ShouldBe("soldier one");
    }

    [Test]
    public void NextClassTip_ForAnUnchosenClass_LeavesTheClassIndexAlone()
    {
        // `GetRandomTip` reads `GetTip` directly and never moves `m_iCurrentClassTip`.
        TfTips tips = new(new UniformRandomStream(), hoursPlayed: 20f);

        Next(tips, 0);
        Next(tips, Soldier).ShouldBe("soldier one");
    }

    private static string Next(TfTips tips, int playerClass, bool arena = false) =>
        tips.NextClassTip(playerClass, arena, Strings().GetValueOrDefault, Keys().GetValueOrDefault);

    private static Dictionary<string, string> Keys() => new() { ["jump"] = "space", ["attack"] = "mouse1", ["abuse_report_queue"] = "f7" };

    /// <summary>Two scout and two soldier tips, one arena tip, and the abuse tip; every other class has none.</summary>
    private static Dictionary<string, string> Strings() => new()
    {
        ["Tip_1_Count"] = "2",
        ["Tip_1_1"] = "scout one",
        ["Tip_1_2"] = "scout two",
        ["Tip_3_Count"] = "2",
        ["Tip_3_1"] = "soldier one",
        ["Tip_3_2"] = "soldier two",
        ["Tip_arena_Count"] = "1",
        ["Tip_arena_1"] = "arena one, press %jump%",
        ["Tip_Abuse_Report"] = "report with %abuse_report_queue%",
    };
}
