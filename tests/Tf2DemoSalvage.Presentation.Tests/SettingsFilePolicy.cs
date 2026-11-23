using System;
using System.IO;

using Tf2DemoSalvage.TestSettings;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>This assembly's settings-file redirection (<see cref="SettingsFileRedirect"/>): a pathless save lands in a temp file.</summary>
[SetUpFixture]
public sealed class SettingsFilePolicy : SettingsFileRedirect;

/// <summary>That the redirection is in force here; deleting <see cref="SettingsFilePolicy"/> reddens it.</summary>
public sealed class SettingsFilePolicyTests
{
    [Test]
    public void Path_InThisAssembly_IsARunsTempFileNotTheOwners()
    {
        string owners = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tf2DemoSalvage", "settings.cfg");

        Tf2DemoSalvage.Scene.ViewerSettings.Path.ShouldStartWith(SettingsFileRedirect.Folder);
        Tf2DemoSalvage.Scene.ViewerSettings.Path.ShouldNotBe(owners);
    }
}
