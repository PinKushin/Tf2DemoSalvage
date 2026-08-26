using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.GameSystems;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// Telling every system about a level, and about the one being torn down.
/// </summary>
/// <remarks>
/// **This was the wiring half of <c>MainForm.ReadMap</c> and <c>ClearMap</c>** (B188, D90), and it
/// is the class of code B193 and B196 keep breaking — a system that stops being told does not fail,
/// it keeps answering with whatever it last held.
///
/// **The tests assert on the SYSTEMS, not on the returned map.** A load that produced a perfectly
/// good <c>LoadedMap</c> and told nobody about it is precisely the defect this type exists to
/// prevent, and asserting the map came back would not notice it.
/// </remarks>
public sealed class LevelSystemsTests
{
    [Test]
    public void Systems_TheRegisteredList_IsTheThreeValveModelsAsGameSystems()
    {
        // **A count, because a system quietly dropped from the list is the regression.** Valve makes
        // the renderables builder, the soundscape and the sound emitter game systems
        // (`clientleafsystem.h:135`, `c_soundscape.cpp:78`, `SoundEmitterSystem.cpp:134`) and does
        // NOT make model geometry or the sound cache into ones — `IVModelInfo` and `IEngineSound`
        // are plain interfaces set up at init. So three, and the two absences are deliberate.
        IReadOnlyList<IGameSystem> systems = Systems().Systems;

        systems.Count.ShouldBe(3);

        systems.Select(system => system.Name)
            .ShouldBe(["clientleafsystem", "soundscape", "soundemitter"]);
    }

    [Test]
    public void Systems_ThePerFrameOnes_AreTheOnesValveMarksPerFrame()
    {
        // The leaf system and the soundscape derive from the PerFrame base in the SDK; the sound
        // emitter derives from the plain one. Getting this backwards would be invisible until
        // something needed a per-frame hook.
        IReadOnlyList<IGameSystem> systems = Systems().Systems;

        systems.Count(system => system is IGameSystemPerFrame).ShouldBe(2);

        systems.Single(system => system.Name == "soundemitter")
            .ShouldNotBeOfType<IGameSystemPerFrame>();
    }

    [Test]
    public void Shutdown_TellsEverySystemTheLevelIsGoing()
    {
        // **The half that did not exist before.** Teardown was split three ways: `ClearMap` reset
        // two systems, `Load` cleared one inline, and the sound schedule was never torn down at
        // all. This asserts all three are reached — by observing state each one owns.
        MomentScene moment = Scene();
        SoundscapeSystem soundscape = Soundscape();
        SoundPresenter sound = new(soundscape, new ActiveLoops(), _ => null, NullLogger.Instance);

        moment.Uploaded = true;
        soundscape.Leaves = null;
        sound.Schedule = null;

        new LevelSystems(
            moment, new EntityModelSet(), new SoundCache(NullLogger.Instance),
            soundscape, sound, NullLoggerFactory.Instance)
            .Shutdown();

        moment.Uploaded.ShouldBeFalse("the scene must forget that THIS level's geometry was uploaded");
    }

    [Test]
    public void Shutdown_TheGeometrySource_GoesBackToNothing()
    {
        // `EntityModelSet` is not a game system, so it is reset explicitly rather than walked — and
        // that explicitness is exactly what gets forgotten. Carried into the next map, the source
        // answers with the previous map's geometry.
        EntityModelSet models = new();

        models.Geometry = _ => null;

        Systems(models).Shutdown();

        models.Geometry.ShouldBeSameAs(EntityModelSet.NoGeometry);
    }

    [Test]
    public void Construct_WithoutASystem_Refuses()
    {
        // A null collaborator is a system that silently stops being told, which is the whole failure
        // mode — refused at construction, the earliest point where the caller still has a stack that
        // names the mistake.
        Should.Throw<ArgumentNullException>(() => new LevelSystems(
            null!, new EntityModelSet(), new SoundCache(NullLogger.Instance),
            Soundscape(), Sound(), NullLoggerFactory.Instance));

        Should.Throw<ArgumentNullException>(() => new LevelSystems(
            Scene(), new EntityModelSet(), new SoundCache(NullLogger.Instance),
            Soundscape(), Sound(), loggers: null!));
    }

