using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Render;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The overhead markers: a dot for every player the world cannot draw.
/// </summary>
/// <remarks>
/// **This was <c>MainForm.ShowPlayers</c>, <c>ShowPositions</c> and <c>PlayerModel</c>** (B188,
/// D90). Two of them carried the SAME six-line camera choice, which is the duplication that makes
/// two drawing paths drift — this project has already lost decals that way.
///
/// **The rules are Valve's, and <c>CMapOverview::CanPlayerBeSeen</c> states them in its own words.**
/// It is worth reading before changing anything below: the engine's spectator overview asks the
/// same questions in the same order.
/// </remarks>
public sealed class MapOverviewTests
{
    /// <summary>Team two is RED, which is the engine's numbering.</summary>
    private const int RedTeam = 2;

    /// <summary>Team three is BLU.</summary>
    private const int BluTeam = 3;

    /// <summary>Team one is spectator, and nought is unassigned.</summary>
    private const int SpectatorTeam = 1;

    private const int SoldierClass = 3;

    private const int NoModelClass = 9;

    [Test]
    public void Players_WithNoModel_GetAMarker()
    {
        // The base case, and the control for every exclusion below: a player the world cannot draw
        // is the only kind that SHOULD get a dot.
        Overview([Player(1, NoModelClass, RedTeam)]).Count.ShouldBe(1);
    }

    [Test]
    public void Players_WithAModel_GetNoMarker()
    {
        // **A dot on top of a drawn player hides the very thing it stands in for**, which happened
        // and made a working render look like a failed one. The bystander is the test above: the
        // same call with a class the appearance has no model for still produces its marker, so this
        // measures the model lookup rather than the whole method going quiet.
        Overview([Player(1, SoldierClass, RedTeam)]).ShouldBeEmpty();
    }

    [Test]
    public void Players_OnTheSpectatorTeam_GetNoMarker()
    {
        // **Valve's own rule, in its own words**: `CMapOverview::CanPlayerBeSeen` carries
        // "we never track unassigned or real spectators" over `if ( player->team <= TEAM_SPECTATOR )`.
        //
        // Spectators and the SourceTV camera are `CTFPlayer` entities with real positions that
        // follow the action, so drawing everything puts convincing dots where nobody is standing.
        Overview([Player(1, NoModelClass, SpectatorTeam)]).ShouldBeEmpty();
    }

    [Test]
    public void Players_AtExactlyTheWorldOrigin_GetNoMarker()
    {
        // **Valve's rule, and it was missing here until the owner asked for it back.**
        // `CMapOverview::CanPlayerBeSeen` has `if( player->position == Vector(0,0,0) ) return false;`
        // commented "Invalid guy" — an entity that exists but has never been given a position.
        //
        // I had left it out, reasoning that a demo's entities are read rather than networked and
        // that `Drawn` already covers it. That reasoning was mine and was never checked against
        // anything; the standing rule is that Valve knew more than we do, every time.
        //
        // Exact equality on all three axes, as Valve writes it. A tolerance would swallow a player
        // legitimately standing near the map's origin, which plenty of maps put geometry at.
        Overview([Player(1, NoModelClass, RedTeam, 0f, 0f)]).ShouldBeEmpty();
    }

    [Test]
    public void Players_OneUnitFromTheOrigin_StillGetAMarker()
    {
        // The bystander, and the reason the check is exact rather than a radius: a player standing
        // one unit from the world origin is a real player at a real position. A tolerance here
        // would silently delete them from the overview on any map built around (0,0,0).
        Overview([Player(1, NoModelClass, RedTeam, 1f, 0f)]).Count.ShouldBe(1);
    }

    [Test]
    public void Players_ThatAreNotDrawn_GetNoMarker()
    {
        // **The dead, and this is the pass where it is easiest to get wrong.** A player the engine
        // would not draw has no model, and the rule above is "no model means a dot" — so filtering
        // corpses out of the MODEL pass alone turns every one of them into a marker gliding around
        // the map behind whoever it was spectating. The same defect in a cheaper primitive.
        Overview([Player(1, NoModelClass, RedTeam) with { Drawn = false }]).ShouldBeEmpty();
    }

    [Test]
    public void Players_OnEachTeam_AreColouredRedAndBlue()
    {
        // Exact values rather than "different", because a wrong team colour is worse than no
        // colour: it is read as information.
        IReadOnlyList<ScenePoint> points =
            Overview([Player(1, NoModelClass, RedTeam), Player(2, NoModelClass, BluTeam)]);

        points.Count.ShouldBe(2);

        (points[0].Red, points[0].Green, points[0].Blue).ShouldBe((0.90f, 0.31f, 0.27f));
        (points[1].Red, points[1].Green, points[1].Blue).ShouldBe((0.34f, 0.60f, 0.78f));
    }

    [Test]
    public void Players_AtTheCentreOfTheFittedView_LandInTheMiddleOfIt()
    {
        // Clip space is -1..1 with the origin in the middle, so a subject at the centre of what the
        // camera was fitted to must arrive at (0,0). This is the assertion that would fail if the
        // camera were built from the wrong bounds — the half of this method with no other check.
        // **The bracket is deliberately NOT centred on the world origin**, because a player standing
        // there is filtered as invalid (see above). Centred on (1000,1000) instead, so the middle
        // subject is a real position that must still project to the middle of the view.
        IReadOnlyList<ScenePoint> points = Overview(
            [
                Player(1, NoModelClass, RedTeam, 0f, 0f),
                Player(2, NoModelClass, RedTeam, 2000f, 2000f),
                Player(3, NoModelClass, RedTeam, 1000f, 1000f),
            ]);

        // The corner subject at the origin is dropped, so two markers come back and the CENTRED one
        // is the last of them. The camera still framed all three, which is what makes this a test
        // of the projection rather than of the filter.
        points.Count.ShouldBe(2);

        points[1].X.ShouldBe(0f, 0.001f);
        points[1].Y.ShouldBe(0f, 0.001f);
    }

