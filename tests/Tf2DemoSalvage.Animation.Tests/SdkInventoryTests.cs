using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The SDK extraction's own tests, because it answered wrongly the first time it was asked.
/// </summary>
/// <remarks>
/// **`SdkInventory.CallsIn` reported three calls that do not exist**, all from one commented-out
/// line in <c>StandardBlendingRules</c>. The denominator it feeds is supposed to say what the
/// engine does; reporting deleted code asks somebody to implement something Valve removed, and that
/// work would look like parity while being its opposite.
///
/// So the instrument gets tests before anything is transcribed from it — this project's standing
/// observation that instrument bugs outnumber decoder bugs, applied to the instrument that measures
/// conformance.
///
/// **These live here rather than beside `SdkReference` because it has no suite of its own**, and
/// this is the first `net10.0` project to need it (B184). They should move if the extraction grows
/// a home.
/// </remarks>
public sealed class SdkInventoryTests
{
    [Test]
    public void Live_ALineComment_HidesTheCallInside()
    {
        SdkInventory.CallsIn("Real( 1 ); // Deleted( 2 );")
            .ShouldBe(["Real"]);
    }

    [Test]
    public void Live_ABlockComment_HidesTheCallInside()
    {
        SdkInventory.CallsIn("Real( 1 ); /* Deleted( 2 ); */ Later( 3 );")
            .ShouldBe(["Real", "Later"]);
    }

    [Test]
    public void Live_AStringLiteral_HidesWhatLooksLikeACall()
    {
        SdkInventory.CallsIn("""Print( "NotACall( 1 )" );""")
            .ShouldBe(["Print"]);
    }

    [Test]
    public void Live_AnApostropheInProse_DoesNotSwallowLaterCode()
    {
        // **The reason comments are tested before quotes.** An apostrophe is a character literal in
        // code and a letter in prose, so scanning for quotes first opens a literal at "don't" that
        // runs to the next apostrophe — here past a real call, which then vanishes from the
        // denominator. That is a gap the tool reports as coverage.
        SdkInventory.CallsIn(
            """
            // we don't call this any more
            Real( 1 );
            // and it isn't coming back
            """)
            .ShouldBe(["Real"]);
    }

    [Test]
    public void Live_AnEscapedQuoteInsideAString_DoesNotEndItEarly()
    {
        SdkInventory.CallsIn("""Print( "he said \" NotACall( 1 )" ); Real( 2 );""")
            .ShouldBe(["Print", "Real"]);
    }

    [Test]
    public void CallsIn_OrdinaryCode_ReportsEveryCallInOrder()
    {
        // **The control, and without it every test above passes on a method that returns nothing.**
        // Each one asserts that something is ABSENT from the result; a Live that blanked the whole
        // input would satisfy all five and be catastrophically wrong. This is the case that fails
        // if the blanking is too eager.
        SdkInventory.CallsIn("First( a ); obj.Second( b ); if ( c ) Third( d );")
            .ShouldBe(["First", "Second", "Third"]);
    }

    [Test]
    public void CallsIn_AControlFlowKeyword_IsNotReportedAsACall()
    {
        SdkInventory.CallsIn("if ( x ) { for ( ; ; ) { while ( y ) Real( z ); } }")
            .ShouldBe(["Real"]);
    }

    [Test]
    public void CallsIn_ACallRepeated_IsReportedOnce()
    {
        SdkInventory.CallsIn("Twice( 1 ); Other( 2 ); Twice( 3 );")
            .ShouldBe(["Twice", "Other"]);
    }

    [Test]
    public void FunctionBody_ASignatureThatIsAPrefixOfAnEarlierFunction_FindsTheRealOne()
    {
        // **A plain IndexOf got this wrong and said nothing.** Asking the SDK for
        // `bool C_BaseAnimating::SetupBones` matched `SetupBones_AttachmentHelper`, declared 700
        // lines earlier — so the denominator for the most important function in the bone pipeline
        // came back as eleven attachment calls, every one of which looks like a plausible stage.
        //
        // The shape is the one this project keeps meeting: a wrong answer indistinguishable from a
        // right one. Reproduced here in eight lines rather than left to the SDK to demonstrate.
        string source =
            """
            void Thing::DoWork_Helper( int a )
            {
                Helper( a );
            }

            void Thing::DoWork( int a )
            {
                Real( a );
            }
            """;

        SdkInventory.CallsIn(SdkInventory.BodyIn(source, "void Thing::DoWork"))
            .ShouldBe(["Real"]);
    }

    [Test]
    public void BodyIn_ASignatureNothingDefines_ReturnsNothing()
    {
        // The control for the search above: a matcher loosened to fix the prefix bug could start
        // matching anything, and every other test here would still pass.
        SdkInventory.BodyIn("void Thing::DoWork() { Real(); }", "void Thing::Absent")
            .ShouldBeEmpty();
    }

    [Test]
    public void BodyIn_ANestedBlock_StopsAtTheFunctionsOwnClosingBrace()
    {
        // Brace matching rather than "find the next }", which would end the body at the if.
        SdkInventory.CallsIn(
            SdkInventory.BodyIn(
                "void Thing::DoWork() { if ( x ) { Inner(); } Outer(); } void Thing::After() { Later(); }",
                "void Thing::DoWork"))
            .ShouldBe(["Inner", "Outer"]);
    }
}
