using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Every input <c>ComputePoseParam_MoveYaw</c> reads for a POV demo's recorder, tick by tick, beside what
/// <c>PlayersAt</c> reported (B442).
/// </summary>
/// <remarks>
/// **The recorder's own inputs are on the wire in a POV demo**, so each term can be read rather than
/// inferred: the <c>CUserCmd</c> (view yaw, the movement keys), the networked <c>m_vecVelocity</c> the engine's
/// <c>EstimateAbsVelocity</c> returns for the local player, and <c>FL_ONGROUND</c>. Beside them, the values
/// <c>PlayersAt</c> CARRIED — <c>move_x</c>, <c>move_y</c> — and the yaw they imply, <c>atan2(-y, x)</c>, which
/// the push-out preserves. The track's eye yaw and heading are recomputed here only as a cross-check of
/// that implied yaw; a disagreement between the two is the instrument failing, not the subject.
///
/// Reports and asserts nothing (D126).
/// </remarks>
public sealed class MoveYawProbe : IProbe
{
    private const string Usage = "move-yaw <demo> <from> <to> [step]";

    /// <summary><c>FL_ONGROUND</c>, <c>const.h</c>.</summary>
    private const int OnGround = 1 << 0;

    /// <summary>The heading window <c>DemoTimeline.HeadingAt</c> differences over, in ticks of 15 ms.</summary>
    private const double HeadingWindowTicks = 0.1d / 0.015d;

    /// <inheritdoc/>
    public string Name => "move-yaw";

    /// <inheritdoc/>
    public string Summary => "the recorder's move_x/move_y beside every input the engine reads for them: " + Usage;

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 3 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine(Usage);
            return;
        }

        int from = int.Parse(arguments[1], CultureInfo.InvariantCulture);
        int to = int.Parse(arguments[2], CultureInfo.InvariantCulture);
        int step = arguments.Count > 3 ? int.Parse(arguments[3], CultureInfo.InvariantCulture) : 1;

        byte[] file = File.ReadAllBytes(path);
        Dictionary<int, UserCommand> commands = Commands(file);
        DemoTimeline timeline = DemoTimeline.Build(file);

        if (timeline.RecorderEntityIndex is not { } recorder)
        {
            output.WriteLine("No recorder: this is not a point-of-view demo.");
            return;
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: recorder #{recorder}, interval {timeline.IntervalPerTick}"));
        output.WriteLine(
            "tick  | cmdYaw fwd side btn | ground | vel(net) dir speed | eyeYaw(frame) | trackYaw heading(re) | " +
            "moveX moveY implied(yaw) | engine(net vel, cmd yaw) x y");

        List<ScenePlayer> interpolated = [];

        for (int tick = from; tick <= to; tick += step)
        {
            ScenePlayer? stored = Find(timeline.PlayersAt(tick), recorder);

            timeline.PlayersAt((double)tick, interpolated);
            ScenePlayer? drawn = Find(interpolated, recorder);

            string command = commands.TryGetValue(tick, out UserCommand? user)
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{user.Yaw,7:0.0} {user.ForwardMove,4:0} {user.SideMove,4:0} {user.Buttons,5:X}")
                : "      -    -    -     -";

            string ground = "  ?   ";

            if (stored?.Flags is { } flags)
            {
                ground = (flags & OnGround) != 0 ? "ground" : "air   ";
            }

            string velocity = stored?.Velocity is { } v
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Degrees(v.Y, v.X),7:0.0} {MathF.Sqrt((v.X * v.X) + (v.Y * v.Y)),6:0.0}")
                : "      -      -";

            string eye = stored is { } s
                ? string.Create(CultureInfo.InvariantCulture, $"{s.EyeYaw ?? float.NaN,7:0.0}")
                : "      -";

            string track = Track(timeline, recorder, tick);

            string move = drawn is { } d
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{d.MoveX,6:0.000} {d.MoveY,6:0.000} {Degrees(-d.MoveY, d.MoveX),7:0.0}")
                : "     -      -       -";

            string engine = user is not null && stored?.Velocity is { } nv
                ? Engine(nv.X, nv.Y, user.Yaw)
                : "     -      -";

            output.WriteLine($"{tick,5} | {command} | {ground} | {velocity} | {eye} | {track} | {move} | {engine}");
        }
    }

    /// <summary>The track's own eye yaw at the sampled moment, and the heading recomputed over the same window.</summary>
    private static string Track(DemoTimeline timeline, int recorder, int tick)
    {
        if (timeline.TrackFor(recorder) is not { } track ||
            track.At(tick) is not { } now ||
            track.At(Math.Max(0d, tick - HeadingWindowTicks)) is not { } was)
        {
            return "      -        -";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{now.EyeYaw ?? now.Yaw,7:0.0} {Degrees(now.Y - was.Y, now.X - was.X),8:0.0}");
    }

    /// <summary>What the engine's function would give from the networked velocity and the command's view yaw.</summary>
    private static string Engine(float velocityX, float velocityY, float viewYaw)
    {
        float estimate = Degrees(velocityY, velocityX);
        float yaw = Normalize(-(Normalize(viewYaw) - estimate));
        float x = MathF.Cos(yaw * (MathF.PI / 180f));
        float y = -MathF.Sin(yaw * (MathF.PI / 180f));
        float scale = MathF.Max(MathF.Abs(x), MathF.Abs(y));

        return string.Create(CultureInfo.InvariantCulture, $"{x / scale,6:0.000} {y / scale,6:0.000}");
    }

    private static float Degrees(float y, float x) => MathF.Atan2(y, x) * (180f / MathF.PI);

    private static float Normalize(float degrees)
    {
        float wrapped = degrees % 360f;

        if (wrapped > 180f)
        {
            return wrapped - 360f;
        }

        return wrapped < -180f ? wrapped + 360f : wrapped;
    }

    private static ScenePlayer? Find(IEnumerable<ScenePlayer> players, int entityIndex)
    {
        foreach (ScenePlayer player in players)
        {
            if (player.EntityIndex == entityIndex)
            {
                return player;
            }
        }

        return null;
    }

    /// <summary>The last user command recorded at each demo tick.</summary>
    private static Dictionary<int, UserCommand> Commands(ReadOnlyMemory<byte> file)
    {
        Dictionary<int, UserCommand> commands = [];

        foreach (DemoCommand command in DemoCommandReader.Read(file[DemoHeader.SizeBytes..]))
        {
            if (command.Type is DemoCommandType.UserCmd)
            {
                commands[command.Tick] = UserCommand.Decode(command.Payload.Span);
            }
        }

        return commands;
    }
}
