using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>How much B555 reaches on the install: names the shipped manifest declares more than once (finding 81).</summary>
/// <remarks>
/// A MEASUREMENT, so it reports and asserts nothing about the install (CLAUDE.md, D38). It walks every file the manifest
/// names, in manifest order, and for each file reads the root's <c>particleSystemDefinitions</c> array as the engine does
/// (<c>000e5150</c>). It prints:
/// <list type="bullet">
/// <item>every name declared by more than one file whose attributes DIFFER, which are the names where first-wins and
/// last-wins give different answers;</item>
/// <item>every name declared twice inside one file;</item>
/// <item>every file whose array order is not its element order;</item>
/// <item>every definition with <c>preventNameBasedLookup</c> set, which <c>AddParticleSystem</c> puts in the id list
/// rather than the name map (<c>000dcd90</c>), so it neither replaces nor is replaced.</item>
/// </list>
/// </remarks>
[TestFixture]
[Explicit("A measurement of the install; run by name.")]
public sealed class ParticleDuplicateNamesDiagnostic
{
    private const string Definition = "DmeParticleSystemDefinition";

    [Test]
    public void Report_TheShippedManifest_ListsNamesDeclaredMoreThanOnce()
    {
        GameArchives archives = GameArchives.Open(GameInstall.Require());
        Dictionary<string, List<(string File, string Hash)>> byName = new(StringComparer.OrdinalIgnoreCase);
        List<string> inFile = [];
        List<string> reordered = [];
        List<string> unnamed = [];
        int files = 0;

        foreach (string path in ParticleManifest.Files(archives.Read))
        {
            if (archives.Read(path) is not { Length: > 0 } file)
            {
                continue;
            }

            files++;
            IReadOnlyList<DmxElement> elements = DmxFile.Read(file);

            if (elements.Count == 0 || !elements[0].Attributes.TryGetValue("particleSystemDefinitions", out DmxValue array) ||
                array.Elements is not { } order)
            {
                TestContext.Out.WriteLine($"{path}: root {(elements.Count > 0 ? elements[0].Type : "none")} has no definitions array");
                continue;
            }

            List<int> stored = [.. Enumerable.Range(0, elements.Count).Where(at => elements[at].Type == Definition)];

            if (!order.SequenceEqual(stored))
            {
                // A permutation reorders; a different SET means the element table holds definitions the engine never reads.
                reordered.Add(order.Order().SequenceEqual(stored) ? path : $"{path} (different set: {order.Count} named, {stored.Count} stored)");
            }

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            foreach (DmxElement one in order.Where(at => at >= 0 && at < elements.Count).Select(at => elements[at]).Where(one => one.Type == Definition))
            {
                if (one.Attributes.TryGetValue("preventNameBasedLookup", out DmxValue prevent) && prevent.Number != 0)
                {
                    unnamed.Add($"{path}:{one.Name}");
                    continue;
                }

                if (!seen.Add(one.Name))
                {
                    inFile.Add($"{path}:{one.Name}");
                }

                if (!byName.TryGetValue(one.Name, out List<(string File, string Hash)>? declared))
                {
                    byName[one.Name] = declared = [];
                }

                declared.Add((path, Hash(one, elements)));
            }
        }

        List<string> differing = [.. byName
            .Where(pair => pair.Value.Select(entry => entry.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1 &&
                           pair.Value.Select(entry => entry.Hash).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(pair => $"{pair.Key}: {string.Join(" then ", pair.Value.Select(entry => entry.File))}")
            .Order(StringComparer.Ordinal)];
        // The instrument's control, not a claim about the install: a walk that found no definitions measured nothing.
        byName.Count.ShouldBeGreaterThan(0, "the control: the manifest's files were read");

        int repeated = byName.Count(pair => pair.Value.Select(entry => entry.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);

        TestContext.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{files} files, {byName.Count} names; {repeated} names in more than one file, {differing.Count} of them differing; " +
            $"{inFile.Count} repeated inside one file; {reordered.Count} files whose array is not element order; " +
            $"{unnamed.Count} with preventNameBasedLookup"));

        foreach (string line in differing.Concat(inFile.Select(one => "in-file " + one))
                     .Concat(reordered.Select(one => "reordered " + one)).Concat(unnamed.Select(one => "unnamed " + one)))
        {
            TestContext.Out.WriteLine(line);
        }
    }

    /// <summary>A definition's attributes and its functions' attributes, flattened to one string.</summary>
    private static string Hash(DmxElement definition, IReadOnlyList<DmxElement> elements)
    {
        IEnumerable<string> Flat(DmxElement element) => element.Attributes
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => pair.Value.Elements is { } children
                ? [pair.Key, .. children.Where(at => at >= 0 && at < elements.Count).SelectMany(at =>
                    elements[at].Type == Definition ? [elements[at].Name] : Flat(elements[at]).Prepend(elements[at].Name))]
                : new[] { $"{pair.Key}={pair.Value.Number.ToString("R", CultureInfo.InvariantCulture)}|{pair.Value.Text}|{pair.Value.Vector}|{string.Join(',', pair.Value.Numbers ?? [])}" });

        return string.Join(";", Flat(definition));
    }
}
