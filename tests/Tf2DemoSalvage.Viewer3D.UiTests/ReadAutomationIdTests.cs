using System;

using FlaUI.Core.Exceptions;

using Tf2DemoSalvage.Viewer3D.UiTests;

// Not in the `UiTests` namespace: its `ViewerSession` fixture launches a viewer for every test beneath it.
namespace Tf2DemoSalvage.Viewer3D.GoldenTests;

/// <summary>The failure reporter's AutomationId read (B558). No viewer is launched.</summary>
public sealed class ReadAutomationIdTests
{
    [Test]
    public void ReadAutomationId_ElementWithoutTheProperty_ReturnsTheNoIdMarker() =>
        ViewerApplication.ReadAutomationId(() => throw new PropertyNotSupportedException(new FlaUI.Core.Identifiers.PropertyId(30011, "AutomationId")))
            .ShouldBe(ViewerApplication.NoIdMarker);

    [Test]
    public void ReadAutomationId_ElementWithAnId_ReturnsIt() =>
        ViewerApplication.ReadAutomationId(() => "MainWindow").ShouldBe("MainWindow");

    [Test]
    public void ReadAutomationId_NullId_ReturnsEmpty() =>
        ViewerApplication.ReadAutomationId(() => null).ShouldBe(string.Empty);

    [Test]
    public void ReadAutomationId_AnyOtherException_Propagates() =>
        Should.Throw<InvalidOperationException>(
            () => ViewerApplication.ReadAutomationId(() => throw new InvalidOperationException("real fault")));
}
