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

    /// <remarks>
    /// **One number to `atof`, then at most a C float suffix and C white space** (B823 reviews 4 and 5, Fable): `0.1f` is
    /// the number 0.1, as `EngineConVar.Number` reads it; `255 255 255`, `2fort`, `infantry` and `nano` are words that
    /// start with a number and stay strings; `1 x` too, since `x` is no suffix — a value with a word after it is text.
    /// **Wider than C, on purpose** (review 6, Fable, Sonnet): `inff`, `nanf`, `infl` and `1f` are no C literals, which
    /// takes a suffix only after a `.` or an exponent, but they are accepted, pinned here, since the question is whether a
    /// declared default is a number and none of those is one a default spells; one suffix only (`1ff` is a string).
    /// </remarks>
    [TestCase("16", true)]
    [TestCase("0x10", true)]
    [TestCase("inf", true)]
    [TestCase("0.1f", true)]
    [TestCase("1L", true)]
    [TestCase("1 ", true)]
    [TestCase("1 x", false)]
    [TestCase("1 2", false)]
    [TestCase("255 255 255", false)]
    [TestCase("", false)]
    [TestCase("abc", false)]
    [TestCase("2fort", false)]
    [TestCase("infantry", false)]
    [TestCase("nano", false)]
    [TestCase("0.1 f", false)]
    [TestCase("2F", true)]
    [TestCase("2.5l", true)]
    [TestCase("1\t", true)]
    [TestCase("1f\n", true)]
    [TestCase("1\tx", false)]
    [TestCase("inff", true)]
    [TestCase("nanf", true)]
    [TestCase("infl", true)]
    [TestCase("1f", true)]
    [TestCase("1ff", false)]
    public void HoldsOneNumber_ANumberThenASuffixThenWhiteSpace_IsOneNumber(string value, bool expected) =>
        ConVarNumber.HoldsOneNumber(value).ShouldBe(expected);
}
