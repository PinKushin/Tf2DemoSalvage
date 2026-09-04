using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The <c>EffectDispatch</c> precache, which turns <c>m_iEffectName 3</c> into a name.
/// </summary>
/// <remarks>
/// **<c>CTEEffectDispatch</c> is a DISPATCHER, not an effect** — everything else in its record is
/// one effect's argument list, so without this table a dispatch says where something happened and
/// not what. Measured in `z1800`: 1,697 dispatches across seven distinct indices, which resolve to
/// `Impact`, `Tracer`, `TF_3rdPersonMuzzleFlash_SentryGun`, `ClientProjectile_Syringe`,
/// `TFBoltImpact`, `bloodimpact` and `ParticleEffect` (B305).
/// </remarks>
public sealed class EffectNamesTests
{
    [Test]
    public void Name_AfterTheCreateMessage_ResolvesByIndex()
    {
        EffectNames names = new();

        names.Add(new CreateStringTableMessage(
            EffectNames.TableName,
            MaxEntries: 512,
            Entries:
            [
                new StringTableEntry(0, "ParticleEffect", []),
                new StringTableEntry(3, "Impact", []),
            ],
            IsCompressed: false,
            UndecodedReason: null));

        names.Name(3).ShouldBe("Impact");
        names.Name(0).ShouldBe("ParticleEffect", "index zero is a real effect, not an absence");
    }

    /// <remarks>
    /// **Null rather than a placeholder**, so a caller can print "index 4, unnamed" instead of
    /// something that reads like a real effect. An index not held is a table that did not arrive.
    /// </remarks>
    [Test]
    public void Name_ForAnIndexNotHeld_IsNull()
    {
        EffectNames names = new();

        names.Add(new CreateStringTableMessage(
            EffectNames.TableName,
            MaxEntries: 512,
            Entries: [new StringTableEntry(1, "Tracer", [])],
            IsCompressed: false,
            UndecodedReason: null));

        names.Name(4).ShouldBeNull();
    }

    /// <remarks>
    /// **The control, and without it a reader that took EVERY table would pass the cases above.**
    /// `soundprecache` is the table sitting next to this one in a real demo, and its indices mean
    /// something entirely different — resolving an effect index against it would name a sound.
    /// </remarks>
    [Test]
    public void Add_AnotherTable_IsIgnored()
    {
        EffectNames names = new();

        names.Add(new CreateStringTableMessage(
            "soundprecache",
            MaxEntries: 16384,
            Entries: [new StringTableEntry(3, "weapons/rocket_shoot.wav", [])],
            IsCompressed: false,
            UndecodedReason: null));

        names.Count.ShouldBe(0);
        names.Name(3).ShouldBeNull("a sound is not an effect");
    }

    /// <remarks>
    /// **A late precache arrives as an UPDATE**, not a fresh create, so a resolver built only from
    /// the create message goes stale part way through a demo — the same reason `SoundNames` takes
    /// both.
    /// </remarks>
    [Test]
    public void Add_AnUpdateToTheEffectTable_IsTaken()
    {
        EffectNames names = new();

        names.Add(
            new UpdateStringTableMessage(
                TableId: 9,
                Entries: [new StringTableEntry(5, "TFBoltImpact", [])],
                UndecodedReason: null),
            EffectNames.TableName);

        names.Name(5).ShouldBe("TFBoltImpact");
    }

    [Test]
    public void Add_AnUpdateToAnotherTable_IsIgnored()
    {
        // The update carries only a table ID; the NAME is resolved by the caller from its own
        // state. Passing the wrong one must not write into this table — which is the failure a
        // single mistyped lookup would cause and nothing else would catch.
        EffectNames names = new();

        names.Add(
            new UpdateStringTableMessage(
                TableId: 9,
                Entries: [new StringTableEntry(5, "weapons/rocket_shoot.wav", [])],
                UndecodedReason: null),
            "soundprecache");

        names.Count.ShouldBe(0);
    }

    /// <remarks>
    /// **An entry with no text updates an existing one's USER DATA.** This table carries none, but
    /// taking the empty string anyway would blank an effect that had already arrived — and the
    /// symptom is an effect that is named early in a demo and numbered later.
    /// </remarks>
    [Test]
    public void Add_AnEntryWithNoText_LeavesTheNameItHad()
    {
        EffectNames names = new();

        names.Add(new CreateStringTableMessage(
            EffectNames.TableName,
            MaxEntries: 512,
            Entries: [new StringTableEntry(2, "Tracer", [])],
            IsCompressed: false,
            UndecodedReason: null));

        names.Add(
            new UpdateStringTableMessage(
                TableId: 1,
                Entries: [new StringTableEntry(2, null, [])],
                UndecodedReason: null),
            EffectNames.TableName);

        names.Name(2).ShouldBe("Tracer");
    }
}
