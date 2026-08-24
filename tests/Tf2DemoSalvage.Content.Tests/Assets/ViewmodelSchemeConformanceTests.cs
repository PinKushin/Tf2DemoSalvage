using System;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Whether a first-person weapon is one model or two, which is a per-item decision.
/// </summary>
/// <remarks>
/// **TF2 has two viewmodel schemes and they are exclusive.** `CTFWeaponBase::GetViewModel`
/// (<c>tf_weaponbase.cpp:651</c>) is the whole rule:
///
/// <code>
/// const CEconItemView *pItem = GetAttributeContainer()->GetItem();
/// if ( pPlayer &amp;&amp; pItem->IsValid() &amp;&amp; pItem->GetStaticData()->ShouldAttachToHands() )
/// {
///     const char *pszHandModel = pPlayer->GetPlayerClass()->GetHandModelName( iHandModelIndex );
///     return pszHandModel;
/// }
///
/// return GetTFWpnData().szViewModel;
/// </code>
///
/// So when the item attaches to hands, the viewmodel IS the player's hands and the weapon is a
/// separate <c>C_ViewmodelAttachmentModel</c> parented to them — two models. When it does not, the
/// viewmodel is the weapon's own <c>v_</c> model, which has the hands built into it — one model,
/// and attaching a weapon to it draws the weapon twice.
///
/// `ShouldAttachToHands()` is `attach_to_hands` from <c>items_game.txt</c>
/// (<c>econ_item_schema.cpp:2378</c>), and it **defaults to 0**.
///
/// **The hands come from shipped class data, not from the item.**
/// <c>CTFPlayerClassShared::GetHandModelName</c> (<c>tf_playerclass_shared.cpp:161</c>) returns
/// <c>GetPlayerClassData( m_iClass )->m_szHandModelName</c>, which
/// <c>tf_classdata.cpp:149</c> fills from the <c>model_hands</c> key of
/// <c>scripts/playerclasses/&lt;class&gt;.txt</c>.
///
/// **Why this cannot be decided from today's schema, which is the trap.** A demo is a recording of
/// the game as it was. The stickybomb launcher attaches to hands NOW and did not in 2011, so asking
/// the installed `items_game.txt` about a 2011 demo returns the wrong answer — and returns it
/// confidently. The demo itself says which scheme was in play, because it networks the viewmodel's
/// model: if that model is the class's hands, the weapon is a separate attachment; if it is the
/// weapon's own, it is not. See <c>docs/memory/the-demo-dates-its-own-fields.md</c>.
/// </remarks>
public sealed class ViewmodelSchemeConformanceTests
{
    private const string Game = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

    private static PlayerClassModels? Classes()
    {
        if (!Directory.Exists(Game))
        {
            Assert.Ignore("Team Fortress 2 is not installed");
            return null;
        }

        GameArchives archives = GameArchives.Open(Game);

        return PlayerClassModels.Read(archives.Read);
    }

    /// <summary>Every playing class names the hands its viewmodel uses.</summary>
    /// <remarks>
    /// Nine classes, because <c>model_hands</c> is what the c_ scheme substitutes for the weapon's
    /// own viewmodel. A class missing it cannot take that path at all, so the reader answering null
    /// would silently force every one of that class's weapons down the single-model branch.
    /// </remarks>
    [Test]
    public void Hands_EveryPlayingClass_NamesItsArmsModel()
    {
        if (Classes() is not { } classes)
        {
            return;
        }

        for (int played = PlayerClassModels.FirstClass;
             played <= PlayerClassModels.LastPlayingClass;
             played++)
        {
            string? hands = classes.Hands(played);

            TestContext.Out.WriteLine($"  class {played}: hands '{hands ?? "NONE"}'");

            hands.ShouldNotBeNull($"class {played} declares no model_hands");

            // **Predicted exactly rather than checked for non-emptiness.** Valve's hand models all
            // live in the c_models folder and are named for their class, and a reader that returned
            // the wrong key — "model", say — would still return a non-empty path, since every class
            // script has one. That is the reading this has to be able to fail on, and
            // ShouldNotBeNullOrEmpty could not.
            hands.ShouldStartWith("models/weapons/c_models/");
            hands.ShouldEndWith("_arms.mdl");
        }
    }

    /// <summary>The demoman's hands, named exactly, as the worked example.</summary>
    /// <remarks>
    /// One pinned value so the suite carries a fact and not only a shape. The demoman because he is
    /// the class in the report that started this — a 2011 recording drawing
    /// <c>v_stickybomb_launcher_demo</c> AND <c>c_stickybomb_launcher</c> at the same point.
    /// </remarks>
    [Test]
    public void Hands_TheDemoman_IsCDemoArms()
    {
        if (Classes() is not { } classes)
        {
            return;
        }

        classes.Hands(PlayerClassModels.Demoman)
            .ShouldBe("models/weapons/c_models/c_demo_arms.mdl");
    }
}
