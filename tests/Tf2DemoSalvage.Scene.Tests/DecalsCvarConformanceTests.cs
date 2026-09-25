namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>That <c>r_decals</c> is Valve's and reachable from a config (B415, D69).</summary>
/// <remarks>
/// The engine sizes its dynamic decal pool from <c>r_decals</c> at level load — `MAX_DECALS = max( 64, r_decals )` — so a
/// config that lowers it holds fewer bullet holes before the oldest go, and one that raises it holds more.
/// </remarks>
public sealed class DecalsCvarConformanceTests
{
    [Test]
    public void DecalsCommand_IsValvesName() => ViewerSettings.DecalsCommand.ShouldBe("r_decals");

    /// <remarks>`r_decals`' shipped default, "2048".</remarks>
    [Test]
    public void Parse_WithNothingSaid_KeepsValvesDefault() => ViewerSettings.Parse(string.Empty).Decals.ShouldBe(2048);

    [Test]
    public void Parse_AConfigsValue_IsCarried() => ViewerSettings.Parse("r_decals 512\n").Decals.ShouldBe(512);

    /// <remarks>The round trip: a setting that cannot be written back is lost on the next save.</remarks>
    [Test]
    public void Write_ThenParse_KeepsTheValue() =>
        ViewerSettings.Parse(ViewerSettings.Parse("r_decals 300\n").Write()).Decals.ShouldBe(300);
}
