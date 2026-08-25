using System;
using System.IO;
using System.Linq;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Opening the installed game once, and what a viewer does when there is not one.
/// </summary>
/// <remarks>
/// **This was scattered through <c>MainForm.ReadMap</c>'s "first map only" branch** — the archives,
/// the FGD palette, the class models, the weapon schema and the sound reader, all opened inline the
/// first time a map was read (B188, D90). None of it is per-map and none of it is window work; it is
/// what the INSTALL provides, opened once and asked many times.
///
/// **Every case here runs without TF2**, which is the point: the viewer is meant to open a demo on a
/// machine that has never had the game, and every one of these degrades to "cannot say" rather than
/// throwing. That path had no test at all, and it is the one a fresh clone takes.
/// </remarks>
public sealed class GameContentTests
{
    [Test]
    public void Open_WithNoGameFolder_SucceedsAndSaysWhatIsMissing()
    {
        // **A viewer with no TF2 still opens demos**, draws the map outline, and reports what it
        // could not find. Refusing here would refuse exactly the salvage cases this project exists
        // for.
        RecordingLoggerFactory loggers = new();

        GameContent install = GameContent.Open(folder: null, loggers);

        install.Archives.IsEmpty.ShouldBeTrue();
        install.EntityClasses.ShouldBeNull();
        install.Classes.ShouldBeNull();
    }

    [Test]
    public void Open_WithNoGameFolder_StillAnswersEveryQuestionRatherThanThrowing()
    {
        // **The null-object half.** Callers ask these on every map read and every frame; a null here
        // is the shape that hid three missed wirings in one day (B193), so the install answers
        // instead of handing back nulls to be checked.
        GameContent install = GameContent.Open(folder: null, new RecordingLoggerFactory());

        Should.NotThrow(() => install.Weapons.For(default));
        Should.NotThrow(() => install.ModelPaths().ToList());
    }

    [Test]
    public void Open_WithAFolderThatHasNoBin_ReportsThePaletteIsAbsentRatherThanFailing()
    {
        // **The FGDs are EDITOR data**, so a dedicated-server or content-only copy has none. Losing
        // them costs one colour in one diagnostic view, which must not interrupt opening a demo —
        // but it is said out loud, because "entities draw as brushwork" is otherwise indistinguishable
        // from the colouring being broken.
        string folder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "tf");
        RecordingLoggerFactory loggers = new();

        GameContent install = GameContent.Open(folder, loggers);

        install.EntityClasses.ShouldBeNull();
        loggers.Recorder.Count("entities draw as brushwork").ShouldBe(1);
    }

    [Test]
    public void Open_WithNoLoggers_Refuses()
    {
        // Everything this reports is reported rather than returned, so a null sink is a caller
        // mistake rather than a quiet mode.
        Should.Throw<ArgumentNullException>(() => GameContent.Open(folder: null, loggers: null!));
    }
}
