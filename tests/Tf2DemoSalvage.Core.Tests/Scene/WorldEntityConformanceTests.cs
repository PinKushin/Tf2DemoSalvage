using System;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Entity zero is the world, and the client never draws it as an entity.
/// </summary>
/// <remarks>
/// **This became answerable only after B132.** Until <c>EntityStateTable</c> merged instance
/// baselines, <c>CWorld</c> reached the entity table with no properties at all, so it had no model
/// index and never became a prop. With the baselines applied it arrives holding model index 1 —
/// <c>maps/cp_granary.bsp</c>, the map itself — and a renderer that treats it like any other
/// model-bearing entity would draw the whole world a second time, on top of the world.
///
/// **Valve excludes it by index, not by type.** <c>C_BaseEntity::ShouldDraw</c>, at
/// <c>game/client/c_baseentity.cpp:1450</c>:
///
/// <code>
/// return (model != 0) &amp;&amp; !IsEffectActive(EF_NODRAW) &amp;&amp; (index != 0);
/// </code>
///
/// So the world model is a perfectly ordinary brush model — <c>mod_brush</c>, the same
/// <c>modtype_t</c> as the <c>*N</c> submodels a door uses — and what keeps it off the screen is
/// that its entity index is zero. Classifying it as something unknown would be a statement about
/// the format that is not true; skipping index zero is the statement Valve actually makes.
/// </remarks>
public sealed class WorldEntityConformanceTests
{
    [Test]
    public void ShouldDraw_TheEngineRule_ExcludesEntityIndexZero()
    {
        // The citation, pinned. If Valve's rule ever stops mentioning the index this reddens rather
        // than leaving a comment quietly describing something that is no longer there.
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string text = SourceSdk.Text("src/game/client/c_baseentity.cpp")
            ?? throw new InvalidOperationException("c_baseentity.cpp is missing from the SDK");

        Match body = new Regex(
            @"bool C_BaseEntity::ShouldDraw\(\)(?s).{0,600}?\n\}",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(10)).Match(text);

        body.Success.ShouldBeTrue("C_BaseEntity::ShouldDraw was not found in the SDK");
        body.Value.ShouldContain("index != 0");
        body.Value.ShouldContain("EF_NODRAW");
    }

    [Test]
    public void Classify_TheWorldModelReference_IsABrushModel()
    {
        // `maps/<name>.bsp` is submodel zero of the map — the world itself. It is mod_brush in
        // Valve's modtype_t exactly as `*1` is, and the only thing separating them is which
        // submodel they name.
        ScenePropTrack.Classify("maps/cp_granary.bsp").ShouldBe(SceneModelKind.Brush);

        // Case, because a precache string is whatever the server sent and nothing normalises it.
        ScenePropTrack.Classify("MAPS/CP_GRANARY.BSP").ShouldBe(SceneModelKind.Brush);

        // The control: the extension is what decides, not the folder. A studio model living under
        // maps/ is still a studio model, and treating the prefix as the rule would misfile it.
        ScenePropTrack.Classify("maps/props/crate.mdl").ShouldBe(SceneModelKind.Studio);
    }
}
