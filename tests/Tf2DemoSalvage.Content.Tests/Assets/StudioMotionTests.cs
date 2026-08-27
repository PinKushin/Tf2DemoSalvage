using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// How fast an animation was authored to travel, read from its own movement records.
/// </summary>
/// <remarks>
/// **The predictions here are TF2's class speeds, and nothing in this code was told them.**
/// <c>GetSequenceGroundSpeed</c> is <c>GetSequenceMoveDist / SequenceDuration</c>, both derived from
/// <c>mstudiomovement_t</c> blocks and the animation's frame rate. Running it over each class's
/// forward run gives 400, 240 and 230 for scout, soldier and heavy — which are exactly the
/// <c>speed_max</c> values the game loads for those classes from its own class scripts
/// (<c>tf_classdata.cpp:152</c>, <c>m_flMaxSpeed = pKeyValuesData->GetFloat( "speed_max" )</c>).
///
/// That agreement is the point of this test. The animations were authored to travel at the speed the
/// class moves, so the two numbers meeting is a check of the whole chain — movement records, the
/// velocity integral, the frame-rate term and the blend weights — against a constant that lives
/// somewhere else entirely. An arithmetic slip anywhere in it would land on some other number.
///
/// **Exact values, not bands.** These are deterministic reads of authored data; a tolerance would
/// only hide the case this is for.
/// </remarks>
public sealed class StudioMotionTests
{
    private static string Game => GameInstall.Require();

    /// <summary><c>speed_max</c> for each class, and the animation model that should match it.</summary>
    [TestCase("models/player/scout_animations.mdl", 400f)]
    [TestCase("models/player/soldier_animations.mdl", 240f)]
    [TestCase("models/player/heavy_animations.mdl", 230f)]
    public void StudioMotion_TheForwardRun_TravelsAtTheClassSpeed(string path, float expected)
    {
        if (Read(path) is not { } file)
        {
            Assert.Ignore($"{path} is not available");
            return;
        }

        GroundSpeedOf(file, moveX: 1f).ShouldBe(expected, 0.5f);
    }

    [Test]
    public void StudioMotion_RunningBackward_TravelsAtTheSameSpeed()
    {
        // **The control for the direction, and it has to be equal rather than merely non-zero.**
        // A blend cell is chosen by the pose parameters, so if the wrong cells were being asked
        // about this would report some other animation's travel. Backpedalling in TF2 is not slower
        // than running forward — the class speed is one number — so the authored speeds match while
        // the ANIMATIONS differ, which is exactly the pair that catches a cell mix-up.
        if (Read("models/player/scout_animations.mdl") is not { } file)
        {
            Assert.Ignore("the scout's animations are not available");
            return;
        }

        GroundSpeedOf(file, moveX: 1f).ShouldBe(400f, 0.5f);
        GroundSpeedOf(file, moveX: -1f).ShouldBe(400f, 0.5f);
    }

    [Test]
    public void AnAnimationThatDoesNotTravel_ReportsNothing()
    {
        // **The other control: a number this size should not appear everywhere.** A standing idle
        // declares no movement blocks at all, so Studio_AnimPosition returns false and the speed is
        // zero. Without this, an implementation that returned a constant would pass every case
        // above by luck of the model being asked.
        if (Read("models/player/scout_animations.mdl") is not { } file)
        {
            Assert.Ignore("the scout's animations are not available");
            return;
        }

        StudioSequence stand = StudioSequences.Read(file)
            .First(sequence => sequence.Activity == "ACT_MP_STAND_PRIMARY");

        StudioMotion.MovementCount(file, stand.Animation)
            .ShouldBe(0, "a standing idle does not carry the model anywhere");

        StudioMotion.GroundSpeed(file, [(stand.Animation, 1f)]).ShouldBe(0f);
    }

    [Test]
    public void AWeightedBlendSumsTheVectors_NotTheSpeeds()
    {
        // **Studio_SeqMovement adds the vectors and takes the length of the SUM**, so a half-and-half
        // blend of forward and backward travel is a standstill rather than a full-speed run. An
        // implementation averaging the magnitudes would report 400 here, which is the difference
        // this asserts.
        if (Read("models/player/scout_animations.mdl") is not { } file)
        {
            Assert.Ignore("the scout's animations are not available");
            return;
        }

        (int forward, int backward) = ForwardAndBackward(file);

        StudioMotion.GroundSpeed(file, [(forward, 0.5f), (backward, 0.5f)])
            .ShouldBeLessThan(
                50f,
                "opposite travel at equal weight cancels; averaging the speeds would give 400");
    }

