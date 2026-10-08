using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// What drives a TF2 face beyond its expressions — flex animation, lip sync, every scene, who draws whose face — quoted
/// from the SDK before it was built (B513, the remainder).
/// </summary>
/// <remarks>
/// **Every assertion is a quotation.** The facts read in disassembly (the target ramp, the dead band, the fixed-point
/// conversion, the sound cache) have no SDK text and are cited in the code that ports them.
/// </remarks>
public sealed class FaceDriverConformanceTests
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
    public void ResetFlexWeights_ATfPlayer_StartsEachControllerAtZeroInItsRange()
    {
        // c_tf_player.cpp:5298-5300: SetFlexWeight normalises, so a -1..1 controller rests at its middle.
        Sdk("src/game/client/tf/c_tf_player.cpp").ShouldContain("SetFlexWeight( iController, 0.0f );", Case.Sensitive);
    }

    [Test]
    public void ProcessSceneEvents_TheFlexPass_DecaysEveryControllerPerCall()
    {
        // c_baseflex.cpp:1703, inside `if ( bFlexEvents )` — once per SetupGlobalWeights call, not per second.
        Sdk("src/game/client/c_baseflex.cpp").ShouldContain("SetFlexWeight( i, GetFlexWeight( i ) * 0.95 );", Case.Sensitive);
    }

    [Test]
    public void AddFlexAnimation_AControllerTheModelLacks_ResolvesToControllerZero()
    {
        // c_baseflex.cpp:1987: MAX( -1, 0 ) is 0, so a missing controller writes the model's first.
        Sdk("src/game/client/c_baseflex.cpp").ShouldContain(
            "track->SetFlexControllerIndex( MAX( FindFlexController( (char *)track->GetFlexControllerName() ), LocalFlexController_t(0)), 0 );",
            Case.Sensitive);
    }

    [Test]
    public void EventThink_AnEvent_IsTestedAtTheFramesStartTime()
    {
        // choreoscene.cpp:2529: the previous frame's time — so an event starts the frame after its start time passes.
        Sdk("src/game/shared/choreoscene.cpp").ShouldContain(
            "where_is_event = IsTimeInRange( frame_start_time, starttime, endtime );", Case.Sensitive);
    }

    [Test]
    public void StartEvent_AnEventNamedNull_IsIgnored()
    {
        // c_sceneentity.cpp:463.
        Sdk("src/game/client/c_sceneentity.cpp").ShouldContain("if ( !Q_stricmp( event->GetName(), \"NULL\" ) )", Case.Sensitive);
    }

    [Test]
    public void SetupWeights_AWornItemAndACorpse_DrawTheirPlayersFace()
    {
        // econ_entity.cpp:1377 (a bone-merged item calls its parent's global pass) and c_tf_player.cpp:636 (a dead
        // player's ragdoll calls the player's whole SetupWeights).
        Sdk("src/game/shared/econ/econ_entity.cpp").ShouldContain(
            "if ( pParentFlex->SetupGlobalWeights( pBoneToWorld, nFlexWeightCount, pFlexWeights, pFlexDelayedWeights ) )",
            Case.Sensitive);
        Sdk("src/game/client/tf/c_tf_player.cpp").ShouldContain(
            "pPlayer->SetupWeights( pBoneToWorld, nFlexWeightCount, pFlexWeights, pFlexDelayedWeights );", Case.Sensitive);
    }

    [Test]
    public void RunFlexDelay_TheDelayedWeights_DecayTowardTheCurrent()
    {
        // c_baseflex.cpp:1271.
        Sdk("src/game/client/c_baseflex.cpp").ShouldContain("d = ExponentialDecay( 0.8, 0.033, d );", Case.Sensitive);
    }

    [Test]
    public void InitPhonemeMappings_ATfPlayer_ReadsItsClassPhonemesFile()
    {
        // c_tf_player.cpp:5276.
        Sdk("src/game/client/tf/c_tf_player.cpp").ShouldContain(
            "Q_snprintf( szExpressionName, sizeof( szExpressionName ), \"%s/phonemes/phonemes\", szBasename );", Case.Sensitive);
    }

    [Test]
    public void UseHWMorphModels_InThisBuild_IsAlwaysFalse()
    {
        // baseplayer_shared.cpp:104: the hwm player models — the only shipped models with wrinkle flexes — are never used.
        Sdk("src/game/shared/baseplayer_shared.cpp").ShouldContain("bool UseHWMorphModels()\n{", Case.Sensitive);
        Sdk("src/game/shared/baseplayer_shared.cpp").ShouldContain("// #endif\n\treturn false;\n}", Case.Sensitive);
    }

    private static string Sdk(string path) =>
        (SourceSdk.Text(path) ?? throw new System.InvalidOperationException($"{path} is missing from the SDK"))
            .Replace("\r\n", "\n", System.StringComparison.Ordinal);
}
