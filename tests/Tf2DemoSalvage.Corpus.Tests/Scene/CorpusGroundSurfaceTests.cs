using System.Globalization;
using System.IO;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The footstep surface on real bytes: <see cref="MapLevel.GroundTrace"/>, the trace the step sounds take their surface from,
/// for players the server says are on the ground.
/// </summary>
/// <remarks>
/// **The control is a point trace from a unit above the feet to a unit below**: what is directly underfoot. A player the
/// server holds on the ground stands `DIST_EPSILON` above the floor, or a rounding nearer, so the hull-wide probe starts
/// inside the epsilon of the floor's face; a clip that refuses that entry runs the probe its whole 64 units and finds nothing
/// or something further down (B604). One real match (lcor, a 2017 SourceTV recording, so every player and not one) on
/// today's cp_badlands — *interpolated:* the 2017 build's map may differ, which can only add disagreements. An era specimen
/// would not do: it is a solo recording.
/// </remarks>
public sealed class CorpusGroundSurfaceTests
{
    [Test]
    public void GroundTrace_PlayersOnTheGround_FindTheFloorTheyStandOn()
    {
        string game = SdkReference.GameInstall.Require();
        byte[] map = File.ReadAllBytes(Path.Combine(game, "maps", "cp_badlands.bsp"));
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(Corpus.Demo("20171113_2240_cp_badlands_red_blu")));
        MapLevel level = MapLevel.Read(map, NullLogger.Instance);
        GameContent content = GameContent.Open(game, NullLoggerFactory.Instance);
        ImpactDecals decals = ImpactDecals.Load(map, content.Archives, content.Surfaces);

        int sampled = 0;
        int missed = 0;
        int startSolid = 0;
        int struck = 0;
        int agreed = 0;

        for (int tick = timeline.FirstTick; tick < timeline.LastTick; tick += 33)
        {
            foreach (ScenePlayer player in timeline.PlayersAt(tick))
            {
                if (!player.IsAlive || ((player.Flags ?? 0) & 1) == 0)
                {
                    continue;
                }

                BspTrace under = level.Trace((player.X, player.Y, player.Z + 1f), (player.X, player.Y, player.Z - 1f), 0f);

                if (under.Fraction >= 1f)
                {
                    continue;
                }

                sampled++;

                BspTrace ground = level.GroundTrace((player.X, player.Y, player.Z), []);

                if (ground.Fraction >= 1f)
                {
                    missed++;
                }
                else if (ground.StartSolid)
                {
                    startSolid++;
                }
                else
                {
                    struck++;
                    agreed += decals.SurfacePropOfTrace(ground) == decals.SurfacePropOfTrace(under) ? 1 : 0;
                }
            }
        }

        string report = string.Create(
            CultureInfo.InvariantCulture,
            $"{sampled} grounded players over a floor: the probe missed {missed}, started solid for {startSolid}, and struck a " +
            $"surface for {struck}, the one directly underfoot for {agreed}");

        TestContext.Out.WriteLine(report);

        // The control: grounded players over a floor were found, or nothing below was asserted.
        sampled.ShouldBeGreaterThan(500, report);
        // Measured 2026-10-26 (Source's clip): 1 miss of 3,993, a player 0.39 above terrain under his centre, which the
        // displacement sweep does not stop — not the brush clip. The clip of before B604, which refused an entry inside the
        // epsilon, missed 5 (sabotaged to that rule, the same run otherwise). Started solid, 3,320, is the same under both:
        // a SourceTV origin rests EXACTLY on the floor plane (z = 296.0), `d1 = 0` is not outside, and the box starts in.
        missed.ShouldBeLessThanOrEqualTo(1, report);
        // A box 48 wide can rest on a ledge the centre is past, so the surfaces need not all agree; nine in ten must.
        agreed.ShouldBeGreaterThan(struck * 9 / 10, report);
    }
}
