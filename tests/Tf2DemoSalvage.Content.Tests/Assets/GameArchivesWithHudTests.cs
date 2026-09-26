using System;
using System.IO;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary><see cref="GameArchives.WithHud"/>: the chosen HUD searched first, the stock files behind it (D193).</summary>
/// <remarks>A game folder holding `resource/layout.res` ("stock") and `resource/only-stock.res`; a HUD folder holding its own `resource/layout.res` ("hud").</remarks>
public sealed class GameArchivesWithHudTests
{
    private string _root = string.Empty;

    private string Game => Path.Combine(_root, "tf");

    private string Hud => Path.Combine(_root, "huds", "myhud");

    [SetUp]
    public void CreateFolders()
    {
        _root = Path.Combine(Path.GetTempPath(), "gamearchives-hud-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Game, "resource"));
        Directory.CreateDirectory(Path.Combine(Hud, "resource"));
        File.WriteAllText(Path.Combine(Game, "resource", "layout.res"), "stock");
        File.WriteAllText(Path.Combine(Game, "resource", "only-stock.res"), "stock only");
        File.WriteAllText(Path.Combine(Hud, "resource", "layout.res"), "hud");
    }

    [TearDown]
    public void DeleteFolders() => Directory.Delete(_root, recursive: true);

    [Test]
    public void WithHud_None_IsTheStockFiles() =>
        Text(GameArchives.Open(Game).WithHud(null), "resource/layout.res").ShouldBe("stock");

    [Test]
    public void WithHud_AChosenHud_WinsOverTheStockFile() =>
        Text(GameArchives.Open(Game).WithHud(Hud), "resource/layout.res").ShouldBe("hud");

    [Test]
    public void WithHud_AFileTheHudDoesNotCarry_FallsBackToStock() =>
        Text(GameArchives.Open(Game).WithHud(Hud), "resource/only-stock.res").ShouldBe("stock only");

    private static string? Text(GameArchives archives, string path) =>
        archives.Read(path) is { } bytes ? Encoding.UTF8.GetString(bytes) : null;
}
