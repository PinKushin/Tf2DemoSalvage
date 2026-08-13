using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// The demos currently open: individual files, and folders treated as playlists.
/// </summary>
/// <remarks>
/// **Opening, not importing.** Nothing is copied and nothing is written — a demo stays where the
/// user keeps it, and this only remembers where to find it. That is why several folders can be
/// open at once and why removing one costs nothing.
///
/// **A folder is a playlist, walked recursively, except through the game's asset folders.**
/// Pointing this at a TF2 install's <c>tf</c> directory and walking everything would trawl
/// gigabytes of materials, models and sound to find the handful of demos the game writes into the
/// top of it.
///
/// Asset folders are matched by NAME at any depth rather than by detecting a game installation,
/// which is the better test in more cases: it works for a copied or archived install too, and
/// TF2's asset folder names have not changed in the game's lifetime. The trade is that a demo
/// genuinely stored in a folder called <c>sound</c> is missed — accepted deliberately, because
/// scanning a full game install is the case people actually have.
/// </remarks>
internal sealed class DemoLibrary
{
    /// <summary>The extension a Source demo carries.</summary>
    public const string DemoExtension = ".dem";

    /// <summary>
    /// Folder names never descended into, matched case-insensitively at any depth.
    /// </summary>
    /// <remarks>
    /// The bulk of a TF2 install. <c>download</c> and <c>custom</c> are here because they hold
    /// the same kinds of asset fetched from servers, and <c>replay</c> holds replay blocks rather
    /// than demos.
    /// </remarks>
    private static readonly HashSet<string> AssetFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "materials", "models", "sound", "maps", "media", "particles", "resource",
        "scripts", "cfg", "expressions", "shaders", "bin", "addons", "custom",
        "download", "replay",
    };

    private readonly List<string> _roots = [];
    private readonly List<DemoEntry> _entries = [];

    /// <summary>The files and folders that have been opened, in the order they were opened.</summary>
    public IReadOnlyList<string> Roots => _roots;

    /// <summary>Every demo found, ordered by folder then name.</summary>
    public IReadOnlyList<DemoEntry> Entries => _entries;

    /// <summary>Opens a demo file, or a folder of demos.</summary>
    /// <param name="path">A <c>.dem</c> file, or a directory to walk.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or blank.</exception>
    /// <remarks>
    /// A path that does not exist adds nothing rather than throwing. A folder can vanish between
    /// being chosen and being read — a network share, an unplugged drive — and the viewer should
    /// report an empty playlist rather than fall over.
    /// </remarks>
    public void Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string full = Path.GetFullPath(path);

        if (_roots.Contains(full, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        if (File.Exists(full))
        {
            _roots.Add(full);
            Add(full);
            Sort();
            return;
        }

        if (!Directory.Exists(full))
        {
            return;
        }

        _roots.Add(full);

        foreach (string file in Walk(full))
        {
            Add(file);
        }

        Sort();
    }

    /// <summary>Forgets a previously opened file or folder and everything it contributed.</summary>
    /// <param name="root">The path as passed to <see cref="Open"/>.</param>
    /// <returns>Whether anything was removed.</returns>
    public bool Close(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        string full = Path.GetFullPath(root);
        if (_roots.RemoveAll(r => string.Equals(r, full, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return false;
        }

        _entries.RemoveAll(e => string.Equals(e.Root, full, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    /// <summary>Walks a directory for demos, skipping the game's asset folders.</summary>
    /// <remarks>
    /// Hand-rolled rather than <c>EnumerateFiles</c> with <c>AllDirectories</c>, because that
    /// offers no way to prune a subtree — it would descend into every asset folder and then throw
    /// the results away, which is the cost this exists to avoid.
    ///
    /// Unreadable directories are skipped rather than fatal: a permission-denied folder somewhere
    /// under a chosen root should not lose the demos found everywhere else.
    /// </remarks>
    private static IEnumerable<string> Walk(string directory)
    {
        Queue<string> pending = new();
        pending.Enqueue(directory);

        while (pending.Count > 0)
        {
            string current = pending.Dequeue();
            string[] files;
            string[] children;

            try
            {
                files = Directory.GetFiles(current, "*" + DemoExtension);
                children = Directory.GetDirectories(current);
            }
            catch (Exception failure) when (failure is UnauthorizedAccessException or IOException)
            {
                // **A folder that cannot be read holds demos that never appear.** Silently, and
                // indistinguishably from a folder that has none - so someone whose recordings sit
                // behind a permission problem sees an empty list and concludes the viewer cannot
                // read their demos.
                ViewerLog.Warn("library", $"listing {current}", failure);

                continue;
            }

            foreach (string file in files)
            {
                yield return file;
            }

            foreach (string child in children.Where(c => !AssetFolders.Contains(Path.GetFileName(c))))
            {
                pending.Enqueue(child);
            }
        }
    }

    private void Add(string file)
    {
        if (!file.EndsWith(DemoExtension, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_entries.Any(e => string.Equals(e.Path, file, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _entries.Add(new DemoEntry(
            file,
            Path.GetFileName(file),
            Path.GetDirectoryName(file) ?? string.Empty,
            _roots[^1]));
    }

    private void Sort() => _entries.Sort((left, right) =>
    {
        int folder = string.Compare(left.Folder, right.Folder, StringComparison.OrdinalIgnoreCase);
        return folder != 0
            ? folder
            : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    });
}

/// <summary>One demo in the library.</summary>
/// <param name="Path">Full path to the file.</param>
/// <param name="Name">File name, as shown in the list.</param>
/// <param name="Folder">Directory holding it, which the list groups by.</param>
/// <param name="Root">The file or folder that was opened to bring it in.</param>
internal sealed record DemoEntry(string Path, string Name, string Folder, string Root);
