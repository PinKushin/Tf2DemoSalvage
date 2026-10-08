using System;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Vertex flex — what moves a TF2 face, from the wire to the vertex — quoted from the SDK before any of it was
/// built (B513, B512's leftover).
/// </summary>
/// <remarks>
/// **Every assertion is a quotation.** Four links: what a demo carries (nothing, for a TF2 player), what the client
/// derives instead (scene EXPRESSION events through `.vfe` flex settings), the rules that turn controllers into flex
/// descriptor weights, and the morph that adds each descriptor's deltas to position, normal and tangent.
/// </remarks>
public sealed class VertexFlexConformanceTests
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
    public void Wire_TheTfPlayer_SendsNoFlexWeightNoBlinkAndNoViewTarget()
    {
        // tf_player.cpp:782-784: all three of DT_BaseFlex's props are excluded, so a demo carries no face for a player.
        string player = Sdk("src/game/server/tf/tf_player.cpp");

        player.ShouldContain("SendPropExclude( \"DT_BaseFlex\", \"m_flexWeight\" ),", Case.Sensitive);
        player.ShouldContain("SendPropExclude( \"DT_BaseFlex\", \"m_blinktoggle\" ),", Case.Sensitive);
        player.ShouldContain("SendPropExclude( \"DT_BaseFlex\", \"m_viewtarget\" ),", Case.Sensitive);
    }

    [Test]
    public void SetupGlobalWeights_TheNetworkedWeight_IsRescaledToTheControllerRange()
    {
        // c_baseflex.cpp:1220-1222: an unsent weight of 0 becomes the controller's MIN, not zero.
        Sdk("src/game/client/c_baseflex.cpp").ShouldContain(
            "g_flexweight[pflex->localToGlobal] = g_flexweight[pflex->localToGlobal] * (pflex->max - pflex->min) + pflex->min;",
            Case.Sensitive);
    }

    [Test]
    public void SetupGlobalWeights_TheBlink_StartsOnlyWhenTheToggleChanges()
    {
        // c_baseflex.cpp:1228-1231: the blink is timed from m_blinktoggle changing, which a TF player never sends.
        string flex = Sdk("src/game/client/c_baseflex.cpp");

        flex.ShouldContain("if (m_blinktoggle != m_prevblinktoggle)", Case.Sensitive);
        flex.ShouldContain("m_blinktime = gpGlobals->curtime + g_CV_BlinkDuration.GetFloat();", Case.Sensitive);
    }

    [Test]
    public void RunFlexRules_TheDmeEyelid_ChecksTheBlinkSlotAndNeverReadsIt()
    {
        // studio.cpp:1579-1641: stack[k-2] — where TF2's models stack the blink controller — is only validated; the
        // lid is CloseLidV times CloseLid (and the eye's pitch), so the blink controller cannot move a TF2 eyelid.
        string studio = Sdk("src/public/studio.cpp");

        studio.ShouldContain("CHECK_VALID_CONTROLLER_INDEX((int)stack[ k - 2 ]);", Case.Sensitive);
        studio.ShouldContain("stack [ k - 3 ] = ( 1.0f - flCloseLidV ) * flCloseLid;", Case.Sensitive);
        studio.ShouldContain("stack [ k - 3 ] = ( 1.0f + flEyeUpDown ) * flCloseLidV * flCloseLid;", Case.Sensitive);
    }

    [Test]
    public void RunFlexRules_TheOps_AreAStackMachineWhoseFirstSlotIsTheWeight()
    {
        // studio.cpp:1462-1652.
        string studio = Sdk("src/public/studio.cpp");

        studio.ShouldContain("stack[ k ] = RemapValClamped( src[m], -1.0f, 0.0f, 1.0f, 0.0f );", Case.Sensitive);
        studio.ShouldContain("stack[ k ] = RemapValClamped( src[m], 0.0f, 1.0f, 0.0f, 1.0f );", Case.Sensitive);
        studio.ShouldContain("stack[ km - 1 ] *= 1.0f - dv;", Case.Sensitive);
        studio.ShouldContain("stack[ k - 5 ] = flValue * src[ v ];", Case.Sensitive);
        studio.ShouldContain("dest[prule->flex] = stack[0];", Case.Sensitive);
    }

    [Test]
    public void ProcessFlexSettingSceneEvent_AnExpression_BlendsItsSettingByTheEventIntensity()
    {
        // c_baseflex.cpp:1761-1764 and :1882-1883: an EXPRESSION event names a .vfe and a setting, scaled by its ramp.
        string flex = Sdk("src/game/client/c_baseflex.cpp");

        flex.ShouldContain("float scale = event->GetIntensity( scenetime );", Case.Sensitive);
        flex.ShouldContain("float s = clamp( scale * pWeights->influence, 0.0f, 1.0f );", Case.Sensitive);
        flex.ShouldContain(
            "g_flexweight[iFlex] = g_flexweight[iFlex] * (1.0f - s) + pWeights->weight * s;", Case.Sensitive);
        flex.ShouldContain(
            "int len = filesystem->ReadFileEx( VarArgs( \"expressions/%s.vfe\", szFilename ), \"GAME\", &buffer );",
            Case.Sensitive);
    }

    [Test]
    public void UseHWMorphVCDs_IsOff_SoTheExpressionFileIsNotRedirected()
    {
        // c_sceneentity.cpp:84-90: the GPU-dependent choice is commented out and the function returns false.
        Sdk("src/game/client/c_sceneentity.cpp").ShouldMatch(
            @"// \treturn mp_usehwmvcds\.GetInt\(\) > 0;\r?\n\treturn false;");
    }

    [Test]
    public void GetIntensity_TheEvent_IsItsRampTimesTheScenesRampWithCatmullRomByDefault()
    {
        // choreoevent.cpp:1758 and :1294.
        string choreo = Sdk("src/game/shared/choreoevent.cpp");

        choreo.ShouldContain("return global_intensity * event_intensity;", Case.Sensitive);
        choreo.ShouldContain("m_nDefaultCurveType\t= CURVE_CATMULL_ROM_TO_CATMULL_ROM;", Case.Sensitive);
    }

    [Test]
    public void Vertanim_TheDeltas_AreFixedPointOnlyWhenTheHeaderSaysSo()
    {
        // studio.h:2395: the default scale is 1/4096, but the flag is "flagged on load" (:2091) — a file without it
        // stores the float16 half of the union (:1017-1027).
        string studio = Sdk("src/public/studio.h");

        studio.ShouldContain(
            "VertAnimFixedPointScale() const { return ( flags & STUDIOHDR_FLAGS_VERT_ANIM_FIXED_POINT_SCALE ) ? flVertAnimFixedPointScale : 1.0f / 4096.0f; }",
            Case.Sensitive);
        studio.ShouldContain("// flagged on load to indicate no animation events on this model", Case.Sensitive);
    }

    [Test]
    public void Morph_TheWeight_IsSpeedBetweenDelayedAndCurrentThenSideBetweenItselfAndItsPartner()
    {
        // morphaccumulate_ps30.fxc: x current, y delayed, z/w the stereo partner's; side.x and speed.y are bytes / 255.
        string accumulate = Sdk("src/materialsystem/stdshaders/morphaccumulate_ps30.fxc");

        accumulate.ShouldContain("float flWeight = lerp( vMorphWeights.y, vMorphWeights.x, sideSpeed.y );", Case.Sensitive);
        accumulate.ShouldContain(
            "float flStereoWeight = lerp( vMorphWeights.w, vMorphWeights.z, sideSpeed.y ); ", Case.Sensitive);
        accumulate.ShouldContain("float w = lerp( flWeight, flStereoWeight, sideSpeed.x );", Case.Sensitive);
    }

    [Test]
    public void Morph_TheDeltas_MovePositionNormalAndTangentByTheNormalsDelta()
    {
        // ApplyMorph, common_vs_fxc.h:384-387.
        string common = Sdk("src/materialsystem/stdshaders/common_vs_fxc.h");

        common.ShouldContain("vPosition.xyz += vPosDelta;", Case.Sensitive);
        common.ShouldContain("vNormal       += vNormalDelta;", Case.Sensitive);
        common.ShouldContain("vTangent.xyz  += vNormalDelta;", Case.Sensitive);
    }

    /// <summary>Reads an SDK file, or fails loudly.</summary>
    private static string Sdk(string path) =>
        SourceSdk.Text(path) ?? throw new InvalidOperationException($"{path} is missing from the SDK");
}
