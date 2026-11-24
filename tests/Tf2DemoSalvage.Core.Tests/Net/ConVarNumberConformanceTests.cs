using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>A ConVar's number, as `ConVar::InternalSetValue` makes it from the string it is set to (B823).</summary>
/// <remarks>
/// <code>
/// // src/tier1/convar.cpp:791-802
/// fNewValue = ( float )atof( value );
/// ...
/// m_fValue = fNewValue;
/// m_nValue = ( int )( fNewValue );
/// </code>
/// `GetFloat` and `GetInt` read those two fields, so a console line `name 1 2`, whose value is `ArgS` `1 2`, is 1.
/// </remarks>
public sealed class ConVarNumberConformanceTests
{
    [TestCase("1 2", 1f)]
    [TestCase("  3.5abc", 3.5f)]
    [TestCase("abc", 0f)]
    [TestCase("", 0f)]
    [TestCase("-0.25\"", -0.25f)]
    public void GetFloat_AValue_IsAtofNarrowedToFloat(string value, float expected) =>
        ConVarNumber.GetFloat(value).ShouldBe(expected);

    /// <remarks>`( int )( fNewValue )` is MSVC's `cvttss2si`: toward zero, and `0x80000000` for infinity or NaN.</remarks>
    [TestCase("2.9", 2)]
    [TestCase("-2.9 x", -2)]
    [TestCase("inf", int.MinValue)]
    [TestCase("nan", int.MinValue)]
    [TestCase("1e10", int.MinValue)]
    [TestCase("-1e10", int.MinValue)]
    [TestCase("-inf", int.MinValue)]
    [TestCase("-nan", int.MinValue)]
    [TestCase("0x10", 16)]
    [TestCase("2147483647", int.MinValue)]
    [TestCase("2147483520", 2147483520)]
    public void GetInt_AValue_IsTheFloatTruncatedAsMsvcDoes(string value, int expected) =>
        ConVarNumber.GetInt(value).ShouldBe(expected);

    /// <remarks>`GetBool() { return !!GetInt(); }` (convar.h:520): 0.5 false, nan true (B823 review 3, Sonnet).</remarks>
    [TestCase("0.5", false)]
    [TestCase("nan", true)]
    [TestCase("1 x", true)]
    [TestCase("abc", false)]
    public void GetBool_AValue_IsGetIntNotZero(string value, bool expected) =>
        ConVarNumber.GetBool(value).ShouldBe(expected);

    /// <remarks>A whole number to `atof`, trailing C white space aside; `0.1f` and a colour are not (B823 review 3, Fable).</remarks>
    [TestCase("16", true)]
    [TestCase("0x10", true)]
    [TestCase("inf", true)]
    [TestCase("1 ", true)]
    [TestCase("0.1f", false)]
    [TestCase("255 255 255", false)]
    [TestCase("", false)]
    public void IsWholeNumber_AText_IsWhetherAtofTakesAllOfIt(string value, bool expected) =>
        ConVarNumber.IsWholeNumber(value).ShouldBe(expected);
}
