using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>Every baked animated model on f12, drawn from its bake and from bones, at the same sequence and cycle (finding 80).</summary>
/// <remarks>
/// **The bones-versus-bake sweep, item 3's first step.** The bake is the oracle: what the viewer draws today. Each model is
/// loaded twice through the production loader, once baked and once forced onto the GPU-skinned path
/// (<c>mustSkin</c>), and both are posed by <see cref="EntityModelSet.Instances"/> at the origin with the same
/// <see cref="ScenePose"/>. The baked corners are the draw's own: frame <c>Frame</c> blended toward the next by
/// <c>Blend</c>, as the shader blends them. The skinned corners are the bind pose moved by the instance's bone matrices with
/// the shader's arithmetic (<c>SkinPosition</c>, <c>WorldRenderer.cs</c>). Reports the largest corner distance per model
/// and sequence; asserts nothing, because it is a measurement.
/// </remarks>
[Explicit("a measurement over f12's models, minutes long; read its output")]
public sealed class BakeAgainstBonesDiagnostic
{
    private const int Cycles = 9;

    [Test]
    [Category(TestCategories.MutationHeavy)]
    public void Sweep_EveryBakedAnimatedModelOnF12_ReportsBonesAgainstBake()
    {
        if (GameInstall.Root is not { } game || Path.Combine(game, "maps", "cp_process_f12.bsp") is not { } path || !File.Exists(path))
        {
            Assert.Ignore("Team Fortress 2 with cp_process_f12 is not installed.");
            return;
        }

        // The demo names the map's animated entities (doors, lockers) and the projectiles; without one only the stock set
        // loads. Passed as `-- TestRunParameters.Parameter(name=\"demo\", value=\"<f12 .dem>\")`.
        DemoTimeline? timeline = TestContext.Parameters.Get("demo") is { Length: > 0 } demo && File.Exists(demo)
            ? DemoTimeline.Build(File.ReadAllBytes(demo))
            : null;

        TestContext.Out.WriteLine(timeline is null ? "no demo: the stock model set only" : "with the demo's models");

        byte[] map = File.ReadAllBytes(path);
        PakFile pak = PakFile.ReadFrom(map);
        // The bake is the oracle, so it is asked for by name now that bones are the default.
        MapAssets assets;
        PropModels.BakeAnimated = true;

        try
        {
            assets = MapAssets.Load(map, SharedGame.Archives, maximumTextureSize: 16, entityModels: DemoModels.Needed(timeline, SharedGame.Content));
        }
        finally
        {
            PropModels.BakeAnimated = false;
        }

        List<(string Model, int Sequence, float Worst, float Mean, int Corners)> rows = [];
        List<string> unskinnable = [];
        int controls = 0;

        foreach ((string model, PropModels.ModelFrames baked) in assets.EntityModels.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (baked.Skinned is not null || baked.Geometry.Count < 2)
            {
                continue;
            }

            PropModels.ModelFrames? skinned = PropModels.LoadFrames(
                NullLogger.Instance, model, pak, SharedGame.Archives, new MaterialTable(), _ => null, mustSkin: true);

            if (skinned?.Skinned is null || skinned.Geometry.Count != 1 || skinned.Geometry[0].Count != baked.Geometry[0].Count)
            {
                unskinnable.Add($"{model}: {(skinned?.Skinned is null ? "no bones" : "corner count differs")}");
                continue;
            }

            for (int sequence = 0; sequence < baked.SequenceAnimation.Count; sequence++)
            {
                if (!baked.Layout.TryGetValue(baked.SequenceAnimation[sequence], out (int Start, int Frames, float CyclesPerSecond) where) ||
                    where.Frames < 2)
                {
                    continue;
                }

                float worst = 0f;
                double sum = 0d;
                long count = 0;
                List<string> steps = [];

                for (int step = 0; step < Cycles; step++)
                {
                    float stepWorst = 0f;
                    ScenePose pose = new() { Scale = 1f, Sequence = sequence, Cycle = step / (float)(Cycles - 1) };

                    IReadOnlyList<(float X, float Y, float Z)> fromBake = Baked(model, baked, pose);
                    IReadOnlyList<(float X, float Y, float Z)> fromBones = Boned(model, skinned, pose);

                    for (int corner = 0; corner < fromBake.Count; corner++)
                    {
                        float dx = fromBake[corner].X - fromBones[corner].X;
                        float dy = fromBake[corner].Y - fromBones[corner].Y;
                        float dz = fromBake[corner].Z - fromBones[corner].Z;
                        float apart = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

                        worst = MathF.Max(worst, apart);
                        stepWorst = MathF.Max(stepWorst, apart);
                        sum += apart;
                        count++;
                    }

                    ModelInstance drawn = Pose(model, baked, pose);
                    steps.Add($"c{pose.Cycle:0.00}:f{drawn.Frame}+{drawn.Blend:0.00}={stepWorst:0.0}");
                }

                if (worst > 0.5f)
                {
                    TestContext.Out.WriteLine(
                        $"detail {model} seq {sequence} frames {where.Frames} at {where.Start}: {string.Join(' ', steps)}");
                }

                controls++;
                rows.Add((model, sequence, worst, (float)(sum / Math.Max(1, count)), baked.Geometry[0].Count));
            }
        }

        controls.ShouldBeGreaterThan(0, "the control: f12 bakes animated models");

        foreach ((string model, int sequence, float worst, float mean, int corners) in rows.OrderByDescending(row => row.Worst))
        {
            TestContext.Out.WriteLine($"{worst,10:0.000} worst {mean,9:0.0000} mean  seq {sequence,3}  {corners,6} corners  {model}");
        }

        foreach (string refused in unskinnable)
        {
            TestContext.Out.WriteLine($"not skinnable: {refused}");
        }
    }