    [Test]
    public void TheVelocityRampIsIntegrated_NotTakenFlat()
    {
        // **The one term no shipped model can test, and sabotage is how that was established.**
        // Replacing Valve's `0.5 * (v1 - v0) * f * f` with a different coefficient leaves every
        // assertion above GREEN, because TF2's run loops are authored at constant velocity — v0
        // equals v1, so the whole term is zero whatever it is multiplied by. Scaling `v0 * f`
        // instead reddens four of them, which is how the difference between "covered" and "happens
        // to agree" was measured rather than assumed.
        //
        // So this is a fixture, built for the single condition the corpus cannot supply: an
        // animation that ACCELERATES. Over one second from rest to 200 units a second, Valve's
        // integral gives the area under the ramp — 100 units — while reading the end velocity flat
        // would give 200 and dropping the term would give 0.
        byte[] file = Accelerating(from: 0f, to: 200f, frames: 31, fps: 30f);

        StudioMotion.MovementCount(file, 0).ShouldBe(1, "the fixture must declare its movement");

        (float X, float Y, float Z, float Yaw) end =
            StudioMotion.Position(file, 0, 1f).ShouldNotBeNull();

        end.X.ShouldBe(100f, 0.01f, "the area under a ramp from 0 to 200 over the cycle");

        // And the speed, which is that distance over the animation's own duration: 31 frames at 30
        // frames a second is one second exactly, so the two numbers coincide here by construction.
        StudioMotion.GroundSpeed(file, [(0, 1f)]).ShouldBe(100f, 0.01f);
    }

    /// <summary>
    /// A minimal model file holding one animation that accelerates along +X.
    /// </summary>
    /// <param name="from"><c>v0</c>, the velocity at the start of the block.</param>
    /// <param name="to"><c>v1</c>, the velocity at its end.</param>
    /// <param name="frames"><c>numframes</c>.</param>
    /// <param name="fps">Frames a second.</param>
    /// <remarks>
    /// Only the fields this reader touches are filled: the header's animation count and index, then
    /// one <c>mstudioanimdesc_t</c> and one <c>mstudiomovement_t</c>. Everything else is zero, which
    /// is legitimate — a real <c>.mdl</c> has far more, and none of it is read here.
    /// </remarks>
    private static byte[] Accelerating(float from, float to, int frames, float fps)
    {
        const int header = 256;
        const int animation = header;
        const int movement = animation + 100;

        byte[] file = new byte[movement + 44];

        void Int(int at, int value) => BitConverter.TryWriteBytes(file.AsSpan(at), value);
        void Real(int at, float value) => BitConverter.TryWriteBytes(file.AsSpan(at), value);

        Int(180, 1);
        Int(184, animation);

        Real(animation + 8, fps);
        Int(animation + 16, frames);
        Int(animation + 20, 1);

        // movementindex is relative to the animation description, as pMovement adds it to `this`.
        Int(animation + 24, movement - animation);

        Int(movement + 0, frames - 1);
        Real(movement + 8, from);
        Real(movement + 12, to);
        Real(movement + 16, 0f);

        // vector: a unit step along +X, which the integrated distance scales.
        Real(movement + 20, 1f);

        return file;
    }

    /// <summary>The ground speed of the run blend at one <c>move_x</c>, with <c>move_y</c> centred.</summary>
    private static float GroundSpeedOf(ReadOnlyMemory<byte> file, float moveX)
    {
        IReadOnlyList<StudioPoseParameter> parameters = StudioSequences.PoseParameters(file);
        int[] identity = [.. Enumerable.Range(0, parameters.Count)];

        StudioBlendGrid grid = Run(file);

        float[] values = Values(parameters, moveX);

        (int x, float settingX) = grid.Locate(0, parameters, values, identity);
        (int y, float settingY) = grid.Locate(1, parameters, values, identity);

        (int[] animations, float[] weights) = grid.ThreeWay(x, y, settingX, settingY);

        List<(int Animation, float Weight)> blend =
            [.. animations.Select((animation, corner) => (animation, weights[corner]))];

        return StudioMotion.GroundSpeed(file, blend);
    }

    /// <summary>The animations at the forward and backward ends of the run grid.</summary>
    private static (int Forward, int Backward) ForwardAndBackward(ReadOnlyMemory<byte> file)
    {
        StudioBlendGrid grid = Run(file);

        // move_x drives the second axis, running −1 at the first row to +1 at the last.
        return (grid.Animation(1, grid.GroupY - 1), grid.Animation(1, 0));
    }

    /// <summary>The forward run sequence's blend grid.</summary>
    private static StudioBlendGrid Run(ReadOnlyMemory<byte> file) =>
        StudioSequences.Read(file)
            .First(sequence =>
                sequence.Activity == "ACT_MP_RUN_PRIMARY" && sequence.Blend is { Blends: true })
            .Blend!;

    /// <summary>Normalised pose parameter values for one <c>move_x</c>.</summary>
    private static float[] Values(IReadOnlyList<StudioPoseParameter> parameters, float moveX)
    {
        float[] values = new float[parameters.Count];

        for (int index = 0; index < parameters.Count; index++)
        {
            float raw = parameters[index].Name switch
            {
                "move_x" => moveX,
                _ => 0f,
            };

            values[index] = StudioBlendGrid.Normalize(parameters[index], raw);
        }

        return values;
    }

    /// <summary>One file out of the game's archives, or null when the game is absent.</summary>
    private static byte[]? Read(string path)
    {
        if (!Directory.Exists(Game))
        {
            return null;
        }

        return new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" }
            .Select(name => Path.Combine(Game, name))
            .Where(File.Exists)
            .Select(VpkArchive.Open)
            .Select(archive => archive.ReadFile(path))
            .FirstOrDefault(found => found is not null);
    }
}
