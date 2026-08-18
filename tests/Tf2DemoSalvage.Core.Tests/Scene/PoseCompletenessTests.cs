using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Every field of a pose survives being rebuilt, including fields nobody has added yet.
/// </summary>
/// <remarks>
/// **Three defects in one session had one shape.** <c>ScenePropTrack.At</c> constructs a new
/// <c>ScenePose</c> field by field when asked for a moment between keyframes, and <c>Body</c> was
/// missing from that list, then <c>Skin</c> was; <c>ScenePlayer</c> was built positionally and
/// <c>Yaw</c> fell off the end. Every one drew something plausible: capture points showed the "?"
/// sign, team colours drew neutral, and players faced due east.
///
/// **The failure is silent by construction.** A forgotten field takes the record's default, and
/// every default here is also a legitimate value — zero IS a body number, zero IS a yaw, family
/// zero IS a skin. Nothing can report the omission, and none of it fails a test that checks the
/// fields someone remembered to check.
///
/// **A hand-written comparison does not close it, and that is why this uses reflection.** The test
/// that caught <c>Skin</c> compares the result against an object built in the test — so a field
/// added tomorrow defaults on BOTH sides, matches, and passes. This one discovers the properties
/// instead, sets every one to a value that is not its default, and asserts none of them came back
/// as one. A field added later is covered the moment it exists, with nobody remembering to add it
/// here.
/// </remarks>
public sealed class PoseCompletenessTests
{
    [Test]
    public void EveryFieldOfAPose_SurvivesInterpolation()
    {
        // Both keyframes carry identical values except the position, so anything the rebuild keeps
        // must come back exactly — there is nothing to interpolate towards except itself.
        ScenePose filled = Distinctive();

        ScenePropTrack track = new(entityIndex: 3, "models/props/anything.mdl");

        track.Add(0, filled with { X = 0f });
        track.Add(10, filled with { X = 100f });

        // **Between keyframes, which is the only condition where the defect exists.** On a keyframe
        // the stored pose is returned whole and every field is right by construction.
        // Sampled a full interpolation delay past the midpoint, because a client draws cl_interp
        // behind the present and cannot be pulled toward an update that has not arrived yet.
        ScenePose between = track.At(12d)!.Value;

        between.X.ShouldBeGreaterThan(0f, "the sample must be BETWEEN the keyframes, not on one");
        between.X.ShouldBeLessThan(100f);

        List<string> lost = [];

        foreach (PropertyInfo property in Readable<ScenePose>())
        {
            // **Three fields legitimately do not survive, and each says why.** Position is what
            // interpolation is FOR. The two movement parameters are derived from where the entity
            // was a tenth of a second ago, so they are a property of the track rather than of a
            // stored moment — PropsAt fills them in after interpolating, and a keyframe carrying
            // them would be wrong at every tick between two.
            //
            // This exclusion list is the dangerous part of the test and is kept to three, each
            // argued. Anything added here without a reason turns the guard back into the silence it
            // exists to break.
            if (property.Name is nameof(ScenePose.X)
                or nameof(ScenePose.MoveX)
                or nameof(ScenePose.MoveY)
                or nameof(ScenePose.Speed))
            {
                continue;
            }

            object? expected = property.GetValue(filled);
            object? actual = property.GetValue(between);

            if (!Equals(expected, actual))
            {
                lost.Add($"{property.Name}: expected {expected}, got {actual}");
            }
        }

        lost.ShouldBeEmpty(
            "ScenePropTrack.At rebuilds the pose field by field, and these did not survive it — " +
            "which is silent in production because every default here is also a legitimate value. " +
            string.Join("; ", lost));
    }

    [Test]
    public void EveryFieldOfAPose_HasADistinctiveValueInThisTest()
    {
        // **The control, and it is what makes the test above mean anything.** If Distinctive left a
        // property at its default, the assertion for that property would compare a default against
        // a default and pass however badly the code lost it. This fails the moment someone adds a
        // field to ScenePose without teaching Distinctive to fill it, which is the only maintenance
        // this pair needs.
        ScenePose filled = Distinctive();
        ScenePose empty = new();

        List<string> untouched =
        [
            .. Readable<ScenePose>()
                .Where(property => Equals(property.GetValue(filled), property.GetValue(empty)))
                .Select(property => property.Name),
        ];

        untouched.ShouldBeEmpty(
            "these fields are left at their default by Distinctive(), so the completeness test " +
            "above cannot tell whether the code keeps them: " + string.Join(", ", untouched));
    }

