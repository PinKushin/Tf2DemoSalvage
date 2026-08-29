using System;
using System.Globalization;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Core.Net;
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

    /// <summary>Where the overhead placement is centred, or null for the map's own centre.</summary>
    /// <remarks>
    /// **`--look x y`, which was accepted and dropped until 2026-08-29** (B226). It centres the
    /// opening view on a world point instead of on the middle of the map — useful for going
    /// straight to a chokepoint rather than framing the whole thing.
    ///
    /// Only affects the FIRST placement, like everything else here: once the camera has an origin
    /// it flies normally and nothing re-centres it.
    /// </remarks>
    public (float X, float Y)? LookAt { get; set; }

    /// <summary>How much of the map the overhead placement frames; 1 is all of it.</summary>
    /// <remarks>
    /// **`--zoom`, dropped the same way and restored with it.** Larger frames less, as zoom means
    /// everywhere; a non-positive value is ignored rather than obeyed, since zero would divide the
    /// extent by nothing and a negative one would invert the rectangle.
    /// </remarks>
    public float Zoom { get; set; } = 1f;

    /// <summary>Degrees the camera turns per pixel dragged.</summary>
    /// <remarks>
    /// A quarter of a degree, so a full turn is about a screen and a half of dragging.
    ///
    /// **Source's own `sensitivity` is a different quantity and is deliberately not used here** — it
    /// scales a raw device count rather than a pixel, so a number taken from a config would not mean
    /// the same thing. This is chosen for the drag. Recorded because it looks like a D69 gap and is
    /// not one.
    /// </remarks>
    public const float DegreesPerPixel = 0.25f;

    /// <summary>How far the camera may pitch, in degrees.</summary>
    /// <remarks>
    /// The engine's own clamp for a player, and it is not a matter of taste: the camera basis is
    /// degenerate looking exactly along the world's up axis.
    /// </remarks>
    public const float PitchLimit = 89f;

    /// <summary>Where the camera is, or null until something places it.</summary>
    public (float X, float Y, float Z)? Origin { get; set; }

    /// <summary>Where it is looking, in degrees.</summary>
    /// <remarks>
    /// **Starts at a shallow angle rather than at zero**, and the reason travelled here from
    /// `MainForm._freeAngles` when that accessor died (B206): a camera on the horizon looking across
    /// a map shows mostly wall, and the first thing anyone wants from this view is to see whether the
    /// players are standing up.
    /// </remarks>
    public (float Pitch, float Yaw) Angles { get; set; } = (35f, 0f);

    /// <summary>Turn the camera by a mouse drag.</summary>
    /// <param name="deltaX">Pixels dragged rightward.</param>
    /// <param name="deltaY">Pixels dragged downward.</param>
    /// <remarks>
    /// **This was inline in `MainForm.OnViewportMouseMove`, and identically in `FreeLookState`,
    /// which nothing ran** (B206). Two copies of one formula and the tested copy was the dead one:
    /// the mouse look the viewer actually performed had no tests at all.
    ///
    /// **Yaw is subtracted and pitch added**, which is not symmetry for its own sake — dragging
    /// right turns the view left because the world moves under a fixed camera, and dragging down
    /// pitches down because screen Y grows downward.
    /// </remarks>
    public void Drag(float deltaX, float deltaY) =>
        Angles = (
            Math.Clamp(Angles.Pitch + (deltaY * DegreesPerPixel), -PitchLimit, PitchLimit),
            Angles.Yaw - (deltaX * DegreesPerPixel));

    /// <summary>How far one wheel notch travels, in world units.</summary>
    /// <remarks>
    /// **A distance, unlike flight, because a wheel notch IS a discrete event.** Key-driven flight
    /// used to work this way and could not — a held key is a duration and became one in
    /// <c>FreeFlight</c> (B97) — but a notch has no duration to integrate over.
    ///
    /// **128 units.** It was written `FlySpeed * 4f` at the call site, where `FlySpeed` was 32 and
    /// used for nothing else (B204, B206) — so the number a reader had to compute is now the number
    /// they read.
    /// </remarks>
    public const float WheelTravel = 128f;

    /// <summary>Move the camera along its own view direction.</summary>
    /// <param name="forward">Whether to travel forwards.</param>
    /// <param name="ifUnplaced">Where to start from when the camera has not been placed yet.</param>
    /// <remarks>
    /// **This was the free-look branch of `MainForm.OnViewportWheel`** (B204, B206), including a
    /// hand-inlined copy of `AngleVectors`' forward vector. In every editor the wheel flies, and it
    /// is far quicker than tapping W across a map.
    ///
    /// **Travels along the full forward vector, pitch included**, so looking down and scrolling
    /// descends. Flattening it to the XY plane would make the wheel refuse to go down through a map,
    /// which reads as the camera being blocked rather than as the travel being wrong.
    ///
    /// **The unplaced fallback matches <c>Fly</c>'s**, deliberately: the camera is placed on first
    /// use, and travelling from an unset origin would silently define it as (0,0,0), the corner of
    /// the map.
    /// </remarks>
    public void Dolly(bool forward, (float X, float Y, float Z) ifUnplaced)
    {
        (float X, float Y, float Z) heading = AngleVectors.Forward(Angles.Pitch, Angles.Yaw);
        (float X, float Y, float Z) from = Origin ?? ifUnplaced;

        float travel = forward ? WheelTravel : -WheelTravel;

        Origin = (
            from.X + (heading.X * travel),
            from.Y + (heading.Y * travel),
            from.Z + (heading.Z * travel));
    }

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

    /// <summary>What the open demo's server had its replicated ConVars set to.</summary>
    /// <remarks>
    /// **The free camera's speed is the server's, not this project's** (D106, B215). `sv_maxspeed`
    /// and `sv_specspeed` are what `FullNoClipMove` multiplies, both are `FCVAR_REPLICATED`, and a
    /// mod that changes movement — a jump or surf server — sends the new values in the demo. Until
    /// this was wired the values arrived, decoded correctly, and were ignored in favour of two
    /// constants.
    ///
    /// **Valve's declared defaults until a demo says otherwise**, which is what the engine does for
    /// a server that changed nothing. <see cref="SetServer"/> is how a demo replaces it, and it logs
    /// what moved so a wrong speed is a line in the log rather than a feeling about the camera.
    /// </remarks>
    public ServerConVars Server { get; private set; } = FreeFlightPath.Shipped;

    /// <summary>Takes the open demo's replicated ConVars, replacing Valve's defaults.</summary>
    /// <param name="server">What the recording server had set.</param>
    /// <remarks>
    /// **Logged rather than silent, and only when something actually moved.** A vanilla competitive
    /// server sends forty values and changes none of these; saying so every time a demo opens would
    /// be noise, and saying nothing when a jump server halves the camera's speed is the failure this
    /// whole change is about.
    /// </remarks>
    public void SetServer(ServerConVars server)
    {
        ArgumentNullException.ThrowIfNull(server);

        Server = server;

        if (server.Changed is { Count: > 0 } moved)
        {
            log.LogInformation(
                "free camera: this server changed {Count} movement convars ({Names}); flying at " +
                "{Speed} units a second rather than {Shipped}",
                moved.Count,
                string.Join(", ", moved),
                FreeFlightPath.SpeedPerSecond(server),
                FreeFlightPath.SpeedPerSecond(FreeFlightPath.Shipped));
        }
    }

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
            intent, Math.Min(seconds, MaximumFrameSeconds), Angles.Pitch, Angles.Yaw, Server);

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

        // **`--look` and `--zoom` reshape what is framed, rather than moving the camera afterwards**
        // (B226). Both were parsed and consumed by nothing until 2026-08-29. Applying them here
        // keeps every other property of the placement — the clearance above geometry, the 89-degree
        // pitch, the fit-the-tighter-axis arithmetic — because `For` still does all of it; only the
        // rectangle handed to it changes.
        MapBounds framed = OverheadPlacement.Framed(map.MainBounds, LookAt, Zoom);

        ((float X, float Y, float Z) origin, float pitch, float yaw) = OverheadPlacement.For(
            framed.MinX,
            framed.MinY,
            framed.MaxX,
            framed.MaxY,
            highest,
            fieldOfView: FieldOfView,
            aspect: aspect);

        Origin = origin;
        Angles = (pitch, yaw);

        // **Said "of a map measuring ..." only when the two differ**, so an ordinary launch reads
        // exactly as it always did and a `--look`/`--zoom` launch says both numbers. A local rather
        // than a nested `string.Create`: nesting one inside an interpolation turns the outer
        // argument into a plain concatenated string, which no longer binds to the interpolated
        // handler overload — CS1620, and the build failure is how that was found.
        string ofMap = framed.Equals(map.MainBounds)
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $" of a map measuring {map.MainBounds.MaxX - map.MainBounds.MinX:0.##} x " +
                $"{map.MainBounds.MaxY - map.MainBounds.MinY:0.##}");

        log.LogInformation(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                // **Reports what was FRAMED, not what the map measures**, and it reported the map
                // until 2026-08-29. With `--zoom 4` the line said "framing 8192 x 9728" while the
                // camera sat at a quarter the height actually framing 2048 x 2432 — a log that
                // contradicts the placement on its own line, which is worse than no log at all.
                $"free camera placed overhead at ({origin.X:0.##},{origin.Y:0.##},{origin.Z:0.##}) " +
                $"pitch {pitch:0.##}, framing {framed.MaxX - framed.MinX:0.##} x " +
                $"{framed.MaxY - framed.MinY:0.##}{ofMap}"));
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
            Math.Clamp(values[3], -PitchLimit, PitchLimit),
            values[4]);
    }
}
