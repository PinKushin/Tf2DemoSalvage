using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The <c>$envmap</c> shading parameters a material carries.
/// </summary>
/// <remarks>
/// **The defaults are the whole difficulty and they point opposite ways.** <c>$envmapcontrast</c>
/// is normal at ZERO and <c>$envmapsaturation</c> is normal at ONE, so a reader defaulting both to
/// zero greys out every reflection on the map and one defaulting both to one squares every
/// reflection. Neither produces an error and both look like a decision someone made.
///
/// Each parameter therefore gets its default asserted AND its stated value asserted — a reader that
/// ignored the key entirely passes a default-only test, and one that ignored the default passes a
/// value-only test.
/// </remarks>
public sealed class VmtEnvMapTests
{
    [Test]
    public void AMaterialNamingNoTintReflectsUnchanged()
    {
        Bare().EnvMapTint.ShouldBe((1f, 1f, 1f));
    }

    [Test]
    public void ATintIsReadFromItsKey()
    {
        // Channels all different, so a transposed component cannot pass.
        Material("$envmaptint", "[0.25 0.5 0.75]").EnvMapTint.ShouldBe((0.25f, 0.5f, 0.75f));
    }

    [Test]
    public void ContrastDefaultsToZeroWhichIsNormal()
    {
        Bare().EnvMapContrast.ShouldBe(0f);
    }

    [Test]
    public void SaturationDefaultsToOneWhichIsAlsoNormal()
    {
        // **The asymmetry, stated beside its opposite.** These two defaults are the reason this
        // file exists: they are declared 0.0 and 1.0 in adjacent SHADER_PARAM lines and mean the
        // same thing — leave the reflection alone.
        Bare().EnvMapSaturation.ShouldBe(1f);
    }

    [Test]
    public void TheDefaultsTogetherAreTheIdentity()
    {
        // Stated as the arithmetic rather than as two numbers, because the numbers alone do not say
        // which way each lerp runs. With the defaults, both lerps must be the identity.
        VmtMaterial bare = Bare();

        float sample = 0.6f;

        Lerp(sample, sample * sample, bare.EnvMapContrast).ShouldBe(sample, 0.0001f);
        Lerp(0.123f, sample, bare.EnvMapSaturation).ShouldBe(sample, 0.0001f);

        static float Lerp(float from, float to, float by) => from + ((to - from) * by);
    }

    [Test]
    public void ContrastAndSaturationAreReadFromTheirKeys()
    {
        Material("$envmapcontrast", "1").EnvMapContrast.ShouldBe(1f);
        Material("$envmapsaturation", "0").EnvMapSaturation.ShouldBe(0f);

        // Values that are neither a default nor a limit, so a reader returning a constant is caught.
        Material("$envmapcontrast", "0.35").EnvMapContrast.ShouldBe(0.35f, 0.0001f);
        Material("$envmapsaturation", "0.8").EnvMapSaturation.ShouldBe(0.8f, 0.0001f);
    }

    [Test]
    public void TheBaseAlphaMaskIsReadAsAFlag()
    {
        // atoi-shaped like every other material flag, so "1" is true and "0" is false, and an
        // absent key is false rather than defaulting a mask on.
        Material("$basealphaenvmapmask", "1").UsesBaseAlphaAsEnvMapMask.ShouldBeTrue();
        Material("$basealphaenvmapmask", "0").UsesBaseAlphaAsEnvMapMask.ShouldBeFalse();
        Bare().UsesBaseAlphaAsEnvMapMask.ShouldBeFalse();
    }

    [Test]
    public void ATintCanExceedOne()
    {
        // Not clamped, for the same reason $color is not: the reflection is the term that carries
        // values above one, and clamping the tint caps a glow the map author asked for.
        Material("$envmaptint", "[2 2 2]").EnvMapTint.ShouldBe((2f, 2f, 2f));
    }

    private static VmtMaterial Bare() =>
        Parse("\"LightmappedGeneric\" { \"$basetexture\" \"concrete/floor\" }");

    private static VmtMaterial Material(string key, string value) =>
        Parse($"\"LightmappedGeneric\" {{ \"$basetexture\" \"a/b\" \"{key}\" \"{value}\" }}");

    private static VmtMaterial Parse(string text) => VmtMaterial.Parse(Encoding.UTF8.GetBytes(text));
}
