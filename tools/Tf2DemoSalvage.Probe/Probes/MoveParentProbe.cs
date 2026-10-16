using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Props hung on a move parent WITHOUT bone merging, and where each composition puts them.
/// </summary>
/// <remarks>
/// **<c>CalcAbsolutePosition</c>'s third branch** (<c>c_baseentity.cpp:4393</c>): world =
/// <c>GetParentToWorldTransform</c> × local, where the parent's transform is its own
/// <c>EntityToWorldTransform</c> — itself composed up the chain — or the named attachment when
/// <c>m_iParentAttachment</c> is set (<c>:4330</c>). <c>DemoTimeline.PropsAt</c> answers the
/// wire's LOCAL pose; composing it is Scene's job.
///
/// Per class: how many, how many have a parent that is itself parented (depth ≥ 2, where a
/// one-level composition is wrong), how many name an attachment, and the largest gap between
/// <see cref="ParentChain.Absolute(SceneProp, Func{int, SceneProp?})"/> and the one-level composition
/// <c>EntityModels</c> applies to a baked or brush child.
///
/// **Control:** the count of parented props with a local offset — an instrument that found no
/// parented props at all on a payload map would be broken, not the map.
/// </remarks>
public sealed class MoveParentProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "move-parent";

    /// <inheritdoc/>
    public string Summary => "non-bone-merged move children, depth, attachment, one-level vs chain: move-parent <demo> [samples]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine(Summary);
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);
        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        int samples = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : 20;
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        // class -> (props seen, deep, attachment, nonzero local, worst one-level error)
        Dictionary<string, (int Seen, int Deep, int Attached, int Offset, float Worst)> rows = [];
        List<SceneProp> props = [];

        for (int i = 0; i < samples; i++)
        {
            double tick = timeline.FirstTick + ((timeline.LastTick - timeline.FirstTick) * (i + 0.5) / samples);
            timeline.PropsAt(tick, props);
            Dictionary<int, SceneProp> byEntity = props.ToDictionary(prop => prop.EntityIndex);

            foreach (SceneProp prop in props)
            {
                if (prop is not { AttachedTo: { } parentIndex, BoneMerged: false })
                {
                    continue;
                }

                if (!byEntity.TryGetValue(parentIndex, out SceneProp? parent))
                {
                    string orphan = $"{prop.ClassName} {prop.ModelPath} on ABSENT {parentIndex}";
                    rows[orphan] = rows.GetValueOrDefault(orphan) with { Seen = rows.GetValueOrDefault(orphan).Seen + 1 };
                    continue;
                }

                ScenePose chain = ParentChain.Absolute(prop, entity => byEntity.GetValueOrDefault(entity));
                (float x, float y, float z) = new PropTransform(
                        parent.Pose.X, parent.Pose.Y, parent.Pose.Z,
                        parent.Pose.Pitch, parent.Pose.Yaw, parent.Pose.Roll, parent.Pose.Scale)
                    .Concat(new PropTransform(
                        prop.Pose.X, prop.Pose.Y, prop.Pose.Z,
                        prop.Pose.Pitch, prop.Pose.Yaw, prop.Pose.Roll, prop.Pose.Scale))
                    .Apply(0f, 0f, 0f);
                float error = MathF.Sqrt(((chain.X - x) * (chain.X - x)) + ((chain.Y - y) * (chain.Y - y)) + ((chain.Z - z) * (chain.Z - z)));

                string key = $"{prop.ClassName} on {parent.ClassName}";
                (int seen, int deep, int attached, int offset, float worst) = rows.GetValueOrDefault(key);
                rows[key] = (
                    seen + 1,
                    deep + (parent.AttachedTo is not null ? 1 : 0),
                    attached + (prop.AttachmentPoint is > 0 ? 1 : 0),
                    // Local read as world would misplace it by more than a unit.
                    offset + (MathF.Abs(chain.X - prop.Pose.X) + MathF.Abs(chain.Y - prop.Pose.Y) + MathF.Abs(chain.Z - prop.Pose.Z) > 1f ? 1 : 0),
                    MathF.Max(worst, error));
            }
        }

        output.WriteLine($"{Path.GetFileName(path)}: {samples} samples; seen deep attached local-is-not-world worst-one-level-error");
        foreach ((string key, (int Seen, int Deep, int Attached, int Offset, float Worst) row) in rows.OrderByDescending(pair => pair.Value.Seen))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {row.Seen,6} {row.Deep,5} {row.Attached,5} {row.Offset,6} {row.Worst,9:0.0}  {key}"));
        }
    }
}
