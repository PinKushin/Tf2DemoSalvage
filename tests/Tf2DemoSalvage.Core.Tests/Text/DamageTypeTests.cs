using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// The damage word, which cannot be read with base-game names in a TF2 demo.
/// </summary>
/// <remarks>
/// **TF2 aliases thirteen of the engine's `DMG_` bits to different meanings** —
/// `tf_shareddefs.h:1162-1175`:
///
/// <code>
/// #define DMG_CRITICAL          (DMG_ACID)             // 1 &lt;&lt; 20
/// #define DMG_USE_HITLOCATIONS  (DMG_AIRBOAT)          // 1 &lt;&lt; 25
/// #define DMG_MELEE             (DMG_BLAST_SURFACE)    // 1 &lt;&lt; 27
/// #define DMG_IGNITE            (DMG_PLASMA)           // 1 &lt;&lt; 24
/// #define DMG_HALF_FALLOFF      (DMG_RADIATION)        // 1 &lt;&lt; 18
/// </code>
///
/// So a decoder using the base names prints **"acid"** where TF2 means **critical**, and
/// **"airboat"** where it means hit locations. Both are plausible words attached to a real kill,
/// which is the failure mode this project keeps meeting.
///
/// **Verified against a real death rather than reasoned about.** `z1800.dem`'s first sampled kill
/// carries `damagebits=34603010` = bits {1, 20, 25}, and the same event carries
/// `customkill=1` (headshot) and `crit_type=2`. Under TF2's names those bits read **bullet,
/// critical, hit locations** — which is exactly what a critical Sniper headshot is. Under the base
/// game's they read bullet, acid, airboat.
///
/// **Two bits are genuinely ambiguous and are reported as the base meaning.** `DMG_IGNORE_MAXHEALTH`
/// is `DMG_BULLET` and `DMG_IGNORE_DEBUFFS` is `DMG_SLASH`, so bit 1 means both "shot" and "ignore
/// max health" with nothing in the word to separate them. Naming the damage kind is the useful half
/// and the modifier is unknowable from here, so the ambiguity is stated in the docs rather than
/// guessed at.
/// </remarks>
public sealed class DamageTypeTests
{
    [Test]
    public void ARealCriticalHeadshotReadsAsBulletCriticalAndHitLocations()
    {
        // The measured value from z1800.dem's first sampled death.
        KillDescription.DamageTypes(34603010)
            .ShouldBe("bullet, critical, hit locations");
    }

    [Test]
    public void TheTfMeaningWinsOverTheBaseGameName()
    {
        // Bit 20 alone. "acid" would be the engine's name for it and is wrong for every TF2 demo.
        KillDescription.DamageTypes(1 << 20).ShouldBe("critical");

        // Bit 25 alone — hit locations, not an airboat.
        KillDescription.DamageTypes(1 << 25).ShouldBe("hit locations");

        // Bit 27 — melee, not a blast on a water surface.
        KillDescription.DamageTypes(1 << 27).ShouldBe("melee");
    }

    [Test]
    public void OrdinaryDamageKindsKeepTheirEngineNames()
    {
        // The low bits TF2 does not alias mean what the engine says.
        KillDescription.DamageTypes(1 << 6).ShouldBe("blast");
        KillDescription.DamageTypes(1 << 3).ShouldBe("burn");
        KillDescription.DamageTypes(1 << 5).ShouldBe("fall");
    }

    [Test]
    public void NoBitsDescribesNothing()
    {
        KillDescription.DamageTypes(0).ShouldBeNull();
    }

    [Test]
    public void AnUnnamedBitIsReportedRatherThanDropped()
    {
        // Bit 30 is above DMG_BUCKSHOT, the last shared flag, and TF2 names nothing there. Reported
        // so a future addition is visible rather than silently absent.
        KillDescription.DamageTypes(1 << 30).ShouldBe("bit 0x40000000");
    }
}
