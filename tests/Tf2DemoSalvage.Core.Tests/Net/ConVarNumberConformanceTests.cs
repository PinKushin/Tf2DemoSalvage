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
    public void GetInt_AValue_IsTheFloatTruncatedAsMsvcDoes(string value, int expected) =>
        ConVarNumber.GetInt(value).ShouldBe(expected);
}
