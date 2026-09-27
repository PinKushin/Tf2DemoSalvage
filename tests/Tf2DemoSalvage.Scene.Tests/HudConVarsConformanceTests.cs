using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `ConVarRef` (src/public/tier1/convar.h:506-590, src/tier1/convar.cpp:1243-1297) as the HUD reads it: the value in
/// force, Valve's declared default when nothing sent one, and `s_EmptyConVar`'s "0" for a name nobody registers.
/// </summary>
public sealed class HudConVarsConformanceTests
{
    [Test]
    public void GetString_AbsentVar_IsTheSdkDefault() =>
        // mp_tournament_redteamname, "RED" (tf_gamerules.cpp:782).
        default(HudConVars).GetString("mp_tournament_redteamname").ShouldBe("RED");

    [Test]
    public void GetBool_AbsentVarDefaultingToOne_IsTrue() =>
        // training_can_pickup_sentry, "1" (tf_gamerules.cpp:699) — a default of "0" would read false.
        default(HudConVars).GetBool("training_can_pickup_sentry").ShouldBeTrue();

    [Test]
    public void GetString_ReplicatedValue_WinsOverTheDefault() =>
        new HudConVars(name => name == "mp_tournament_redteamname" ? "Cats" : null)
            .GetString("mp_tournament_redteamname").ShouldBe("Cats");

    [Test]
    public void GetString_UnregisteredName_IsTheEmptyConVarsZero() =>
        // `CEmptyConVar() : ConVar( "", "0" )` (convar.cpp:1246).
        default(HudConVars).GetString("no_such_var_anywhere").ShouldBe("0");

    [Test]
    public void GetInt_FractionalValue_TruncatesTheFloat() =>
        // `m_nValue = ( int )( fNewValue )` (convar.cpp:802).
        new HudConVars(_ => "3.9").GetInt("mp_winlimit").ShouldBe(3);

    [Test]
    public void GetFloat_NonNumericValue_IsZero() =>
        // `( float )atof( value )` (convar.cpp:792) — atof of text with no number is 0.
        new HudConVars(_ => "abc").GetFloat("mp_winlimit").ShouldBe(0f);

    [Test]
    public void GetFloat_LeadingNumberThenText_IsTheLeadingNumber() =>
        new HudConVars(_ => " 3.5abc").GetFloat("mp_winlimit").ShouldBe(3.5f, "atof reads the leading number (convar.cpp:792)");

    [Test]
    public void GetBool_HalfValue_IsFalse() =>
        // `GetBool() { return !!GetInt(); }` (convar.h:520): 0.5 truncates to 0.
        new HudConVars(_ => "0.5").GetBool("mp_tournament").ShouldBeFalse();
}
