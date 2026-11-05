using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Every feature above the engine names a test, and that test exists (D246): the parity rule cannot see a loss here.</summary>
public sealed partial class AboveValveConformanceTests
{
    private const int KnownFeatures = 9;

    [GeneratedRegex(@"^\|[^|]+\|[^|]+\|\s*`(\w+)`\s*\|\s*$", RegexOptions.Multiline)]
    private static partial Regex TestColumn();

    [Test]
    public void Table_EveryFeatureAboveValve_NamesATestThatExists()
    {
        DirectoryInfo root = new(TestContext.CurrentContext.TestDirectory);

        while (!File.Exists(Path.Combine(root.FullName, "Tf2DemoSalvage.slnx")))
        {
            root = root.Parent ?? throw new InvalidOperationException("no repository root above the test directory");
        }

        string[] named = [.. TestColumn().Matches(File.ReadAllText(Path.Combine(root.FullName, "docs", "ABOVE-VALVE.md"))).Select(match => match.Groups[1].Value)];

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
