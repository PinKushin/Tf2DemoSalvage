using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Every feature above the engine names a test, and that test exists (D246): the parity rule cannot see a loss here.</summary>
public sealed partial class AboveValveConformanceTests
{
    /// <summary>The test names the table holds, counted 2026-11-25 once every name in a cell is read (B841 review 8).</summary>
    private const int KnownFeatures = 34;

    /// <summary>A row's third cell, its tests: every one it names is read, not only a lone one (B841 review 8, Fable, Sonnet).</summary>
    [GeneratedRegex(@"^\|[^|\r\n]+\|[^|\r\n]+\|([^|\r\n]+)\|\s*$", RegexOptions.Multiline)]
    private static partial Regex TestColumn();

    /// <summary>One backticked name in a test cell.</summary>
    [GeneratedRegex(@"`(\w+)`")]
    private static partial Regex TestName();

    [Test]
    public void Table_EveryFeatureAboveValve_NamesATestThatExists()
    {
        DirectoryInfo root = new(TestContext.CurrentContext.TestDirectory);

        while (!File.Exists(Path.Combine(root.FullName, "Tf2DemoSalvage.slnx")))
        {
            root = root.Parent ?? throw new InvalidOperationException("no repository root above the test directory");
        }

        string[] named =
        [
            .. TestColumn().Matches(File.ReadAllText(Path.Combine(root.FullName, "docs", "ABOVE-VALVE.md")))
                .SelectMany(row => TestName().Matches(row.Groups[1].Value))
                .Select(name => name.Groups[1].Value),
        ];

        // The control: a table that stopped parsing would otherwise find nothing missing.
        named.Length.ShouldBeGreaterThanOrEqualTo(KnownFeatures);

        string separator = Path.DirectorySeparatorChar.ToString();
        string source = string.Concat(
            Directory.EnumerateFiles(Path.Combine(root.FullName, "tests"), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal) && !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal))
                .Select(File.ReadAllText));

        named.Where(test => !source.Contains($" {test}(", StringComparison.Ordinal)).ShouldBeEmpty();
    }
}
