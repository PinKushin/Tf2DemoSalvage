using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// What each corpus map's own <c>env_fog_controller</c> was authored with.
/// </summary>
/// <remarks>
/// **The independent source for the fog a demo networks.** A demo carries <c>m_fog.colorPrimary</c>
/// and the rest as a <c>CFogController</c>'s send-table state; the map carries the same settings as
/// the keyvalues a mapper typed into Hammer. Nothing this project wrote connects the two, so an
/// agreement between them is evidence about the decode rather than evidence that the decoder agrees
/// with itself — which is the whole failure mode a fixture-authored expectation cannot escape.
///
/// **A probe rather than an assertion, and deliberately.** The shipped map is today's version and a
/// 2008 demo recorded an earlier one, so a strict equality check would fail for real reasons on the
/// era specimens and say nothing about the decoder. What the numbers are for is reading, once,
/// against a trace of the same map — which is how the values pinned in
/// <c>Tf2DemoSalvage.Corpus.Tests.Scene.FogDecodeTests</c> were confirmed.
///
/// Measured 2026-08-21 against the installed game:
///
/// <code>
/// cp_granary          fogcolor 225 225 225  start 0     end 14000  maxdensity .8
/// cp_badlands         fogcolor 113 115 142  start 100   end 7000   maxdensity 1
/// koth_viaduct        fogcolor 213 174 221  start 0     end 6500   maxdensity 1
/// cp_foundry          fogcolor 131 121 134  start 1707  end 4634   maxdensity .7
/// koth_harvest_final  fogcolor 232 205 155  start 100   end 11000  maxdensity 1
/// </code>
///
/// **Every map whose demo was checked agrees with what that demo networks**, unpacked through
/// <c>EntityState.Fog</c>: granary's 14803425 is 0xE1E1E1, viaduct's 14528213 is 0xDDAED5 and
/// foundry's 8812931 is 0x867983, each with the map's red in the LOW byte.
///
/// **Viaduct is the specimen that proves the byte order rather than assuming it.** A
/// <c>color32</c> travels as one 32-bit int and reading it the other way round is the plausible
/// mistake; granary is 225 grey and cannot tell the two apart, foundry's 131/134 differ by three,
/// and viaduct's 213/221 with a distinct 174 in the middle can only be read one way.
/// </remarks>
public sealed class MapFogProbe
{
    [Test]
    public void MapFog_AcrossTheCorpusMaps_IsReported()
    {
        string maps = Path.Combine(GameInstall.Require(), "maps");

        if (!Directory.Exists(maps))
        {
            Assert.Ignore("Team Fortress 2 is not installed, so its maps cannot be read.");
            return;
        }

        string[] names =
            ["cp_granary", "cp_badlands", "koth_viaduct", "cp_foundry", "koth_harvest_final"];

        int found = 0;

        foreach (string name in names)
        {
            string path = Path.Combine(maps, name + ".bsp");

            if (!File.Exists(path))
            {
                TestContext.Out.WriteLine($"{name}: not installed");
                continue;
            }

            foreach (BspEntity entity in BspEntities.ReadFrom(File.ReadAllBytes(path)))
            {
                if (!entity.TryGetValue("classname", out string className) ||
                    !className.Contains("fog_controller", StringComparison.Ordinal))
                {
                    continue;
                }

                found++;

                List<string> settings = [];

                foreach (string key in
                    new[] { "fogcolor", "fogstart", "fogend", "fogmaxdensity", "fogenable" })
                {
                    settings.Add(
                        entity.TryGetValue(key, out string value) ? $"{key} {value}" : $"{key} -");
                }

                TestContext.Out.WriteLine($"{name}: {className}, {string.Join(", ", settings)}");
            }
        }

        // The control this probe would be worthless without: no fog controller found at all reads
        // exactly like a map with fog switched off, and one of those is a defect in the reader.
        found.ShouldBeGreaterThan(0, "no corpus map declared an env_fog_controller");
    }
}
