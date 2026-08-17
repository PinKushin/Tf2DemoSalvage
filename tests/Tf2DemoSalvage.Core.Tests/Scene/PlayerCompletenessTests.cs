using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Every field of a player survives the walk from its track to the drawn scene.
/// </summary>
/// <remarks>
/// **The record that lost <c>Yaw</c>, guarded the way the pose is.** <c>ScenePlayer</c> is built
/// POSITIONALLY — twelve arguments, seven of them with defaults — and the argument list stopped at
/// <c>LifeState</c>, so every player in a frame faced due east while the number sat correctly in a
/// track nobody asked.
///
/// **Positional construction is the specific hazard here**, and it is worse than the pose's. A named
/// initialiser that forgets a field leaves it at its default; a positional call that stops early
/// does the same thing while LOOKING complete, because the remaining parameters have defaults and
/// the compiler is content. Nothing marks the difference between "chose the default" and "did not
/// get that far".
///
/// So this asserts by reflection, like <see cref="PoseCompletenessTests"/>: every property is given
/// a value that is not its default, and none may come back as one.
/// </remarks>
public sealed class PlayerCompletenessTests
{
    [Test]
    public void EveryFieldOfAPlayer_SurvivesBeingRebuilt()
    {
        // A `with` expression is the operation this checks, because it is what PlayersAt does to
        // attach position and movement to a player taken from a frame. Anything the rebuild drops
        // shows up here as a default.
        ScenePlayer filled = Distinctive();

        ScenePlayer rebuilt = filled with { X = 99f };

        List<string> lost = [];

        foreach (PropertyInfo property in Readable<ScenePlayer>())
        {
            if (property.Name is nameof(ScenePlayer.X))
            {
                continue;
            }

            object? expected = property.GetValue(filled);
            object? actual = property.GetValue(rebuilt);

            if (!Equals(expected, actual))
            {
                lost.Add($"{property.Name}: expected {expected}, got {actual}");
            }
        }

        lost.ShouldBeEmpty("a rebuilt player lost these: " + string.Join("; ", lost));
    }

    [Test]
    public void EveryFieldOfAPlayer_HasADistinctiveValueInThisTest()
    {
        // **The control that keeps the test above honest**, and the one that fails when someone adds
        // a field. Without it a new property sits at its default on both sides of every comparison
        // and passes no matter what the code does with it.
        ScenePlayer filled = Distinctive();
        ScenePlayer empty = new(EntityIndex: 0, X: 0f, Y: 0f, Z: 0f, Team: null, Health: null, PlayerClass: null);

        List<string> untouched =
        [
            .. Readable<ScenePlayer>()
                .Where(property => Equals(property.GetValue(filled), property.GetValue(empty)))
                .Select(property => property.Name),
        ];

        untouched.ShouldBeEmpty(
            "Distinctive() leaves these at their default, so the test above cannot measure them: " +
            string.Join(", ", untouched));
    }

    /// <summary>A player whose every field differs from the default for its type.</summary>
    private static ScenePlayer Distinctive() => new(
        EntityIndex: 3,
        X: 12f,
        Y: 34f,
        Z: 56f,
        Team: SceneTeams.Blu,
        Health: 125,
        PlayerClass: 4,
        Yaw: -139f,
        Speed: 320f,
        LifeState: 2,
        MoveX: 0.5f,
        MoveY: -0.5f,

        // Crouched and off the ground at once, so IsCrouched and IsAirborne are BOTH distinctive —
        // this test reads derived properties too, and a value that leaves either at its default is
        // one the test above cannot measure.
        Flags: PlayerActivityState.Ducking);

    /// <summary>Every property of a type that a test can read.</summary>
    private static IEnumerable<PropertyInfo> Readable<T>() =>
        typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0);
}