    /// <summary>What the baked draw puts at each corner: its frame, blended toward the next as the shader does.</summary>
    private static IReadOnlyList<(float X, float Y, float Z)> Baked(string model, PropModels.ModelFrames frames, ScenePose pose)
    {
        ModelInstance instance = Pose(model, frames, pose);
        IReadOnlyList<PropVertex> at = frames.Geometry[instance.Frame];
        IReadOnlyList<PropVertex> ahead = frames.NextOf(instance.Frame);
        float blend = instance.Blend;

        return [.. at.Select((corner, index) =>
        {
            PropVertex next = index < ahead.Count ? ahead[index] : corner;
            return (corner.X + ((next.X - corner.X) * blend), corner.Y + ((next.Y - corner.Y) * blend), corner.Z + ((next.Z - corner.Z) * blend));
        })];
    }

    /// <summary>What the skinned draw puts at each corner: the bind pose moved by the bones, as `SkinPosition` moves it.</summary>
    private static IReadOnlyList<(float X, float Y, float Z)> Boned(string model, PropModels.ModelFrames frames, ScenePose pose)
    {
        ModelInstance instance = Pose(model, frames, pose);
        IReadOnlyList<float[]> bones = instance.Bones ?? throw new InvalidOperationException($"{model} posed with no bones");

        return [.. frames.Geometry[0].Select(corner => Skin(corner, bones))];
    }

    private static (float X, float Y, float Z) Skin(PropVertex corner, IReadOnlyList<float[]> bones)
    {
        (float w0, float w1, float w2) = corner.Weights;
        (int b0, int b1, int b2) = corner.Bones;
        float total = w0 + w1 + w2;

        if (bones.Count < 1 || total <= 0f)
        {
            return (corner.X, corner.Y, corner.Z);
        }

        float x = 0f, y = 0f, z = 0f;

        foreach ((int bone, float weight) in new[] { (b0, w0), (b1, w1), (b2, w2) })
        {
            if (weight <= 0f || bone < 0 || bone >= bones.Count)
            {
                continue;
            }

            float[] m = bones[bone];
            float share = weight / total;
            x += share * ((m[0] * corner.X) + (m[1] * corner.Y) + (m[2] * corner.Z) + m[3]);
            y += share * ((m[4] * corner.X) + (m[5] * corner.Y) + (m[6] * corner.Z) + m[7]);
            z += share * ((m[8] * corner.X) + (m[9] * corner.Y) + (m[10] * corner.Z) + m[11]);
        }

        return (x, y, z);
    }

    private static ModelInstance Pose(string model, PropModels.ModelFrames frames, ScenePose pose)
    {
        SceneProp[] props = [new(1, model, SceneModelKind.Studio, pose, null)];
        EntityModelSet set = new();
        List<ModelInstance> instances = [];

        set.Add(props, _ => frames);
        set.Instances(props, instances);

        return instances.Count == 1 ? instances[0] : throw new InvalidOperationException($"{model} drew {instances.Count} instances");
    }
}
