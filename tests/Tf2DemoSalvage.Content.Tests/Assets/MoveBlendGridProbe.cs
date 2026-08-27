using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Which cell of the run's blend grid a forward-running player actually selects.
/// </summary>
/// <remarks>
/// **A probe, not a test — B101's last unmeasured hop.** Players were seen running backwards
/// whenever they moved. The pose parameter is now measured and is not the cause: on a POV demo
/// recorded for this, seven sampled ticks of recorded <c>forwardmove 450</c> with
/// <c>IN_FORWARD</c> held all produce <c>move_x = 1.000, move_y = -0.000</c>, which is exactly
/// forward. So the parameter is right and the fault is in what the model does with it.
///
/// This prints the grid itself: the declared range of each pose parameter, the sequence's own
/// window into that range, the grid's shape, and which local animation sits in each cell. Then it
/// runs the project's own <c>Locate</c> at the measured value so the chosen cell is read out rather
/// than reasoned about.
/// </remarks>
public sealed class MoveBlendGridProbe
{
    private static string Game => GameInstall.Require();

    [Test]
    public void MoveBlendGrid_ForwardRunning_IsReported()
    {
        if (!Directory.Exists(Game))
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        VpkArchive[] archives =
        [
            .. new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" }
                .Select(name => Path.Combine(Game, name))
                .Where(File.Exists)
                .Select(VpkArchive.Open),
        ];

        foreach (string path in new[]
        {
            "models/player/scout.mdl",
            "models/player/scout_animations.mdl",
            "models/player/soldier.mdl",
            "models/player/soldier_animations.mdl",
        })
        {
            if (archives.Select(archive => archive.ReadFile(path)).FirstOrDefault(f => f is not null)
                is not { } file)
            {
                TestContext.Out.WriteLine($"MISSING {path}");
                continue;
            }

            IReadOnlyList<StudioPoseParameter> parameters = StudioSequences.PoseParameters(file);

            TestContext.Out.WriteLine($"=== {path}");

            foreach (StudioPoseParameter parameter in parameters)
            {
                TestContext.Out.WriteLine(
                    $"  param {parameter.Name} start {parameter.Start:0.###} " +
                    $"end {parameter.End:0.###} loop {parameter.Loop:0.###}");
            }

            foreach (StudioSequence sequence in StudioSequences.Read(file)
                .Where(candidate => candidate.Activity.Contains("RUN", StringComparison.Ordinal))
                .Take(3))
            {
                Describe(sequence, parameters);
            }
        }
    }

    [Test]
    public void MoveBlendGrid_TheRunBlendGroundSpeed_IsReported()
    {
        if (!Directory.Exists(Game))
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        VpkArchive[] archives =
        [
            .. new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" }
                .Select(name => Path.Combine(Game, name))
                .Where(File.Exists)
                .Select(VpkArchive.Open),
        ];

        foreach (string path in new[]
        {
            "models/player/scout_animations.mdl",
            "models/player/soldier_animations.mdl",
            "models/player/heavy_animations.mdl",
        })
        {
            if (archives.Select(a => a.ReadFile(path)).FirstOrDefault(f => f is not null) is not { } file)
            {
                continue;
            }

            IReadOnlyList<StudioPoseParameter> parameters = StudioSequences.PoseParameters(file);
            int[] identity = [.. Enumerable.Range(0, parameters.Count)];

            foreach (StudioSequence sequence in StudioSequences.Read(file)
                .Where(c => c.Activity == "ACT_MP_RUN_PRIMARY" && c.Blend is { Blends: true })
                .Take(1))
            {
                StudioBlendGrid grid = sequence.Blend!;

                float[] values = new float[parameters.Count];

                for (int index = 0; index < parameters.Count; index++)
                {
                    float raw = parameters[index].Name switch { "move_x" => 1f, _ => 0f };
                    values[index] = StudioBlendGrid.Normalize(parameters[index], raw);
                }

                (int x, float sx) = grid.Locate(0, parameters, values, identity);
                (int y, float sy) = grid.Locate(1, parameters, values, identity);

                (int[] animations, float[] weights) = grid.ThreeWay(x, y, sx, sy);

                List<(int, float)> blend =
                    [.. animations.Select((a, i) => (a, weights[i])).Where(pair => pair.Item2 > 0f)];

                TestContext.Out.WriteLine(
                    $"SPEED {Path.GetFileName(path)} " +
                    string.Join(", ", blend.Select(b => $"anim {b.Item1} w {b.Item2:0.###}")) +
                    $" => ground speed {StudioMotion.GroundSpeed(file, blend):0.##}");
            }
        }
    }

    private static void Describe(
        StudioSequence sequence, IReadOnlyList<StudioPoseParameter> parameters)
    {
        TestContext.Out.WriteLine(
            $"  seq {sequence.Label} activity {sequence.Activity} weight {sequence.ActivityWeight}");

        if (sequence.Blend is not { } grid)
        {
            TestContext.Out.WriteLine("    no blend grid");
            return;
        }

        TestContext.Out.WriteLine(
            $"    grid {grid.GroupX}x{grid.GroupY} paramX {grid.ParameterX} paramY {grid.ParameterY} " +
            $"x [{grid.StartX:0.###},{grid.EndX:0.###}] y [{grid.StartY:0.###},{grid.EndY:0.###}]");

        for (int row = 0; row < grid.GroupY; row++)
        {
            TestContext.Out.WriteLine(
                "    row " + row.ToString(CultureInfo.InvariantCulture) + ": " +
                string.Join(
                    " ",
                    Enumerable.Range(0, grid.GroupX)
                        .Select(column => grid.Animation(column, row)
                            .ToString(CultureInfo.InvariantCulture)
                            .PadLeft(4))));
        }

        // The measured values: running dead forward.
        float[] values = new float[parameters.Count];

        for (int index = 0; index < parameters.Count; index++)
        {
            float raw = parameters[index].Name switch
            {
                "move_x" => 1f,
                "move_y" => 0f,
                _ => 0f,
            };

            values[index] = StudioBlendGrid.Normalize(parameters[index], raw);
        }

        // Identity, because this probe reads the animation model's own list directly rather than a
        // merged one — its local indices already ARE the indices of the list handed in.
        int[] identity = [.. Enumerable.Range(0, parameters.Count)];

        (int cellX, float alongX) = grid.Locate(0, parameters, values, identity);
        (int cellY, float alongY) = grid.Locate(1, parameters, values, identity);

        TestContext.Out.WriteLine(
            $"    forward (move_x 1, move_y 0) normalised [{string.Join(", ", values.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)))}] " +
            $"selects x cell {cellX} +{alongX:0.###}, y cell {cellY} +{alongY:0.###} " +
            $"=> animations {grid.Animation(cellX, cellY)} and {grid.Animation(cellX + 1, cellY)}");
    }
}
