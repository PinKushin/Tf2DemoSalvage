using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The list of sounds a recording will play, which is what a precache loads before playback.
/// </summary>
/// <remarks>
/// **Valve refuses to load audio during play, rather than merely preferring not to.**
/// <c>CBaseEntity::PrecacheSound</c> (<c>SoundEmitterSystem.cpp:1489</c>) opens with
/// <c>if ( !CBaseEntity::IsPrecacheAllowed() )</c> and asserts
/// <c>"CBaseEntity::PrecacheSound:  too late"</c>. So a viewer that decodes a voice line the first
/// time it is heard is doing something the engine treats as a programming error, and it shows up
/// exactly where B163's model packing did: a freeze in one frame with the frame rate unchanged.
///
/// Measured on cp_process 2026-08-25 — six of eleven slow frames dominated by the sound step at
/// 27-91 ms, against posing and drawing at 1.7-2.6 ms on the same frames.
/// </remarks>
public sealed class SoundPrecacheTests
{
    [Test]
    public void SoundNames_WithOneSoundPlayedRepeatedly_NamesItOnce()
    {
        // A footstep or a hit sound plays hundreds of times in a match. The precache wants the file
        // once; naming it per play would decode it hundreds of times or need the caller to dedupe.
        DemoTimeline timeline = DemoTimeline.ForSounds(
            [Sound("weapons/shotgun_shoot.wav"), Sound("weapons/shotgun_shoot.wav"),
             Sound("weapons/shotgun_shoot.wav")]);

        timeline.SoundsToPrecache().ShouldBe(["weapons/shotgun_shoot.wav"]);
    }

    [Test]
    public void SoundNames_WithSeveralDistinctSounds_NamesEveryOne()
    {
        // **The control for the dedupe above.** Without it, an implementation that returned only the
        // first sound — or nothing at all — would pass that test, since one name is all it predicts.
        DemoTimeline timeline = DemoTimeline.ForSounds(
            [Sound("weapons/shotgun_shoot.wav"), Sound("vo/soldier_PainSharp07.mp3"),
             Sound("ambient/machine_hum.wav")]);

        timeline.SoundsToPrecache().OrderBy(name => name, System.StringComparer.Ordinal).ShouldBe(
            ["ambient/machine_hum.wav", "vo/soldier_PainSharp07.mp3", "weapons/shotgun_shoot.wav"]);
    }

    [Test]
    public void SoundNames_WithTheSameNameInDifferentCase_NamesItOnce()
    {
        // The string table's casing is not consistent, and the cache this feeds is keyed
        // OrdinalIgnoreCase. A case-sensitive dedupe here would hand it two names for one file and
        // decode it twice, which is the cost this exists to remove.
        DemoTimeline timeline = DemoTimeline.ForSounds(
            [Sound("Weapons/Shotgun_Shoot.wav"), Sound("weapons/shotgun_shoot.wav")]);

        timeline.SoundsToPrecache().Count().ShouldBe(1);
    }

    [Test]
    public void SoundNames_WithAStopCommand_OmitsIt()
    {
        // A stop names a sound in order to silence it and plays nothing. Precaching it would read a
        // file to throw the samples away.
        DemoTimeline timeline = DemoTimeline.ForSounds(
            [Sound("ambient/machine_hum.wav", stop: true)]);

        timeline.SoundsToPrecache().ShouldBeEmpty();
    }

    [Test]
    public void SoundNames_WithASoundThatIsBothPlayedAndStopped_StillNamesIt()
    {
        // **The control for the stop above**, and the case a real demo always has: cp_process starts
        // six machine_hum loops at tick 4 and stops them at a round restart. An implementation that
        // rejected a name because ANY of its entries was a stop would drop the map's ambience from
        // the precache and put its decode back in a frame.
        DemoTimeline timeline = DemoTimeline.ForSounds(
            [Sound("ambient/machine_hum.wav"), Sound("ambient/machine_hum.wav", stop: true)]);

        timeline.SoundsToPrecache().ShouldBe(["ambient/machine_hum.wav"]);
    }

    [Test]
    public void SoundNames_WithAnEmptyName_OmitsIt()
    {
        // An entry whose name never resolved. Handing it on would make the precache report a failure
        // for a sound nothing was ever going to play.
        DemoTimeline timeline = DemoTimeline.ForSounds([Sound(""), Sound("ambient/hum.wav")]);

        timeline.SoundsToPrecache().ShouldBe(["ambient/hum.wav"]);
    }

    private static SceneSound Sound(string name, bool stop = false) =>
        new(
            Tick: 0,
            Name: name,
            SoundNumber: 0,
            EntityIndex: 0,
            Channel: 0,
            Volume: 1f,
            SoundLevel: 75,
            Pitch: 100,
            DelaySeconds: 0f,
            OriginX: 0f,
            OriginY: 0f,
            OriginZ: 0f,
            IsStop: stop);
}
