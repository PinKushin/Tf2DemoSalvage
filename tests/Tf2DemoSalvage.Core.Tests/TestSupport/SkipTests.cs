using System;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.TestSupport;

/// <summary>
/// That a missing prerequisite skips the calling test rather than failing it.
/// </summary>
/// <remarks>
/// **This exists because the distinction has already cost two red CI runs**, on separate pushes on
/// 2026-08-27. CI is the machine without Team Fortress 2 and is the only place the no-install path
/// runs at all; a test that asserts rather than skips when the game is absent turns "not installed
/// here" into a failed build, and the message it prints is true while the conclusion drawn from it
/// is wrong.
///
/// Ninety-one test files carry their own copy of the install gate, each spelling the check by hand,
/// so getting one of them wrong is a matter of time rather than of care. <see cref="Skip"/> is the
/// one place that decides what an absent prerequisite does.
///
/// **The variable under test is the KIND of failure, not whether one happens.** Both a skip and an
/// assertion stop the test and both print the reason; they differ only in the exception type NUnit
/// sees, which is why every prediction here names an exact type. <c>IgnoreException</c> and
/// <c>AssertionException</c> are siblings under <c>ResultStateException</c> — neither is assignable
/// to the other — so an assertion that merely caught "some exception" would pass against exactly
/// the defect this is written to catch.
///
/// Both branches of every method are exercised on every machine: nothing here reads the filesystem,
/// so a run on CI and a run on a developer's machine measure the same thing.
/// </remarks>
public sealed class SkipTests
{
    /// <summary>The reason text used where its content does not matter.</summary>
    private const string Reason = "Team Fortress 2 is not installed on this machine.";

    [Test]
    public void Because_WithAReason_ThrowsIgnoreRatherThanAssertion()
    {
        Exception thrown = Assert.Catch(() => Skip.Because(Reason));

        thrown.ShouldBeOfType<IgnoreException>();
    }

    [Test]
    public void Because_WithAReason_CarriesItAsTheMessage()
    {
        Exception thrown = Assert.Catch(() => Skip.Because(Reason));

        thrown.Message.ShouldBe(Reason);
    }

    [Test]
    public void Unless_GivenAValue_ReturnsThatSameValue()
    {
        const string Located = @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf";

        Skip.Unless(Located, Reason).ShouldBe(Located);
    }

    [Test]
    public void Unless_GivenNull_ThrowsIgnoreRatherThanAssertion()
    {
        Exception thrown = Assert.Catch(() => Skip.Unless<string>(null, Reason));

        thrown.ShouldBeOfType<IgnoreException>();
    }

    [Test]
    public void Unless_GivenNull_CarriesTheReasonAsTheMessage()
    {
        Exception thrown = Assert.Catch(() => Skip.Unless<string>(null, Reason));

        thrown.Message.ShouldBe(Reason);
    }

    /// <summary>
    /// That the install gate answers the same question <see cref="GameInstall.Available"/> does.
    /// </summary>
    /// <remarks>
    /// **Written as one test with a branch rather than two, so neither branch can pass by vacuum.**
    /// A pair of tests each skipping itself on the wrong machine would report two skips on CI and
    /// two skips on a developer's machine for the branch it cannot reach — and a skip is neither a
    /// pass nor a failure. Exactly one branch here runs on any given machine, and it asserts an
    /// exact value.
    /// </remarks>
    [Test]
    public void Require_WhicheverMachineThisIs_AgreesWithAvailable()
    {
        if (GameInstall.Available)
        {
            GameInstall.Require().ShouldBe(GameInstall.Root);
        }
        else
        {
            Assert.Catch(() => GameInstall.Require()).ShouldBeOfType<IgnoreException>();
        }
    }

    /// <summary>That the SDK gate does the same for <c>source-sdk-2013</c>.</summary>
    [Test]
    public void Require_WhicheverMachineThisIs_AgreesWithTheSdkBeingAvailable()
    {
        if (SourceSdk.Available)
        {
            SourceSdk.Require().ShouldBe(SourceSdk.Root);
        }
        else
        {
            Assert.Catch(() => SourceSdk.Require()).ShouldBeOfType<IgnoreException>();
        }
    }

    /// <summary>That a file gate names the file, so the skip says what to install.</summary>
    /// <remarks>
    /// A reason of "not installed" is useless when the install IS there and one map is not. The two
    /// cases are different facts about the machine and the text has to tell them apart.
    /// </remarks>
    [Test]
    public void RequireFile_ForAFileTheInstallDoesNotHave_NamesThatFile()
    {
        const string Absent = "maps/no_such_map_exists.bsp";

        Exception thrown = Assert.Catch(() => GameInstall.RequireFile(Absent));

        thrown.ShouldBeOfType<IgnoreException>();

        if (GameInstall.Available)
        {
            thrown.Message.ShouldContain(Absent);
        }
        else
        {
            thrown.Message.ShouldBe(GameInstall.Missing);
        }
    }
}
