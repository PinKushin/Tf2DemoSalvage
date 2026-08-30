using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// What <c>cp_fulgur</c> places as STATIC props around its setup gates — a MEASUREMENT.
/// </summary>
/// <remarks>
/// **The entity lump does not hold the whole answer, and assuming it did nearly closed this off.**
/// `SpawnRoomEntityProbe` established that each setup gate is one `door_grate003` pair on a
/// `func_door`, and that no entity anywhere on the map names a fence model. The map nonetheless
/// PACKS `models/props_gameplay/security_fence80_gate.mdl` and `security_fence_smallgate.mdl` in its
/// pakfile — 4,439 files, 32 MB of custom content — and the viewer loads the second and never the
/// first.
///
/// A model that is packed, never named by an entity, and never loaded is either dead weight in the
/// pakfile or a static prop this project is not placing. Those are opposite conclusions and only
/// the game lump separates them.
///
/// Reports numbers, asserts only that the walk ran (D38).
/// </remarks>
[Explicit("Diagnostic: reports static props around cp_fulgur's setup gates.")]
public sealed class SetupGateStaticPropProbe
{
    private const string Map = "cp_fulgur.bsp";

    /// <summary>The three setup gates, from the map's own <c>func_door</c> origins.</summary>
    private static readonly (float X, float Y)[] Gates =
    [
        (5416f, -2168f),
        (5568f, -2552f),
        (5720f, -3248f),
    ];

    /// <summary>How far from a gate still counts as part of it.</summary>
    private const float Radius = 240f;

    [Test]
    public void StaticProps_AroundTheSetupGates_AreReported()
    {
        string path = Locate();

        if (path.Length == 0)
        {
            Assert.Ignore($"{Map} not installed");
            return;
        }

        IReadOnlyList<BspStaticProp> props = BspStaticProps.Read(File.ReadAllBytes(path));

        TestContext.Out.WriteLine($"{props.Count} static props in {Map}");

        foreach (BspStaticProp prop in props.Where(Near).OrderBy(prop => prop.Model, StringComparer.Ordinal))
        {
            TestContext.Out.WriteLine(
                $"NEAR ({prop.X:0} {prop.Y:0} {prop.Z:0}) yaw {prop.Yaw:0} skin {prop.Skin} {prop.Model}");
        }

        // **Every fence placement anywhere on the map, not just by the gates.** If the packed gate
        // model is placed at all, this finds it wherever it is; if it is placed nowhere, that is a
        // fact about the pakfile rather than about our reader.
        foreach (BspStaticProp prop in props.Where(
            prop => prop.Model.Contains("fence", StringComparison.OrdinalIgnoreCase))
            .OrderBy(prop => prop.Model, StringComparer.Ordinal))
        {
            TestContext.Out.WriteLine(
                $"FENCE ({prop.X:0} {prop.Y:0} {prop.Z:0}) yaw {prop.Yaw:0} skin {prop.Skin} {prop.Model}");
        }

        props.Count.ShouldBeGreaterThan(0, "the map yielded no static props at all");
    }

    private static bool Near(BspStaticProp prop) =>
        Gates.Any(gate =>
            Math.Abs(prop.X - gate.X) < Radius && Math.Abs(prop.Y - gate.Y) < Radius);

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
