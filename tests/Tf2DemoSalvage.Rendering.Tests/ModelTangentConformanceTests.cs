using System;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A model's tangent frame — where it is stored, how it is skinned, what reads it — quoted from the SDK before
/// any of it was built (B170's tangent leftover, B508's cloak refract).
/// </summary>
/// <remarks>
/// **Every assertion is a quotation.** The tangent is `Vector4D` per vertex in its own array after the vertices,
/// reordered by the same fixups; it is skinned by the vertex's blend matrix, rotation only, and its w carries the
/// binormal's sign. Every model shader that bumps reads it through the same three-column frame.
/// </remarks>
public sealed class ModelTangentConformanceTests
{
    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void Vvd_TheTangents_AreAFourComponentArrayAfterTheVertices()
    {
        // studio.h:1954 and :1485-1491: one Vector4D per vertex, in a separate array the header points at.
        string studio = Sdk("src/public/studio.h");

        studio.ShouldContain("int\t\ttangentDataStart;\t\t\t\t// offset from base to tangent block", Case.Sensitive);
        studio.ShouldContain("return (Vector4D *)pTangentData + GetGlobalTangentIndex( i );", Case.Sensitive);
        studio.ShouldContain("return ( Vector4D * ) ( tangentDataStart + (byte *)this );", Case.Sensitive);
    }

    [Test]
    public void Vvd_TheFixups_ReorderTheTangentsAsTheyReorderTheVertices()
    {
        // Studio_LoadVertexes, studio.h:3323-3329: each fixup run copies its tangents from the same source vertex
        // into the same target slot as its vertices, so the tangent of vertex i is the tangent array's i after fixup.
        string studio = Sdk("src/public/studio.h");

        studio.ShouldContain(
            "(Vector4D *)((byte *)pTempVvdHdr+pTempVvdHdr->tangentDataStart) + pFixupTable[i].sourceVertexID,",
            Case.Sensitive);
        studio.ShouldContain(
            "(Vector4D *)((byte *)pNewVvdHdr+pNewVvdHdr->tangentDataStart) + target,", Case.Sensitive);
    }

    [Test]
    public void Skinning_TheTangent_TurnsByTheBlendMatrixAndTheBinormalTakesItsSign()
    {
        // SkinPositionNormalAndTangentSpace, common_vs_fxc.h:724, :738, :740: the tangent's xyz goes through the
        // same 3x3 as the normal (rotation only, never translated), and the binormal is N x T times the stored w.
        string common = Sdk("src/materialsystem/stdshaders/common_vs_fxc.h");

        common.ShouldContain(
            "worldTangentS = mul3x3( ( float3 )modelTangentS, ( const float3x3 )blendMatrix );", Case.Sensitive);
        common.ShouldContain(
            "worldTangentS = mul3x3( ( float3 )modelTangentS, ( const float3x3 )cModel[0] );", Case.Sensitive);
        common.ShouldContain(
            "worldTangentT = cross( worldNormal, worldTangentS ) * modelTangentS.w;", Case.Sensitive);
    }

    [Test]
    public void Morph_TheTangent_TakesTheNormalDelta()
    {
        // ApplyMorph, common_vs_fxc.h:387: a flex moves the tangent by the NORMAL's delta, not one of its own.
        // Ported with vertex flex in B513; FaceFlexRenderTests draws it.
        Sdk("src/materialsystem/stdshaders/common_vs_fxc.h")
            .ShouldContain("vTangent.xyz  += vNormalDelta;", Case.Sensitive);
    }

    [Test]
    public void Skin_TheShadingNormal_IsTheNormalMapThroughTheFrameUnlessTheBaseAlphaMasks()
    {
        // skin_ps20b.fxc:199 and :207: $basemapalphaphongmask selects a FLAT tangent-space normal, which the frame
        // turns back into the vertex normal; diffuse, Fresnel, rim and the envmap all read the result (:209-:220).
        string skin = Sdk("src/materialsystem/stdshaders/skin_ps20b.fxc");

        skin.ShouldContain(
            "tangentSpaceNormal = lerp( 2.0f * normalTexel.xyz - 1.0f, float3(0, 0, 1), g_fBaseMapAlphaPhongMask );",
            Case.Sensitive);
        skin.ShouldContain(
            "worldSpaceNormal = normalize( mul( i.tangentSpaceTranspose, tangentSpaceNormal ) );", Case.Sensitive);
        skin.ShouldContain("diffuseLighting = PixelShaderDoLighting( vWorldPos, worldSpaceNormal,", Case.Sensitive);
    }

    [Test]
    public void VertexLitBump_TheShadingNormal_IsTheNormalMapThroughTheFrame()
    {
        // vertexlit_and_unlit_generic_bump_ps2x.fxc:167 and :177, Vec3TangentToWorld (common_fxc.h:1135): no
        // base-alpha escape on the non-phong path; the decoded texel always goes through T, B, N.
        string bump = Sdk("src/materialsystem/stdshaders/vertexlit_and_unlit_generic_bump_ps2x.fxc");

        bump.ShouldContain("float3 tangentSpaceNormal = normalTexel * 2.0f - 1.0f;", Case.Sensitive);
        bump.ShouldContain(
            "worldSpaceNormal = Vec3TangentToWorld( tangentSpaceNormal, i.vWorldNormal, i.vWorldTangent, vWorldBinormal );",
            Case.Sensitive);
        bump.ShouldContain(
            "float3 vWorldBinormal = cross( i.vWorldNormal.xyz, i.vWorldTangent.xyz ) * i.vWorldTangent.w;",
            Case.Sensitive);
    }

    [Test]
    public void Cloak_TheRefractNormal_IsTheBumpThroughTheMeshFrameAndTheFresnelKeepsTheVertexNormal()
    {
        // cloak_blended_pass_ps2x.fxc:56 and :85; the frame comes from cloak_blended_pass_vs20.fxc:97.
        string pixel = Sdk("src/materialsystem/stdshaders/cloak_blended_pass_ps2x.fxc");
        string vertex = Sdk("src/materialsystem/stdshaders/cloak_blended_pass_vs20.fxc");

        pixel.ShouldContain("vWorldNormal.xyz = mul( i.mTangentSpaceTranspose, vTangentNormal.xyz );", Case.Sensitive);
        pixel.ShouldContain(
            "float flFresnel = 1.0f - saturate( dot( i.vWorldNormal.xyz, normalize( -i.vWorldViewVector.xyz ) ) );",
            Case.Sensitive);
        vertex.ShouldContain(
            "SkinPositionNormalAndTangentSpace( g_bSkinning, vObjPosition, vObjNormal.xyz, vObjTangent.xyzw,",
            Case.Sensitive);
    }

    /// <summary>Reads an SDK file, or fails loudly.</summary>
    private static string Sdk(string path) =>
        SourceSdk.Text(path) ?? throw new InvalidOperationException($"{path} is missing from the SDK");
}
