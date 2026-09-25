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

    /// <remarks>`r_maxmodeldecal`, shipped "50" (`engine.dll` `0x180006fe0`): each model's cap, and 1.5 times it the pool's.</remarks>
    [Test]
    public void Parse_MaxModelDecal_IsValvesNameAndDefault()
    {
        ViewerSettings.MaxModelDecalCommand.ShouldBe("r_maxmodeldecal");
        ViewerSettings.Parse(string.Empty).MaxModelDecals.ShouldBe(50);
        ViewerSettings.Parse("r_maxmodeldecal 10\n").MaxModelDecals.ShouldBe(10);
    }

    /// <remarks>A pool told `r_maxmodeldecal 2` holds three decals in all, 1.5 times the cap, and retires the oldest.</remarks>
    [Test]
    public void AddClipped_PastAPoolOfTwoPerModel_HoldsThree()
    {
        ModelDecals pool = new() { PerModel = 2 };
        System.Numerics.Vector3 down = new(-1.1f, 0f, 0f);
        WorldVertex[] face =
        [
            new(0f, -100f, -100f, 0f, 0f, 0f, 0f, 1f, NormalX: 1f, NormalY: 0f, NormalZ: 0f),
            new(0f, 100f, -100f, 0f, 0f, 0f, 0f, 1f, NormalX: 1f, NormalY: 0f, NormalZ: 0f),
            new(0f, 0f, 100f, 0f, 0f, 0f, 0f, 1f, NormalX: 1f, NormalY: 0f, NormalZ: 0f),
        ];

        for (int model = 0; model < 4; model++)
        {
            pool.AddClipped(model, face, System.Numerics.Vector3.Zero, down, 4f, 1);
        }

        pool.Count.ShouldBe(3);
    }

    /// <remarks>The round trip: a setting that cannot be written back is lost on the next save.</remarks>
    [Test]
    public void Write_ThenParse_KeepsTheValue() =>
        ViewerSettings.Parse(ViewerSettings.Parse("r_decals 300\n").Write()).Decals.ShouldBe(300);
}
