using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>FeetYaw</c>'s four constants, against the source they were copied out of.
/// </summary>
/// <remarks>
/// **Found by auditing what is implemented against what has a parity test.** All four were
/// hand-transcribed from <c>multiplayer_animstate.cpp</c> for B61, and nothing checked any of them.
/// They decide how a player's body is oriented on screen, and a wrong one is invisible in the way
/// that matters: the body still turns, just not when or how fast the engine turns it.
///
/// **None of them can be looked up by name, which is why this file exists separately from
/// <c>EngineConstantConformanceTests</c>.** That suite reads named constants out of <c>const.h</c>;
/// these are inline literals, three of them sitting next to the commented-out named constant they
/// replaced:
///
/// <code>
/// if ( fabs( flYawDelta ) > 45.0f/*m_AnimConfig.m_flMaxBodyYawDegrees*/ )
/// ConvergeYawAngles( m_flGoalFeetYaw, /*DOD_BODYYAW_RATE*/720.0f, ... );
/// #define FADE_TURN_DEGREES 60.0f
/// bool bMoving = ( vecVelocity.Length() &gt; 1.0f ) ? true : false;
/// </code>
///
/// So each is matched in the CONTEXT that gives it its meaning rather than by searching the file
/// for the number. A bare search for "45.0f" would pass against the unrelated 45 at line 1459,
/// which is a different quantity entirely — and matching a literal anywhere in a 2,000-line file is
/// barely a test at all.
/// </remarks>
public sealed class FeetYawConformanceTests
{
    private const string AnimState = "src/game/shared/Multiplayer/multiplayer_animstate.cpp";

    [Test]
    public void FeetYaw_MaxBodyYaw_IsTheLiteralBesideTheDisabledConfigValue()
    {
        // Matched against the commented-out m_flMaxBodyYawDegrees it stands in for, which is what
        // identifies this 45 as the body-yaw limit rather than any other 45 in the file.
        Source().ShouldMatch(
            @"fabs\(\s*flYawDelta\s*\)\s*>\s*" +
            Regex.Escape(Literal(FeetYaw.MaxBodyYaw)) +
            @"\s*/\*\s*m_AnimConfig\.m_flMaxBodyYawDegrees\s*\*/");
    }

    [Test]
    public void FeetYaw_TheFeetStep_ByThatSameLimit()
    {
        // The limit appears twice - once as the test, once as the step taken when it is exceeded -
        // and FeetYaw.Advance uses one constant for both. If Valve ever made them differ, this is
        // where that shows up.
        Source().ShouldMatch(
            @"m_flGoalFeetYaw\s*\+=\s*\(\s*" +
            Regex.Escape(Literal(FeetYaw.MaxBodyYaw)) +
            @"\s*/\*\s*m_AnimConfig\.m_flMaxBodyYawDegrees\s*\*/");
    }

    [Test]
    public void FeetYaw_TheYawRate_IsTheLiteralPassedToConvergeYawAngles()
    {
        Source().ShouldMatch(
            @"ConvergeYawAngles\(\s*m_flGoalFeetYaw,\s*/\*\s*DOD_BODYYAW_RATE\s*\*/\s*" +
            Regex.Escape(Literal(FeetYaw.YawRate)));
    }

    [Test]
    public void FeetYaw_TheFadeThreshold_IsTheMacroConvergeDefines()
    {
        // FADE_TURN_DEGREES is #defined and #undef'd inside ConvergeYawAngles, so it is a named
        // constant that exists for eight lines and cannot be read from any header.
        Source().ShouldMatch(
            @"#define\s+FADE_TURN_DEGREES\s+" + Regex.Escape(Literal(FeetYaw.FadeTurnDegrees)));
    }

    [Test]
    public void FeetYaw_TheMovingThreshold_IsTestedOnTheThreeDimensionalLength()
    {
        // **The dimensionality is the load-bearing half, not the number.** `Length()` is 3D, so a
        // player rising in a lift with no horizontal motion counts as moving and their feet follow
        // their eyes. Matching `Length2D` here would be a different behaviour with the same 1.0f.
        Source().ShouldMatch(
            @"vecVelocity\.Length\(\)\s*>\s*" + Regex.Escape(Literal(FeetYaw.MovingSpeed)));
    }

    /// <summary>The constant as the SDK spells it: <c>45f</c> becomes <c>45.0f</c>.</summary>
    private static string Literal(float value) =>
        value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "f";

    private static string Source()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore("the Source SDK is not available");
        }

        return SourceSdk.Text(AnimState).ShouldNotBeNull();
    }
}
