using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>The pulse `CControlPointIcon` starts (hud_controlpointicons.cpp:327-393) and the per-team value it sits beside.</summary>
public sealed class CapPulseTests
{
    private const float Now = 30f;

    [Test]
    public void Next_NothingChanged_IsTheSamePulse()
    {
        CapPulse before = new(0, 0f, 0f, false);

        before.Next(0, 2, cappingChanged: false, Now).ShouldBe(before);
    }

    [Test]
    public void Next_AnotherTeamStartsCapturing_StartsAnAcceleratingPulseAtNow() =>
        default(CapPulse).Next(3, 2, cappingChanged: true, Now).ShouldBe(new CapPulse(3, Now, 0f, true));

    [Test]
    public void Next_CappingStopsWithTheOwnerUnchanged_ClearsTheStartAndKeepsTheRest()
    {
        CapPulse before = new(3, 20f, 0f, true);

        // Owner 2 is not the previous capper 3, so the finish does not follow.
        before.Next(0, 2, cappingChanged: true, Now).ShouldBe(new CapPulse(3, 0f, 0f, true));
    }

    [Test]
    public void Next_TheOwnerItselfIsCapping_ClearsTheStart()
    {
        CapPulse before = new(0, 20f, 4f, false);

        before.Next(2, 2, cappingChanged: true, Now).ShouldBe(new CapPulse(0, 0f, 4f, false));
    }

    [Test]
    public void Next_TheCapturingTeamBecomesTheOwner_StartsTheFinishPulse()
    {
        CapPulse before = new(3, 20f, 0f, true);

        before.Next(0, 3, cappingChanged: true, Now).ShouldBe(new CapPulse(0, Now + CapPulse.FinishDelay, CapPulse.FinishLength, false));
    }

    [Test]
    public void Next_TheOwnerChangesWithoutACappingChange_StillFinishesTheCapture()
    {
        CapPulse before = new(3, 20f, 0f, true);

        before.Next(3, 3, cappingChanged: false, Now).ShouldBe(new CapPulse(0, Now + CapPulse.FinishDelay, CapPulse.FinishLength, false));
    }

    [Test]
    public void Next_APreviousCapperThatIsNotTheOwner_LeavesThePulseRunning()
    {
        CapPulse before = new(3, 20f, 0f, true);

        before.Next(3, 2, cappingChanged: false, Now).ShouldBe(before);
    }

    [TestCase(0, 10)]
    [TestCase(1, 11)]
    [TestCase(2, 12)]
    [TestCase(3, 13)]
    [TestCase(4, 0)]
    [TestCase(-1, 0)]
    public void TeamQuadIndexer_ByTeam_IsThatTeamsValueOrDefaultPastTheFourth(int team, int expected) =>
        new TeamQuad<int>(10, 11, 12, 13)[team].ShouldBe(expected);
}
