using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// Locates the reference demos, shared by every test that needs real files.
/// </summary>
internal static class Corpus
{
    /// <summary>Anything smaller than this is a Git LFS pointer stub, not a demo.</summary>
    public const int SmallestPlausibleDemo = 4096;

    /// <summary>Every usable demo in the corpus, in a stable order.</summary>
    public static IReadOnlyList<string> Files()
    {
        string? directory = Directory();
        if (directory is null)
        {
            return [];
        }

        return
        [
            .. System.IO.Directory
                .EnumerateFiles(directory, "*.dem")
                .Where(p => new FileInfo(p).Length >= SmallestPlausibleDemo)
                .OrderBy(p => p, StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// Walks up from the test binary looking for the corpus, rather than hard-coding a
    /// relative depth that breaks whenever the output path changes.
    /// </summary>
    public static string? Directory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "tools", "corpus", "demos");
            if (System.IO.Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }
}
