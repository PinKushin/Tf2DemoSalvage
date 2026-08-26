using System;
using System.Globalization;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Where the free camera is, and how it flies.</summary>
/// <remarks>
/// **Presenter work, not view work** (D90). Nothing here needs a window: the only thing the viewer
/// contributed was the viewport's aspect ratio, which is one float and arrives as an argument. The
/// rest is a position, two angles and the arithmetic between them.
///
/// **Placed once, then flown.** Both placements below apply only while the camera has no origin, so
/// a camera the user has moved is never snapped back — which is why they are `??=`-shaped rather
/// than conditions on a mode.
/// </remarks>
/// <param name="log">Where placements are reported, since a camera that starts somewhere
/// unexpected is otherwise indistinguishable from one that ignored its input.</param>
public sealed class FreeCameraController(ILogger log)
{
    /// <summary>The environment variable that places the camera for a parity capture.</summary>
    /// <remarks>
    /// **A camera placed from the environment, for comparing against a capture from the game.**
    /// TF2's <c>pos</c> and <c>ang</c> readouts give an exact viewpoint, and reproducing one by hand
    /// with mouse and keys is neither quick nor repeatable. Parity work keeps needing the same frame
    /// twice — once from the engine and once from here — so the coordinates are worth taking as
    /// input.
    /// </remarks>
    public const string CameraVariable = "TF2VIEW_CAMERA";

    /// <summary>Where the camera is, or null until something places it.</summary>
    public (float X, float Y, float Z)? Origin { get; set; }

    /// <summary>Where it is looking.</summary>
    public (float Pitch, float Yaw) Angles { get; set; } = (35f, 0f);

    /// <summary>The world field of view, in degrees.</summary>
    /// <remarks>
    /// **Settable, because the game lets a player set it** (D69, D91). It was compiled in three
    /// places before — here, <see cref="OverheadPlacement"/>'s default and the call site — so the
    /// choice was nobody's. Defaults to <see cref="ViewerSettings.DefaultFieldOfView"/>, which is
    /// 90 rather than TF2's shipped 75 and says why.
    /// </remarks>
    public float FieldOfView
    {
        get => _fieldOfView;

        // Clamped to what the engine allows a demo to be watched at, rather than trusted: 10..90,
        // from `clamp( demo_fov_override.GetFloat(), 10.0f, 90.0f )` (`c_baseplayer.cpp:2444`).
        set => _fieldOfView = Math.Clamp(
            value, ViewerSettings.MinimumFieldOfView, ViewerSettings.MaximumFieldOfView);
    }

    private float _fieldOfView = ViewerSettings.DefaultFieldOfView;

    /// <summary>The longest a single frame may count as flight time, in seconds.</summary>
    /// <remarks>
    /// **A stall is not flight time, for the same reason it is not playback time**: a map load or a
    /// window drag would otherwise fling the camera across the map the moment the loop resumes.
    ///
    /// **This clamp is for FLIGHT and for nothing else, which is a distinction that was got wrong
    /// once.** The frame meter used to read its duration through the same `Math.Min`, so the worst
    /// frame could never be reported as worse than 100 ms — the ceiling. The owner's report was
    /// "everything freezes for a half a second to maybe a second" and the log for those exact
    /// seconds said `longest 100 ms`: not a measurement, just the clamp showing through. A
    /// saturating instrument is worse than a missing one, because 100 looks like a number somebody
    /// measured.
    /// </remarks>
    public const double MaximumFrameSeconds = 0.1;

    /// <summary>Flies the camera by however long the last frame took.</summary>
    /// <param name="intent">What is held this frame.</param>
    /// <param name="seconds">How long the last frame took; clamped, see <see cref="MaximumFrameSeconds"/>.</param>
    /// <param name="ifUnplaced">Where to fly from when nothing has placed the camera yet.</param>
    /// <returns>Whether the camera actually moved.</returns>
    /// <remarks>
    /// **Frame-driven rather than message-driven, which is the whole of B97.** A camera moved by
    /// key-repeat messages travels at whatever rate Windows repeats a held key; one moved by
    /// elapsed time travels at a speed.
    ///
    /// **The return value exists so the caller knows whether to re-upload**, rather than uploading
    /// every frame or guessing from the input. A frame where nothing is held must cost nothing.
    ///
    /// **`ifUnplaced` rather than a placement call, because placing needs the viewport.** Flight can
    /// happen on the same frame that first frames the map, and starting from the world origin
    /// instead would put the viewer at the centre of the world, under the floor.
    /// </remarks>
    public bool Fly(FlightInput intent, double seconds, (float X, float Y, float Z) ifUnplaced)
    {
        (float X, float Y, float Z) moved = FreeFlightPath.Movement(
            intent, Math.Min(seconds, MaximumFrameSeconds), Angles.Pitch, Angles.Yaw);

        if (moved == (0f, 0f, 0f))
        {
            return false;
        }

        (float X, float Y, float Z) where = Origin ?? ifUnplaced;

        Origin = (where.X + moved.X, where.Y + moved.Y, where.Z + moved.Z);

        return true;
    }

