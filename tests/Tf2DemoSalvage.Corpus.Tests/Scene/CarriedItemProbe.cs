using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Whether a demo says what a player is carrying, and where it hangs.
/// </summary>
/// <remarks>
/// A probe, not a test. The viewer draws player models and nothing they wear or hold, and weapon
/// models are already being loaded — a dropped one lies on the ground correctly. So the entities
/// exist; what is missing is knowing that a carried one hangs off a player rather than standing at
/// its own origin.
///
/// Source networks that as <c>m_hMoveParent</c> and <c>m_iParentAttachment</c>
/// (<c>server/baseentity.cpp:287</c>) — and the wire name for the first is <c>moveparent</c>, not
/// the member name, because it is declared with <c>SENDINFO_NAME</c>. Looking for the member name
/// finds nothing and reads as "demos do not carry parenting", which they plainly must.
/// </remarks>
public sealed class CarriedItemProbe
{
    [Test]
    public void WhatDoesADemoSayAboutCarriedItems()
    {
        // **The modern demos, not the era ones.** A 2007 recording carries a handful of weapon
        // models and no cosmetics at all, because cosmetics did not exist yet — so measuring the
        // oldest files first answers a question about 2007 rather than about what a viewer has to
        // draw today.
        string[] wanted =
        [
            .. Corpus.FilesWithSchema()
                .Where(path => Path.GetFileName(path).Contains("cp_process", StringComparison.Ordinal))
                .Take(1),
        ];

        foreach (string path in wanted.Length > 0 ? wanted : [.. Corpus.FilesWithSchema().Take(2)])
        {
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

            Dictionary<string, int> byModel = [];

            foreach (ScenePropTrack track in timeline.Props)
            {
                string name = track.ModelPath;

                byModel[name] = byModel.TryGetValue(name, out int seen) ? seen + 1 : 1;
            }

            string[] weapons =
            [
                .. byModel
                    .Where(entry => entry.Key.Contains("weapons/", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(entry => entry.Value)
                    .Take(6)
                    .Select(entry => $"{entry.Value}x {Path.GetFileName(entry.Key)}"),
            ];

            string[] worn =
            [
                .. byModel
                    .Where(entry =>
                        entry.Key.Contains("player/items/", StringComparison.OrdinalIgnoreCase) ||
                        entry.Key.Contains("workshop/player/", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(entry => entry.Value)
                    .Take(6)
                    .Select(entry => $"{entry.Value}x {Path.GetFileName(entry.Key)}"),
            ];

            TestContext.Out.WriteLine(
                $"CARRY {Path.GetFileName(path)}: {timeline.Props.Count} prop tracks, " +
                $"{byModel.Count} distinct models");

            TestContext.Out.WriteLine($"CARRY   weapons: {string.Join(", ", weapons)}");
            TestContext.Out.WriteLine($"CARRY   worn:    {string.Join(", ", worn)}");

            // **Absolute or parent-relative — this is the question that decides the work.** A
            // carried weapon is parented to its owner, and a parented entity's origin is an OFFSET
            // from an attachment point rather than a place in the world. If these cluster at the
            // origin they are relative and need the parent resolved; if they are spread across the
            // map they are absolute and can simply be drawn.
            int atOrigin = 0;
            int placed = 0;

            foreach (ScenePropTrack track in timeline.Props)
            {
                if (!track.ModelPath.Contains("weapons/", StringComparison.OrdinalIgnoreCase) ||
                    track.At(track.FirstTick) is not { } pose)
                {
                    continue;
                }

                bool near = MathF.Abs(pose.X) < 1f && MathF.Abs(pose.Y) < 1f && MathF.Abs(pose.Z) < 1f;

                _ = near ? atOrigin++ : placed++;
            }

            TestContext.Out.WriteLine(
                $"CARRY   weapon origins: {atOrigin} at (0,0,0), {placed} somewhere in the map");

            // **How many are actually present at one moment**, which is a different question from
            // how many tracks exist. Twelve players each hold a weapon, so a tick in the middle of
            // a match should offer about twelve — and the viewer was drawing one.
            List<SceneProp> now = [];

            // The exact tick the viewer captures at, so the two measurements are of one thing.
            timeline.PropsAt(20000, now);

            int carried = now.Count(prop =>
                prop.ModelPath.Contains("weapons/", StringComparison.OrdinalIgnoreCase));

            TestContext.Out.WriteLine(
                $"CARRY   at one tick: {now.Count} props, {carried} of them weapons");

            // **Dropped or carried — lifetime tells them apart.** A weapon dropped on death exists
            // for about thirty seconds and vanishes; one carried by a player lives as long as the
            // player does. If these tracks are all short, then what the timeline holds is the
            // litter of the match and not a single thing anybody is holding.
            List<int> lives = [];

            foreach (ScenePropTrack track in timeline.Props)
            {
                if (!track.ModelPath.Contains("weapons/", StringComparison.OrdinalIgnoreCase) ||
                    track.KeyframeCount == 0)
                {
                    continue;
                }

                lives.Add(track.Keyframes[^1].Tick - track.FirstTick);
            }

            lives.Sort();

            TestContext.Out.WriteLine(
                $"CARRY   weapon track lifetimes in ticks: shortest {lives.FirstOrDefault()}, " +
                $"median {(lives.Count > 0 ? lives[lives.Count / 2] : 0)}, " +
                $"longest {lives.LastOrDefault()} (a match tick is 15 ms)");

            // A sample of the models, since the "worn" filters matched nothing and the paths may
            // simply not be the ones guessed at.
            TestContext.Out.WriteLine(
                "CARRY   sample: " + string.Join(
                    ", ",
                    byModel.Keys
                        .Where(name => !name.Contains("weapons/", StringComparison.OrdinalIgnoreCase))
                        .Take(8)));
        }

        Assert.Pass();
    }
}
