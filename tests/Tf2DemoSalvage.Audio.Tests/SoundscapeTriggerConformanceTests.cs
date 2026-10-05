using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>Triggerable soundscapes as the SERVER runs them, read from <c>game/server/soundscape.cpp</c> (B483).</summary>
/// <remarks>
/// **Nothing here reaches a SourceTV client, and a POV client receives only the result for the recorder.** The trigger
/// list (<c>CBasePlayer::m_hTriggerSoundscapeList</c>) and the touch state are server-only; what a client receives is
/// the <c>audioparams_t</c> they write, in <c>DT_LocalPlayerExclusive</c>, sent to the owning player alone. The viewer
/// listens at its camera, so it plays the server's part from the map, as it does for the radius contest (B173).
/// </remarks>
public sealed class SoundscapeTriggerConformanceTests
{
    private static readonly SoundscapeCatalog Catalog = SoundscapeCatalog.Load(
        path => path switch
        {
            "scripts/soundscapes_manifest.txt" =>
                Encoding.UTF8.GetBytes("\"soundscapes_manifest\"\n{\n    \"file\"    \"scripts/soundscapes_test.txt\"\n}\n"),
            "scripts/soundscapes_test.txt" =>
                Encoding.UTF8.GetBytes("\"test.first\"\n{\n}\n\"test.second\"\n{\n}\n"),
            _ => null,
        });

    /// <summary>Model 0 is the world; *1 and *2 are the triggers' brushes, head nodes 11 and 22.</summary>
    private static readonly IReadOnlyList<BspModel> Models =
    [
        new(default, default, default, 0, 0, 0),
        new(default, default, default, 11, 0, 0),
        new(default, default, default, 22, 0, 0),
    ];

    private static IReadOnlyList<BspEntity> Entities(string lump) => BspEntities.Parse(Encoding.UTF8.GetBytes(lump));

    private static string Triggerable(string name, string soundscape, string origin = "0 0 0") =>
        $"{{\n\"classname\" \"env_soundscape_triggerable\"\n\"targetname\" \"{name}\"\n\"soundscape\" \"{soundscape}\"\n" +
        $"\"origin\" \"{origin}\"\n\"radius\" \"128\"\n}}\n";

    private static string Trigger(string model, string soundscape, string extra = "") =>
        $"{{\n\"classname\" \"trigger_soundscape\"\n\"model\" \"{model}\"\n\"soundscape\" \"{soundscape}\"\n" +
        $"\"origin\" \"100 200 300\"\n{extra}}}\n";

    /// <summary>Two triggerables (outer, inner) and a trigger for each, *1 to outer and *2 to inner.</summary>
    private static SoundscapePlacements TwoNested() => SoundscapePlacements.From(
        Entities(
            Triggerable("outer", "test.first") + Triggerable("inner", "test.second") +
            Trigger("*1", "outer") + Trigger("*2", "inner")),
        Catalog,
        models: Models);

    /// <remarks>
    /// **A triggerable contends by radius like any soundscape.** It is a <c>CEnvSoundscape</c>
    /// (<c>soundscape.h:93</c>), so its constructor lists it (<c>soundscape.cpp:108</c>), and
    /// <c>FrameUpdatePostEntityThink</c> calls <c>UpdateForPlayer</c> on every listed entity in the listener's cluster
    /// (<c>soundscape_system.cpp:296-369</c>). Its <c>Think</c> override (<c>soundscape.cpp:461-464</c>) stops nothing,
    /// because the contest is not a think.
    /// </remarks>
    [Test]
    public void From_ATriggerable_IsPlacedInTheRadiusContest()
    {
        SoundscapePlacement placed = SoundscapePlacements.From(
            Entities(Triggerable("lake", "test.second", "5 6 7")), Catalog).Placements.ShouldHaveSingleItem();

        placed.Name.ShouldBe("test.second");
        placed.Index.ShouldBe(1);
        placed.X.ShouldBe(5f);
        placed.Radius.ShouldBe(128f);
    }

    /// <remarks>
    /// <c>CTriggerSoundscape::Activate</c>: <c>dynamic_cast&lt; CEnvSoundscapeTriggerable* &gt;( gEntList.FindEntityByName(
    /// NULL, m_SoundscapeName ) )</c> (<c>soundscape.cpp:538-544</c>) — the FIRST entity of the name, and only if it is
    /// a triggerable. A trigger naming a plain <c>env_soundscape</c> has a null handle, so its touches do nothing
    /// (<c>:510-526</c>). Its brush is its own model, <c>*N</c>, placed at its <c>origin</c>.
    /// </remarks>
    [Test]
    public void From_ATriggerSoundscape_LinksOnlyATriggerableOfThatName()
    {
        SoundscapePlacements placements = SoundscapePlacements.From(
            Entities(
                "{\n\"classname\" \"env_soundscape\"\n\"targetname\" \"plain\"\n\"soundscape\" \"test.first\"\n\"origin\" \"0 0 0\"\n}\n" +
                Triggerable("lake", "test.second") +
                Trigger("*1", "plain") +
                Trigger("*2", "lake")),
            Catalog,
            models: Models);

        SoundscapeTrigger trigger = placements.Triggers.ShouldHaveSingleItem("the trigger naming a plain soundscape links nothing");

        trigger.Soundscape.ShouldBe(1, "the triggerable is placement 1, after the plain one");
        trigger.HeadNode.ShouldBe(22, "*2's head node");
        (trigger.X, trigger.Y, trigger.Z).ShouldBe((100f, 200f, 300f));
        trigger.Enabled.ShouldBeTrue();
    }

