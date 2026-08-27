using System;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Reports the layouts of the studio structures the bone pipeline needs and the reader does not yet
/// have.
/// </summary>
/// <remarks>
/// **A probe rather than a test, because there is nothing to check against yet.** D88 commits this
/// project to matching Valve's bone pipeline, and B182's denominator found the stages that are
/// missing — bone controllers, IK, procedural and jiggle bones, local hierarchy. Every one of them
/// is blocked on the same thing: the <c>.mdl</c> reader does not read the structures that carry
/// their data.
///
/// So this measures the layouts first, and <c>StudioLayout</c>'s constants are written FROM its
/// output. Hand-counting <c>mstudiojigglebone_t</c> is forty floats of arithmetic with no error
/// signal, and this project's standing observation is that a wrong offset never throws — it lands
/// on real data and decodes something plausible.
///
/// <c>[Explicit]</c>, like every probe here: it asserts nothing about this project and would be
/// noise in a normal run. <c>BonePipelineStructTests</c> is what holds the numbers once they exist.
/// </remarks>
public sealed class BonePipelineStructProbe
{
    /// <summary>Where the engine declares the studio model structures.</summary>
    private const string StudioFile = "src/public/studio.h";

    [Test]
    [Explicit("Reports SDK struct layouts; run it when the bone pipeline needs a new offset.")]
    public void Probe_TheStructsTheBonePipelineNeeds_ReportsTheirLayouts()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string[] wanted =
        [
            "mstudiobone_t",
            "mstudiobonecontroller_t",
            "mstudioikchain_t",
            "mstudioiklink_t",
            "mstudiojigglebone_t",
            "mstudiolocalhierarchy_t",
        ];

        foreach (string name in wanted)
        {
            CLayoutAttempt attempt = Attempt(name);

            if (attempt.Layout is not { } layout)
            {
                TestContext.Out.WriteLine($"{name}: REFUSED at {attempt.Refused}");
                continue;
            }

            TestContext.Out.WriteLine($"=== {name}: {layout.Size} bytes");

            foreach (CMember member in layout.Members)
            {
                TestContext.Out.WriteLine(
                    $"    {member.Offset,4}  {member.Name} " +
                    $"({member.Size} bytes{(member.Elements > 1 ? $", {member.Elements} elements" : string.Empty)})");
            }
        }

        // The header's own pairs, which say where the two new tables live.
        CLayout header = Attempt("studiohdr_t").Layout
            ?? throw new InvalidOperationException("studiohdr_t could not be derived");

        string[] fields =
        [
            "numbonecontrollers", "bonecontrollerindex",
            "numikchains", "ikchainindex",
            "numflexcontrollers", "flexcontrollerindex",
        ];

        TestContext.Out.WriteLine($"=== studiohdr_t: {header.Size} bytes");

        foreach (string field in fields)
        {
            CMember? member =
                header.Members.FirstOrDefault(
                    entry => string.Equals(entry.Name, field, StringComparison.Ordinal));

            TestContext.Out.WriteLine(
                member is null ? $"    {field}: ABSENT" : $"    {member.Offset,4}  {member.Name}");
        }

        // The two constant families the pipeline switches on.
        IReadOnlyDictionary<string, int> constants = SourceSdk.Constants(StudioFile);

        foreach (KeyValuePair<string, int> constant in constants
            .Where(entry =>
                entry.Key.StartsWith("BONE_", StringComparison.Ordinal) ||
                entry.Key.StartsWith("STUDIO_PROC_", StringComparison.Ordinal) ||
                entry.Key.StartsWith("JIGGLE_", StringComparison.Ordinal))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            TestContext.Out.WriteLine($"    {constant.Key} = 0x{constant.Value:X}");
        }
    }

    /// <summary>Derives one structure's layout the way <c>StudioStructTests</c> does.</summary>
    /// <remarks>
    /// The composite sizes and the four-byte pointer are facts about the FILE rather than about
    /// this process: studiomdl is a 32-bit tool and writes the structure it compiled.
    /// </remarks>
    private static CLayoutAttempt Attempt(string name)
    {
        string text = SourceSdk.Text(StudioFile)
            ?? throw new InvalidOperationException($"{StudioFile} is missing from the SDK checkout");

        Dictionary<string, CTypeSize> composites = new(StringComparer.Ordinal)
        {
            ["Vector"] = new(12, 4),
            ["QAngle"] = new(12, 4),
            ["RadianEuler"] = new(12, 4),
            ["Vector2D"] = new(8, 4),
            ["Vector4D"] = new(16, 4),
            ["Quaternion"] = new(16, 4),
            ["Quaternion48"] = new(6, 2),
            ["matrix3x4_t"] = new(48, 4),
        };

        return CStruct.Attempt(text, name, SourceSdk.Constants(StudioFile), composites, pointerBytes: 4);
    }
}
