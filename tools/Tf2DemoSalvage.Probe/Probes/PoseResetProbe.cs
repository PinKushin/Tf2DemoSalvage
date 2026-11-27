using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What the build-time `OnNewModel` changes on a real demo (B870): the pose parameters drawn with the install's model
/// facts against without, sampled over every prop track that states two or more.
/// </summary>
/// <remarks>
/// **The production path**: `DemoTimeline.Build` with `GameContent.ModelFacts`, the object the viewer hands it, and the
/// pose read back through `ScenePropTrack.At`, what the viewer samples. **The control** is the same build twice with the
/// facts, which must differ nowhere; and the count of models the facts answered, which must be above zero on any demo
/// with an animating prop, or the instrument found no model.
/// <code>
///   pose-reset f12            every 33rd tick
///   pose-reset f12 7          every 7th
/// </code>
/// </remarks>
public sealed class PoseResetProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "pose-reset";

    /// <inheritdoc/>
    public string Summary =>
        "pose parameters drawn with the install's model facts against without, per prop track (B870): pose-reset <demo> [every]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine(Summary);
            return;
        }

        int every = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : 33;

        if (ProbeInstall.Game(output) is not { Archives.IsEmpty: false } game)
        {
            output.WriteLine("The game could not be found.");
            return;
        }

        Counting facts = new(game.ModelFacts);
        byte[] file = File.ReadAllBytes(path);
        ClientInterp interp = ProbeInstall.Interp();

        // Built here rather than through `ProbeInstall.Timeline`, because the facts are the variable — counted, the install's,
        // none; the class scripts and the interp are the viewer's in all three, so only the facts differ (B874).
        DemoTimeline with = DemoTimeline.Build(file, client: interp, classes: game.ClassAnimation, models: facts);
        DemoTimeline again = DemoTimeline.Build(file, client: interp, classes: game.ClassAnimation, models: game.ModelFacts);
        DemoTimeline without = DemoTimeline.Build(file, client: interp, classes: game.ClassAnimation);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"facts asked {facts.Asked}, answered {facts.Answered} ({facts.Models.Count} models with pose parameters: " +
            $"{string.Join(", ", facts.Models.Take(8))})"));

        (int samples, int differ, float worst, string where) = Compare(with, without, every);
        (int controlSamples, int controlDiffer, _, _) = Compare(with, again, every);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"with against without: {differ} of {samples} samples differ, worst {worst:0.####} at {where}"));

        output.WriteLine(controlDiffer == 0 && controlSamples == samples
            ? "CONTROL pass: two builds with the facts draw the same"
            : string.Create(CultureInfo.InvariantCulture, $"CONTROL FAIL: {controlDiffer} of {controlSamples} differ"));
    }

    private static (int Samples, int Differ, float Worst, string Where) Compare(DemoTimeline left, DemoTimeline right, int every)
    {
        int samples = 0;
        int differ = 0;
        float worst = 0f;
        string where = "-";

        foreach ((ScenePropTrack a, ScenePropTrack b) in left.Props.Zip(right.Props))
        {
            for (int tick = 0; tick <= left.LastTick; tick += every)
            {
                if (a.At(tick) is not { PoseParameters.Count: >= 2 } x || b.At(tick) is not { } y)
                {
                    continue;
                }

                samples++;
                float delta = x.PoseParameters.Zip(y.PoseParameters).Select(pair => MathF.Abs(pair.First - pair.Second)).Max();

                if (delta > 0f)
                {
                    differ++;
                }

                if (delta > worst)
                {
                    worst = delta;
                    where = string.Create(CultureInfo.InvariantCulture, $"{a.ModelPath} #{a.EntityIndex} tick {tick}");
                }
            }
        }

        return (samples, differ, worst, where);
    }

    /// <summary>The facts, counted: how often the build asked and how often a model answered.</summary>
    private sealed class Counting(IModelFacts inner) : IModelFacts
    {
        public int Asked { get; private set; }

        public int Answered { get; private set; }

        public SortedSet<string> Models { get; } = new(StringComparer.Ordinal);

        public ModelFacts? FactsOf(string modelPath)
        {
            Asked++;
            ModelFacts? facts = inner.FactsOf(modelPath);

            if (facts is not null)
            {
                Answered++;

                if (facts.PoseParameterLoops.Count > 0)
                {
                    Models.Add(modelPath);
                }
            }

            return facts;
        }
    }
}
