using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// What <c>cp_fulgur</c> itself says about its spawn-room gates and cabinets — a MEASUREMENT.
/// </summary>
/// <remarks>
/// **The map is the ground truth the demo cannot supply.** A recording says where an entity ended
/// up; only the map says what the author asked for — which prop is parented to which brush, at what
/// local offset, and with what angles. Every position claim this investigation has made so far was
/// checked against another reading of our own decode, which cannot falsify a wrong premise.
///
/// The owner's report is specific: *"i am in blue spwan"*, and the gates and the resupply cabinet do
/// not draw there. So this reports the BLU spawn's contents by name, with the `parentname` links
/// spelled out, and leaves the conclusion to the reader.
///
/// **`origin` in the entity lump is a WORLD position for an unparented entity and a LOCAL offset for
/// a parented one** — Hammer writes the offset the author dragged it to, and
/// `C_BaseEntity::CalcAbsolutePosition` composes it at runtime. A prop whose `origin` is a plausible
/// world position AND which names a `parentname` is the shape that produced
/// `parent (2246 2384 59) + local (3440 -2096 240) = (5686 288 299)` in the viewer's log, so the
/// pairing is what this prints.
///
/// Reports numbers, asserts only that the walk ran (D38).
/// </remarks>
[Explicit("Diagnostic: reports cp_fulgur's spawn-room props and their parents.")]
public sealed class SpawnRoomEntityProbe
{
    /// <summary>The map the owner's recording is on.</summary>
    private const string Map = "cp_fulgur.bsp";

    /// <summary>Models whose absence the owner reported.</summary>
    private static readonly string[] Wanted =
    [
        "door_grate003",
        "door_slide_large_door",
        "windowed_door",
        "resupply_locker",
    ];

    [Test]
    public void Entities_TheSpawnRoomProps_ReportTheirParents()
    {
        string path = Locate();

        if (path.Length == 0)
        {
            Assert.Ignore($"{Map} not installed");
            return;
        }

        IReadOnlyList<BspEntity> entities = BspEntities.ReadFrom(File.ReadAllBytes(path));

        TestContext.Out.WriteLine($"{entities.Count} entities in {Map}");

        // Everything addressable by name, so a `parentname` can be resolved to a real entity rather
        // than reported as a bare string.
        Dictionary<string, BspEntity> byName = new(StringComparer.OrdinalIgnoreCase);

        foreach (BspEntity entity in entities)
        {
            // `Value` rather than `TryGetValue`, which hands a NULL back through a non-nullable
            // `out string` when the key is absent — measured here as an NRE on `parent.Length`.
            string name = Value(entity, "targetname");

            if (name.Length != 0 && !byName.ContainsKey(name))
            {
                byName[name] = entity;
            }
        }

        foreach (BspEntity entity in entities)
        {
            if (!entity.TryGetValue("model", out string model) ||
                !Wanted.Any(want => model.Contains(want, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string own = Value(entity, "targetname");
            string resolved = Describe(Value(entity, "parentname"), byName);

            TestContext.Out.WriteLine(
                $"PROP {entity.ClassName} {model}"
                + $"\n       name    ({own})"
                + $"\n       origin  ({Value(entity, "origin")})"
                + $"\n       angles  ({Value(entity, "angles")})"
                + $"\n       parent  {resolved}");
        }

        // **The spawn points, so 'which spawn is BLU' is measured rather than guessed.** The gate
        // positions on their own say nothing about which team's room they belong to, and every
        // position in this investigation so far has been read against an assumed layout.
        foreach (BspEntity entity in entities.Where(
            entity => entity.ClassName.Contains("teamspawn", StringComparison.OrdinalIgnoreCase)))
        {
            TestContext.Out.WriteLine(
                $"SPAWN team {Value(entity, "TeamNum")} at ({Value(entity, "origin")})");
        }

        entities.Count.ShouldBeGreaterThan(0);
    }

    /// <summary>What a <c>parentname</c> points at, spelled out.</summary>
    /// <param name="parent">The <c>parentname</c> value, empty when the entity has none.</param>
    /// <param name="byName">Every entity addressable by <c>targetname</c>.</param>
    /// <returns>A description naming the parent's class, model and placement.</returns>
    private static string Describe(string parent, Dictionary<string, BspEntity> byName)
    {
        if (parent.Length == 0)
        {
            return "[unparented]";
        }

        // A name that resolves to nothing is a distinct answer from having no name at all, and the
        // two would otherwise both read as "no parent".
        return byName.TryGetValue(parent, out BspEntity? found)
            ? $"{parent} = {found.ClassName} model {Value(found, "model")} "
                + $"origin ({Value(found, "origin")}) angles ({Value(found, "angles")})"
            : $"{parent} = NAMES NOTHING";
    }

    /// <summary>A key's value, or a placeholder, so a missing key prints rather than throwing.</summary>
    private static string Value(BspEntity entity, string key) =>
        entity.TryGetValue(key, out string value) ? value : string.Empty;

    /// <summary>The map, wherever it is installed.</summary>
    /// <remarks>
    /// The viewer's own cache first, since that is what a corpus run reads, then the game folder —
    /// which the owner supplies and CI does not have (D-"the game folder is the user's to provide").
    /// </remarks>
    private static string Locate()
    {
        string[] candidates =
        [
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tf2DemoSalvage", "maps", Map),
            Path.Combine(
                "F:", "SteamLibrary", "steamapps", "common", "Team Fortress 2", "tf", "maps", Map),
        ];

        return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
    }
}
