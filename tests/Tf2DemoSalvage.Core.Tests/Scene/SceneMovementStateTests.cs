using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>The networked state the recorder's prediction restores (D205, B450), off authored demos.</summary>
public sealed class SceneMovementStateTests
{
    [Test]
    public void PlayersAt_TheDtLocalMovementFields_ReachTheScenePlayer()
    {
        ScenePlayer player = DemoTimeline.Build(SyntheticPlayer.DemoWithLocalMovementFields()).PlayersAt(100).ShouldHaveSingleItem();

        SceneLocalMovement movement = player.Movement.ShouldNotBeNull();

        movement.Ducked.ShouldBeTrue();
        movement.Ducking.ShouldBeFalse();
        movement.InDuckJump.ShouldBeTrue();
        movement.DuckTime.ShouldBe(812.5f);
        movement.DuckJumpTime.ShouldBe(3.5f);
        movement.JumpTime.ShouldBe(7.5f);
        movement.FallVelocity.ShouldBe(120.25f);
        movement.AllowAutoMovement.ShouldBeFalse();
        movement.BaseVelocity.ShouldBe((1f, 2f, 3f));
        movement.TickBase.ShouldBe(4000);
        movement.DuckTimer.ShouldBe(55.5f);
        movement.AirDucked.ShouldBe(2);
        movement.AirDash.ShouldBe(1);
    }

    [Test]
    public void PlayersAt_APlayerWithoutDtLocal_HasNoMovementState()
    {
        // The control: only the recorder receives DT_Local, so anyone else answers null rather than zeros.
        ScenePlayer player = DemoTimeline.Build(SyntheticPlayer.DemoWithItemEffectMeterFields()).PlayersAt(100).ShouldHaveSingleItem();

        player.Movement.ShouldBeNull();
    }

    [Test]
    public void CollisionAt_ADoorThatTurnsNonSolid_AnswersEachTicksSolidity()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfBrushCollision((100, 1, 0, 0), (200, 1, 4, 15)));
        ScenePropTrack door = timeline.Props.ShouldHaveSingleItem();

        door.CollisionAt(150).ShouldBe(new SceneCollision(1, 0, 0));
        door.CollisionAt(200).ShouldBe(new SceneCollision(1, 4, 15));
        door.CollisionAt(99).ShouldBeNull();
    }
}