    [Test]
    public void OpenGame_WithNoInstall_Refuses()
    {
        Should.Throw<ArgumentNullException>(() => Systems().OpenGame(null!));
    }

    [Test]
    public void OpenGame_WithNoArchives_LeavesTheCatalogNullRatherThanEmpty()
    {
        // **Null and empty are different claims.** An empty catalog says the install HAS no
        // soundscapes, which is a statement about TF2; null says we could not read it.
        SoundscapeSystem soundscape = Soundscape();

        new LevelSystems(
            Scene(), new EntityModelSet(), new SoundCache(NullLogger.Instance),
            soundscape, Sound(), NullLoggerFactory.Instance)
            .OpenGame(GameContent.Open(folder: null, NullLoggerFactory.Instance));

        soundscape.Catalog.ShouldBeNull();
    }

    [Test]
    public void Install_AskedTwice_OpensTheGameOnceAndAnswersTheSameContent()
    {
        // **The lazy open was `if (_game is null) { … }` inside `MainForm.ReadMap`** (B188, D90),
        // and it is deferred for a reason that is not slowness: the TF2 folder is not known until
        // the user points at it. Deferred-because-not-yet-knowable is not laziness and cannot be
        // made eager — but it does not need a window either.
        //
        // **Opening twice is not merely wasteful, it is wrong**: `OpenGame` destructures the content
        // into the sound cache, the weapon table and the soundscape catalog, so a second open would
        // rebuild all three mid-session and reload every catalog.
        int opened = 0;

        LevelSystems systems = Systems();

        GameContent first = systems.Install(() => { opened++; return null; });
        GameContent again = systems.Install(() => { opened++; return null; });

        opened.ShouldBe(1, "the install is located once, however many maps are read");
        again.ShouldBeSameAs(first);
    }

    [Test]
    public void Install_WithNoInstallFound_AnswersEmptyContentRatherThanThrowing()
    {
        // **The owner's requirement**: *"the program cant crash because its missing it must just
        // error and mention it"*. Empty content is a normal answer — the demo still plays, it just
        // loses the stock assets.
        GameContent content = Systems().Install(() => null);

        content.Archives.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Install_OnceOpened_HasHandedTheArchivesToTheSoundCache()
    {
        // **The positive half, and the one an "opens once" test cannot make.** `Install` is only
        // useful if it also does what `OpenGame` did — a version that cached the content and told
        // nobody would pass the case above perfectly and leave the viewer silent.
        SoundCache sounds = new(NullLogger.Instance);
        SoundscapeSystem soundscape = Soundscape();

        new LevelSystems(
            Scene(), new EntityModelSet(), sounds, soundscape,
            new SoundPresenter(soundscape, new ActiveLoops(), _ => null, NullLogger.Instance),
            NullLoggerFactory.Instance)
            .Install(() => null);

        sounds.Read.ShouldNotBeNull("Install must do OpenGame's work, not merely remember the content");
    }

    private static LevelSystems Systems(EntityModelSet? models = null)
    {
        SoundscapeSystem soundscape = Soundscape();

        return new LevelSystems(
            Scene(),
            models ?? new EntityModelSet(),
            new SoundCache(NullLogger.Instance),
            soundscape,
            new SoundPresenter(soundscape, new ActiveLoops(), _ => null, NullLogger.Instance),
            NullLoggerFactory.Instance);
    }

    private static MomentScene Scene() =>
        new(new EntityModelSet(), new ViewmodelScene(), NullLogger.Instance);

    private static SoundscapeSystem Soundscape() =>
        new(new ActiveLoops(), _ => null, NullLogger.Instance);

    private static SoundPresenter Sound() =>
        new(Soundscape(), new ActiveLoops(), _ => null, NullLogger.Instance);
}