    /// <summary>The camera to draw with, placing it first if nothing has.</summary>
    /// <param name="aspect">The viewport's width over its height.</param>
    /// <param name="map">The map's outline, for framing it. Null before a map is read.</param>
    /// <param name="highest">The highest drawn geometry, for clearing it.</param>
    /// <returns>The camera.</returns>
    public FreeCamera Camera(float aspect, MapOutline? map, float highest)
    {
        PlaceFromEnvironment();
        PlaceOverhead(aspect, map, highest);

        // No map yet — a demo whose map failed to load still has to draw something rather than
        // dividing by a bounds that does not exist.
        Origin ??= (0f, 0f, OverheadPlacement.ClearanceAboveGeometry);

        return new FreeCamera
        {
            Origin = Origin.Value,
            Angles = (Angles.Pitch, Angles.Yaw, 0f),
            Aspect = aspect,
            FieldOfView = FieldOfView,
        };
    }

    /// <summary>Takes a placement from <see cref="CameraVariable"/>, once.</summary>
    private void PlaceFromEnvironment()
    {
        if (Origin is not null ||
            Environment.GetEnvironmentVariable(CameraVariable) is not { Length: > 0 } placement ||
            Parse(placement) is not { } placed)
        {
            return;
        }

        Origin = placed.Origin;
        Angles = (placed.Pitch, placed.Yaw);

        log.LogInformation(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"free camera placed from {CameraVariable} at " +
                $"({placed.Origin.X:0.##},{placed.Origin.Y:0.##},{placed.Origin.Z:0.##}) " +
                $"pitch {placed.Pitch:0.##} yaw {placed.Yaw:0.##}"));
    }

    /// <summary>Puts the camera above the map looking down, once.</summary>
    /// <remarks>
    /// **D49's replacement for the ortho camera.** It used to orbit a focus anchored to the LOWEST
    /// drawn geometry anywhere in the file, which on anything with a basement or a deep skybox is
    /// far below where anybody stands — so the camera started down there, under the map.
    ///
    /// <see cref="OverheadPlacement"/> anchors to the HIGHEST geometry instead, plus clearance, and
    /// takes whichever is greater of that and the distance needed to frame the play area — so the
    /// camera is above the map on a tall one and far enough back on a wide one (D66).
    /// </remarks>
    private void PlaceOverhead(float aspect, MapOutline? map, float highest)
    {
        if (Origin is not null || map is null)
        {
            return;
        }

        ((float X, float Y, float Z) origin, float pitch, float yaw) = OverheadPlacement.For(
            map.MainBounds.MinX,
            map.MainBounds.MinY,
            map.MainBounds.MaxX,
            map.MainBounds.MaxY,
            highest,
            fieldOfView: FieldOfView,
            aspect: aspect);

        Origin = origin;
        Angles = (pitch, yaw);

        log.LogInformation(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"free camera placed overhead at ({origin.X:0.##},{origin.Y:0.##},{origin.Z:0.##}) " +
                $"pitch {pitch:0.##}, framing {map.MainBounds.MaxX - map.MainBounds.MinX:0.##} x " +
                $"{map.MainBounds.MaxY - map.MainBounds.MinY:0.##}"));
    }

    /// <summary>Reads a camera placement, or null when the text is not five numbers.</summary>
    /// <param name="text">Whitespace or comma separated <c>x y z pitch yaw</c>.</param>
    /// <returns>The placement, or <c>null</c>.</returns>
    /// <remarks>
    /// Null rather than a default placement, because a mistyped variable that silently put the
    /// camera at the origin would look like the viewer ignoring it — and the whole point is to be
    /// somewhere specific. The log line only prints when a placement was actually read.
    /// </remarks>
    public static ((float X, float Y, float Z) Origin, float Pitch, float Yaw)? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] parts = text.Split(
            [' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 5)
        {
            return null;
        }

        Span<float> values = stackalloc float[5];

        for (int index = 0; index < 5; index++)
        {
            if (!float.TryParse(
                    parts[index], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out values[index]))
            {
                return null;
            }
        }

        // **Pitch is clamped to the same ±89 the mouse drag uses**, and this was once missing. The
        // drag clamps because the camera basis is degenerate looking exactly along the world's up
        // axis; this path did not, so a placement of pitch 90 — a perfectly ordinary thing to copy
        // out of the game's own `ang` readout — put the camera in that degenerate state.
        //
        // The visible consequence was in flight rather than here: forward and up cancel at pitch 90,
        // and the residue left by `cos(90°)` was normalised up to full speed, sending the camera 300
        // units sideways. Fixed at both ends (D65) — the movement guards its own division, and this
        // stops producing an angle the rest of the viewer treats as impossible.
        return (
            (values[0], values[1], values[2]),
            Math.Clamp(values[3], -89f, 89f),
            values[4]);
    }
}
