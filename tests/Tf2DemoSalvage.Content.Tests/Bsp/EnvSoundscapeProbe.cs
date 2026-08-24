using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The <c>env_soundscape</c> entities a map declares, which is where ambience actually comes from.
/// </summary>
/// <remarks>
/// **The map is the source, and asking the running game for every map does not scale.** The owner,
/// after producing seven `soundscape_dumpclient` captures by hand: *"i really dont want to have to
/// make manual dumps like that for every map, so we need to figure out how to do this right, so we
/// dont have to do that, that means following valve as close as possible... and probably looking at
/// bsps instead of making me manually do it"*.
///
/// Those captures did their job — they verified that this project's reconstruction of the soundscape
/// INDEX matches the client's, 153 entries for 153. That question is settled and needs no more
/// dumps. What remains per map is which soundscape applies WHERE, and the map itself carries that:
/// `env_soundscape` entities with a name, an origin, a radius, and up to eight position targets.
///
/// **A SourceTV recording makes this necessary rather than merely tidier.** `m_audio` is sent only
/// to the client owning the entity, so an STV demo carries the SourceTV camera's soundscape — not
/// the spectated player's. Measured: the STV recording of cp_process has two samples and one index,
/// while the POV recording of the same session has 64 samples across three.
/// </remarks>
[Explicit("Reports a map's env_soundscape entities; run deliberately.")]
public sealed class EnvSoundscapeProbe
{
    [TestCase("cp_process_f12")]
    public void EnvSoundscape_AMapsEntities_AreReported(string map)
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tf2DemoSalvage", "maps", $"{map}.bsp");

        if (!File.Exists(path))
        {
            Assert.Ignore($"{map}.bsp is not cached locally");
            return;
        }

        IReadOnlyList<BspEntity> entities = BspEntities.ReadFrom(File.ReadAllBytes(path));

        List<BspEntity> soundscapes =
        [
            .. entities.Where(entity =>
                entity.ClassName.Equals("env_soundscape", StringComparison.OrdinalIgnoreCase) ||
                entity.ClassName.StartsWith("env_soundscape", StringComparison.OrdinalIgnoreCase)),
        ];

        TestContext.Out.WriteLine(
            $"{map}: {entities.Count.ToString(CultureInfo.InvariantCulture)} entities, " +
            $"{soundscapes.Count.ToString(CultureInfo.InvariantCulture)} env_soundscape");

        // Which classes exactly — env_soundscape, env_soundscape_proxy and
        // env_soundscape_triggerable are three different things in the SDK and only the first
        // carries a soundscape name of its own.
        foreach (IGrouping<string, BspEntity> kind in soundscapes.GroupBy(entity => entity.ClassName))
        {
            TestContext.Out.WriteLine(
                $"  {kind.Key}: {kind.Count().ToString(CultureInfo.InvariantCulture)}");
        }

        foreach (BspEntity entity in soundscapes.Take(12))
        {
            // Every key, because this is the first look and guessing which matter is how a reader
            // ends up missing the one that decides placement.
            TestContext.Out.WriteLine(
                $"    {entity.ClassName}: " +
                string.Join(", ", entity.Values.Select(pair => $"{pair.Key}={pair.Value}")));
        }
    }
}
