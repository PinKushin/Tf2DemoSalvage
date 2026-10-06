using System;
using System.IO;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A demo from the committed corpus, <c>tools/corpus/demos</c>, found by walking up from the test binary.</summary>
internal static class CommittedDemo
{
    /// <summary>The demo's path, or a skip when it is not checked out.</summary>
    /// <param name="name">The file name, extension included.</param>
    /// <returns>The path.</returns>
    public static string Require(string name) =>
        Skip.Unless(Find(name, "demos"), $"the committed corpus demo {name} is not checked out");

    /// <summary>A demo from the local corpus, <c>tools/corpus/local</c>, or a skip when this machine has none.</summary>
    /// <param name="name">The file name, extension included.</param>
    /// <returns>The path.</returns>
    public static string RequireLocal(string name) =>
        Skip.Unless(Find(name, "local"), $"the local corpus demo {name} is not on this machine");

    private static string? Find(string name, string corpus)
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            string candidate = Path.Combine(folder.FullName, "tools", "corpus", corpus, name);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
