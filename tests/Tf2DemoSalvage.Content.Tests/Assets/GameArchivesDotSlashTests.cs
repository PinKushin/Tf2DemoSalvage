using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>`V_RemoveDotSlashes` (tier1/strtools.cpp:2315), which `CBaseFileSystem::FixUpPath` runs on every open.</summary>
/// <remarks>
/// The stock HUD names its health cross `vgui/../hud/health_bg`: a lookup that does not collapse the `..` finds nothing in
/// a VPK, and the cross is not drawn.
/// </remarks>
public sealed class GameArchivesDotSlashTests
{
    [TestCase("materials/vgui/../hud/health_bg.vmt", "materials/hud/health_bg.vmt")]
    [TestCase("a/./b", "a/b")]
    [TestCase("./a/b", "a/b")]
    [TestCase("a//b", "a/b")]
    [TestCase(@"a\b\..\c", "a/c")]
    [TestCase("a/b/c/../../d", "a/d")]
    public void RemoveDotSlashes_APath_IsCollapsed(string path, string expected) =>
        GameArchives.RemoveDotSlashes(path).ShouldBe(expected);

    [TestCase("../a")]
    [TestCase("a/../../b")]
    public void RemoveDotSlashes_ClimbingAboveTheRoot_Fails(string path) => GameArchives.RemoveDotSlashes(path).ShouldBeNull();

    [Test]
    public void Read_TheStockHealthCross_IsFoundThroughItsDotDotName()
    {
        GameArchives archives = GameArchives.Open(GameInstall.Require());

        archives.Read("materials/hud/health_bg.vmt").ShouldNotBeNull("the control: the file is in the install");
        archives.Read("materials/vgui/../hud/health_bg.vmt").ShouldNotBeNull();
    }
}
