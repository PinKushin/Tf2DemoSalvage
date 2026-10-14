using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The owner of the viewer's settings (D214) [no engine counterpart: a ConVar there is global state; here one record is
/// replaced whole by one owner], from settings a test wrote and saved to a file a test owns.
/// </summary>
/// <remarks>
/// **Never the machine's settings file**: every presenter here starts from a record the test built and saves to a
/// path under the test's own folder, so nothing reads or rewrites what a person set.
/// </remarks>
public sealed class SettingsPresenterTests
{
    private string _folder = string.Empty;

    private string File_ => Path.Combine(_folder, "settings.cfg");

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "tf2salvage-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void RemoveFolder()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException held)
        {
            // A disposable temp folder: a file still held open is reported and left, and fails no test.
            TestContext.Out.WriteLine($"left {_folder} behind: {held.Message}");
        }
    }

    private (SettingsPresenter Settings, RecordingLogger Log) Owner(ViewerSettings? settings = null, string? path = null)
    {
        RecordingLogger log = new();

        return (new SettingsPresenter(new OneLogger(log), settings ?? new ViewerSettings(), path ?? File_), log);
    }

    [Test]
    public void Menu_OfDefaultSettings_OffersFourTextureLevelsWithFullChosenAndTheStockHud()
    {
        (SettingsPresenter settings, _) = Owner();

        MenuState menu = settings.Menu(customRoot: null);

        menu.TextureQualities.Select(static choice => choice.Key).ShouldBe(["Full", "High", "Medium", "Low"]);
        menu.TextureQualities[0].Label.ShouldBe("&Full");
        menu.TextureQualities[1].Label.ShouldBe($"High ({(int)TextureQuality.High} px)");
        menu.TextureQualities.Select(static choice => choice.Chosen).ShouldBe([true, false, false, false]);
        menu.FullScreenExclusive.ShouldBeFalse();
        menu.ShowFrameRate.ShouldBeFalse();
        menu.ShowPosition.ShouldBeFalse();
        menu.Phong.ShouldBeTrue();
        menu.Huds.ShouldBe([new MenuChoice(null, "TF2 default", true)]);
    }

    [Test]
    public void Menu_OfChangedSettings_OpensShowingThem()
    {
        (SettingsPresenter settings, _) = Owner(new ViewerSettings
        {
            FullScreenMode = FullScreenMode.Exclusive,
            TextureQuality = TextureQuality.Low,
            ShowFrameRate = 2,
            ShowPosition = 1,
            Phong = false,
            ChosenHud = "somewhere/a-hud",
        });

        MenuState menu = settings.Menu(customRoot: null);

        menu.FullScreenExclusive.ShouldBeTrue();
        menu.TextureQualities.Single(static choice => choice.Chosen).Key.ShouldBe("Low");
        menu.ShowFrameRate.ShouldBeTrue();
        menu.ShowPosition.ShouldBeTrue();
        menu.Phong.ShouldBeFalse();
        menu.Huds.Single().Chosen.ShouldBeFalse("the remembered HUD is not the stock one");
    }

    [Test]
    public void SetFullScreenExclusive_WhileNotFullScreen_RemembersSavesAndSaysNothing()
    {
        (SettingsPresenter settings, _) = Owner();

        settings.SetFullScreenExclusive(exclusive: true, switchDevice: null).ShouldBeNull();

        settings.FullScreenExclusive.ShouldBeTrue();
        File.ReadAllText(File_).ShouldContain(ViewerSettings.FullScreenModeCommand + " 1");

        settings.SetFullScreenExclusive(exclusive: false, switchDevice: null).ShouldBeNull();

        settings.FullScreenExclusive.ShouldBeFalse();
    }

    [Test]
    public void SetFullScreenExclusive_WhenTheDeviceRefuses_SaysSoAndStillRemembers()
    {
        (SettingsPresenter settings, _) = Owner();
        List<bool> asked = [];

        string? said = settings.SetFullScreenExclusive(
            exclusive: true,
            wanted =>
            {
                asked.Add(wanted);

                return false;
            });

        said.ShouldBe(ViewerSettings.ExclusiveFullScreenRefused);
        asked.ShouldBe([true]);
        settings.FullScreenExclusive.ShouldBeTrue();

        // Leaving exclusive cannot be "refused": a device that answers false to borderless is not reported.
        settings.SetFullScreenExclusive(exclusive: false, static _ => false).ShouldBeNull();
    }

    [Test]
    public void SetFullScreenExclusive_WhenTheFileCannotBeWritten_SaysItHoldsForThisSessionOnly()
    {
        (SettingsPresenter settings, _) = Owner(path: Path.Combine(_folder, "\0bad", "settings.cfg"));

        string? said = settings.SetFullScreenExclusive(exclusive: true, switchDevice: null);

        said.ShouldNotBeNull().ShouldStartWith(ViewerSettings.SavedForThisSessionOnly(string.Empty)[..10]);
        settings.FullScreenExclusive.ShouldBeTrue("the choice holds for this run");
    }

    [Test]
    public void SetTextureQuality_ByALevelsName_RemembersItAndSaysWhenItApplies()
    {
        (SettingsPresenter settings, _) = Owner();

        settings.SetTextureQuality("Low").ShouldBe("Texture quality: Low. Applies to the next map opened.");

        settings.TextureQuality.ShouldBe("Low");
        settings.Current.TextureQuality.ShouldBe(TextureQuality.Low);
        settings.Menu(null).TextureQualities.Single(static choice => choice.Chosen).Key.ShouldBe("Low");
        File.ReadAllText(File_).ShouldContain(ViewerSettings.TextureQualityCommand + " " + (int)TextureQuality.Low);
    }

    [Test]
    public void SetFrameRateMeter_OnAndOff_IsShowFpsTwoAndZeroAndSaysSoInTheLog()
    {
        (SettingsPresenter settings, RecordingLogger log) = Owner();

        settings.SetFrameRateMeter(on: true);

        settings.Current.ShowFrameRate.ShouldBe(2, "cl_showfps 2 names the worst and best frame, which the average hides");
        log.Count("cl_showfps 2").ShouldBe(1);

        settings.SetFrameRateMeter(on: false);

        settings.Current.ShowFrameRate.ShouldBe(0);
        log.Count("cl_showfps 0").ShouldBe(1);
    }

    [Test]
    public void SetPositionReadout_OnAndOff_IsShowPosOneAndZeroAndSaysSoInTheLog()
    {
        (SettingsPresenter settings, RecordingLogger log) = Owner();

        settings.SetPositionReadout(on: true);

        settings.Current.ShowPosition.ShouldBe(PositionReadout.View);
        log.Count("cl_showpos 1").ShouldBe(1);

        settings.SetPositionReadout(on: false);

        settings.Current.ShowPosition.ShouldBe(0);
        log.Count("cl_showpos 0").ShouldBe(1);
    }

    [Test]
    public void SetChosenHud_ToAFolderAndBackToStock_RemembersAndSaves()
    {
        (SettingsPresenter settings, _) = Owner();

        settings.SetChosenHud("custom/a-hud").ShouldBeNull();
        settings.Current.ChosenHud.ShouldBe("custom/a-hud");
        File.ReadAllText(File_).ShouldContain(ViewerSettings.ChosenHudCommand);

        settings.SetChosenHud(null).ShouldBeNull();
        settings.Current.ChosenHud.ShouldBeNull();
    }

    [Test]
    public void DeclineGameFolder_AtStartup_StopsAskingAndSaysWhereToSetIt()
    {
        (SettingsPresenter settings, _) = Owner();

        settings.ShouldAskForGameFolder(found: null, headless: false).ShouldBeTrue("the control: nothing found, nobody asked yet");

        settings.DeclineGameFolder().ShouldBe("No TF2 folder chosen. File > TF2 folder sets it any time.");

        settings.ShouldAskForGameFolder(found: null, headless: false).ShouldBeFalse();
        File.ReadAllText(File_).ShouldContain(ViewerSettings.AskForGameFolderCommand + " 0");
    }

    [Test]
    public void ChooseGameFolder_AFolderThatIsNotTf2s_IsRefusedAndChangesNothing()
    {
        (SettingsPresenter settings, RecordingLogger log) = Owner();

        GameFolderChoice choice = settings.ChooseGameFolder(_folder, installOpen: false);

        choice.Accepted.ShouldBeFalse();
        choice.Status.ShouldBe(
            $"{SettingsPresenter.NotAGameFolder} {_folder} holds no {SteamInstall.Recogniser}. Choose the tf folder inside Team Fortress 2.");
        settings.GameFolder.ShouldBeNull();
        log.Lines.ShouldContain((LogLevel.Warning, choice.Status));
        File.Exists(File_).ShouldBeFalse("a refusal saves nothing");
    }

    [Test]
    public void ChooseGameFolder_TheFolderAboveTf_SetsTheTfFolderAndSaysSo()
    {
        string tf = Path.Combine(_folder, "tf");
        Directory.CreateDirectory(tf);
        File.WriteAllBytes(Path.Combine(tf, SteamInstall.Recogniser), []);
        (SettingsPresenter settings, RecordingLogger log) = Owner();

        GameFolderChoice choice = settings.ChooseGameFolder(_folder, installOpen: false);

        choice.ShouldBe(new GameFolderChoice(true, $"{SettingsPresenter.GameFolderSet}{tf}."));
        settings.GameFolder.ShouldBe(tf);
        log.Lines.ShouldContain((LogLevel.Information, choice.Status));

        settings.ChooseGameFolder(tf, installOpen: true).Status
            .ShouldBe($"{SettingsPresenter.GameFolderSet}{tf}. It takes effect the next time the viewer starts.");
    }

    [Test]
    public void ApplyTo_ANewDevice_AssignsEverySettingInTheWindowsOrderWithTheMenusTwoBetween()
    {
        (SettingsPresenter settings, _) = Owner(new ViewerSettings
        {
            VerticalSync = true,
            Phong = false,
            DrawMonitors = false,
            DisableBloom = true,
            ForceToneMapScale = 1.5f,
            DrawHud = false,
            Specular = false,
            Fullbright = 2,
            ThreeDimensionalSky = 0,
            DetailDistance = 100f,
            DetailFade = 50f,
        });
        FakeViewerDevice device = new();

        settings.ApplyOver(device, () => device.Mark("specular off"), mode => device.Mark("fullbright " + mode));

        device.Set.ShouldBe(
        [
            "VerticalSync True", "Phong False", "DrawMonitors False", "DisableBloom True", "ForceToneMapScale 1.5", "DrawHud False",
            "specular off", "fullbright 2",
            "Draw3dSky 0", "DetailDistance 100", "DetailFadeWidth 50",
        ]);
    }

    [Test]
    public void ApplyTo_WithReflectionsOnAndNormalLighting_LeavesTheMenuAlone()
    {
        (SettingsPresenter settings, _) = Owner();
        FakeViewerDevice device = new();

        settings.ApplyOver(device, () => device.Mark("specular off"), mode => device.Mark("fullbright " + mode));

        device.Set.ShouldNotContain("specular off");
        device.Set.ShouldNotContain("fullbright 0");
        device.Set.Count.ShouldBe(9);
    }

    [Test]
    public void FrameRateLine_WithAndWithoutALimit_NamesItAndTheSync()
    {
        Owner(new ViewerSettings { FrameRateLimit = 120, VerticalSync = true }).Settings.FrameRateLine
            .ShouldBe("frame rate limit 120 a second, vertical sync on");
        Owner(new ViewerSettings { FrameRateLimit = 0 }).Settings.FrameRateLine
            .ShouldBe("frame rate limit none, vertical sync off");
    }

    [Test]
    public void ReadLaunch_APlusCvarOption_AppliesToThisRunAndSavesNothing()
    {
        (SettingsPresenter settings, _) = Owner();

        LaunchOptions launch = settings.ReadLaunch(["+" + ViewerSettings.FrameRateLimitCommand, "0", "+developer", "1"]);

        settings.FrameRateLimit.ShouldBe(0);
        settings.Developer.ShouldBe(1);
        settings.Verbosity.ShouldBe(launch.Settings.Verbosity);
        File.Exists(File_).ShouldBeFalse("a launch option does not rewrite the file");
    }

    [Test]
    public void ConVar_OneTheViewerKeepsNoSettingFor_IsTheWatchersValueOrNull()
    {
        (SettingsPresenter settings, _) = Owner(ViewerSettings.Parse("demo_interpolateview 0"));

        settings.ConVar("demo_interpolateview").ShouldBe("0");
        settings.ConVar("never_set").ShouldBeNull();
    }

    [Test]
    public void SetConVar_ShowPositionAtRunTime_AppliesAsALaunchOptionDoesAndSavesNothing()
    {
        (SettingsPresenter settings, _) = Owner();

        settings.SetConVar("cl_showpos", "1");
        settings.Current.ShowPosition.ShouldBe(1);
        settings.ConVar("cl_showpos").ShouldBe("1");

        settings.SetConVar("cl_showpos", "0");
        settings.Current.ShowPosition.ShouldBe(0);
        File.Exists(File_).ShouldBeFalse("a run-time set, like a launch option, does not rewrite the file");
    }

    /// <summary>A console bound to <paramref name="settings"/> as the session binds it, with <c>k</c> bound to <paramref name="command"/>.</summary>
    private static ConfigConsole Console(SettingsPresenter settings, string command, string setup = "")
    {
        ConfigConsole console = new();
        settings.Attach(console);
        console.Load($"{setup}\nbind k \"{command}\"");

        return console;
    }

    private static void Press(ConfigConsole console)
    {
        console.KeyDown("k");
        console.KeyUp("k");
    }

    [Test]
    public void Toggle_AfterTheMenuTurnedThePositionReadoutOn_TurnsItOffOnTheFirstPress()
    {
        // The owner's report (D215): the console read its own copy, 0, and the first press set 1 again.
        (SettingsPresenter settings, _) = Owner();
        ConfigConsole console = Console(settings, "toggle cl_showpos");

        settings.SetPositionReadout(true);
        Press(console);

        settings.Current.ShowPosition.ShouldBe(0);
    }

    [Test]
    public void Toggle_WithValuesAfterTheMenuSetOne_ComparesTheStringTheEngineHolds()
    {
        // The menu's 1 is held as "1", as `cl_showpos 1` would leave it, so it matches and moves to the next value.
        (SettingsPresenter settings, _) = Owner();
        ConfigConsole console = Console(settings, "toggle cl_showpos 0 1 2");

        settings.SetPositionReadout(true);
        Press(console);

        settings.Current.ShowPosition.ShouldBe(2);
    }

    [Test]
    public void Menu_AfterAKeyTurnedThePositionReadoutOn_ShowsItChecked()
    {
        (SettingsPresenter settings, _) = Owner();
        ConfigConsole console = Console(settings, "toggle cl_showpos");

        Press(console);

        settings.Menu(customRoot: null).ShowPosition.ShouldBeTrue();
    }

    [Test]
    public void IncrementVar_FromAMenuSetValue_StepsFromIt()
    {
        (SettingsPresenter settings, _) = Owner();
        ConfigConsole console = Console(settings, "incrementvar cl_showpos 0 2 1");

        settings.SetPositionReadout(true);
        Press(console);

        settings.Current.ShowPosition.ShouldBe(2);
    }

    [Test]
    public void Toggle_AConVarTheViewerDoesNotImplement_RoundTripsThroughTheConsole()
    {
        (SettingsPresenter settings, _) = Owner();
        ConfigConsole console = Console(settings, "toggle cl_hud_minmode", "cl_hud_minmode 1");

        Press(console);
        console.ConVar("cl_hud_minmode").ShouldBe("0");
        Press(console);

        console.ConVar("cl_hud_minmode").ShouldBe("1");
        settings.ConVar("cl_hud_minmode").ShouldBe("1");
    }

    /// <summary>A factory whose every category is one recording logger.</summary>
    private sealed class OneLogger(RecordingLogger log) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider)
        {
            // One logger for every category; a provider has nothing to add.
        }

        public ILogger CreateLogger(string categoryName) => log;

        public void Dispose()
        {
            // The logger holds nothing to release.
        }
    }
}
