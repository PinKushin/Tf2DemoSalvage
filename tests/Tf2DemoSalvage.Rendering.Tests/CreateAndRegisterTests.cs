using System;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A model's outline and vertex buffer are made and registered together or not at all (B803 reviews 8-9): the step
/// <c>WorldRenderer.UploadModels</c> runs for each model, with the buffer creation handed in so a failure can be forced.
/// </summary>
public sealed class CreateAndRegisterTests
{
    /// <remarks>
    /// **A vertex buffer that cannot be made releases the outline and registers nothing** (Grok): the outline was made
    /// first and is no one else's yet, so it is released once; the failure reaches the caller unchanged; and the
    /// registration, which writes both of the renderer's dictionaries, is never called.
    /// </remarks>
    [Test]
    public void CreateAndRegister_WhenTheVertexBufferThrows_ReleasesTheOutlineRethrowsAndRegistersNothing()
    {
        Outline outline = new();
        InvalidOperationException failure = new("no vertex buffer");
        int registered = 0;

        Should.Throw<InvalidOperationException>(() => WorldRenderer.CreateAndRegister<Outline, int>(
                () => outline,
                () => throw failure,
                (_, _) => registered++))
            .ShouldBeSameAs(failure);

        (outline.Released, registered).ShouldBe((1, 0));
    }

    /// <remarks>The control: both made, both registered, the outline not released.</remarks>
    [Test]
    public void CreateAndRegister_WhenBothAreMade_RegistersBothAndReleasesNothing()
    {
        Outline outline = new();
        (Outline Outline, int Buffer)? registered = null;

        WorldRenderer.CreateAndRegister(() => outline, () => 7, (made, buffer) => registered = (made, buffer));

        registered.ShouldBe((outline, 7));
        outline.Released.ShouldBe(0);
    }

    /// <summary>An outline that counts its releases.</summary>
    private sealed class Outline : IDisposable
    {
        /// <summary>How many times it was released.</summary>
        public int Released { get; private set; }

        /// <inheritdoc/>
        public void Dispose() => Released++;
    }
}
