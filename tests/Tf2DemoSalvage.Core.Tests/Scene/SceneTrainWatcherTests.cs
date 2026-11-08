using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`C_TeamTrainWatcher::OnDataChanged`'s events applied to `CTFHudEscort`'s state (c_team_train_watcher.cpp, tf_hud_escort.cpp:879-925).</summary>
public sealed class SceneTrainWatcherTests
{
    private const float Now = 12.5f;

    [Test]
    public void Created_ForATeam_IsTheConstructorsForcedValues() =>
        SceneTrainWatcher.Created(3).ShouldBe(new SceneTrainWatcher(3, -1f, -2, -1f, 0));

    [Test]
    public void Apply_SpeedRisesFromStill_StartsTheSwoopAtNow()
    {
        SceneTrainWatcher old = SceneTrainWatcher.Created(2);
        SceneTrainWatcher received = new(2, -1f, 1, -1f, 3);

        received.Apply(old, default, Now).ShouldBe(new SceneEscort(0f, 1, 3, 0f, Now));
    }

    [Test]
    public void Apply_SpeedChangesWhileAlreadyMoving_KeepsTheSwoopStart()
    {
        SceneTrainWatcher old = new(2, -1f, 1, -1f, 3);
        SceneTrainWatcher received = new(2, -1f, 2, -1f, 3);
        SceneEscort escort = new(0.5f, 1, 3, 0f, 4f);

        received.Apply(old, escort, Now).ShouldBe(new SceneEscort(0.5f, 2, 3, 0f, 4f));
    }

    [Test]
    public void Apply_SpeedFallsToStill_KeepsTheSwoopStart()
    {
        SceneTrainWatcher old = new(2, -1f, 1, -1f, 3);
        SceneTrainWatcher received = new(2, -1f, 0, -1f, 3);
        SceneEscort escort = new(0.5f, 1, 3, 0f, 4f);

        received.Apply(old, escort, Now).ShouldBe(new SceneEscort(0.5f, 0, 3, 0f, 4f));
    }

    [Test]
    public void Apply_OnlyTheCappersChange_UpdatesSpeedAndPlayersTogether()
    {
        SceneTrainWatcher old = new(2, -1f, 0, -1f, 1);
        SceneTrainWatcher received = new(2, -1f, 0, -1f, 4);

        received.Apply(old, default, Now).ShouldBe(new SceneEscort(0f, 0, 4, 0f, 0f));
    }

    [Test]
    public void Apply_OnlyTheProgressChanges_UpdatesProgressAlone()
    {
        SceneTrainWatcher old = new(2, 0.25f, 1, -1f, 2);
        SceneTrainWatcher received = new(2, 0.75f, 1, -1f, 2);
        SceneEscort escort = new(0.25f, 1, 2, 7f, 3f);

        received.Apply(old, escort, Now).ShouldBe(new SceneEscort(0.75f, 1, 2, 7f, 3f));
    }

    [Test]
    public void Apply_OnlyTheRecedeTimeChanges_UpdatesRecedeAlone()
    {
        SceneTrainWatcher old = new(2, 0.25f, 1, -1f, 2);
        SceneTrainWatcher received = new(2, 0.25f, 1, 9f, 2);
        SceneEscort escort = new(0.25f, 1, 2, 7f, 3f);

        received.Apply(old, escort, Now).ShouldBe(new SceneEscort(0.25f, 1, 2, 9f, 3f));
    }

    [Test]
    public void Apply_NothingChanged_ReturnsTheEscortUntouched()
    {
        SceneTrainWatcher same = new(2, 0.25f, 1, 5f, 2);
        SceneEscort escort = new(0.1f, 3, 4, 6f, 8f);

        same.Apply(same, escort, Now).ShouldBe(escort);
    }
}
