using System;
using System.Threading;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>The installed game, opened once for the whole assembly and read by every test that needs it (D213).</summary>
/// <remarks>
/// **Shared because it is immutable, which is the only thing the parallelism policy asks.** <see cref="GameArchives"/> is
/// a list of archive indexes built by <c>Open</c> and never added to; every read opens its own file stream
/// (<c>VpkArchive</c>), so two tests reading at once share nothing but the index. Opening it costs the directory tree of
/// every VPK the game mounts, which forty-odd tests were paying each.
///
/// **<see cref="GameContent"/> has three lazy members, and they are forced here before anything else can see it** —
/// the item schema (<c>WeaponModels.Items</c>), <c>ClassAnimation</c>, and the schema's default bodygroup states. Each
/// is an unsynchronised <c>??=</c> whose value is the same whoever computes it, so a race would cost a duplicate read
/// rather than a wrong answer; forcing them makes the object read-only in fact instead of by argument
/// (`docs/memory/a-lazy-cache-makes-reading-a-write.md`).
///
/// **Not for a test whose subject is opening the game** — one asserting what <c>GameArchives.Open</c> finds, or what a
/// load does with no game at all, opens its own.
/// </remarks>
internal static class SharedGame
{
    private static readonly Lazy<GameArchives> OpenedArchives = new(
        () => GameArchives.Open(GameInstall.Root), LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<GameContent> OpenedContent = new(
        () =>
        {
            GameContent content = GameContent.Open(GameInstall.Root, NullLoggerFactory.Instance);

            _ = content.Weapons.Items?.DefaultBodygroupStates;
            _ = content.ClassAnimation;

            return content;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The game's archives, or skips the calling test when the game is not installed.</summary>
    /// <remarks>The skip is thrown BEFORE the lazy is touched: one thrown inside it would be cached for every later caller.</remarks>
    public static GameArchives Archives
    {
        get
        {
            GameInstall.Require();

            return OpenedArchives.Value;
        }
    }

    /// <summary>The game's content — class models, weapons, the item schema — or skips when the game is not installed.</summary>
    public static GameContent Content
    {
        get
        {
            GameInstall.Require();

            return OpenedContent.Value;
        }
    }
}