    [Test]
    public void Players_WhenNoneAreGiven_AreNoMarkers()
    {
        // Not an error and not a camera: fitting a view to an empty set has no answer, so this must
        // return before trying.
        MapOverview.Players([], map: null, MapCamera(), 800, 600, new Appearance()).ShouldBeEmpty();
    }

    [Test]
    public void Players_WithNoAppearance_Refuse()
    {
        Should.Throw<ArgumentNullException>(() => MapOverview.Players(
            [Player(1, NoModelClass, RedTeam)], map: null, MapCamera(), 800, 600, appearance: null!));
    }

    [Test]
    public void Players_WithNoList_Refuse()
    {
        Should.Throw<ArgumentNullException>(() =>
            MapOverview.Players(null!, map: null, MapCamera(), 800, 600, new Appearance()));
    }

    [Test]
    public void Positions_AtTheCentreOfTheFittedView_LandInTheMiddleOfIt()
    {
        // The second entry point, which fits to bare world positions and asks the appearance
        // nothing. It shares the camera choice with the one above, which is the whole reason both
        // live here — they were the same six lines in two methods.
        IReadOnlyList<ScenePoint> points = MapOverview.Positions(
            [(-2000f, -1500f), (2000f, 1500f), (0f, 0f)], map: null, MapCamera(), 800, 600);

        points.Count.ShouldBe(3);

        points[2].X.ShouldBe(0f, 0.001f);
        points[2].Y.ShouldBe(0f, 0.001f);
    }

    [Test]
    public void Positions_EveryOne_AreInClipSpace()
    {
        // A camera fitted to the subjects cannot place any of them outside the view, so this holds
        // for every input rather than only for the ones that happen to be central.
        foreach (ScenePoint point in MapOverview.Positions(
            [(-2000f, -1500f), (2000f, 1500f), (0f, 0f)], map: null, MapCamera(), 800, 600))
        {
            point.X.ShouldBeInRange(-1f, 1f);
            point.Y.ShouldBeInRange(-1f, 1f);
        }
    }

    [Test]
    public void Positions_WhenNoneAreGiven_AreNoMarkers()
    {
        MapOverview.Positions([], map: null, MapCamera(), 800, 600).ShouldBeEmpty();
    }

    [Test]
    public void Positions_WithNoList_Refuse()
    {
        Should.Throw<ArgumentNullException>(() =>
            MapOverview.Positions(null!, map: null, MapCamera(), 800, 600));
    }

    [Test]
    public void Players_WithNoMapCamera_Refuse()
    {
        // **Refused even though this call would never READ it** — with `map: null` the fitted
        // branch runs and the map camera goes unused. A caller that could not supply one has a
        // problem worth being told about at the boundary, rather than on whichever later frame
        // happens to load a map.
        Should.Throw<ArgumentNullException>(() => MapOverview.Players(
            [Player(1, NoModelClass, RedTeam)], map: null, mapCamera: null!, 800, 600, new Appearance()));
    }

    [Test]
    public void Positions_WithNoMapCamera_Refuse()
    {
        Should.Throw<ArgumentNullException>(() =>
            MapOverview.Positions([(0f, 0f)], map: null, mapCamera: null!, 800, 600));
    }

    /// <summary>The markers a set of players produces, through a camera fitted to them.</summary>
    private static IReadOnlyList<ScenePoint> Overview(IReadOnlyList<ScenePlayer> players) =>
        MapOverview.Players(players, map: null, MapCamera(), 800, 600, new Appearance());

    /// <summary>A camera framing the whole world, standing in for the view's own.</summary>
    /// <remarks>
    /// **Passed rather than used, because with no map loaded it must be IGNORED.** Every test here
    /// passes <c>map: null</c>, which is the branch that fits to the subjects — and this camera
    /// frames 32,768 units, so a `Fit` result and this one are nowhere near each other. If the map
    /// camera were silently preferred, the centring assertions could not pass.
    /// </remarks>
    private static TopDownCamera MapCamera() =>
        TopDownCamera.Fit([(-16384f, -16384f), (16384f, 16384f)], 800, 600);

    /// <summary>A player standing somewhere, defaulting to somewhere that is NOT the origin.</summary>
    /// <remarks>
    /// **The default used to be (0,0), and that was the fixture sitting on Valve's sentinel.** The
    /// engine treats a player at exactly the world origin as invalid, so every test that did not
    /// care where its player stood was, unknowingly, testing the one position with a special
    /// meaning. Harmless while the check was missing and misleading the moment it arrived.
    /// </remarks>
    private static ScenePlayer Player(
        int entity, int playerClass, int team, float x = 64f, float y = 64f) =>
        new(entity, x, y, 0f, team, Health: 100, PlayerClass: playerClass);

    /// <summary>An appearance that knows one class and nothing else.</summary>
    private sealed class Appearance : IPlayerAppearance
    {
        public string? ModelOf(int playerClass) =>
            playerClass == SoldierClass ? "models/player/soldier.mdl" : null;

        public string? WeaponSuffix(string? weaponClass, int? playerClass) => null;

        public bool Airwalks(int playerClass) => false;

        public string? Hands(int playerClass) => null;
    }
}
