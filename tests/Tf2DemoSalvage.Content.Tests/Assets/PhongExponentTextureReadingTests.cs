using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The exponent texture read out of a material TF2 actually ships (B334).
/// </summary>
/// <remarks>
/// **A synthetic fixture cannot fail the way a real file can.** `PhongExponentTextureConformanceTests`
/// hands the reader text this project wrote, so it proves the RULES; it cannot notice that no
/// shipped material spells the parameter the way the test does, or that the ones which use it put it
/// somewhere the reader does not look — which is exactly what happened to `$selfillum`, hidden
/// inside a `>=DX90` block on 5,415 materials for months.
///
/// **Measured before this was written**: 1,862 of the 30,684 materials TF2 ships name a
/// `$phongexponenttexture`, every one of them `VertexLitGeneric`
/// (`vmt-param $phongexponenttexture`). None is on cp_process_final, which is why the map-level
/// wiring test cannot reach this and this one exists.
/// </remarks>
public sealed class PhongExponentTextureReadingTests
{
    /// <summary>A shipped material that names an exponent texture, a rim and a rim mask.</summary>
    /// <remarks>
    /// A community cosmetic rather than a stock class material, deliberately: stock materials such
    /// as `soldier_red` state `$phongexponent 20` and no map at all, so they cannot exercise this.
    /// The workshop items are where the exponent texture actually lives.
    /// </remarks>
    private const string Subject =
        "materials/models/workshop/player/items/all_class/jump_fortress_third/jump_fortress_third.vmt";

    [Test]
    public void PhongExponentTexture_AShippedMaterialThatNamesOne_IsFound()
    {
        if (Read(Subject) is not { } material)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        material.HasPhong.ShouldBeTrue("the exponent texture is only reachable through phong");

        material.PhongExponentTexture.ShouldNotBeNull(
            "measured: this material names one, and a reader that missed it would report null "
            + "for all 1,862 that do");

        // **The exponent is taken from the map here**, which is the branch worth pinning against a
        // real file: whether shipped materials state a positive $phongexponent BESIDE their map is
        // the question the whole sentinel exists to answer, and the answer decides which of two
        // very different pictures TF2 draws.
        TestContext.Out.WriteLine(
            $"{Subject}: exponent {material.PhongExponent}, from the map "
            + $"{material.PhongExponentFromTexture}, rim mask {material.RimMaskControl}");
    }

    /// <remarks>
    /// **The control, and it is the one that matters here.** Every assertion above would also pass
    /// against a reader that answered a non-null texture for every material — so a stock class
    /// material, which states `$phong` and `$phongexponent` and NO exponent texture, has to come
    /// back with none.
    /// </remarks>
    [Test]
    public void PhongExponentTexture_AStockMaterialWithAStatedExponent_HasNone()
    {
        if (Read("materials/models/player/soldier/soldier_red.vmt") is not { } material)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        material.HasPhong.ShouldBeTrue();
        material.PhongExponentTexture.ShouldBeNull();
        material.PhongExponentFromTexture.ShouldBeFalse();

        material.PhongExponent.ShouldBe(
            20f, "it states $phongexponent 20, which wins over the white the engine would bind");

        material.RimMaskControl.ShouldBe(0f, "no exponent texture, so nothing to mask the rim with");
    }

    /// <summary>The material, or null when the game is not installed.</summary>
    private static VmtMaterial? Read(string path)
    {
        if (GameInstall.Vpk("tf2_misc") is not { } archive ||
            VpkArchive.Open(archive).ReadFile(path) is not { } bytes)
        {
            return null;
        }

        return VmtMaterial.Parse(bytes);
    }
}
