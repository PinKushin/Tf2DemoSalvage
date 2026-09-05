using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <see cref="EntityState"/>'s <c>m_audio</c> accessors, and the type check under them (B335).
/// </summary>
/// <remarks>
/// **Written alongside `SceneSoundscapeTests`, which tests the record these four fill.** The record
/// had no `Core.Tests` coverage at all and neither did the accessors — a whole feature tested only
/// through a corpus suite whose coverage nothing measures (B335).
///
/// **`m_audio` is private per-player data and most demos do not carry it**: it sits in `DT_Local`,
/// which reaches the wire through `SendProxy_SendLocalDataTable` — `pRecipients->SetOnly( objectID
/// - 1 )`, one recipient. So a point-of-view recording carries the recorder's soundscape and a
/// SourceTV recording carries nobody's. A corpus test asserting these therefore measures which demo
/// it happened to open; a synthetic one measures the reader.
/// </remarks>
public sealed class EntitySoundscapeStateTests
{
    private const string Local = "DT_Local";

    [Test]
    public void SoundscapeIndex_APlayerCarryingOne_ReportsIt()
    {
        EntityState player = Player();
        player.Set($"{Local}.m_audio.soundscapeIndex", PropertyValue.FromInt(12));

        player.SoundscapeIndex().ShouldBe(12);
    }

    /// <remarks>
    /// **-1 is the engine's "none" and is a REAL value**, not an absence: `CEnvSoundscape` starts
    /// there (`soundscape.cpp:105`). A reader collapsing it to null would make "the player is in no
    /// soundscape" indistinguishable from "this demo does not carry the field", and the second is
    /// true of every SourceTV recording.
    /// </remarks>
    [Test]
    public void SoundscapeIndex_TheEnginesNoneValue_IsMinusOneRatherThanNull()
    {
        EntityState player = Player();
        player.Set($"{Local}.m_audio.soundscapeIndex", PropertyValue.FromInt(-1));

        player.SoundscapeIndex().ShouldBe(-1);
    }

    /// <remarks>
    /// **The control: a demo that never sent the field answers null**, which is the case the
    /// paragraph above must stay distinct from.
    /// </remarks>
    [Test]
    public void SoundscapeIndex_AnEntityThatNeverSentIt_IsNull()
    {
        Player().SoundscapeIndex().ShouldBeNull();
    }

    /// <remarks>
    /// **Eight slots, sent as eight separate vectors rather than an array** —
    /// `NUM_AUDIO_LOCAL_SOUNDS` is 8 (`playernet_vars.h:16`). A soundscape's `"position" "3"` names
    /// slot three, which is how one soundscape scatters its loops across a whole map.
    /// </remarks>
    [Test]
    public void SoundscapePosition_ASlotThatWasSent_ReportsIt()
    {
        EntityState player = Player();
        player.Set($"{Local}.m_audio.localSound[3]", PropertyValue.FromVector(1f, 2f, 3f));

        player.SoundscapePosition(3).ShouldBe((1f, 2f, 3f));

        // The control: a neighbouring slot is not the one that was set, so the index is read rather
        // than ignored. An accessor returning slot 0 for everything would pass the line above.
        player.SoundscapePosition(2).ShouldBeNull();
        player.SoundscapePosition(4).ShouldBeNull();
    }

    /// <remarks>
    /// **Both ends of the eight**, because the guard is a range and a range fails two ways. Slot 7
    /// is the last real one, so 8 must be refused and 7 must not be.
    /// </remarks>
    [Test]
    public void SoundscapePosition_ASlotOutsideTheEight_IsNullRatherThanAKeyLookup()
    {
        EntityState player = Player();

        player.Set($"{Local}.m_audio.localSound[7]", PropertyValue.FromVector(7f, 7f, 7f));

        player.SoundscapePosition(7).ShouldBe((7f, 7f, 7f), "seven is the last real slot");
        player.SoundscapePosition(8).ShouldBeNull("eight is past the end");
        player.SoundscapePosition(-1).ShouldBeNull("and a negative slot is not a key");
    }

    /// <remarks>
    /// **A property of the wrong KIND is not a value**, which is the check that separates these
    /// accessors from a bare dictionary lookup. The wire's types come from the send table, so a
    /// schema change can put a float where a vector was — and reading `AsVector` off an int would
    /// give three numbers rather than an error.
    /// </remarks>
    [Test]
    public void SoundscapePosition_ASlotSentAsSomethingOtherThanAVector_IsNull()
    {
        EntityState player = Player();
        player.Set($"{Local}.m_audio.localSound[0]", PropertyValue.FromInt(5));

        player.SoundscapePosition(0).ShouldBeNull();
    }

    /// <remarks>
    /// The same kind check on the two integer accessors, which share <c>Integer</c>: a float in an
    /// integer's place must answer null rather than a truncation.
    /// </remarks>
    [Test]
    public void SoundscapeBitsAndEntity_SentAsFloats_AreNullRatherThanTruncated()
    {
        EntityState player = Player();

        player.Set($"{Local}.m_audio.localBits", PropertyValue.FromFloat(3.5f));
        player.Set($"{Local}.m_audio.entIndex", PropertyValue.FromFloat(9f));

        player.SoundscapePositionBits().ShouldBeNull();
        player.SoundscapeEntity().ShouldBeNull();

        // And the control, so the two assertions above are about the KIND rather than the keys.
        player.Set($"{Local}.m_audio.localBits", PropertyValue.FromInt(0b1011));
        player.Set($"{Local}.m_audio.entIndex", PropertyValue.FromInt(9));

        player.SoundscapePositionBits().ShouldBe(0b1011);
        player.SoundscapeEntity().ShouldBe(9);
    }

    /// <summary>A player entity carrying nothing yet.</summary>
    private static EntityState Player() => new(1, 0, 0, "CTFPlayer");
}
