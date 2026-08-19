using System;
using System.IO;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>Where Team Fortress 2 is on this machine, for the tests that need real content.</summary>
/// <remarks>
/// **Nine test files carried a private copy of this before it existed**, each with its own list of
/// Steam library roots. That is the DRY failure that goes unnoticed longest: they all work, and a
/// tenth library path added to one of them silently leaves the other eight looking in the old
/// places. New files use this; the existing copies are worth migrating and are not this change's
/// business.
/// </remarks>
internal static class Tf2Install
{
    /// <summary>Steam libraries this project has actually been run against.</summary>
    private static readonly string[] Roots =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common\Team Fortress 2\tf",
        @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
        @"D:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
    ];

    /// <summary>The <c>tf</c> folder, or null when the game is not installed.</summary>
    /// <remarks>
    /// <c>TF2_FOLDER</c> wins so a machine with the game somewhere else needs no code change. The
    /// probe is <c>tf2_textures_dir.vpk</c> rather than the folder existing, because an uninstall
    /// leaves the folder behind and an empty one would look installed.
    /// </remarks>
    public static string? Folder
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("TF2_FOLDER");

            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            {
                return configured;
            }

            foreach (string root in Roots)
            {
                if (File.Exists(Path.Combine(root, "tf2_textures_dir.vpk")))
                {
                    return root;
                }
            }

            return null;
        }
    }
}
