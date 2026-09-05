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

    /// <remarks>
    /// **The other half of the exclusion above, moved to the path that actually computes them**
    /// (B258). `Speed`, `move_x` and `move_y` come from
    /// `CBasePlayerAnimState::ComputePoseParam_MoveYaw`, which the engine runs for players and for
    /// nothing else — so `PropsAt` no longer derives them, and the test that asserted it did has
    /// become the pair below: the player path fills them, and the prop path leaves them alone.
    ///
    /// The prop half is not a formality. It is the assertion that would fail if somebody restored
    /// `Moving()` to `PropsAt` for a plausible-looking reason, and it is cheap to state.
    /// </remarks>
    [Test]
    public void PoseCompleteness_TheDerivedFields_AreFilledInByPlayersAt()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/player/scout.mdl");

        track.Add(0, new ScenePose { X = 0f, Y = 0f, Z = 0f, Yaw = 0f });
        track.Add(7, new ScenePose { X = 200f, Y = 0f, Z = 0f, Yaw = 0f });

        DemoTimeline timeline = DemoTimeline.ForPlayerTracks(
            [track],
            [new ScenePlayer(
                EntityIndex: 3, X: 0f, Y: 0f, Z: 0f, Team: 2, Health: 100, PlayerClass: 1)]);

        List<ScenePlayer> players = [];
        timeline.PlayersAt(13d, players);

        ScenePlayer sampled = players.Single();

        sampled.Speed.ShouldBeGreaterThan(
            0f, "a player that is moving must report a speed to choose an animation");

        // Running straight forward is move_x = 1 in the body's own frame, which is the far end of
        // the grid rather than its middle.
        sampled.MoveX.ShouldBeGreaterThan(0.5f, "running forward should drive move_x towards 1");
    }

    /// <remarks>
    /// **A prop is not a player and the engine derives none of this for one.** `PropsAt` computed
    /// `Speed`, `move_x` and `move_y` for every crate on the map — three timeline lookups each, per
    /// frame — and never for a player, since player tracks are not in the prop list at all.
    /// Measured on `tf2-2026-pub-pov-clean`: zero of 79 prop groups are `CTFPlayer`.
    /// </remarks>
    [Test]
    public void PoseCompleteness_TheDerivedFields_AreLeftAloneByPropsAt()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props/crate.mdl");

        track.Add(0, new ScenePose { X = 0f, Y = 0f, Z = 0f, Yaw = 0f });
        track.Add(7, new ScenePose { X = 200f, Y = 0f, Z = 0f, Yaw = 0f });

        DemoTimeline timeline = DemoTimeline.ForTracks([track]);

        List<SceneProp> props = [];
        timeline.PropsAt(13d, props);

        ScenePose pose = props.Single().Pose;

        pose.Speed.ShouldBeNull("a prop has no animation state to derive a speed for");
        pose.MoveX.ShouldBe(0f);
        pose.MoveY.ShouldBe(0f);
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

        // **The three per-BONE scales, distinct from each other and from the model scale above**
        // (B312). All four are floats defaulting to 1, so equal values would let a carry into the
        // wrong one pass.
        HeadScale = 1.5f,
        TorsoScale = 0.5f,
        HandScale = 3f,
        Sequence = 5,
        Cycle = 0.25f,

        // Not zero, which is the default meaning "measure the cycle from demo time". A viewmodel
        // restarts its animation on `m_nAnimationParity` and measures from here instead, so a
        // default here would compare a default against a default and never notice the field.
        AnimationStartSeconds = 8.75d,

        // Distinct from the clock above, because the two are separate events (B346): a sequence
        // restarting is not the entity jumping, and a fixture sharing one value could not tell a
        // swap between them from a correct carry.
        DiscontinuitySeconds = 3.25d,
        Speed = 320f,
        MoveX = 0.5f,
        MoveY = -0.5f,
        Body = 3,
        Skin = 1,

        // **All three away from their defaults, and each default is a legitimate value** — 255 is
        // opaque, 0 is `kRenderFxNone` and 0 is `kRenderNormal` — so a rebuild that dropped any of
        // them would look correct on every ordinary entity and wrong only on the ones that fade.
        // That is the exact shape this file exists for, and the shape `ScenePropTrack`'s own comment
        // warns about beside `Body`.
        //
        // `kRenderFxFlickerSlow` and `kRenderNone` rather than arbitrary numbers: both occur in real
        // matches (76 and 118 entities respectively), so the fixture is a state the code will meet.
        RenderAlpha = 128,
        RenderFx = 12,
        RenderMode = 10,
        FadeMinimumDistance = 826f,
        FadeMaximumDistance = 900f,

        // **Non-empty, because empty is the default AND the answer for every player** — so a
        // rebuild that dropped this would look correct on the entities this project draws most and
        // wrong only on buildings. Two values rather than one, so a rebuild that kept the array but
        // truncated it is distinguishable from one that kept it whole.
        PoseParameters = [0.25f, 0.75f],

        // Non-zero, because zero is both the default and a legal counter value — an entity that
        // has never replayed an animation sits at it, so a rebuild that dropped this would look
        // right on everything except the taunt it exists for.
        ResetEventsParity = 3,

        // Not 1, which is the default and would make the completeness assertion compare a default
        // against a default. This pair caught PlaybackRate the moment it was added, which is the
        // entire point of the control.
        PlaybackRate = 1.75f,
        Hidden = true,

        // A reload in the slot it belongs to. Null is the default, and a pose that lost its
        // gestures in a rebuild would look identical to a player who is holding none — which is
        // most players most of the time, so nothing else would notice (B282).
        Gestures =
        [
            new SceneGesture(
                GestureSlot.AttackAndReload, "ACT_MP_RELOAD_STAND", null, AutoKill: true, 13.6d),
        ],

        // A sentry's aim layer, which is the other source of layers and the one a player never
        // has (B285). Empty is the default, and an entity whose layers were dropped in a rebuild
        // looks exactly like one that sends none — which is most of them.
        Layers = [new SceneAnimationLayer(Order: 1, Sequence: 7, Cycle: 0.25f, Weight: 0.5f)],

        // Four inputs, none of them the 0 or 1 an endpoint would produce anyway, so a rebuild that
        // dropped them reads as a bone at rest rather than as a bone bent to its limit (B288).
        BoneControllers = [0.25f, 0.5f, 0.75f, 0.125f],

        // **`WEAPON_IS_ACTIVE`, and neither of the two values it must be told apart from is safe
        // here.** Null means "not a weapon" and 0 is `WEAPON_NOT_CARRIED`, so a rebuild that
        // dropped this would read as a wearable or as a weapon on the floor — and both of those
        // DRAW, which is why losing it was invisible for as long as it was (B244). Two is the one
        // value whose loss changes what appears on screen.
        WeaponState = 2,

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

        // Non-zero, so a dropped value reads as looking level rather than as the default.
        EyePitch = 21f,

        // Different from Yaw, because the feet and the eyes are different angles the moment a
        // player turns on the spot — equal values would hide a rebuild that confused them.
        EyeYaw = -95f,
        AimYaw = 44f,

        // Waist deep, so a dropped value reads as dry land.
        WaterLevel = 2,
    };

    /// <summary>Every property of a type that a test can read.</summary>
    private static IEnumerable<PropertyInfo> Readable<T>() =>
        typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0);
}
