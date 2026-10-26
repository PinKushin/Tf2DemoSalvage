using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// **A rewind lands in a fresh read's state, whatever the file's shape** (batch-33 review 2): `SkipToTick` reloads, so after
/// any rewind the parse-ahead list, `m_bInterpolateView` and the view must be exactly a fresh player's.
/// </summary>
/// <remarks>
/// Randomised over seeded demos rather than enumerated, because the shapes that broke the bound — a gap, a backward run, a
/// backward run after a synctick, signons with no synctick — were each found one review at a time. Half the demos are the
/// shape the bound is for (signons, a synctick, packets whose ticks never step back, gaps and equal ticks included); the
/// other half add mid-file signons and signon blocks, syncticks, stops and backward steps. Every target from before the
/// first packet to past the last is a landing, so a target before the first packet, on a step, and past the end are all in.
/// </remarks>
public sealed class DemoPlayerRewindPropertyTests
{
    private const int Demos = 600;

    [Test]
    public void InterpolateViewpoint_RewoundToEveryTargetOfRandomDemos_IsAFreshPlayersStateAndView()
    {
        int simple = 0;
        int bounded = 0;
        long compared = 0;

        for (int seed = 0; seed < Demos; seed++)
        {
            Random random = new(seed);
            bool tame = seed % 2 == 0;
            List<DemoCommand> commands = RandomCommands(random, tame);
            byte[] demo = SyntheticViews.Demo(seed % 5 == 0 ? (byte)1 : (byte)24, commands);
            DemoTimeline timeline = DemoTimeline.Build(demo);
            DemoPlayer played = new(timeline);

            // An untame draw can still come out simple; what is asserted below holds on either path.
            simple += played.IsSimple ? 1 : 0;

            // The packets' span, past the ServerInfo packet `SyntheticViews.Demo` puts at index 0, tick 0.
            // A mid-file dem_stop ends the demo where it stands, so an untame one can hold no packet of ours at all.
            int[] ticks = [.. timeline.ViewCommands.Skip(1).Where(command => command.Type == DemoCommandType.Packet).Select(command => command.Tick)];

            if (ticks.Length == 0)
            {
                continue;
            }

            int first = ticks.Min();
            int last = ticks.Max();
            int commandsRead = timeline.ViewCommands.Count;

            for (int target = first - 3; target <= last + 3; target++)
            {
                played.InterpolateViewpoint(last + 2.5d);
                long before = played.CommandsRead;
                DemoPlayer fresh = new(timeline);

                foreach (double tick in new[] { target, target + 0.5d, target + 1.25d })
                {
                    RecordedView? expected = fresh.InterpolateViewpoint(tick);
                    string at = $"seed {seed}, target {target}, tick {tick}";

                    played.InterpolateViewpoint(tick).ShouldBe(expected, at);
                    played.QueuedPackets.ShouldBe(fresh.QueuedPackets, at);
                    played.InterpolatesView.ShouldBe(fresh.InterpolatesView, at);
                    compared++;
                }

                bounded += played.CommandsRead - before < commandsRead / 2 ? 1 : 0;
            }

            // **Before every command**, from mid-file where the parse-ahead found its interval: the rewind reads nothing at
            // all, so only Restart itself can clear `m_bInterpolateView` to a fresh player's false.
            played.InterpolateViewpoint((first + last) / 2d);
            played.InterpolateViewpoint(-1d).ShouldBe(new DemoPlayer(timeline).InterpolateViewpoint(-1d), $"seed {seed}, before the file");
            played.InterpolatesView.ShouldBeFalse($"seed {seed}: a fresh player has read nothing");
            played.QueuedPackets.ShouldBeEmpty($"seed {seed}");
        }

        compared.ShouldBeGreaterThan(Demos * 100, "the control: every demo was landed on many times");
        simple.ShouldBeGreaterThan(Demos / 4, "the control: the bound's own shape is generated");
        bounded.ShouldBeGreaterThan(1000, "the control: the bounded path was taken, not only the full re-read");
    }

    /// <summary>A demo's commands after the schema: a signon block, then packets, with or without the untame shapes.</summary>
    private static List<DemoCommand> RandomCommands(Random random, bool tame)
    {
        List<DemoCommand> commands = [];

        // The opening a recording has: signons, then a synctick — except, untame, sometimes not. Their ticks are the
        // server's: past every packet on a modern match (146,124 on etf2l-12025), inside the packets' span on the era
        // specimens (158 and 6,041 on the 2007 and 2013 ones).
        int signons = random.Next(0, 4);

        for (int at = 0; at < signons; at++)
        {
            commands.Add(SyntheticViews.Signon(random.Next(3) == 0 ? 146124 : random.Next(80, 400)));
        }

        if (tame || random.Next(3) > 0)
        {
            commands.Add(SyntheticViews.SyncTick(100));
        }

        int tick = 100;
        int count = random.Next(20, 110);

        for (int at = 0; at < count; at++)
        {
            int roll = random.Next(100);

            if (!tame && roll < 4)
            {
                commands.Add(SyntheticViews.Signon(random.Next(2) == 0 ? 146124 : Math.Max(1, tick + random.Next(-50, 50))));
                continue;
            }

            if (!tame && roll < 7)
            {
                commands.Add(SyntheticViews.SyncTick(tick));
                continue;
            }

            if (!tame && roll < 8)
            {
                commands.Add(SyntheticViews.Stop(tick));
                continue;
            }

            int step = random.Next(100) switch
            {
                < 10 => 0,
                < 80 => 1,
                < 88 => 2,
                < 95 => random.Next(9, 80),
                _ => tame ? 1 : -random.Next(1, 60),
            };

            // Never below 1: the container stores ticks unsigned, and tick 0 is the ServerInfo packet's.
            tick = Math.Max(1, tick + step);
            int flags = random.Next(100) < 5 ? SyntheticViews.NoInterpolation : 0;
            commands.Add(SyntheticViews.Packet(tick, ((tick * 4f) + random.Next(3), 0f, 0f), (0f, random.Next(0, 30), 0f), flags: flags));
        }

        if (random.Next(2) == 0)
        {
            commands.Add(SyntheticViews.Stop(tick));
        }

        return commands;
    }
}