    [Test]
    public void TheDerivedFieldsAreFilledInByPropsAt()
    {
        // **The other half of the exclusion above, and without it that list is just new silence.**
        // Speed, move_x and move_y are excluded from the survival test because they are properties
        // of the TRACK rather than of a keyframe — so this asserts the place that does compute them
        // actually does.
        //
        // They were computed onto ScenePlayer and read off SceneProp, so both were permanently
        // zero: the viewer picks an animation from Speed and blends it with the move parameters, so
        // a running player kept a standing sequence AND the standing corner of its blend grid. The
        // numbers existed the whole time, on a record nobody asked.
        ScenePropTrack track = new(entityIndex: 3, "models/player/scout.mdl");

        // Moving straight along +X, fast enough to be running rather than noise: MOVING_MINIMUM_SPEED
        // is half a unit a second, and this is 200 units over a tenth of a second.
        track.Add(0, new ScenePose { X = 0f, Y = 0f, Z = 0f, Yaw = 0f });
        track.Add(7, new ScenePose { X = 200f, Y = 0f, Z = 0f, Yaw = 0f });

        DemoTimeline timeline = DemoTimeline.ForTracks([track]);

        List<SceneProp> props = [];
        // Likewise one delay later, so the second update has landed and a speed can be derived.
        timeline.PropsAt(13d, props);

        ScenePose pose = props.Single().Pose;

        pose.Speed.ShouldNotBeNull("an entity that is moving must report a speed to choose an animation");
        pose.Speed.Value.ShouldBeGreaterThan(0f);

        // Running straight forward is move_x = 1 in the body's own frame, which is the far end of
        // the grid rather than its middle.
        pose.MoveX.ShouldBeGreaterThan(0.5f, "running forward should drive move_x towards 1");
    }

    /// <summary>A pose whose every field differs from the default for its type.</summary>
    /// <remarks>
    /// Written by hand rather than generated, because the VALUES have to be legal: a sequence of
    /// −1 means "does not animate" and a scale of 0 collapses the model, so a generator filling
    /// everything with 1 would test a pose the code is entitled to treat specially.
    /// </remarks>
    private static ScenePose Distinctive() => new()
    {
        X = 12f,
        Y = 34f,
        Z = 56f,
        Pitch = 11f,
        Yaw = -139f,
        Roll = 7f,
        Scale = 2.5f,
        Sequence = 5,
        Cycle = 0.25f,
        Speed = 320f,
        MoveX = 0.5f,
        MoveY = -0.5f,
        Body = 3,
        Skin = 1,

        // Not 1, which is the default and would make the completeness assertion compare a default
        // against a default. This pair caught PlaybackRate the moment it was added, which is the
        // entire point of the control.
        PlaybackRate = 1.75f,
        Hidden = true,

        // Non-null and non-zero, because null is the "nothing said" case and zero would mean
        // airborne — neither is distinctive enough for this test to measure the field being lost.
        Flags = PlayerActivityState.OnGround | PlayerActivityState.Ducking,

        // Not PRIMARY, which is the value the lookup falls back to when this is lost — so a pose
        // that dropped it would still animate plausibly and this test would not notice.
        Slot = "SECONDARY",

        // Inside the push-off window, so a lost value reads as the float and the difference is
        // visible. Zero would be indistinguishable from the default.
        AirborneSeconds = 0.25f,

        // True, because false is the default and the air-walk supersedes the jump — a pose that
        // dropped this would draw a rocket jump as an ordinary one.
        Airwalking = true,
    };

    /// <summary>Every property of a type that a test can read.</summary>
    private static IEnumerable<PropertyInfo> Readable<T>() =>
        typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0);
}
