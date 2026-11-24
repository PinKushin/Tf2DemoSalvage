using System;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A model's outline and vertex buffer are made and registered together or not at all (B803 reviews 8-10): the step
/// <c>WorldRenderer.UploadModel</c> runs for each model, with the creation and registration handed in so a failure can
/// be forced.
/// </summary>
public sealed class CreateAndRegisterTests
{
    /// <remarks>
    /// **A vertex buffer that cannot be made releases the outline and registers nothing** (review 9, Grok): the outline
    /// was made first and is no one else's yet, so it is released once; the failure reaches the caller unchanged; and the
    /// registration, which writes both of the renderer's dictionaries, is never called.
    /// </remarks>
    [Test]
    public void CreateAndRegister_WhenTheVertexBufferThrows_ReleasesTheOutlineRethrowsAndRegistersNothing()
    {
        Resource outline = new();
        InvalidOperationException failure = new("no vertex buffer");
        int registered = 0;
        int unregistered = 0;

        Should.Throw<InvalidOperationException>(() => WorldRenderer.CreateAndRegister<Resource, Resource>(
                () => outline,
                () => throw failure,
                (_, _) => registered++,
                () => unregistered++))
            .ShouldBeSameAs(failure);

        (outline.Released, registered, unregistered).ShouldBe((1, 0, 0));
    }

    /// <remarks>
    /// **A registration that throws part way is undone and releases both** (review 10, Grok, Fable): the registration writes
    /// the vertex buffer's dictionary, then the outline's, and an insert can fail between them. The undo runs, both are
    /// released once, and the failure reaches the caller unchanged, so no dictionary is left naming a released buffer.
    /// </remarks>
    [Test]
    public void CreateAndRegister_WhenTheRegistrationThrows_UndoesItAndReleasesBoth()
    {
        Resource outline = new();
        Resource vertices = new();
        InvalidOperationException failure = new("no room");
        int unregistered = 0;

        Should.Throw<InvalidOperationException>(() => WorldRenderer.CreateAndRegister(
                () => outline,
                () => vertices,
                (_, _) => throw failure,
                () => unregistered++))
            .ShouldBeSameAs(failure);

        (outline.Released, vertices.Released, unregistered).ShouldBe((1, 1, 1));
    }

    /// <remarks>The control: both made, both registered, nothing released or undone.</remarks>
    [Test]
    public void CreateAndRegister_WhenBothAreMade_RegistersBothAndReleasesNothing()
    {
        Resource outline = new();
        Resource vertices = new();
        (Resource Outline, Resource Vertices)? registered = null;
        int unregistered = 0;

        WorldRenderer.CreateAndRegister(() => outline, () => vertices, (made, buffer) => registered = (made, buffer), () => unregistered++);

        registered.ShouldBe((outline, vertices));
        (outline.Released, vertices.Released, unregistered).ShouldBe((0, 0, 0));
    }

    /// <remarks>
    /// **A studio model's outline is a default ComPtr, and releasing it is a no-op** (review 10, Fable): a studio model has
    /// no outline, so when its vertex buffer fails the release runs on a null handle. Silk's <c>ComPtr.Dispose</c> skips a
    /// null handle; were it to throw or fault, the vertex buffer's failure would be replaced by another. The failure still
    /// reaches the caller unchanged.
    /// </remarks>
    [Test]
    public void CreateAndRegister_ForAStudioModelWhoseVertexBufferThrows_RethrowsItsFailure()
    {
        InvalidOperationException failure = new("no vertex buffer");

        Should.Throw<InvalidOperationException>(() => WorldRenderer.CreateAndRegister<ComPtr<ID3D11Buffer>, ComPtr<ID3D11Buffer>>(
                () => default,
                () => throw failure,
                (_, _) => { },
                () => { }))
            .ShouldBeSameAs(failure);
    }

    /// <summary>A buffer that counts its releases.</summary>
    private sealed class Resource : IDisposable
    {
        /// <summary>How many times it was released.</summary>
        public int Released { get; private set; }

        /// <inheritdoc/>
        public void Dispose() => Released++;
    }
}
