using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// The damage bit word carried by <c>player_hurt</c> and <c>player_death</c>.
/// </summary>
/// <remarks>
/// **Seventeenth batch.** The kill feed batch recorded that <c>damagebits</c> is one of eight fields
/// on a death event that nothing here reads. This is what is inside it.
///
/// **Thirty-one flags in one integer, and the last usable bit is 29.** That headroom is the part
/// worth knowing before anything stores this: two bits are still free, and Valve marks the end of
/// the shared range explicitly with <c>DMG_LASTGENERICFLAG</c> so a game can add its own above it
/// without colliding.
///
/// The same prefix trap as the death flags appears here too, and it is now the third instance in
/// this project — a naming convention is not a category.
/// </remarks>
public sealed class DamageBitConformanceTests
{
    /// <summary>Where the damage bits are declared.</summary>
    private const string SharedDefs = "src/game/shared/shareddefs.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void DamageBits_EveryFlag_IsADistinctBitAndGenericIsNotOne()
    {
        // shareddefs.h — DMG_GENERIC is 0, "generic damage", and every other DMG_ is (1 << n).
        //
        // **DMG_GENERIC shares the prefix and is not a flag**, exactly like TF_DEATH_ANIMATION_TIME
        // among the death flags and TF_FLAGINFO_HOME among the flag states. Third instance in this
        // project of a value collected by prefix that is not a member of the set the prefix
        // suggests. Testing `value != 0` before treating an entry as a bit is the cheap defence.
        //
        // DMG_LASTGENERICFLAG is excluded for a different reason: it is an ALIAS for DMG_BUCKSHOT,
        // marking where the shared range ends, so it duplicates a bit legitimately. Including it
        // would make the disjointness check fail against correct data — which is the kind of
        // false positive that gets a good assertion deleted.
        IReadOnlyDictionary<string, int> defs = SourceSdk.Constants(SharedDefs);

        List<KeyValuePair<string, int>> flags =
        [
            .. defs.Where(entry =>
                entry.Key.StartsWith("DMG_", StringComparison.Ordinal) &&
                entry.Key != "DMG_GENERIC" &&
                entry.Key != "DMG_LASTGENERICFLAG"),
        ];

        flags.Count.ShouldBeGreaterThan(25);
        defs["DMG_GENERIC"].ShouldBe(0);

        int union = 0;

        foreach ((string name, int value) in flags)
        {
            (value & (value - 1)).ShouldBe(0, $"{name} is not a single bit");
            (union & value).ShouldBe(0, $"{name} reuses a bit already claimed");
            union |= value;
        }
    }

    [Test]
    public void DamageBits_TheSharedRange_LeavesRoomForAGameToExtendIt()
    {
        // DMG_BUCKSHOT is (1<<29) and DMG_LASTGENERICFLAG is defined as DMG_BUCKSHOT — an alias
        // rather than a new value, marking where the engine's own flags stop.
        //
        // **Bits 30 and 31 are left free on purpose**, which is why a game's private damage types do
        // not collide with the shared ones. Bit 31 is the sign bit of a 32-bit int, so anything
        // reading this word into a signed type and comparing it as a number rather than masking it
        // has a trap waiting exactly there.
        //
        // Derived: the alias must equal the flag it names, and the highest shared bit must leave
        // room. Asserting "29" alone would say nothing about why 29.
        IReadOnlyDictionary<string, int> defs = SourceSdk.Constants(SharedDefs);

        defs["DMG_LASTGENERICFLAG"].ShouldBe(defs["DMG_BUCKSHOT"]);

        int highest = defs
            .Where(entry => entry.Key.StartsWith("DMG_", StringComparison.Ordinal))
            .Max(entry => entry.Value);

        highest.ShouldBe(defs["DMG_BUCKSHOT"]);
        highest.ShouldBeLessThan(1 << 30);

        Assert.Ignore(
            "damage bits are not decoded. 30 flags in one word ending at DMG_BUCKSHOT (1<<29), " +
            "with bits 30 and 31 deliberately free for a game's own types — and bit 31 is the sign " +
            "bit, so this word must be masked rather than compared as a number.");
    }
}
