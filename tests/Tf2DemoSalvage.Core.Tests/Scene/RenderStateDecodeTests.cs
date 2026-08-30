using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>m_clrRender</c>, <c>m_nRenderFX</c> and <c>m_nRenderMode</c> off a hand-built entity.
/// </summary>
/// <remarks>
/// **Synthetic rather than corpus, deliberately, and the owner's rule is that this is the DEFAULT
/// rather than the fallback**: *"they are suppose to be used above real demos in tests"*.
/// `SyntheticDemo`'s own remarks make the argument — a corpus test does not know the right answer
/// and has to compare two readings of the same file, while a hand-built entity HAS ground truth,
/// because this test put the values there.
///
/// It also runs everywhere. `CorpusRenderModeTests` needs `lcor` and skips on CI, a fresh clone and
/// the fast gate; this needs nothing, so the decode is covered on every run rather than on the runs
/// that happen to have 774 MB of demos on disk.
///
/// **The two are not redundant.** This one asks *"does the decode read the field"*; the corpus one
/// asks *"do real matches contain it"*, which no fixture can answer — and the answer turned out to
/// be 410 of 1,973 entities not fully opaque, with 118 at <c>kRenderNone</c>.
/// </remarks>
public sealed class RenderStateDecodeTests
{
    /// <summary>Where all three live: <c>baseentity.cpp:276-279</c>.</summary>
    private const string BaseEntity = "DT_BaseEntity";

    [Test]
    public void RenderAlpha_FromAPackedColour_IsTheTopByte()
    {
        // **`color32` is `byte r, g, b, a` (`tier0/basetypes.h:248`) sent as one 32-bit int**
        // (`SendPropInt(SENDINFO(m_clrRender), 32, SPROP_UNSIGNED)`), so on a little-endian machine
        // red is the LOW byte and alpha the high one.
        //
        // The four channels are deliberately all different here. A fixture using 255,255,255,128
        // cannot tell the top byte from the bottom three, and reading the wrong end is the exact
        // mistake that tints every entity while the alpha reads as a colour channel.
        //   r = 0x11, g = 0x22, b = 0x33, a = 0x80  ->  0x80332211
        EntityState state = Entity(
            Property("m_clrRender", unchecked((int)0x80332211)));

        state.RenderAlpha().ShouldBe((byte)0x80);
    }

    [Test]
    public void RenderAlpha_WhenNeverSent_IsOpaque()
    {
        // **Absent means opaque, not unknown.** A delta-compressed format sends only what changed,
        // and an entity nobody has tinted is solid — so a null here would make every ordinary
        // entity a special case at the call site
        // (`docs/memory/sentinels-conflate-unknown-with-answer.md`).
        Entity().RenderAlpha().ShouldBe((byte)255);
    }

    [Test]
    public void RenderFxAndMode_WhenSent_AreRead()
    {
        // Both are 8 bits unsigned. Given DIFFERENT values, because they sit next to each other in
        // the send table and reading one for the other is the plausible mistake.
        EntityState state = Entity(
            Property("m_nRenderFX", 12),
            Property("m_nRenderMode", 10));

        state.RenderFx().ShouldBe(12);
        state.RenderMode().ShouldBe(10);
    }

    [Test]
    public void RenderFxAndMode_WhenNeverSent_AreNull()
    {
        // **Null rather than zero, and the caller applies the default.** Zero is `kRenderFxNone`
        // and `kRenderNormal` — both legitimate values — so the accessor reports what the demo
        // said and `ScenePose` decides what silence means. Conflating them here would make "the
        // entity is normal" and "the entity never mentioned it" the same answer at the one place
        // that can still tell them apart.
        Entity().RenderFx().ShouldBeNull();
        Entity().RenderMode().ShouldBeNull();
    }

    [Test]
    public void RenderAlpha_AFullyTransparentEntity_ReadsZeroRatherThanOpaque()
    {
        // **The case the "absent means opaque" default must not swallow.** An alpha of zero is a
        // real value — an entity told to be invisible — and an implementation that treated a
        // missing OR zero alpha as 255 would draw exactly the things the engine hides.
        EntityState state = Entity(Property("m_clrRender", 0x00FFFFFF));

        state.RenderAlpha().ShouldBe((byte)0);
    }

    [Test]
    public void ObserverMode_WhenSentAndWhenNot_IsReadOrDefaultsToNone()
    {
        // **Here because `CorpusObserverModeTests` was standing in for it, and should not have
        // been** (D38). That suite asserted which observer modes a real recording contains — a
        // claim about TF2 rather than about this decode — needed Git LFS, and lived in
        // `Corpus.Tests`, which Stryker never mutates. It is a `*Diagnostic` now and this is the
        // test.
        //
        // `m_iObserverMode` is on `DT_BasePlayer` proper rather than the local-player table
        // (`player.cpp:8184`), so it arrives for every player in any recording. Absent means
        // `OBS_MODE_NONE`, which is zero and the ordinary case.
        EntityState observing = Player(PlayerProperty("m_iObserverMode", 7));

        observing.ObserverMode().ShouldBe(7, "OBS_MODE_ROAMING, where TF2 puts a spectator");

        Player().ObserverMode().ShouldBeNull(
            "absent is reported as absent; ScenePlayer decides that it means OBS_MODE_NONE");
    }

    /// <summary>An entity carrying the given <c>DT_BasePlayer</c> properties.</summary>
    private static EntityState Player(params DecodedProperty[] properties)
    {
        EntityStateTable table = new(EntityBaselines.None);

        table.Apply(new DecodedEntity(
            2, ClassId: 0, SerialNumber: 1, EntityUpdateType.Enter, properties));

        table.TryGet(2, out EntityState? state).ShouldBeTrue();

        return state;
    }

    private static DecodedProperty PlayerProperty(string name, int value) =>
        new(0, new FlatProperty(
                new SendProperty(SendPropType.Int, name, 0, string.Empty, 0f, 0f, 32, 0),
                "DT_BasePlayer",
                null),
            PropertyValue.FromInt(value));

    /// <summary>An entity carrying the given <c>DT_BaseEntity</c> properties and nothing else.</summary>
    private static EntityState Entity(params DecodedProperty[] properties)
    {
        EntityStateTable table = new(EntityBaselines.None);

        table.Apply(new DecodedEntity(
            1, ClassId: 0, SerialNumber: 1, EntityUpdateType.Enter, properties));

        table.TryGet(1, out EntityState? state).ShouldBeTrue();

        return state;
    }

    private static DecodedProperty Property(string name, int value) =>
        new(0, new FlatProperty(
                new SendProperty(SendPropType.Int, name, 0, string.Empty, 0f, 0f, 32, 0),
                BaseEntity,
                null),
            PropertyValue.FromInt(value));
}
