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
    public void RetainedProperties_TheAnimatingTable_KeepsEverythingThePoseNeeds()
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

        // **`m_flModelWidthScale` is the same value as `m_flModelScale` under TF2's pre-2013 wire
        // name**, kept by the engine as a second receiver into one member "for demo compatibility
        // only" (`c_baseanimating.cpp:181`). Four of the six era specimens send it and no send
        // table in the 2013 SDK declares it, so it is listed here and the SendProp conformance
        // denominator had to learn about `RECVINFO_NAME` for it (B271).
        // **`m_flEncodedController` is an ARRAY** and is listed by its bare name, which is how the
        // values arrive keyed — `m_flEncodedController.000` upward. Eleven bits each over nought
        // to one (`baseanimating.cpp:248`), read by `CalcBoneAdj` to bend one bone: a sentry's
        // barrel, a door's hinge (B287).
        retained[AnimatingTable].ShouldBe(
            [
                "m_nSequence", "m_nBody", "m_flPlaybackRate",
                "m_flModelScale", "m_flModelWidthScale", "m_nSkin",
                "m_flEncodedController",
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
