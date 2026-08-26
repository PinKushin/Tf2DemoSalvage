using System;
using System.Globalization;
using System.IO;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Logging;

namespace Tf2DemoSalvage.Scene;

/// <summary>What a world upload did, or why it did nothing.</summary>
/// <param name="Uploaded">Whether geometry reached the device this time.</param>
/// <param name="Problem">What went wrong, or null when nothing did.</param>
public readonly record struct WorldUpload(bool Uploaded, string? Problem);

/// <summary>Gets a level's world onto the device, and points it at a camera.</summary>
/// <remarks>
/// **This was <c>MainForm.ProjectMap</c>** (B188, D90). It reads as a map member and is really an
/// orchestration: decide whether textures are resident, decide whether geometry is, build it if not,
/// and set the camera. None of those decisions needs a window; only the uploads need a device, and
/// they arrive through <see cref="IWorldUpload"/>.
///
/// **Valve does this at level load, in the engine.** `modelloader` uploads world geometry when a map
/// is loaded; `CViewRender::DrawWorldAndEntities` then draws what is already resident rather than
/// checking whether it exists. This viewer has to do the engine's half as well as the client's, so
/// the check exists — but keeping it OUT of the draw path is what stops it becoming a per-frame
/// question about state that only changes per map.
///
/// **The order is load-bearing and is Valve's.** Textures before geometry, because a batch names a
/// material index into the table the texture upload builds; a geometry upload against an empty table
/// draws every surface with the missing-material chequer.
/// </remarks>
public sealed class WorldPresenter(ILogger render)
{
    /// <summary>Whether the resident textures belong to the level currently loaded.</summary>
    /// <remarks>
    /// **The one piece of state a caller must keep, because the device cannot know it.**
    /// <see cref="IWorldUpload.HasWorldTextures"/> answers "are textures resident", which stays true
    /// across a map change — the textures are simply the wrong ones. So this records "resident AND
    /// for THIS level", and a level load clears it.
    ///
    /// That distinction is the shape of B196 from the other side: two pieces of state describing one
    /// fact, where only one of them knows about maps.
    /// </remarks>
    public bool TexturesAreCurrent { get; set; }

    /// <summary>Ensures the level is on the device and aimed at a camera.</summary>
    /// <param name="map">The loaded level, or null when none is open.</param>
    /// <param name="upload">The device, or null before one exists.</param>
    /// <param name="camera">The overhead camera the world is built for.</param>
    /// <param name="view">The view-projection to draw with, row major.</param>
    /// <param name="surfaceColours">Whether to draw the surface-category view.</param>
    /// <param name="heightCut">Where to cut the world away above, or zero for none.</param>
    /// <param name="viewport">The viewport's size, which the world is BUILT for and so is reported.</param>
    /// <param name="loggers">Where the world build reports what it built.</param>
    /// <returns>What happened, and what to tell the user if it failed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loggers"/> is null.</exception>
    /// <remarks>
    /// **Nothing to do is a normal answer, not a guard clause worth logging.** A viewer with no map,
    /// no device or a map whose outline is empty is in an ordinary state — the same one it is in
    /// before anything is opened — so it reports neither an upload nor a problem.
    /// </remarks>
    public WorldUpload Project(
        LoadedMap? map,
        IWorldUpload? upload,
        TopDownCamera camera,
        float[] view,
        bool surfaceColours,
        float heightCut,
        (int Width, int Height) viewport,
        ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(loggers);

        if (map is not { } level || level.Outline.IsEmpty || upload is null ||
            level.Assets is not { } assets || level.Level.Surfaces.Count == 0)
        {
            return new WorldUpload(Uploaded: false, Problem: null);
        }

        try
        {
            // **Textures first, and only once per map.** They do not depend on the camera, so a
            // resize needs new vertices and nothing else.
            //
            // **Asks the device as well as the flag.** `TexturesAreCurrent` knows about maps and
            // `HasWorldTextures` knows about the GPU; either can be false on its own — a new level,
            // or a device that lost them — and both have to be true to skip the work.
            if (!TexturesAreCurrent || !upload.HasWorldTextures)
            {
                using (render.Time("uploading textures"))
                {
                    upload.UploadWorldTextures(assets);
                }

                TexturesAreCurrent = true;
            }

            // **The camera is a matrix, so a resize is not a rebuild.** The world's vertices are in
            // map coordinates and never move; only the view does. That is what took a viewport
            // change from 0.33 seconds to a 64-byte upload, and it is the reason a free camera or a
            // per-player view can exist at all.
            upload.SetCamera(view, surfaceColours, heightCut);

            // **Logged because this is the whole cost of a resize**, and a rebuild is not. Counting
            // these against "building the world" lines is what PROVES the geometry survived a
            // viewport change rather than being quietly rebuilt: many camera lines and one build
            // line is the fix working, one of each per resize is not. The viewport size is in both
            // for the same reason — a world built at one size and drawn at another is the defect,
            // and a line naming only the vertex count cannot show it.
            render.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"camera set for a {viewport.Width}x{viewport.Height} viewport"));

            if (upload.HasWorld)
            {
                return new WorldUpload(Uploaded: false, Problem: null);
            }

            MapWorld built;

            using (render.Time("building the world"))
            {
                built = level.BuildWorld(camera, loggers);
            }

            render.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"world: {built.Vertices.Count} vertices in {built.Batches.Count} material " +
                    $"batches for a {viewport.Width}x{viewport.Height} viewport"));

            upload.UploadWorldGeometry(built);

            return new WorldUpload(Uploaded: true, Problem: null);
        }
        catch (Exception failure) when (
            failure is InvalidOperationException or InvalidDataException or IOException)
        {
            // **The world is released rather than left half-built.** A partial upload draws worse
            // than none: the batches reference a material table the failure interrupted, so every
            // surface lands on the chequer and reads as a texture bug rather than a load failure.
            upload.ClearWorld();
            TexturesAreCurrent = false;

            render.LogWarning(failure, "{Message}", "uploading the textured world");

            return new WorldUpload(Uploaded: false, Problem: "Textures unavailable: " + failure.Message);
        }
    }
}