    /// <remarks>
    /// <c>CBaseTrigger::InitTrigger</c>: <c>if (m_bDisabled) RemoveSolidFlags( FSOLID_TRIGGER )</c>
    /// (<c>triggers.cpp:352-355</c>) — a trigger that starts disabled touches nothing. Its Enable input is entity I/O,
    /// which no demo records, so only the initial state is knowable.
    /// </remarks>
    [Test]
    public void Touch_AStartDisabledTrigger_NeverWritesTheParams()
    {
        SoundscapePlacements placements = SoundscapePlacements.From(
            Entities(Triggerable("lake", "test.second") + Trigger("*1", "lake", "\"StartDisabled\" \"1\"\n")),
            Catalog,
            models: Models);

        placements.Triggers.ShouldHaveSingleItem().Enabled.ShouldBeFalse();
        placements.Touch(new SoundscapeTouches(), _ => true, current: null).ShouldBeNull();
    }

    /// <remarks>
    /// <c>DelegateStartTouch</c> (<c>soundscape.cpp:417-430</c>): the triggerable goes to the HEAD of the player's list
    /// and writes its params — the listener is now in its soundscape, whatever was current.
    /// </remarks>
    [Test]
    public void Touch_Entering_WritesTheTriggerablesParams()
    {
        SoundscapePlacements placements = TwoNested();

        SoundscapePlacement? current = placements.Touch(
            new SoundscapeTouches(), trigger => trigger.HeadNode == 11, current: null);

        current.ShouldNotBeNull().Name.ShouldBe("test.first");
        current.Value.Id.ShouldBe(0);
    }

    /// <remarks>
    /// **A touch is an EDGE, not a level.** The params are written on start touch only; standing inside writes nothing,
    /// so a soundscape the radius contest chose since (<c>soundscape.cpp:296-311</c>) stays chosen.
    /// </remarks>
    [Test]
    public void Touch_StayingInside_LeavesWhatTheContestChose()
    {
        SoundscapePlacements placements = TwoNested();
        SoundscapeTouches touches = new();
        SoundscapePlacement other = placements.Placements[1];

        placements.Touch(touches, trigger => trigger.HeadNode == 11, current: null);

        placements.Touch(touches, trigger => trigger.HeadNode == 11, other).ShouldBe(other);
    }

    /// <remarks>
    /// <c>DelegateEndTouch</c> (<c>soundscape.cpp:433-458</c>): removed from the list, then the new head writes its
    /// params. Leaving the inner of two nested triggers puts the listener back in the outer's soundscape.
    /// </remarks>
    [Test]
    public void Touch_LeavingTheInnerOfTwo_RestoresTheOuter()
    {
        SoundscapePlacements placements = TwoNested();
        SoundscapeTouches touches = new();

        placements.Touch(touches, trigger => trigger.HeadNode == 11, current: null);
        placements.Touch(touches, _ => true, placements.Placements[0])
            .ShouldNotBeNull().Name.ShouldBe("test.second", "entering the inner makes it the head");

        placements.Touch(touches, trigger => trigger.HeadNode == 11, placements.Placements[1])
            .ShouldNotBeNull().Name.ShouldBe("test.first", "the outer is the head again");
    }

    /// <remarks>
    /// <c>pPlayer-&gt;GetAudioParams().entIndex = 0;</c> (<c>soundscape.cpp:457</c>) when the list empties: the player
    /// has NO current soundscape, so the next update's contest starts from nothing (<c>soundscape_system.cpp:332-338</c>)
    /// — and the client, told of no entity, starts nothing and lets the loops play on (B484).
    /// </remarks>
    [Test]
    public void Touch_LeavingTheLastTrigger_LeavesNoCurrentSoundscape()
    {
        SoundscapePlacements placements = TwoNested();
        SoundscapeTouches touches = new();

        placements.Touch(touches, trigger => trigger.HeadNode == 11, current: null).ShouldNotBeNull();

        placements.Touch(touches, _ => false, placements.Placements[0]).ShouldBeNull();
    }
}
