using System.Collections.Generic;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Gameplay entities a review tool reads: buildings, the medigun, and the spectator camera.
/// </summary>
/// <remarks>
/// **Fourteenth batch, and two of these are era hazards rather than missing features** — cases where
/// a value's meaning depends on which build recorded the demo, which is the axis this whole project
/// exists to handle.
///
/// The observer enum had a value inserted into its MIDDLE, so every mode after it renumbered. The
/// medigun's charge level is networked twice at two different precisions, and which one a demo
/// carries depends on whether the recording player was holding it.
///
/// Neither fails loudly. Both produce a number.
/// </remarks>
public sealed class UnimplementedGameplayEntityConformanceTests
{
    /// <summary>Where the observer modes are declared.</summary>
    private const string SharedDefs = "src/game/shared/shareddefs.h";

    /// <summary>The medigun, whose charge level is networked twice.</summary>
    private const string Medigun = "src/game/shared/tf/tf_weapon_medigun.cpp";

    /// <summary>The base building entity.</summary>
    private const string BaseObject = "src/game/client/tf/c_baseobject.cpp";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void Gameplay_TheObserverEnum_HadAValueInsertedIntoItsMiddle()
    {
        // shareddefs.h:499, and Valve's own comment is the finding:
        //
        //   OBS_MODE_POI,  // PASSTIME point of interest - ... added in the middle of the enum
        //                  // due to tons of hard-coded "<ROAMING" enum compares
        //
        // **A new value went into the middle of an enumeration on purpose**, because the code around
        // it compares `< OBS_MODE_ROAMING` and appending would have broken those comparisons. The
        // cost was moved onto the wire instead: every mode at or after POI has a different integer
        // than it did before PASSTIME shipped.
        //
        // **For this project that is an era hazard, not a trivium.** A demo recorded before that
        // change encodes ROAMING as one number and a later demo encodes it as another, so a decoder
        // with a hardcoded value silently reports the wrong camera mode on one era — and reports it
        // as a perfectly ordinary mode, not as an error.
        //
        // Directly relevant to POV and SourceTV demos, where the observer mode is what the recording
        // was actually showing.
        string defs = SourceSdk.Text(SharedDefs).ShouldNotBeNull();

        defs.ShouldContain("added in the middle of the enum due to tons of hard-coded");

        // The ordering the insertion changed: POI sits between CHASE and ROAMING, so ROAMING is not
        // the number it once was.
        //
        // Read from the text rather than through Enumerators because this enum is ANONYMOUS and has
        // no type name to ask for. Written unconditionally on purpose — the first draft guarded this
        // behind `if (modes.Count == 0)`, which would have quietly asserted nothing the moment that
        // lookup returned anything at all. A branch around an assertion is a test that can stop
        // testing without ever failing.
        int chase = defs.IndexOf("OBS_MODE_CHASE", System.StringComparison.Ordinal);
        int poi = defs.IndexOf("OBS_MODE_POI", System.StringComparison.Ordinal);
        int roaming = defs.IndexOf("OBS_MODE_ROAMING", System.StringComparison.Ordinal);

        chase.ShouldBeGreaterThan(0);
        poi.ShouldBeGreaterThan(chase);
        roaming.ShouldBeGreaterThan(poi);

        Assert.Ignore(
            "observer mode is not decoded, and its numbering is era-dependent: OBS_MODE_POI was " +
            "inserted into the MIDDLE of the enum (shareddefs.h:499), renumbering every mode after " +
            "it. A hardcoded value reports the wrong camera mode on one era, as an ordinary value.");
    }

