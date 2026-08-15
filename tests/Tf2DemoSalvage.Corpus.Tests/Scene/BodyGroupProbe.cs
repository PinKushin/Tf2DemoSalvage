using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Whether a demo carries a body number, and what values it holds.
/// </summary>
/// <remarks>
/// A probe, not a test. Capture points now draw one label each rather than three, but every one is
/// the same "?" — which is body zero, the first alternative of the first part. So either the entity
/// does not network <c>m_nBody</c>, or it does and this project is not finding it, and those need
/// completely different fixes.
///
/// Asking the demo settles it in seconds and needs no desktop.
/// </remarks>
public sealed class BodyGroupProbe
{
    [Test]
    public void WhatBodyNumbersDoesTheDemoCarry()
    {
        string path = Corpus.Demo("cp_process");

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        Dictionary<string, HashSet<int>> bodies = [];

        foreach (ScenePropTrack track in timeline.Props)
        {
            foreach ((int _, ScenePose pose) in track.Keyframes)
            {
                string name = Path.GetFileName(track.ModelPath);

                if (!bodies.TryGetValue(name, out HashSet<int>? seen))
                {
                    seen = [];
                    bodies[name] = seen;
                }

                seen.Add(pose.Body);
            }
        }

        int carrying = bodies.Count(entry => entry.Value.Any(body => body != 0));

        TestContext.Out.WriteLine(
            $"BODY {bodies.Count} models tracked, {carrying} of them ever showing a non-zero body");

        // The models that vary, which are the ones a body number is doing work for.
        foreach ((string name, HashSet<int> seen) in bodies
            .Where(entry => entry.Value.Count > 1 || entry.Value.Any(body => body != 0))
            .OrderByDescending(entry => entry.Value.Count)
            .Take(10))
        {
            TestContext.Out.WriteLine(
                $"BODY   {name}: {string.Join(", ", seen.Order())}");
        }

        // And the hologram specifically, whatever it turns out to say.
        foreach ((string name, HashSet<int> seen) in bodies
            .Where(entry => entry.Key.Contains("hologram", StringComparison.OrdinalIgnoreCase)))
        {
            TestContext.Out.WriteLine(
                $"BODY   hologram {name}: bodies {string.Join(", ", seen.Order())}");
        }

        Assert.Pass();
    }
}
