using Tf2DemoSalvage.TestSettings;

namespace Tf2DemoSalvage.Fonts.Tests;

/// <summary>This assembly's settings-file redirection (<see cref="SettingsFileRedirect"/>): Fonts references Scene.</summary>
[SetUpFixture]
public sealed class SettingsFilePolicy : SettingsFileRedirect;

/// <summary>That the redirection is in force here, to this assembly's own file; deleting <see cref="SettingsFilePolicy"/> reddens it.</summary>
public sealed class SettingsFilePolicyTests
{
    [Test]
    public void Path_InThisAssembly_IsARunsTempFileNotTheOwners() =>
        SettingsFileRedirect.ShouldBeRedirectedFor(typeof(SettingsFilePolicy));
}