    [Test]
    public void Gameplay_TheUberchargeLevel_IsSentTwiceAtTwoPrecisions()
    {
        // tf_weapon_medigun.cpp, three tables, and the split is the point:
        //
        //   DT_LocalTFWeaponMedigunData      "Only sent when a player's holding it"
        //     SendPropFloat( m_flChargeLevel, 0, SPROP_NOSCALE | SPROP_CHANGES_OFTEN )
        //
        //   DT_TFWeaponMedigunDataNonLocal   "sent at low precision to non-holding observers"
        //     SendPropFloat( m_flChargeLevel, 12, SPROP_NOSCALE | SPROP_CHANGES_OFTEN, 0.0, 100.0f )
        //
        //   DT_WeaponMedigun                 the direct send is COMMENTED OUT
        //
        // **The same field, two encodings, and which one a demo carries depends on who recorded it.**
        // A POV demo by the Medic gets the unquantised value; everyone else — including SourceTV —
        // gets 12 bits over a 0..100 range. A decoder that assumes one shape reads the other
        // incorrectly, and the result is a plausible percentage either way.
        //
        // The commented-out line in the always-sent table matters too: it means there is no
        // unconditional charge level to fall back on. If neither sub-table was sent, the value is
        // simply absent, which is not the same as zero.
        string medigun = SourceSdk.Text(Medigun).ShouldNotBeNull();

        medigun.ShouldContain("BEGIN_NETWORK_TABLE_NOBASE( CWeaponMedigun, DT_LocalTFWeaponMedigunData )");
        medigun.ShouldContain("BEGIN_NETWORK_TABLE_NOBASE( CWeaponMedigun, DT_TFWeaponMedigunDataNonLocal )");
        medigun.ShouldContain("SendPropFloat( SENDINFO(m_flChargeLevel), 12, SPROP_NOSCALE | SPROP_CHANGES_OFTEN, 0.0, 100.0f )");

        Assert.Ignore(
            "übercharge level is not decoded. It is networked twice — full precision to the holder, " +
            "12 bits over 0..100 to observers — so a POV demo and a SourceTV demo of the same game " +
            "carry different encodings of the same field, and neither is a fallback for the other.");
    }

    [Test]
    public void Gameplay_ABuilding_CarriesUpgradeStateAndWhetherItIsSapped()
    {
        // c_baseobject.cpp:50-70 — health, m_bHasSapper, m_bBuilding, m_flPercentageConstructed,
        // m_iUpgradeLevel, m_iUpgradeMetal, m_iUpgradeMetalRequired.
        //
        // **Engineer play is most of what a reviewer would want from an Engineer demo**, and none of
        // it is read here. A sentry at level 1 and the same sentry at level 3 are the same entity
        // with a different m_iUpgradeLevel; a sapped building is the same entity with a flag set.
        //
        // Worth noting for whoever implements it: m_flPercentageConstructed is separate from
        // m_bBuilding, so "is it up yet" and "how far along" are two fields, and a building can be
        // complete while still being upgraded.
        string building = SourceSdk.Text(BaseObject).ShouldNotBeNull();

        foreach (string field in new[]
        {
            "m_bHasSapper", "m_bBuilding", "m_flPercentageConstructed",
            "m_iUpgradeLevel", "m_iUpgradeMetal", "m_iUpgradeMetalRequired",
        })
        {
            building.ShouldContain($"RECVINFO({field})");
        }

        Assert.Ignore(
            "building state is not decoded. Upgrade level, sapper, construction progress and metal " +
            "are all on the entity (c_baseobject.cpp:50-70) — without them an Engineer demo shows " +
            "props that never change.");
    }

    [Test]
    public void Gameplay_TheCameraTarget_IsAnEntityHandleNotAPlayerIndex()
    {
        // c_baseplayer.cpp:300-301 — m_iObserverMode with a receive proxy, and m_hObserverTarget as
        // an EHandle rather than an index.
        //
        // **An EHandle is an index plus a serial number**, and this project already learned what
        // masking one down to its index does: the entity-handle work recorded that masking first
        // turns an invalid handle into 2047, a legal index. The same mistake here would point the
        // camera at whatever entity happens to occupy that slot rather than reporting "nobody".
        //
        // For a SourceTV demo this is the single most useful field about the recording itself: it
        // says who the camera was following at each moment, which no amount of geometry recovers.
        string player = SourceSdk.Text("src/game/client/c_baseplayer.cpp").ShouldNotBeNull();

        player.ShouldContain("RecvPropInt		(RECVINFO(m_iObserverMode), 0, RecvProxy_ObserverMode )");
        player.ShouldContain("RecvPropEHandle	(RECVINFO(m_hObserverTarget), RecvProxy_ObserverTarget )");

        Assert.Ignore(
            "the observer target is not decoded. It is an EHandle, so the serial number has to be " +
            "checked rather than masked away — masking turns 'nobody' into a legal index, which " +
            "points the camera at an arbitrary entity instead of reporting no target.");
    }
}
