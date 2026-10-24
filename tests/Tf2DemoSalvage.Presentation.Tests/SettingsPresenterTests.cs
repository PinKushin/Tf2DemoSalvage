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
            MaterialQueueMode = 2,
        });
        FakeViewerDevice device = new();

        settings.ApplyOver(device, () => device.Mark("specular off"), mode => device.Mark("fullbright " + mode));

        device.Set.ShouldBe(
        [
            "VerticalSync True", "QueueMode 2", "Phong False", "DrawMonitors False", "DisableBloom True", "ForceToneMapScale 1.5", "DrawHud False",
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
        device.Set.Count.ShouldBe(10);
        device.Set.ShouldContain("QueueMode -1", "mat_queue_mode defaults to the engine's -1 (D229)");
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

    [Test]
    public void MasterGain_AtTheDefaults_IsOneFocusedAndSilentUnfocused()
    {
        (SettingsPresenter settings, _) = Owner();

        settings.MasterGain(focused: true).ShouldBe(1f);
        settings.MasterGain(focused: false).ShouldBe(0f, "snd_mute_losefocus ships at 1");
    }

    [Test]
    public void MasterGain_WithMuteLoseFocusOff_IsTheVolumeEvenUnfocused()
    {
        (SettingsPresenter settings, _) = Owner(ViewerSettings.Parse("volume 0.25\nsnd_mute_losefocus 0"));

        settings.MasterGain(focused: false).ShouldBe(0.25f);
    }

    [TestCase("volume 0.3", 0.3f)]
    [TestCase("toggle volume 0 1", 0f)]
    [TestCase("incrementvar volume 0 1 -0.25", 0.75f)]
    [TestCase("volume 7", 1f)]
    public void MasterGain_AfterABoundKey_IsWhatTheKeySet(string command, float expected)
    {
        (SettingsPresenter settings, _) = Owner();
        ConfigConsole console = Console(settings, command);

        Press(console);

        settings.MasterGain(focused: true).ShouldBe(expected, 1e-6f);
    }

    [TestCase("snd_musicvolume 0.3", 0.3f)]
    [TestCase("snd_musicvolume 7", 1f)]
    [TestCase("volume 0.3", 1f)]
    public void MusicVolume_AfterABoundKey_IsWhatTheKeySet(string command, float expected)
    {
        (SettingsPresenter settings, _) = Owner();
        ConfigConsole console = Console(settings, command);

        Press(console);

        settings.MusicVolume.ShouldBe(expected, 1e-6f);
    }

    [TestCase("voice_scale 0.3", 0.3f)]
    [TestCase("voice_scale 3", 3f)]
    [TestCase("volume 0.3", 1f)]
    public void VoiceScale_AfterABoundKey_IsWhatTheKeySet(string command, float expected)
    {
        (SettingsPresenter settings, _) = Owner();
        ConfigConsole console = Console(settings, command);

        Press(console);

        settings.VoiceScale.ShouldBe(expected, 1e-6f);
    }

    [TestCase("voice_enable 0", false)]
    [TestCase("voice_enable 1", true)]
    public void VoiceEnable_AfterABoundKey_IsWhatTheKeySet(string command, bool expected)
    {
        (SettingsPresenter settings, _) = Owner();
        ConfigConsole console = Console(settings, command);

        Press(console);

        settings.VoiceEnable.ShouldBe(expected);
    }

    [Test]
    public void ReadLaunch_TheSuitesSilenceThenATestsOwnVolume_TheLaterWins()
    {
        (SettingsPresenter settings, _) = Owner();

        _ = settings.ReadLaunch(["+volume", "0", "+volume", "1"]);

        settings.MasterGain(focused: true).ShouldBe(1f, "a sound test's own +volume follows the suite's +volume 0");
    }

    [Test]
    public void MasterGain_AfterAConfigLine_IsTheConfigsVolume()
    {
        (SettingsPresenter settings, _) = Owner();
        _ = Console(settings, "", setup: "volume 0.4");

        settings.MasterGain(focused: true).ShouldBe(0.4f, 1e-6f);
    }

    [Test]
    public void SetVolume_FromTheMenu_ReachesTheMixAndIsSavedAndChecked()
    {
        (SettingsPresenter settings, _) = Owner();

        settings.SetVolume("30").ShouldBeNull();

        settings.MasterGain(focused: true).ShouldBe(0.3f, 1e-6f);
        ViewerSettings.Load(File_).Volume.ShouldBe(0.3f, 1e-6f);
        settings.Menu(null).Volumes.Single(static level => level.Chosen).Key.ShouldBe("30");
        settings.ConVar("volume").ShouldBe("0.3", "a key's toggle reads the menu's value (D215)");
    }

    [Test]
    public void SetMuteLoseFocus_Off_KeepsSoundUnfocusedAndIsSaved()
    {
        (SettingsPresenter settings, _) = Owner();

        settings.SetMuteLoseFocus(false);

        settings.MasterGain(focused: false).ShouldBe(1f);
        settings.Menu(null).MuteLoseFocus.ShouldBeFalse();
        ViewerSettings.Load(File_).MuteLoseFocus.ShouldBeFalse();
    }

    [TestCase("hud")]
    [TestCase("mute")]
    [TestCase("quality")]
    public void MenuSave_AfterALaunchOptionAndABind_SavesNeitherOnlyTheMenusChoice(string item)
    {
        (SettingsPresenter settings, _) = Owner(new ViewerSettings { Volume = 0.7f });
        _ = settings.ReadLaunch(["+volume", "0"]);
        ConfigConsole console = Console(settings, "cl_showpos 1");
        Press(console);

        _ = item switch
        {
            "hud" => settings.SetChosenHud("somehud"),
            "mute" => settings.SetMuteLoseFocus(false),
            _ => settings.SetTextureQuality("Low"),
        };

        ViewerSettings saved = ViewerSettings.Load(File_);
        saved.Volume.ShouldBe(0.7f, 1e-6f, "+volume 0 applies to this run and is not saved");
        saved.ShowPosition.ShouldBe(0, "a bound key's set is not saved either");
        settings.MasterGain(focused: true).ShouldBe(0f, "the launch value still holds for the run");
    }

    [TestCase("nan")]
    [TestCase("NaN")]
    public void SetConVar_VolumeNotANumber_StoresWhatTheEnginesClampLeaves(string value)
    {
        // ConVar::ClampValue (convar.cpp): `if ( m_bHasMin && ( value < m_fMinVal ) )` and the max twin are both false
        // for NaN, so the engine stores NaN. A NaN gain is not a level OpenAL accepts; see the presenter's mix.
        (SettingsPresenter settings, _) = Owner();

        settings.SetConVar("volume", value);

        settings.MasterGain(focused: true).ShouldBe(0f, "a NaN master gain reaches the mix as silence, never as NaN");
    }

    [Test]
    public void Menu_AVolumeThatIsNotANumber_ChecksNoLevel()
    {
        // NaN is no level: the cast after rounding saturates it to 0, which would check "0%" for a volume nobody set.
        (SettingsPresenter settings, _) = Owner();
        settings.SetConVar("volume", "nan");

        settings.Menu(null).Volumes.ShouldAllBe(static level => !level.Chosen);
    }

    [TestCase(0.45f, "50")]
    [TestCase(0.25f, "30")]
    [TestCase(0.05f, "10")]
    public void Menu_AVolumeHalfwayBetweenTwoLevels_ChecksTheOneAwayFromZero(float volume, string expected)
    {
        (SettingsPresenter settings, _) = Owner(new ViewerSettings { Volume = volume });

        settings.Menu(null).Volumes.Single(static level => level.Chosen).Key.ShouldBe(expected);
    }

    [Test]
    public void Save_AVolumeWrittenWithThreePlaces_KeepsTheStringAsWritten()
    {
        (SettingsPresenter settings, _) = Owner(new ViewerSettings().Set("volume", "0.333"));

        settings.SetMuteLoseFocus(false);

        File.ReadAllLines(File_).ShouldContain("volume 0.333", "the cfg holds the string ConVarText holds, not \"0.33\"");
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
