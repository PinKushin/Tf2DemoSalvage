using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CEscortHillPanel` (game/client/tf/tf_hud_escort.cpp:37): a hill's arrows along the track, scrolling while the cart is on it.</summary>
/// <remarks>
/// The alpha pulse Valve ticks every 750 ms (:57-80) is a 32-to-96 cycle on the hill the cart is on, else 64; this sets it from the
/// clock rather than through the animation controller. The UV scroll steps 0.02 a paint (:112), so its speed follows the frame
/// rate in Valve's too; here it follows the clock at 66 steps a second.
/// </remarks>
/// <param name="parent">The escort panel.</param>
/// <param name="name">`hill_panel%d`.</param>
public sealed class TfEscortHillPanel(VguiPanel parent, string name) : VguiPanel(parent, name)
{
    private const string Material = "hud/cart_track_arrow";

    /// <summary>`m_nHill`.</summary>
    public int Hill { get; set; }

    /// <summary>`m_nTeam`.</summary>
    public int Team { get; set; }

    /// <summary>Whether the cart is on this hill: `IsTrainOnHill`, which the watcher sets from its progress (c_team_train_watcher.cpp).</summary>
    public bool OnHill { get; set; }

    /// <summary>`IsHillDownhill`.</summary>
    public bool Downhill { get; set; }

    /// <summary>`m_flUVScroll`.</summary>
    public float Scroll { get; set; }

    /// <inheritdoc/>
    /// <remarks>`Paint` (:100): the arrow texture across the panel, mirrored downhill.</remarks>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        (int textureWide, int textureTall) = surface.DrawGetTextureSize(Material);
        float uvWide = textureTall > 0 && textureWide > 0 ? Wide / (textureWide * (Tall / (float)textureTall)) : 1f;
        float left = Scroll;
        float right = uvWide + Scroll;

        surface.DrawSetTexture(Material);
        surface.DrawSetColor((255, 255, 255, 255));

        Span<VguiVertex> quad = Downhill
            ? [new(0, 0, right, 0), new(Wide, 0, left, 0), new(Wide, Tall, left, 1), new(0, Tall, right, 1)]
            : [new(0, 0, left, 0), new(Wide, 0, right, 0), new(Wide, Tall, right, 1), new(0, Tall, left, 1)];

        surface.DrawTexturedPolygon(quad);
    }
}

/// <summary>`CTFHudEscortProgressBar` (tf_hud_escort.cpp:322): the coloured fill of a multiple-train track up to the cart.</summary>
/// <param name="parent">The escort panel.</param>
/// <param name="name">The panel's name.</param>
public sealed class TfEscortProgressBar(VguiPanel? parent, string? name) : VguiImagePanel(parent, name)
{
    /// <inheritdoc/>
    public override string ClassName => "CTFHudEscortProgressBar";

    /// <summary>`m_flPercent`.</summary>
    public float Percentage { get; set; }

    /// <summary>`m_nTeam`.</summary>
    public int Team { get; set; } = 3;

    /// <inheritdoc/>
    /// <remarks>`Paint` (:359): the team's track up to the cart and a line at its end.</remarks>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (Percentage <= 0f)
        {
            return;
        }

        string material = Team switch
        {
            2 => "hud/cart_track_red_opaque",
            3 => "hud/cart_track_blue_opaque",
            _ => "hud/cart_track_neutral_opaque",
        };
        int middle = (int)(Wide * Percentage);

        surface.DrawSetTexture(material);
        surface.DrawSetColor((255, 255, 255, 210));
        surface.DrawTexturedPolygon([new(0, 0, 0, 0), new(middle, 0, 1, 0), new(middle, Tall, 1, 1), new(0, Tall, 0, 1)]);
        surface.DrawSetColor((245, 229, 196, 210));
        // `DrawLine( nMiddle - 1, 0, nMiddle - 1, nBarH )`: a vertical line, one pixel wide.
        surface.DrawFilledRect(middle - 1, 0, middle, Tall);
    }
}

/// <summary>`CTFHudMultipleEscort` (tf_hud_escort.cpp:1120): a track per team, the local player's on top.</summary>
public sealed class TfHudMultipleEscort : VguiEditablePanel
{
    private int _topTeam = -1;

    /// <summary>`CTFHudMultipleEscort( parent, name )`: BLU's and RED's panels.</summary>
    /// <param name="parent">The objective status.</param>
    public TfHudMultipleEscort(VguiPanel parent)
        : base(parent, "ObjectiveStatusMultipleEscort")
    {
        BluePanel = new TfHudEscort(this, "BlueEscortPanel");
        RedPanel = new TfHudEscort(this, "RedEscortPanel") { Team = 2 };
    }

    /// <summary>`m_pBluePanel`.</summary>
    public TfHudEscort BluePanel { get; }

    /// <summary>`m_pRedPanel`.</summary>
    public TfHudEscort RedPanel { get; }

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:1177): `if_blue_is_top` for a BLU player, else `if_red_is_top`.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);

        HudState state = HudViewport.Of(this)?.State ?? default;
        string top = state.Team == 3 ? "if_blue_is_top" : "if_red_is_top";
        string[]? conditions = state.Rules.MultipleTrains ? [top] : null;

        LoadControlSettings("resource/UI/ObjectiveStatusMultipleEscort.res", context, conditions);
    }

    /// <summary>Both panels' updates, and `localplayer_changeteam`'s top panel (:1213-1241).</summary>
    /// <param name="state">The game state.</param>
    public void Update(HudState state)
    {
        if (state.Team != _topTeam)
        {
            _topTeam = state.Team;
            BluePanel.TopPanel = state.Team == 3;
            RedPanel.TopPanel = state.Team != 3;
            InvalidateLayout(reloadScheme: true);
        }

        BluePanel.Visible = Visible;
        RedPanel.Visible = Visible;
        BluePanel.Update(state);
        RedPanel.Update(state);
    }
}

/// <summary>`CTFHudEscort` (game/client/tf/tf_hud_escort.cpp:395): a payload track, its points, its hills and the cart along it.</summary>
/// <remarks>
/// **From state each think, where the engine waits for its events**: the watcher's changes come through
/// <see cref="DemoTimeline.EscortAt"/>, so a seek lands on what playing accumulated. **Not ported:** the status teardrop over the
/// cart (`CEscortStatusTeardrop`, :158-317), the alarm's animation sequences, `cl_hud_minmode`'s swoop size, and the freeze-cam
/// screenshot check.
/// </remarks>
public sealed class TfHudEscort : VguiEditablePanel
{
    private const int TeamRed = 2;
    private const float RecedeCountdown = 20f;

    private readonly VguiEditablePanel _itemPanel;
    private readonly VguiImagePanel _speedBackwards;
    private readonly VguiImagePanel _capPlayerImage;
    private readonly TfExLabel _capNumPlayers;
    private readonly VguiImagePanel _itemImage;
    private readonly VguiImagePanel _itemImageBottom;
    private readonly VguiImagePanel _blocked;
    private readonly VguiImagePanel _itemImageAlert;
    private readonly VguiImagePanel _cpTemplate;
    private readonly VguiImagePanel _levelBar;
    private readonly TfEscortProgressBar _progressBar;
    private readonly TfEscortHillPanel[] _hills = new TfEscortHillPanel[SceneTrack.MaxHills];
    private readonly List<VguiImagePanel> _icons = [];
    private readonly List<int> _iconPoints = [];
    private TfControlPointSwoop? _swoop;
    private bool _multipleTrains;
    private int _hillCount;
    private bool _haveValidPointPositions;

    /// <summary>`CTFHudEscort( parent, name )` (:395).</summary>
    /// <param name="parent">The objective status, or the multiple escort.</param>
    /// <param name="name">The panel's name.</param>
    public TfHudEscort(VguiPanel parent, string name)
        : base(parent, name)
    {
        _itemPanel = new VguiEditablePanel(this, "EscortItemPanel");
        _speedBackwards = new VguiImagePanel(_itemPanel, "Speed_Backwards");
        _capPlayerImage = new VguiImagePanel(_itemPanel, "CapPlayerImage");
        _capNumPlayers = new TfExLabel(_itemPanel, "CapNumPlayers");
        _itemImage = new VguiImagePanel(_itemPanel, "EscortItemImage");
        _itemImageBottom = new VguiImagePanel(_itemPanel, "EscortItemImageBottom");
        _blocked = new VguiImagePanel(_itemPanel, "Blocked");
        _itemImageAlert = new VguiImagePanel(_itemPanel, "EscortItemImageAlert");
        _ = new VguiImagePanel(this, "HomeCPIcon");
        _cpTemplate = new VguiImagePanel(this, "SimpleControlPointTemplate");
        _levelBar = new VguiImagePanel(this, "LevelBar");
        _progressBar = new TfEscortProgressBar(this, "ProgressBar");

        for (int hill = 0; hill < _hills.Length; hill++)
        {
            _hills[hill] = new TfEscortHillPanel(this, $"hill_panel{hill}") { Hill = hill, Team = Team };
        }
    }

    /// <summary>`m_nTeam`: BLU by default (:445).</summary>
    public int Team { get; set; } = 3;

    /// <summary>`m_bTopPanel` (:446).</summary>
    public bool TopPanel { get; set; } = true;

    /// <summary>`m_pEscortItemPanel`.</summary>
    public VguiEditablePanel ItemPanel => _itemPanel;

    /// <summary>Whether the cart is drawn: `IsVisible`'s `m_bHaveValidPointPositions` (:507).</summary>
    public bool HaveValidPointPositions => _haveValidPointPositions;

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:565): the layout with the team's, the trains' and the hills' conditions.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);

        if (_swoop is null && HudViewport.Of(this) is { } viewport)
        {
            // :434-437: the highlight swoop lives on the viewport.
            _swoop = new TfControlPointSwoop(viewport, "EscortHilightSwoop") { ZPos = 10 };
        }

        LoadControlSettings("resource/UI/ObjectiveStatusEscort.res", context, Conditions(Team, _multipleTrains, TopPanel, _hillCount));

        _itemImageBottom.Visible = _multipleTrains && !TopPanel;
        _itemImage.Visible = !_multipleTrains || TopPanel;
    }

    /// <summary>The `.res` conditions `ApplySchemeSettings` builds (:569-624).</summary>
    /// <param name="team">`m_nTeam`.</param>
    /// <param name="multipleTrains">`m_bMultipleTrains`.</param>
    /// <param name="topPanel">`m_bTopPanel`.</param>
    /// <param name="hills">`m_nNumHills`.</param>
    /// <returns>The condition names, or null for none.</returns>
    public static string[]? Conditions(int team, bool multipleTrains, bool topPanel, int hills)
    {
        if (team <= 1)
        {
            return null;
        }

        string colour = team == TeamRed ? "red" : "blue";
        List<string> conditions = [$"if_team_{colour}"];

        if (multipleTrains)
        {
            conditions.Add("if_multiple_trains");
            conditions.Add($"if_multiple_trains_{colour}");
            conditions.Add(topPanel ? "if_multiple_trains_top" : "if_multiple_trains_bottom");
        }
        else if (hills > 0)
        {
            conditions.Add("if_single_with_hills");
            conditions.Add($"if_single_with_hills_{colour}");
        }

        return [.. conditions];
    }

    /// <summary>`OnTick` (:696) and the escort events (:870-925), from this frame's state.</summary>
    /// <param name="state">The game state.</param>
    public void Update(HudState state)
    {
        if (state.Objectives is not { } objectives)
        {
            return;
        }

        SceneTrack track = objectives.Track ?? new SceneTrack([], [0, 0, 0, 0], [], [false, false, false, false], []);
        bool reload = false;

        if (_multipleTrains != state.Rules.MultipleTrains)
        {
            _multipleTrains = state.Rules.MultipleTrains;
            reload = true;
        }

        if (_hillCount != track.HillCount(Team))
        {
            _hillCount = track.HillCount(Team);
            reload = true;
        }

        if (reload)
        {
            InvalidateLayout(reloadScheme: true);
        }

        UpdateCpImages(objectives, track);
        LayOutHills(track, Team == TeamRed ? state.EscortRed.Progress : state.EscortBlue.Progress);

        SceneEscort escort = Team == TeamRed ? state.EscortRed : state.EscortBlue;

        // "escort_speed" (:895-921): backwards, the cappers, and the blocked mark they hide.
        _speedBackwards.Visible = escort.Speed < 0;
        bool showPlayers = escort.Speed > 0 && escort.Players > 0;
        _capPlayerImage.Visible = showPlayers;
        _capNumPlayers.Visible = showPlayers;
        _itemPanel.SetDialogVariable("numcappers", escort.Players.ToString(CultureInfo.InvariantCulture));

        bool anyBlocked = false;

        foreach (int point in _iconPoints)
        {
            anyBlocked |= point < objectives.Points.Count && objectives.Points[point].Blocked;
        }

        _blocked.Visible = anyBlocked && !showPlayers && escort.Speed >= 0;
        _itemImageAlert.Visible = _multipleTrains && track.TrackAlarm(Team);

        // "The escort item position approaches its actual pos" (:744-766). Valve reads `m_flProgress`, not the interpolated
        // value it computes on the line before: the cart jumps to each update.
        int x = _levelBar.X + (int)(_levelBar.Wide * escort.Progress);
        int swoopX = x - (int)(12f * (HudViewport.Of(this)?.Wide ?? 640) / 640f);

        _itemPanel.X = x - (_itemPanel.Wide / 2);

        UpdateSwoop(state, escort, swoopX);

        // "5 second countdown to receding" (:829-842), which hides the blocked mark.
        float secondsToRecede = Math.Max(0f, escort.RecedeTime <= 0f ? 0f : escort.RecedeTime - state.CurTime);

        if (secondsToRecede is > 0f and <= RecedeCountdown)
        {
            _itemPanel.SetDialogVariable("recede", ((int)secondsToRecede + 1).ToString(CultureInfo.InvariantCulture));
            _blocked.Visible = false;
        }
        else
        {
            _itemPanel.SetDialogVariable("recede", string.Empty);
        }

        _progressBar.Visible = _multipleTrains;
        _progressBar.Team = Team;
        _progressBar.Percentage = escort.Progress;

        Visible = Visible && _haveValidPointPositions && state.ObserverMode != ObserverModes.FreezeCam;
    }

    /// <summary>`CSimpleControlPoint::UpdateImage` (tf_hud_escort.h:126): the point's owner, opaque with several trains or hills.</summary>
    /// <param name="owner">`GetOwningTeam`.</param>
    /// <param name="opaque">Whether the opaque image is used.</param>
    /// <returns>The image.</returns>
    public static string PointImage(int owner, bool opaque)
    {
        string colour = owner switch
        {
            3 => "blue",
            TeamRed => "red",
            _ => "neutral",
        };

        return opaque ? $"../hud/cart_point_{colour}_opaque" : $"../hud/cart_point_{colour}";
    }

    /// <summary>`UpdateCPImages` (:1046): an icon per visible point in the mini-round, at its path distance along the bar.</summary>
    private void UpdateCpImages(SceneObjectives objectives, SceneTrack track)
    {
        int valid = 0;
        int count = 0;
        bool opaque = _multipleTrains || _hillCount > 0;

        for (int point = 0; point < objectives.Points.Count; point++)
        {
            SceneControlPoint cp = objectives.Points[point];

            if (!cp.InMiniRound || !cp.Visible || (_multipleTrains && cp.Group != Team - 2))
            {
                continue;
            }

            if (count == _icons.Count)
            {
                _icons.Add(new VguiImagePanel(this, $"cp_{point}"));
                _iconPoints.Add(point);
            }

            VguiImagePanel icon = _icons[count];
            float distance = point < track.PathDistances.Count ? track.PathDistances[point] : 0f;

            _iconPoints[count] = point;
            icon.SetShouldScaleImage(true);
            (icon.X, icon.Y, icon.Wide, icon.Tall) = (_levelBar.X + (int)(_levelBar.Wide * distance) - (_cpTemplate.Wide / 2), _cpTemplate.Y, _cpTemplate.Wide, _cpTemplate.Tall);
            icon.ZPos = _cpTemplate.ZPos;
            icon.Visible = true;
            icon.SetImage(PointImage(cp.Owner, opaque));
            valid += distance > 0f ? 1 : 0;
            count++;
        }

        for (int extra = count; extra < _icons.Count; extra++)
        {
            _icons[extra].Visible = false;
        }

        _iconPoints.RemoveRange(count, _iconPoints.Count - count);
        _icons.RemoveRange(count, _icons.Count - count);

        // "we don't want to draw if we haven't got good point positions yet"
        _haveValidPointPositions = valid > 0;
    }

    /// <summary>`PerformLayout`'s hills (:516-560), and the watcher's `SetTrainOnHill` from its progress.</summary>
    private void LayOutHills(SceneTrack track, float progress)
    {
        for (int hill = 0; hill < _hills.Length; hill++)
        {
            TfEscortHillPanel panel = _hills[hill];

            panel.Team = Team;
            panel.Visible = hill < _hillCount;

            if (!panel.Visible)
            {
                continue;
            }

            (float start, float end) = track.Hill(Team, hill);
            int left = (int)(_levelBar.Wide * start);
            int right = (int)(_levelBar.Wide * end);

            (panel.X, panel.Y, panel.Wide, panel.Tall) = (_levelBar.X + left, _levelBar.Y, right - left, _levelBar.Tall);
            panel.ZPos = _levelBar.ZPos + 1;
            panel.OnHill = progress >= start && progress <= end;
            panel.Downhill = track.IsDownhill(Team, hill);
        }
    }

    /// <summary>`m_bShowSwoop` (:768-825): a defender's highlight over the cart when it starts to move.</summary>
    private void UpdateSwoop(HudState state, SceneEscort escort, int swoopX)
    {
        if (_swoop is null)
        {
            return;
        }

        int screenWide = HudViewport.Of(this)?.Wide ?? 640;
        int screenTall = HudViewport.Of(this)?.Tall ?? 480;
        float xres = screenWide / 640f;
        float yres = screenTall / 480f;
        int yOffset = (int)(138 * yres);
        int xOffset = 0;
        int wide = (int)(24 * xres);
        int tall = (int)(100 * yres);

        if (_multipleTrains)
        {
            (wide, tall, xOffset) = ((int)(20 * xres), (int)(70 * yres), (int)(2 * xres));
            yOffset = state.Team == Team ? (int)(97 * yres) : (int)(61 * yres);
        }

        int middle = _levelBar.Y + (_levelBar.Tall / 2);

        (_swoop.X, _swoop.Y, _swoop.Wide, _swoop.Tall) = (AbsX + swoopX + xOffset, AbsY + middle - yOffset, wide, tall);
        _swoop.Step(state.Team > 1 && Visible ? escort.SwoopStart : 0f, state.CurTime, state.ObserverMode == ObserverModes.FreezeCam);
    }
}
