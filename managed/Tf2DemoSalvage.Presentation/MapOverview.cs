using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Render;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The overhead markers: a dot for every player the world cannot draw.</summary>
/// <remarks>
/// **This was <c>MainForm.ShowPlayers</c>, <c>ShowPositions</c> and <c>PlayerModel</c>** (B188,
/// D90), and the two public methods carried the SAME six-line camera choice. That duplication is
/// how two drawing paths drift: decals were added to one of this viewer's two draw paths and not
/// the other, and its pictures went on being read as though they showed the viewer.
///
/// **Presentation rather than Scene, because it turns domain state into drawable primitives** —
/// exactly what <see cref="FpsOverlay"/> does with <c>HudQuad</c>. It is also the only project that
/// can see both halves: <c>ScenePoint</c> is Render's and <c>LoadedMap</c> is Scene's.
///
/// **Valve's equivalent is <c>CMapOverview</c>** (`game/client/game_controls/MapOverview.cpp`),
/// the spectator map panel, and the name is taken from it deliberately. Its `MapPlayer_t` carries
/// `Color color; // players team color` per player, and `CanPlayerBeSeen` asks the same questions
/// this does — including, in its own words, "we never track unassigned or real spectators".
///
/// **Every rule of `CanPlayerBeSeen` is implemented, including the origin check — and that last one
/// was missing until the owner asked for it back.** The first version of this type left out
/// `if( player->position == Vector(0,0,0) ) return false;` ("Invalid guy") on my reasoning that a
/// demo's entities are read rather than networked and that <c>Drawn</c> already covered it. That
/// reasoning was mine, was never checked against anything, and was recorded in a comment instead of
/// being asked about.
///
/// **The standing rule, in the owner's words: "i assume and want you to assume valve knew more than
/// us and has the better idea, every time".** A departure is a question, not a note.
/// </remarks>
public static class MapOverview
{
    /// <summary>RED's marker colour, matching the team colour the world models are tinted with.</summary>
    private static readonly (float Red, float Green, float Blue) RedMarker = (0.90f, 0.31f, 0.27f);

    /// <summary>BLU's marker colour.</summary>
    private static readonly (float Red, float Green, float Blue) BluMarker = (0.34f, 0.60f, 0.78f);

    /// <summary>The colour of a bare world position, which belongs to no team.</summary>
    /// <remarks>Amber, so it reads as an annotation rather than as a player of some third team.</remarks>
    private static readonly (float Red, float Green, float Blue) PositionMarker = (1f, 0.85f, 0.3f);

