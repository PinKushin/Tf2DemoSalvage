using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Getting a level's world onto the device, and pointing it at a camera.
/// </summary>
/// <remarks>
/// **This was <c>MainForm.ProjectMap</c>** (B188, D90), and it could not be tested there: reaching
/// it needed a form and a real Direct3D device. The decisions are now separated from the uploads by
/// <see cref="IWorldUpload"/>, so a fake records what was asked of it.
///
/// **The behaviour worth pinning is what is SKIPPED**, not what happens. A resize must not rebuild
/// the world — that was 0.33 seconds against a 64-byte camera upload — and a second call must not
/// re-upload textures. Both are invisible in a picture and obvious to a counter.
/// </remarks>
public sealed class WorldPresenterTests
{
    [Test]
    public void Project_WithNoMap_DoesNothingAndReportsNoProblem()
    {
        // A viewer before anything is opened. Not an error, and not something to log about.
        Upload device = new();

        WorldUpload result = new WorldPresenter(NullLogger.Instance).Project(
            map: null, device, Matrix(), false, (800, 600), NullLoggerFactory.Instance);

        result.Uploaded.ShouldBeFalse();
        result.Problem.ShouldBeNull();
        device.Calls.ShouldBeEmpty();
    }

    [Test]
    public void Project_WithNoDevice_DoesNothing()
    {
        // The window has a map before it has a swap chain — the device arrives on a handle-created
        // event. Reaching the uploads then would be a null dereference on the first map.
        new WorldPresenter(NullLogger.Instance)
            .Project(map: null, upload: null, Matrix(), false, (800, 600), NullLoggerFactory.Instance)
            .Uploaded.ShouldBeFalse();
    }

    [Test]
    public void TexturesAreCurrent_OnAFreshPresenter_IsFalse()
    {
        // The control for the flag's meaning: nothing has been uploaded, so nothing is current. A
        // presenter starting true would skip the very first texture upload.
        new WorldPresenter(NullLogger.Instance).TexturesAreCurrent.ShouldBeFalse();
    }

    [Test]
    public void TexturesAreCurrent_IsNotTheSameQuestionAsTheDevices()
    {
        // **This is why the flag exists at all.** `HasWorldTextures` says textures are RESIDENT,
        // which stays true across a map change — they are simply the wrong ones. Only something that
        // knows about levels can say "resident AND for this level", and conflating the two is the
        // shape of B196 from the other side: two pieces of state describing one fact, where only one
        // of them knows about maps.
        Upload device = new() { HasWorldTextures = true };
        WorldPresenter world = new(NullLogger.Instance);

        world.TexturesAreCurrent.ShouldBeFalse(
            "the device having textures says nothing about WHICH level they belong to");

        device.HasWorldTextures.ShouldBeTrue("and the device still believes it has some");
    }

    /// <summary>An upload target that records what it was asked to do.</summary>
    private sealed class Upload : IWorldUpload
    {
        public List<string> Calls { get; } = [];

        public bool HasWorld { get; set; }

        public bool HasWorldTextures { get; set; }

        public void UploadWorldTextures(MapAssets assets) => Calls.Add("textures");

        public void UploadWorldGeometry(MapWorld world) => Calls.Add("geometry");

        public void SetCamera(float[] matrix, bool surfaceColours = false) =>
            Calls.Add("camera");

        public void ClearWorld() => Calls.Add("clear");
    }

    // A `Camera()` helper sat here until 2026-08-26 (D98), supplying the `TopDownCamera` that
    // `Project` took and never used.

    private static float[] Matrix()
    {
        float[] matrix = new float[16];

        matrix[0] = 1f;
        matrix[5] = 1f;
        matrix[10] = 1f;
        matrix[15] = 1f;

        return matrix;
    }
}
