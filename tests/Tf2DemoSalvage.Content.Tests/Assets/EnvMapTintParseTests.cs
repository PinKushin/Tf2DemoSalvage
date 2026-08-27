using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// That a tint written the way TF2 writes it survives parsing.
/// </summary>
/// <remarks>
/// **`c_shotgun.vmt` limits its reflection with a TINT and no mask at all**, read from the shipped
/// file 2026-08-27 while chasing B170:
///
/// <code>
/// "$envmap" "env_cubemap"
/// "$envmaptint" "[.05 .05 .05]"
/// </code>
///
/// **Five percent, and written without a leading zero.** `.05` rather than `0.05` is how Valve's
/// own materials are authored throughout — `$phongfresnelranges "[.25 1.5 20]"` on the same file —
/// so a parser that needs a digit before the point would read this material as untinted and
/// reflect the sky at TWENTY TIMES the authored strength. That is the shape of B170's symptom, and
/// this pins down whether it is the cause.
/// </remarks>
public sealed class EnvMapTintParseTests
{
    [Test]
    public void EnvMapTint_WrittenWithoutALeadingZero_KeepsItsValue()
    {
        VmtMaterial material = VmtMaterial.Parse(Encoding.UTF8.GetBytes("""
            "VertexLitGeneric"
            {
                "$basetexture" "models/weapons/c_models/c_shotgun/c_shotgun"
                "$envmap" "env_cubemap"
                "$envmaptint" "[.05 .05 .05]"
            }
            """));

        material.EnvMapTint.Red.ShouldBe(0.05f, 0.0001f);
        material.EnvMapTint.Green.ShouldBe(0.05f, 0.0001f);
        material.EnvMapTint.Blue.ShouldBe(0.05f, 0.0001f);
    }

    [Test]
    public void EnvMapTint_WhenTheMaterialDeclaresNone_IsValvesFullStrengthDefault()
    {
        // **The control, and it is what makes the test above mean something.** Valve's own default
        // is `[1 1 1]` — SHADER_PARAM( ENVMAPTINT, SHADER_PARAM_TYPE_COLOR, "[1 1 1]", ... ) — so
        // "the tint is 1" and "the tint failed to parse" produce the same reading. Without a case
        // that legitimately yields 1, the assertion above cannot tell a working parser from a
        // broken one falling back to the default.
        VmtMaterial material = VmtMaterial.Parse(Encoding.UTF8.GetBytes("""
            "VertexLitGeneric"
            {
                "$basetexture" "models/weapons/c_models/c_shotgun/c_shotgun"
                "$envmap" "env_cubemap"
            }
            """));

        material.EnvMapTint.ShouldBe((1f, 1f, 1f));
    }
}
