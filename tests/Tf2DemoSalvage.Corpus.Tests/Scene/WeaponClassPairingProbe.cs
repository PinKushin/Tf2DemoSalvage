using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>How often each weapon is seen in each class's hands.</summary>
/// <remarks>
/// A probe for B106. A scout was logged holding <c>CTFShotgun_Revenge</c>, which is engineer-only,
/// while a medic held a medigun correctly in the same line. A SET of pairs cannot tell a systematic
/// misattribution from a handful of ticks during a class change, so this counts them.
///
/// The prediction if the cause is a stale class: the impossible pairs are rare against the possible
/// ones, because <c>m_iPlayerClass</c> comes from the resource entity while the weapon comes from
/// the player, and the two need not update on the same tick.
/// </remarks>
public sealed class WeaponClassPairingProbe
{
    [Test]
    public void HowOftenIsEachWeaponHeldByEachClass()
    {
        string path = Corpus.Demo("cp_process_f12-2026-08-08-2207");

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        Dictionary<(string Weapon, int Class), int> counts = [];

        foreach (TimelineFrame frame in timeline.Frames)
        {
            foreach (ScenePlayer player in frame.Players)
            {
                if (player.WeaponClass is { } weapon && player.PlayerClass is { } who)
                {
                    (string, int) key = (weapon, who);
                    counts[key] = counts.TryGetValue(key, out int seen) ? seen + 1 : 1;
                }
            }
        }

        counts.ShouldNotBeEmpty("the recording must carry weapons and classes");

        foreach (((string weapon, int who), int seen) in counts
            .Where(pair => pair.Key.Weapon.Contains("Shotgun", StringComparison.Ordinal) ||
                           pair.Key.Weapon.Contains("Medigun", StringComparison.Ordinal) ||
                           pair.Key.Weapon.Contains("Revolver", StringComparison.Ordinal))
            .OrderBy(pair => pair.Key.Weapon, StringComparer.Ordinal)
            .ThenByDescending(pair => pair.Value))
        {
            TestContext.Out.WriteLine(
                $"PAIR {weapon} class {who.ToString(CultureInfo.InvariantCulture)} " +
                $"x{seen.ToString(CultureInfo.InvariantCulture)}");
        }

        int total = counts.Values.Sum();

        TestContext.Out.WriteLine(
            $"PAIR total player-ticks with both {total.ToString(CultureInfo.InvariantCulture)}, " +
            $"distinct pairs {counts.Count.ToString(CultureInfo.InvariantCulture)}");
    }
}
