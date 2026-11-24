using System;
using System.Collections.Generic;

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
    /// released once, and the failure reaches the caller unchanged, so no dictionary is left naming a released buffer. The
    /// undo runs first (review 11, Fable), so nothing is released while a dictionary still names it.
    /// </remarks>
    [Test]
    public void CreateAndRegister_WhenTheRegistrationThrows_UndoesItAndReleasesBoth()
    {
        List<string> steps = [];
        Resource outline = new(steps, "outline released");
        Resource vertices = new(steps, "vertices released");
        InvalidOperationException failure = new("no room");

        Should.Throw<InvalidOperationException>(() => WorldRenderer.CreateAndRegister(
                () => outline,
                () => vertices,
                (_, _) => throw failure,
                () => steps.Add("undone")))
            .ShouldBeSameAs(failure);

        steps.ShouldBe(["undone", "vertices released", "outline released"]);
    }

    /// <remarks>
    /// **The renderer's own undo removes the path from both dictionaries** (review 11, Fable): the registration stores the
    /// vertex buffer, then its outline insert throws. The path is in neither dictionary afterwards, and each buffer is
    /// released once; without the vertex buffers' `Remove`, `_modelBuffers` would name a released buffer.
    /// </remarks>
    [Test]
    public void CreateAndRegisterModel_WhenTheOutlineInsertThrows_LeavesThePathInNeitherDictionary()
    {
        Resource outline = new();
        Resource vertices = new();
        Dictionary<string, Resource> buffers = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, (Resource Buffer, FacePlane[] Faces)> outlines = new(new FailsItsFirstHash());

        Should.Throw<ArgumentException>(() => WorldRenderer.CreateAndRegisterModel(
            buffers, outlines, "*1", [], () => outline, () => vertices));

        (buffers.ContainsKey("*1"), outlines.ContainsKey("*1"), outline.Released, vertices.Released).ShouldBe((false, false, 1, 1));
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
    /// **Releasing a default ComPtr does nothing** (reviews 10-11, Fable, Grok, Sonnet): a studio model has no outline, so
    /// when its vertex buffer fails, <c>CreateAndRegister</c> releases a default one. This pins Silk's null-handle
    /// <c>Dispose</c>, not code of this project's: only a Silk update can turn it red.
    /// </remarks>
    [Test]
    public void Dispose_ADefaultComPtr_DoesNothing()
    {
        ComPtr<ID3D11Buffer> none = default;

        Should.NotThrow(() => none.Dispose());
    }

    /// <summary>A buffer that counts its releases, and writes each to a shared list of steps when given one.</summary>
    private sealed class Resource(List<string>? steps = null, string name = "") : IDisposable
    {
        /// <summary>How many times it was released.</summary>
        public int Released { get; private set; }

        /// <inheritdoc/>
        public void Dispose()
        {
            Released++;
            steps?.Add(name);
        }
    }

    /// <summary>A key comparer whose first hash throws, so a dictionary's first insert fails and later calls work.</summary>
    private sealed class FailsItsFirstHash : IEqualityComparer<string>
    {
        private bool _failed;

        /// <inheritdoc/>
        public bool Equals(string? x, string? y) => StringComparer.OrdinalIgnoreCase.Equals(x, y);

        /// <inheritdoc/>
        public int GetHashCode(string obj)
        {
            if (!_failed)
            {
                _failed = true;
                // An argument exception, the one a hash may raise (CA1065); it stands in for the insert's out-of-memory.
                throw new ArgumentException("no room", nameof(obj));
            }

            return StringComparer.OrdinalIgnoreCase.GetHashCode(obj);
        }
    }
}
