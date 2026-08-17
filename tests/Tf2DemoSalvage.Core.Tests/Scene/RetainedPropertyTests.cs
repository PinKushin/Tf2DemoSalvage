using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// What the scene layer keeps from a decoded entity, and what it silently drops.
/// </summary>
/// <remarks>
/// **Correction, 2026-08-16: `NetworkedProperties` is an INVENTORY, not a filter.** This file said
/// it was a whitelist "so anything missing from it is discarded before any consumer could ask for
/// it". That is false, and it was my own claim from earlier the same day.
///
/// `EntityStateTable.Apply` writes **every** decoded property into the state unconditionally, and
/// the list has no production consumer at all — only tests read it. So adding `m_nSkin` to it fixed
/// nothing; the skin defect was entirely the missing <c>Skin = state.Skin() ?? 0</c> line in the
/// pose construction, and <c>EntityState.Skin()</c> would have answered correctly all along.
///
/// **What the list is actually for**, and it earns its place: it is the set of property names this
/// project looks for, and `SendPropConformanceTests` checks every one against the SDK's send tables.
/// A name Source does not send is caught there rather than silently finding nothing for ever. That
/// is why a field missing from this list is still worth fixing — not because it is discarded, but
/// because nothing checks that its name is real.
///
/// The four names added on 2026-08-16 — the eye angle components, the origin's Z component and
/// `moveparent` — were read in production and absent from this list, so they had no such check.
///
/// **Third instance of one shape, and the third happened inside the fix for the second.** A record
/// built field by field, one field forgotten, and a default that is also a legitimate value so
/// nothing can report it: `ScenePlayer.Yaw`, then `ScenePose.Body`, now `ScenePose.Skin`. The fix
/// for `Body` added `Skin` to the CLONE and not to the CONSTRUCTION.
///
/// So the assertion is on the list rather than on one field. A test naming only `m_nSkin` would have
/// been satisfied by the same partial fix that caused this.
/// </remarks>
public sealed class RetainedPropertyTests
{
    /// <summary>The table every drawable animating entity sends its appearance on.</summary>
    private const string AnimatingTable = "DT_BaseAnimating";

    [Test]
    public void TheAnimatingTableKeepsEverythingThePoseIsBuiltFrom()
    {
        // Each name here is read by ScenePose construction in DemoTimeline. The list is asserted
        // whole so that adding a field to the pose without retaining it — or retaining one and
        // forgetting to read it — shows up as a difference rather than as a silent zero.
        //
        // m_nSkin is the reason this test exists. It is sent by DT_BaseAnimating
        // (c_baseanimating.cpp:176, RecvPropInt(RECVINFO(m_nSkin))) and was not retained, so TF2's
        // team colouring — which is two skin families of one model, not a tint — went unchecked
        // against the SDK, which is what this list buys.
        IReadOnlyDictionary<string, IReadOnlyList<string>> retained =
            EntityState.NetworkedProperties;

        retained.ShouldContainKey(AnimatingTable);

        retained[AnimatingTable].ShouldBe(
            [
                "m_nSequence", "m_nBody", "m_flPlaybackRate",
                "m_flModelScale", "m_nSkin",
            ],
            ignoreOrder: true);

        // **m_flCycle is NOT on this table**, and pinning it here was this project asserting its
        // own mistake. baseanimating.cpp:223 puts it in a sub-table of its own, under the comment
        // "Sendtable for fields we don't want to send to clientside animating entities" — so a door
        // sends its cycle and a player, which calls UseClientSideAnimation, never does. Measured on
        // a real demo: 97 DT_ServerAnimationData.m_flCycle and no DT_BaseAnimating.m_flCycle.
        retained.ShouldContainKey("DT_ServerAnimationData");
        retained["DT_ServerAnimationData"].ShouldBe(["m_flCycle"]);
    }
}
