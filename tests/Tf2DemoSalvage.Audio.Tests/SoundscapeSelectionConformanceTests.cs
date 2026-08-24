using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// Choosing a soundscape from the map, checked against what a running client chose.
/// </summary>
/// <remarks>
/// **These positions are captures, not inventions.** The owner walked cp_process in TF2 running
/// `soundscape_dumpclient` and reported each position with the index the client had picked. That
/// makes this a differential: the engine's answer is the expectation, and this implementation can
/// disagree with it — which a test written from the same SDK pages as the code could not.
///
/// It is also what those captures were FOR. They verified the catalog's ordering first (153 for
/// 153); now they verify the selection. The owner's constraint was explicit — *"i really dont want
/// to have to make manual dumps like that for every map"* — so having captured one map's answers,
/// the job is to reproduce them from the BSP and never need another.
///
/// **All seven are blue-side**, and the owner noted cp_process is mirror-symmetrical, so the red
/// half is implied rather than measured. Only the measured ones are asserted: an implied
/// expectation is a guess wearing a citation.
/// </remarks>
public sealed class SoundscapeSelectionConformanceTests
{
    private const string Game = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

    /// <summary>What the live client answered at each place the owner stood.</summary>
    /// <remarks>
    /// Entity indices are the client's own numbering and are not compared — they are network slots,
    /// not map entities. The position and the resulting soundscape are what transfer.
    /// </remarks>
    private static IEnumerable<TestCaseData> Captures()
    {
        yield return new TestCaseData(-4816f, -1280f, 576.03f, "tf2.respawn_room")
            .SetName("Captured_BlueSpawn_IsTheRespawnRoom");
        yield return new TestCaseData(-3469.22f, -2034.26f, 576.03f, "Gorge.Inside")
            .SetName("Captured_JustOutsideBlueSpawn_IsInside");
        yield return new TestCaseData(-2496.74f, -2194.83f, 704.03f, "Gorge.Inside")
            .SetName("Captured_BlueSideBuilding_IsInside");
        yield return new TestCaseData(-1272.55f, -1953.14f, 479.11f, "Gorge.Outside")
            .SetName("Captured_TowardsMid_IsOutside");
        yield return new TestCaseData(-607.12f, -21.91f, 576.03f, "Gorge.Outside")
            .SetName("Captured_Mid_IsOutside");
        yield return new TestCaseData(-1422.15f, -316.26f, 544.03f, "Gorge.Outside")
            .SetName("Captured_MidLower_IsOutside");
        yield return new TestCaseData(85.96f, 1866.73f, 624.03f, "Gorge.Inside")
            .SetName("Captured_FarSide_IsInside");
    }

    private static (SoundscapePlacements Placements, BspLeafTree Leaves)? Map()
    {
        string bsp = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

        if (!Directory.Exists(Game) || !File.Exists(bsp))
        {
            Assert.Ignore("the map or the game is not installed");
            return null;
        }

        byte[] file = File.ReadAllBytes(bsp);

        return (
            SoundscapePlacements.From(
                BspEntities.ReadFrom(file),
                SoundscapeCatalog.Load(GameArchives.Open(Game).Read)),
            BspLeafTree.Read(file));
    }

    [TestCaseSource(nameof(Captures))]
    public void Choose_WhereTheOwnerStood_PicksWhatTheClientPicked(
        float x, float y, float z, string expected)
    {
        if (Map() is not { } map)
        {
            return;
        }

        SoundscapePlacement? chosen = map.Placements.Choose(
            x, y, z,
            (from, to) => map.Leaves.IsClear(from.X, from.Y, from.Z, to.X, to.Y, to.Z));

        TestContext.Out.WriteLine(
            $"({x.ToString("0.#", CultureInfo.InvariantCulture)}, " +
            $"{y.ToString("0.#", CultureInfo.InvariantCulture)}, " +
            $"{z.ToString("0.#", CultureInfo.InvariantCulture)}) -> " +
            $"{chosen?.Name ?? "none"} (client said {expected})");

        chosen.ShouldNotBeNull("the client found a soundscape here, so this must too");
        chosen.Value.Name.ShouldBe(expected);
    }

    [Test]
    public void From_TheMap_ResolvesEveryEntityIncludingProxies()
    {
        if (Map() is not { } map)
        {
            return;
        }

        IReadOnlyList<SoundscapePlacement> placements = map.Placements.Placements;

        foreach (IGrouping<string, SoundscapePlacement> group in
                 placements.GroupBy(placement => placement.Name))
        {
            TestContext.Out.WriteLine(
                $"  {group.Key}: {group.Count().ToString(CultureInfo.InvariantCulture)} entities, " +
                $"index {group.First().Index.ToString(CultureInfo.InvariantCulture)}");
        }

        // 4 env_soundscape + 40 env_soundscape_proxy, measured on this map. A proxy that failed to
        // resolve its master would simply be absent, so the count is what catches it.
        placements.Count.ShouldBe(44, "cp_process has 4 env_soundscape and 40 proxies");

        // **Every one resolved to a real index.** -1 means the map named a soundscape the catalog
        // does not hold, which would be either a bad manifest walk or a missing file — and it would
        // otherwise show up as silence in that part of the map rather than as an error.
        placements.ShouldAllBe(
            placement => placement.Index >= 0,
            "a placement with no index names a soundscape the catalog could not find");
    }
}
