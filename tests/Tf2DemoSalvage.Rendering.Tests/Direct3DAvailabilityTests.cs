using System;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Off Windows there is no Direct3D 11, and that must read as an absent platform — a skip — never as
/// the `NullReferenceException` Silk.NET's loader throws (measured on the Linux mutation box, B217).
/// </summary>
/// <remarks>Excluded on Windows, where the subject is present; this is the mutation box's test.</remarks>
[Platform(Exclude = "Win", Reason = "asserts the behaviour of a machine without Direct3D 11")]
public sealed class Direct3DAvailabilityTests
{
    [Test]
    public void IsAvailable_OffWindows_IsFalse() => Direct3DApi.IsAvailable.ShouldBeFalse();

    [Test]
    public void TryCreate_OffWindows_ReturnsNull() => OffscreenTarget.TryCreate(4, 4).ShouldBeNull();

    [Test]
    public void Api_OffWindows_ThrowsPlatformNotSupported() =>
        Should.Throw<PlatformNotSupportedException>(() => Direct3DApi.Api);
}