    /// <summary>A dot for every player at this moment that the world will not draw.</summary>
    /// <param name="players">The players, from the timeline.</param>
    /// <param name="map">The loaded map, or null when none is open.</param>
    /// <param name="mapCamera">The overhead camera, used when there is a map to frame.</param>
    /// <param name="viewportWidth">The viewport's width in pixels.</param>
    /// <param name="viewportHeight">The viewport's height in pixels.</param>
    /// <param name="appearance">What the install says each class looks like.</param>
    /// <returns>Clip-space points, coloured by team.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Team two is RED and team three is BLU**, which is the engine's own numbering: nought is
    /// unassigned and one is spectator. A player whose team has not arrived yet is drawn in BLU's
    /// colour rather than guessed at — see the note on the branch itself.
    /// </remarks>
    public static IReadOnlyList<ScenePoint> Players(
        IReadOnlyList<ScenePlayer> players,
        LoadedMap? map,
        TopDownCamera mapCamera,
        int viewportWidth,
        int viewportHeight,
        IPlayerAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(appearance);

        // Required even though it goes unused when there is no map: a caller with a camera it
        // cannot supply has a problem worth being told about, and "it only matters on the other
        // branch" is how a null reaches production on the branch that does matter.
        ArgumentNullException.ThrowIfNull(mapCamera);

        if (players.Count == 0)
        {
            return [];
        }

        TopDownCamera camera = CameraFor(
            map,
            mapCamera,
            [.. players.Select(player => (player.X, player.Y))],
            viewportWidth,
            viewportHeight);

        List<ScenePoint> points = new(players.Count);

        foreach (ScenePlayer player in players)
        {
            // **Spectators and the SourceTV camera are CTFPlayer entities too**, with real
            // positions that follow the action — so drawing everything puts convincing dots on the
            // map where nobody is standing. Valve's own words, in `CanPlayerBeSeen`: "we never
            // track unassigned or real spectators".
            //
            // **The dead are skipped here for the same reason, and the marker pass is where that is
            // easiest to get wrong.** A player the engine would not draw has no model, and the rule
            // below is "no model means a dot" — so removing dead players from the model pass alone
            // would have turned every corpse into a marker gliding around the map behind whoever it
            // was spectating, which is the same defect in a cheaper primitive.
            // **"Invalid guy" — Valve's own comment, and its own rule.** `CanPlayerBeSeen` refuses a
            // player whose position is exactly the world origin. Checked BEFORE the team test, as
            // Valve checks it.
            //
            // **Why, in the owner's reading:** "valve does the no draw at orgin thing because it
            // doesnt want dead players or spectators drawn in the map or sky somewhere if the origin
            // is not under the map". An entity that exists without a position sits at (0,0,0), and
            // (0,0,0) is a REAL place — it can be mid-air, inside a wall, or under the floor
            // depending on where the mapper put the world. So the dot is not merely meaningless, it
            // is convincing: it is exactly the same failure the spectator test below prevents, which
            // is why the two sit together.
            //
            // Exact equality on all three axes, as Valve writes it. A tolerance would swallow a
            // player legitimately standing near (0,0,0), which plenty of maps put geometry at.
            if (player is { X: 0f, Y: 0f, Z: 0f })
            {
                continue;
            }

            if (!player.IsPlaying || !player.Drawn)
            {
                continue;
            }

            // **A marker only for a player with no model.** Once the class models draw, a dot on
            // top of one hides the very thing it was standing in for — which is exactly what
            // happened, and made a working render look like a failed one.
            if (PlayerProps.ModelFor(player, appearance) is not null)
            {
                continue;
            }

            (float x, float y) = camera.Project(player.X, player.Y);

            (float red, float green, float blue) =
                player.Team == SceneTeams.Red ? RedMarker : BluMarker;

            points.Add(new ScenePoint(x, y, red, green, blue));
        }

        return points;
    }

    /// <summary>A dot for each of a set of bare world positions.</summary>
    /// <param name="positions">World XY positions, in Source units.</param>
    /// <param name="map">The loaded map, or null when none is open.</param>
    /// <param name="mapCamera">The overhead camera, used when there is a map to frame.</param>
    /// <param name="viewportWidth">The viewport's width in pixels.</param>
    /// <param name="viewportHeight">The viewport's height in pixels.</param>
    /// <returns>Clip-space points.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="positions"/> is null.</exception>
    public static IReadOnlyList<ScenePoint> Positions(
        IReadOnlyList<(float X, float Y)> positions,
        LoadedMap? map,
        TopDownCamera mapCamera,
        int viewportWidth,
        int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(mapCamera);

        if (positions.Count == 0)
        {
            return [];
        }

        TopDownCamera camera = CameraFor(map, mapCamera, positions, viewportWidth, viewportHeight);

        List<ScenePoint> points = new(positions.Count);

        foreach ((float worldX, float worldY) in positions)
        {
            (float x, float y) = camera.Project(worldX, worldY);

            points.Add(new ScenePoint(x, y, PositionMarker.Red, PositionMarker.Green, PositionMarker.Blue));
        }

        return points;
    }

    /// <summary>The camera the markers project through.</summary>
    /// <remarks>
    /// **With a map loaded the subjects are projected through the MAP's camera, so they land where
    /// they actually are in the world.** Fitting to the subjects instead would place them correctly
    /// relative to each other and wrongly relative to everything around them — a squad in one
    /// corner would fill the screen.
    ///
    /// Fitting is the fallback rather than the rule because the map's bounds are not known until a
    /// BSP has been read, and a viewer with no map still has something to show.
    ///
    /// **These six lines were duplicated across the two methods above**, which is the shape that
    /// lets two drawing paths drift apart while both keep working.
    /// </remarks>
    private static TopDownCamera CameraFor(
        LoadedMap? map,
        TopDownCamera mapCamera,
        IReadOnlyList<(float X, float Y)> subjects,
        int viewportWidth,
        int viewportHeight) =>
        map is { } outlined && !outlined.Outline.IsEmpty
            ? mapCamera
            : TopDownCamera.Fit(
                subjects,
                Math.Max(1, viewportWidth),
                Math.Max(1, viewportHeight));
}
