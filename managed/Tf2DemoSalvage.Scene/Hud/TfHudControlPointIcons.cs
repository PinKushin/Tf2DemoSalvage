using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CHudControlPointIcons` (game/client/hud_controlpointicons.cpp:646): one icon per visible control point in the mini-round.</summary>
/// <remarks>
/// **From state each think, where the engine waits for `controlpoint_*` events**, as a seek or a rewind has no event history to
/// replay. The progress teardrop is `TfControlPointProgressBar`, arena's enable time `TfHudArenaCapPointCountdown`.
/// </remarks>
public sealed class TfHudControlPointIcons : VguiEditablePanel, IHudElement
{
    private readonly List<TfControlPointIcon> _icons = [];
    private readonly int[] _shown = new int[SceneObjectives.MaxControlPoints];
    private int _shownCount;
    private string _layout = string.Empty;

    /// <summary>`CHudControlPointIcons( "CHudControlPointIcons" )`.</summary>
    /// <param name="viewport">The viewport.</param>
    public TfHudControlPointIcons(VguiPanel viewport)
        : base(viewport, "HudControlPointIcons")
    {
        DeclareAnimationVar("separator_width", VguiPanelVarType.ProportionalInt, "7");
        DeclareAnimationVar("separator_height", VguiPanelVarType.ProportionalInt, "9");
        DeclareAnimationVar("height_offset", VguiPanelVarType.ProportionalInt, "0");
    }

    /// <inheritdoc/>
    public override string ClassName => "CHudControlPointIcons";

    /// <inheritdoc/>
    public int HiddenBits => HudVisibility.HideMiscStatus;

    /// <summary>The icon whose `GetCapIndex()` is a control point (:1206-1212), or null.</summary>
    /// <param name="index">The control point.</param>
    /// <returns>The icon, or null.</returns>
    public TfControlPointIcon? IconFor(int index)
    {
        for (int slot = 0; slot < _shownCount; slot++)
        {
            if (_shown[slot] == index)
            {
                return _icons[slot];
            }
        }

        return null;
    }

    /// <summary>`m_Icons`, those in use.</summary>
    public IReadOnlyList<TfControlPointIcon> Icons => _icons;

    /// <inheritdoc/>
    protected override void OnThink()
    {
        if (HudViewport.Of(this) is not { } viewport)
        {
            return;
        }

        HudState state = viewport.State;
        SceneObjectives? objectives = state.Objectives;
        int count = 0;
        bool changed = !string.Equals(_layout, objectives?.CapLayout ?? string.Empty, StringComparison.Ordinal);

        // `InitIcons` (:1221): `IsInMiniRound( i ) && IsCPVisible( i )`.
        for (int index = 0; index < (objectives?.Points.Count ?? 0); index++)
        {
            SceneControlPoint point = objectives!.Points[index];

            if (point.InMiniRound && point.Visible)
            {
                changed |= count >= _shownCount || _shown[count] != index;
                _shown[count++] = index;
            }
        }

        changed |= count != _shownCount;
        _shownCount = count;
        _layout = objectives?.CapLayout ?? string.Empty;

        while (_icons.Count < count)
        {
            // `vgui::EditablePanel( parent, "ControlPointIcon" )` (:119), whatever the element's own name: the name its `.res` block finds it by.
            _icons.Add(new TfControlPointIcon(this, "ControlPointIcon"));
        }

        for (int slot = 0; slot < _icons.Count; slot++)
        {
            _icons[slot].Visible = slot < count;

            if (slot < count)
            {
                _icons[slot].Update(state, _shown[slot]);
            }
            else
            {
                // The timer sits among the icons, so it does not hide with its icon.
                _icons[slot].HideTimer();
            }
        }

        if (changed)
        {
            InvalidateLayout();
        }
    }

    /// <inheritdoc/>
    /// <remarks>`PerformLayout` (:969): the lines, the panel's bounds at the screen's bottom centre, then each icon.</remarks>
    protected override void PerformLayout()
    {
        base.PerformLayout();

        if (Parent is not { } parent)
        {
            return;
        }

        int gapWide = GetInt("separator_width");
        int gapTall = GetInt("separator_height");
        List<List<TfControlPointIcon>> lines = Lines();
        int tall = gapTall;
        int tallest = gapTall;
        int widest = gapWide;
        int[] lineWide = new int[lines.Count];

        for (int line = 0; line < lines.Count; line++)
        {
            lineWide[line] = gapWide;

            int lineTall = 0;

            foreach (TfControlPointIcon icon in lines[line])
            {
                lineWide[line] += icon.Wide + gapWide;
                lineTall = Math.Max(lineTall, icon.Tall);
            }

            widest = Math.Max(widest, lineWide[line]);
            tallest = Math.Max(tallest, lineTall);
            tall += lineTall + gapTall;
        }

        // `ScreenWidth()` and `ScreenHeight()`, then `GetCapLayoutCustomPosition` (:1114-1129).
        int screenWide = HudViewport.Of(this)?.Wide ?? parent.Wide;
        int screenTall = HudViewport.Of(this)?.Tall ?? parent.Tall;
        float x0 = (screenWide - widest) * 0.5f;
        float y0 = screenTall - tall - GetInt("height_offset");
        SceneObjectives? objectives = HudViewport.Of(this)?.State.Objectives;

        if (objectives is not null)
        {
            if (MathF.Abs(objectives.CustomPositionX - -1f) > 0f)
            {
                x0 = objectives.CustomPositionX * screenWide;
            }

            if (MathF.Abs(objectives.CustomPositionY - -1f) > 0f)
            {
                y0 = objectives.CustomPositionY * screenTall;
            }
        }

        (X, Y, Wide, Tall) = ((int)x0, (int)y0, widest, tall);

        for (int line = 0; line < lines.Count; line++)
        {
            int x = (int)((widest - lineWide[line]) * 0.5f) + gapWide;
            int y = (line * gapTall) + (line * tallest) + gapTall;

            for (int position = 0; position < lines[line].Count; position++)
            {
                TfControlPointIcon icon = lines[line][position];

                (icon.X, icon.Y) = (x, y);
                x += icon.Wide + gapWide;

                // "If we have multiple lines, swipe up when capping."
                icon.SwipeUp = lines.Count > 1;

                // :1157-1174: north on the top line, else north-west left of the centre and north-east at or right of it.
                icon.CapProgressDirection = Direction(line, position, lines[line].Count);
            }
        }
    }

    /// <summary>
    /// `m_pszCapLayoutInHUD` read as `PerformLayout` reads it (:976-1046): a number before a space joins the line, one before a
    /// comma ends it, and a number that names no icon moves neither, so its separator is lost. Empty: all on one line.
    /// </summary>
    /// <param name="layout">The layout string.</param>
    /// <param name="slotOf">The icon slot of a control point, or -1.</param>
    /// <returns>Each line's icon slots.</returns>
    public static IReadOnlyList<IReadOnlyList<int>> ParseLayout(string layout, Func<int, int> slotOf)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(slotOf);

        List<List<int>> lines = [[]];
        int last = 0;
        int at = 0;

        // `while ( *pszChar ) { pszChar++; ... }`: the test is before the step, so the character after a separator, which
        // the separator's own step lands on, is stepped over unread.
        while (at < layout.Length)
        {
            at++;

            if (at >= layout.Length || layout[at] is not (' ' or ','))
            {
                continue;
            }

            int slot = slotOf(PanelLayout.Atoi(layout.AsSpan(last, at - last)));

            if (slot >= 0)
            {
                lines[^1].Add(slot);

                if (layout[at] == ',')
                {
                    lines.Add([]);
                }
            }

            at++;
            last = at;
        }

        // "Now get the trailing number": it never starts a line.
        int trailing = slotOf(PanelLayout.Atoi(layout.AsSpan(Math.Min(last, layout.Length))));

        if (trailing >= 0)
        {
            lines[^1].Add(trailing);
        }

        // "If we've hit a line with nothing in it, we're done."
        int empty = lines.FindIndex(line => line.Count == 0);

        return empty < 0 ? lines : lines.GetRange(0, empty);
    }

    /// <summary>:1157-1174: north on the top line; below it, north-east past the line's middle, else north-west.</summary>
    private static CapProgressDirection Direction(int line, int position, int count)
    {
        if (line == 0)
        {
            return CapProgressDirection.North;
        }

        return (position + 1) / (float)count > 0.5f ? CapProgressDirection.NorthEast : CapProgressDirection.NorthWest;
    }

    private List<List<TfControlPointIcon>> Lines()
    {
        if (_layout.Length == 0)
        {
            return _shownCount > 0 ? [_icons.GetRange(0, _shownCount)] : [];
        }

        List<List<TfControlPointIcon>> lines = [];

        foreach (IReadOnlyList<int> slots in ParseLayout(_layout, point => Array.IndexOf(_shown, point, 0, _shownCount)))
        {
            List<TfControlPointIcon> line = [];

            foreach (int slot in slots)
            {
                line.Add(_icons[slot]);
            }

            lines.Add(line);
        }

        return lines;
    }
}

/// <summary>`CControlPointIcon` (hud_controlpointicons.cpp:119): one control point's owner, lock and capture.</summary>
public sealed class TfControlPointIcon : VguiEditablePanel
{
    private const int FirstGameTeam = 2;
    private const int LastGameTeam = 3;
    private const int TeamRed = 2;

    /// <summary>The Arena value of `m_nGameType`, `TF_GAMETYPE_ARENA`.</summary>
    private const int GameTypeArena = 4;

    private readonly TfControlPointPulseable _baseImage;
    private readonly TfControlPointCapArrow _capImage;
    private VguiImagePanel? _overlayImage;
    private VguiImagePanel? _capPlayerImage;
    private VguiLabel? _capNumPlayers;
    private string? _material;
    private string? _overlay;
    private bool _locked;
    private int _players = -1;
    private const float TimerTick = 0.15f;

    private readonly TfControlPointCountdown _countdown;
    private readonly TfExLabel _timerLabel;
    private readonly VguiImagePanel _timerBackground;
    private (byte, byte, byte, byte) _regularColor = (235, 226, 202, 255);
    private (byte, byte, byte, byte) _highlightColor = (192, 28, 0, 255);
    private float _timerSeen = -1f;
    private float _timerSetAt;
    private float _timerLastNow;
    private TfControlPointSwoop? _capHighlightImage;
    private TfControlPointCapturePulse? _capPulseImage;

    /// <summary>`CControlPointIcon( parent, name, index )`, and the children `ApplySchemeSettings` makes on its first pass (:174).</summary>
    /// <param name="parent">The icons panel.</param>
    /// <param name="name">The panel's name.</param>
    public TfControlPointIcon(VguiPanel parent, string name)
        : base(parent, name)
    {
        _baseImage = new TfControlPointPulseable(this, "BaseImage");
        _baseImage.SetShouldScaleImage(true);
        _capImage = new TfControlPointCapArrow(this, "CapImage") { Visible = false, ZPos = 2 };
        _countdown = new TfControlPointCountdown(this, "Countdown") { ZPos = 4 };
        _timerLabel = new TfExLabel(this, "CPTimerLabel") { ZPos = 0 };
        _timerBackground = new VguiImagePanel(this, "CPTimerBG") { ZPos = -1 };
        _timerBackground.SetShouldScaleImage(true);
    }

    /// <summary>The timer's label and background hidden, as a point leaving the round leaves them among the icons.</summary>
    public void HideTimer()
    {
        _timerLabel.Visible = false;
        _timerBackground.Visible = false;
    }

    /// <summary>
    /// `m_bRedText` at a moment: `SetTimerTime` clears it and ticks every 150 ms from then, and a tick only flips it once
    /// `ceil( m_flCPTimerTime - curtime )` is ten or fewer (:593-605), so the first such tick is red.
    /// </summary>
    /// <param name="setAt">When `SetTimerTime` ran.</param>
    /// <param name="timerTime">`m_flCPTimerTime`.</param>
    /// <param name="now">`gpGlobals->curtime`.</param>
    /// <returns>Whether the text is red.</returns>
    public static bool TimerIsRed(float setAt, float timerTime, float now)
    {
        int last = (int)((now - setAt) / TimerTick);
        int first = Math.Max(0, (int)MathF.Ceiling((timerTime - 10f - setAt) / TimerTick));

        return last >= first && (last - first) % 2 == 0;
    }

    /// <summary>`m_pCPTimerLabel`.</summary>
    public VguiLabel TimerLabel => _timerLabel;

    /// <summary>`SetTimerTime` and `OnTick` (:579-641): a `CCPTimerLogic` countdown beside the icon, flashing red every
    /// 150 ms tick from ten seconds.</summary>
    /// <param name="state">The game state.</param>
    /// <param name="timerTime">`m_flCPTimerTimes`.</param>
    private void UpdateTimer(HudState state, float timerTime)
    {
        // `controlpoint_timer_updated` fires on a change (c_team_objectiveresource.cpp:230); a seek back replays to it.
        if (MathF.Abs(timerTime - _timerSeen) > 1e-4f || state.CurTime < _timerLastNow)
        {
            (_timerSeen, _timerSetAt) = (timerTime, state.CurTime);
        }

        _timerLastNow = state.CurTime;

        bool shown = timerTime >= 0f;

        _timerLabel.Visible = shown;
        _timerBackground.Visible = shown;

        if (!shown)
        {
            return;
        }

        _timerBackground.X = X;
        _timerBackground.Y = Y;

        // `XRES( 1 )`: the screen's width over 640.
        _timerLabel.X = X + Wide - (int)((HudViewport.Of(this)?.Wide ?? 640) / 640f);
        _timerLabel.Y = Y + (Tall / 2) - (_timerLabel.Tall / 2);

        float left = timerTime - state.CurTime;
        int seconds = left > 0f ? (int)MathF.Ceiling(left) : 0;

        _timerLabel.FgColor = TimerIsRed(_timerSetAt, timerTime, state.CurTime) ? _highlightColor : _regularColor;
        _timerLabel.SetText(seconds.ToString(CultureInfo.InvariantCulture), text => text);
    }

    /// <inheritdoc/>
    public override string ClassName => "CControlPointIcon";

    /// <summary>`m_bSwipeUp`.</summary>
    public bool SwipeUp { get; set; }

    /// <summary>`m_iCapProgressDir`.</summary>
    public CapProgressDirection CapProgressDirection { get; set; }

    /// <summary>The base image last chosen.</summary>
    public VguiImagePanel BaseImage => _baseImage;

    /// <summary>`m_pCapImage`.</summary>
    public TfControlPointCapArrow CapImage => _capImage;

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);

        // :157-172: both on the viewport, the swoop above the HUD and the capture pulse below the icons.
        if (_capHighlightImage is null && HudViewport.Of(this) is { } viewport)
        {
            _capHighlightImage = new TfControlPointSwoop(viewport, "CapHighlightImage") { ZPos = 10 };
            _capPulseImage = new TfControlPointCapturePulse(viewport, "CapPulse") { ZPos = -1, Visible = false };
        }

        _regularColor = context.Scheme.GetColor("TanLight", (235, 226, 202, 255));
        _highlightColor = context.Scheme.GetColor("RedSolid", (192, 28, 0, 255));

        LoadControlSettings("resource/UI/ControlPointIcon.res", context);

        // :213-221: the timer's label and background are placed among the icons, not inside one.
        if (Parent is { } icons)
        {
            _timerLabel.SetParent(icons);
            _timerBackground.SetParent(icons);
        }

        _capPlayerImage = FindChildByName("CapPlayerImage") as VguiImagePanel;
        _capNumPlayers = FindChildByName("CapNumPlayers") as VguiLabel;
        _overlayImage = FindChildByName("OverlayImage") as VguiImagePanel;
    }

    /// <summary>`IsPointLocked` (:414): no team that does not own the point may capture it.</summary>
    /// <param name="state">The game state.</param>
    /// <param name="objectives">The objectives.</param>
    /// <param name="index">The point.</param>
    /// <returns>Whether it is locked.</returns>
    public static bool IsPointLocked(HudState state, SceneObjectives objectives, int index)
    {
        ArgumentNullException.ThrowIfNull(objectives);

        SceneControlPoint point = objectives.Points[index];

        for (int team = FirstGameTeam; team <= LastGameTeam; team++)
        {
            if (point.Owner != team && point.CanCap[team] && TeamMayCapturePoint(state, objectives, team, index))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>`TeamplayGameRules()->TeamMayCapturePoint( team, index )`, for the progress bar's text.</summary>
    /// <param name="state">The game state.</param>
    /// <param name="objectives">The objectives.</param>
    /// <param name="team">The team.</param>
    /// <param name="index">The point.</param>
    /// <returns>Whether the team may capture it.</returns>
    public static bool TeamMayCapture(HudState state, SceneObjectives objectives, int team, int index)
    {
        ArgumentNullException.ThrowIfNull(objectives);

        return TeamMayCapturePoint(state, objectives, team, index);
    }

    /// <summary>`CTFGameRules::TeamMayCapturePoint` (tf_gamerules.cpp:16770). `tf_caplinear` is on, its default.</summary>
    private static bool TeamMayCapturePoint(HudState state, SceneObjectives objectives, int team, int index)
    {
        SceneControlPoint point = objectives.Points[index];
        int previous = team == TeamRed ? point.PreviousRed : point.PreviousBlue;
        int needed = SceneObjectives.UnpackPrevious(previous, 0);

        // "Points set to require themselves are always cappable."
        if (needed == index)
        {
            return true;
        }

        if ((state.Rules.Koth && state.Rules.WaitingForPlayers) || point.Locked)
        {
            return false;
        }

        if (needed == -1)
        {
            if (state.Rules.GameType == GameTypeArena)
            {
                return false;
            }

            // "No custom previous point, team must own all previous points."
            return objectives.PlayingMiniRounds || Math.Abs(FarthestOwned(objectives, team) - index) <= 1;
        }

        for (int slot = 0; slot < SceneObjectives.MaxPreviousPoints; slot++)
        {
            int required = SceneObjectives.UnpackPrevious(previous, slot);

            if (required >= 0 && required < objectives.Points.Count && objectives.Points[required].Owner != team)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>`GetFarthestOwnedControlPoint( iTeam, false )` (tf_gamerules.cpp).</summary>
    private static int FarthestOwned(SceneObjectives objectives, int team)
    {
        int ownedEnd = team == TeamRed ? objectives.BaseRed : objectives.BaseBlue;

        if (ownedEnd < 0 || ownedEnd >= objectives.Points.Count)
        {
            return -1;
        }

        int walk = ownedEnd != 0 ? -1 : 1;
        int enemyEnd = ownedEnd != 0 ? 0 : objectives.Points.Count - 1;
        int farthest = ownedEnd;

        for (int point = ownedEnd; point != enemyEnd; point += walk)
        {
            if (objectives.Points[point].Owner != team)
            {
                break;
            }

            farthest = point;
        }

        return farthest;
    }

    /// <summary>`UpdateImage` (:287), `UpdateCapImage` (:344) and the player count of `PerformLayout` (:523), from this tick's state.</summary>
    /// <param name="state">The game state.</param>
    /// <param name="index">`m_iCPIndex`.</param>
    internal void Update(HudState state, int index)
    {
        if (state.Objectives is not { } objectives)
        {
            _baseImage.PulseAlpha = null;
            HideTimer();
            return;
        }

        SceneControlPoint point = objectives.Points[index];

        _baseImage.Visible = true;
        (_baseImage.X, _baseImage.Y, _baseImage.Wide, _baseImage.Tall) = (0, 0, Wide, Tall);

        (_countdown.X, _countdown.Y, _countdown.Wide, _countdown.Tall) = (0, 0, Wide, Tall);
        _countdown.Update(state, point.UnlockTime);
        UpdateTimer(state, point.TimerTime);

        // Only a change builds a name: this runs for every icon every frame.
        string? material = objectives.IconNames.GetValueOrDefault(point.Icons[point.Owner]);

        // `UpdateImage` (:299): the lock comes off once the unlock countdown is under six seconds (:438-453).
        bool locked = IsPointLocked(state, objectives, index)
            && !(_countdown.UnlockTime > 0f && !state.Rules.WaitingForPlayers && (int)(_countdown.UnlockTime - state.CurTime) < 6);

        if (material is not null && (!ReferenceEquals(material, _material) || locked != _locked))
        {
            (_material, _locked) = (material, locked);
            _baseImage.SetImage(locked ? $"../{material}_locked" : $"../{material}");
        }

        if (_overlayImage is { } overlay)
        {
            string? name = objectives.IconNames.GetValueOrDefault(point.Overlays[point.Owner]);

            overlay.Visible = name is not null;

            if (name is not null && !ReferenceEquals(name, _overlay))
            {
                _overlay = name;
                overlay.SetImage($"../{name}");
            }
        }

        bool capping = point.CappingTeam != 0 && point.CappingTeam != point.Owner;

        // :369-380: only a player on another team waits for the swoop's switch; a spectator's pulse starts at once.
        bool defending = capping && state.Team > TfControlPointPulseable.LastSharedTeam && state.Team != point.CappingTeam;

        _baseImage.Step(point.Pulse, state.CurTime, Math.Clamp(1f - point.LazyCapPercent, 0f, 1f), defending);

        bool freezeCam = state.ObserverMode == ObserverModes.FreezeCam;

        // `PerformLayout` (:517-521, :565-571): the swoop rises from the icon three quarters of the screen; the capture pulse
        // is three icons wide, centred on it.
        if (_capHighlightImage is { } swoop)
        {
            if (Parent?.Parent is { } screen)
            {
                // `XRES( 2 )` and `YRES( 2 )` are floats (cdll_util.h:53-54); each sum truncates once.
                float indentX = 2f * screen.Wide / 640f;
                float indentY = 2f * screen.Tall / 480f;
                int height = (int)(screen.Tall * 0.75f);

                (swoop.X, swoop.Y, swoop.Wide, swoop.Tall) =
                    ((int)(AbsX + indentX), AbsY - height, (int)(Wide - (indentX * 2f)), (int)(height + Tall - indentY));
            }

            swoop.Step(point.Pulse.Accelerate && capping && defending ? point.Pulse.Start : 0f, state.CurTime, freezeCam);
        }

        if (_capPulseImage is { } flash)
        {
            int size = Wide * 3;

            (flash.X, flash.Y, flash.Wide, flash.Tall) = (AbsX - ((size - Wide) / 2), AbsY - ((size - Tall) / 2), size, size);

            // `StartPulse( curtime, GetWide() )` (:334) runs with `StartPulsing( FINISHCAPANIM_SWOOP_LENGTH, ... )`.
            bool finished = !point.Pulse.Accelerate && point.Pulse.Length > 0f;

            flash.Step(finished ? point.Pulse.Start - CapPulse.FinishDelay : 0f, Wide, state.CurTime, freezeCam);
        }

        _capImage.Visible = capping;
        (_capImage.X, _capImage.Y, _capImage.Wide, _capImage.Tall) = (0, 0, Wide, Tall);

        if (capping)
        {
            string team = point.CappingTeam == TeamRed ? "red" : "blu";

            _capImage.Material = (team, SwipeUp) switch
            {
                ("red", true) => "sprites/obj_icons/icon_obj_cap_red_up",
                ("red", false) => "sprites/obj_icons/icon_obj_cap_red",
                (_, true) => "sprites/obj_icons/icon_obj_cap_blu_up",
                _ => "sprites/obj_icons/icon_obj_cap_blu",
            };
            _capImage.SwipeUp = SwipeUp;
            _capImage.CappingTeam = point.CappingTeam;

            // `m_flCapTimeLeft` starts each update from `m_flLazyCapPerc * m_flTeamCapTime` (c_team_objectiveresource.cpp), so
            // the share elapsed is its complement. **Interpolated:** the client's run-down between two updates is not stepped.
            _capImage.CapPercent = Math.Clamp(1f - point.LazyCapPercent, 0f, 1f);
        }

        int players = point.TeamMembers[point.CappingTeam];

        if (_capPlayerImage is { } image)
        {
            image.Visible = players > 0 && point.CappingTeam != 0;

            if (image.Visible)
            {
                // `XRES( 4 )`: 640 is the width `XRES` scales from.
                int nudge = players < 2 || _capNumPlayers is null ? 0 : 4 * (Parent?.Parent?.Wide ?? 640) / 640;

                (image.X, image.Y) = (((Wide - image.Wide) / 2) - nudge, (Tall - image.Tall) / 2);
            }
        }

        if (_capNumPlayers is { } label)
        {
            label.Visible = players > 1 && point.CappingTeam != 0;

            if (players != _players)
            {
                _players = players;
                SetDialogVariable("numcappers", players.ToString(CultureInfo.InvariantCulture));
            }

            label.FgColor = (0, 0, 0, 255);
        }
    }
}

/// <summary>`CControlPointIconPulseable` (hud_controlpointicons.cpp:54): the base image, with the white icon pulsing over it.</summary>
/// <param name="parent">The icon.</param>
/// <param name="name">The panel's name.</param>
public sealed class TfControlPointPulseable(VguiPanel parent, string name) : VguiImagePanel(parent, name)
{
    /// <summary>`LAST_SHARED_TEAM`.</summary>
    public const int LastSharedTeam = 1;

    private const float IconSwitch = 0.15f;
    private const float OpaqueAlpha = 255f;

    private readonly VguiBitmap _pulseImage = new("vgui/../sprites/obj_icons/icon_obj_white");
    private float _startSeen;
    private bool _delayed;
    private bool _stopped;
    private float _lastNow;

    /// <summary>`255 * flPulseMod` this frame, or null when not pulsing.</summary>
    public float? PulseAlpha { get; set; }

    /// <summary>This frame's pulse: the delay is fixed when a pulse starts (:376), and the frame that reaches the length still draws before `StopPulsing` (:91).</summary>
    /// <param name="pulse">The point's pulse.</param>
    /// <param name="now">`gpGlobals->curtime`.</param>
    /// <param name="capPercent">`GetCPCapPercentage`.</param>
    /// <param name="defending">Whether a pulse starting now waits for the icon switch.</param>
    public void Step(CapPulse pulse, float now, float capPercent, bool defending)
    {
        // Two starts are at least a tick apart.
        if (MathF.Abs(pulse.Start - _startSeen) > 1e-4f)
        {
            (_startSeen, _delayed, _stopped) = (pulse.Start, defending, false);
        }
        else if (now < _lastNow)
        {
            // A seek back replays to the landing, where the engine's panel has not stopped a pulse it has not drawn.
            _stopped = false;
        }

        _lastNow = now;

        PulseAlpha = _stopped ? null : PulseAlphaAt(pulse, now, capPercent, _delayed);

        // `elapsed >= m_flPulseTime`, as `PulseAlphaAt` starts counting.
        _stopped = _stopped
            || (PulseAlpha is not null && pulse.Length != 0f && now - pulse.Start - (_delayed ? IconSwitch : 0f) >= pulse.Length);
    }

    /// <summary>`flPulseMod` at a moment (:65-78), times 255; null before the start.</summary>
    /// <param name="pulse">The point's pulse.</param>
    /// <param name="now">`gpGlobals->curtime`.</param>
    /// <param name="capPercent">`GetCPCapPercentage`.</param>
    /// <param name="defending">Whether `StartPulsing` was given `STARTCAPANIM_ICON_SWITCH` (:376).</param>
    /// <returns>The alpha, or null.</returns>
    public static float? PulseAlphaAt(CapPulse pulse, float now, float capPercent, bool defending)
    {
        float start = pulse.Start + (defending ? IconSwitch : 0f);

        if (pulse.Start == 0f || now <= start)
        {
            return null;
        }

        float elapsed = now - start;

        // `RemapValClamped( flCapPercentage, 0, 1, 2, 5 )`.
        float speed = pulse.Accelerate ? 2f + (3f * Math.Clamp(capPercent, 0f, 1f)) : 20f;

        return OpaqueAlpha * MathF.Abs(MathF.Sin(elapsed * speed));
    }

    /// <inheritdoc/>
    public override void PaintBackground(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        // Valve sets the alpha here, after `PaintTraverse` read it, so the multiplier on this frame's two draws is what the
        // last frame left (Panel.cpp:1141, :1199): the base fades with the white, a frame behind.
        if (Image is not null)
        {
            SetAlpha(OpaqueAlpha);
            base.PaintBackground(surface, context);
        }

        if (PulseAlpha is { } alpha)
        {
            SetAlpha(alpha);
            _pulseImage.SetPos(0, 0);
            _pulseImage.SetSize(Wide, Tall);
            _pulseImage.Paint(surface);
        }
    }
}

/// <summary>`CP_DIR_N`, `CP_DIR_NW`, `CP_DIR_NE` (hud_controlpointicons.h:47-52): which way the progress teardrop points from an icon.</summary>
public enum CapProgressDirection
{
    /// <summary>`CP_DIR_N`: above.</summary>
    North,

    /// <summary>`CP_DIR_NW`: above and to the left.</summary>
    NorthWest,

    /// <summary>`CP_DIR_NE`: above and to the right.</summary>
    NorthEast,
}

/// <summary>`CControlPointCountdown` (hud_controlpointicons.cpp:1771): the seconds left before a locked point unlocks, from five.</summary>
/// <remarks>
/// Valve ticks it every 100 ms (`AddTickSignal( 100 )`); this updates every frame, so a change lands up to 0.1 s sooner.
/// **Not ported:** the announcer's countdown and "capture enabled" lines it plays (:1857, :1878-1910).
/// </remarks>
/// <param name="parent">The icon.</param>
/// <param name="name">The panel's name.</param>
public sealed class TfControlPointCountdown(VguiPanel parent, string name) : VguiEditablePanel(parent, name)
{
    private const int RoundRunning = 4;

    private float _unlockTime;
    private float _seen;
    private readonly bool[] _fire = [true, true, true, true, true, true];
    private bool _fire0 = true;
    private float _lastNow;

    /// <summary>`m_flUnlockTime`.</summary>
    public float UnlockTime => _unlockTime;

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);
        LoadControlSettings("resource/UI/ControlPointCountdown.res", context);
    }

    /// <inheritdoc/>
    protected override void PerformLayout()
    {
        if (FindChildByName("CapCountdownLabel") is { } label)
        {
            (label.X, label.Y, label.Wide, label.Tall) = (0, 0, Wide, Tall);
        }
    }

    /// <summary>`SetUnlockTime` when `controlpoint_unlock_updated` would fire, then `OnTick`.</summary>
    /// <param name="state">The game state.</param>
    /// <param name="unlockTime">`m_flUnlockTimes` for the point.</param>
    public void Update(HudState state, float unlockTime)
    {
        // The event fires on a change (c_team_objectiveresource.cpp:219); a seek back replays to it.
        if (MathF.Abs(unlockTime - _seen) > 1e-4f || state.CurTime < _lastNow)
        {
            (_seen, _unlockTime) = (unlockTime, unlockTime);

            // `SetUnlockTime` (:1814): the five lines re-arm by the time left, `m_bFire0SecRemain` does not.
            for (int line = 1; line <= 5; line++)
            {
                _fire[line] = unlockTime - state.CurTime >= line;
            }
        }

        _lastNow = state.CurTime;

        if (!state.InGame || _unlockTime <= 0f)
        {
            Visible = false;
            return;
        }

        if (state.Rules.WaitingForPlayers || state.RoundState != RoundRunning)
        {
            return;
        }

        int timeLeft = (int)(_unlockTime - state.CurTime);

        if (timeLeft > 5 || timeLeft <= 0)
        {
            // Only the expiry that `m_bFire0SecRemain` still allows clears the time (:1854-1859).
            if (timeLeft <= 0 && _fire0)
            {
                (_fire0, _unlockTime) = (false, 0f);
            }

            Visible = false;
            return;
        }

        Visible = true;

        // One announcer line per tick, five down to one (:1878-1903); the last re-arms the expiry. The lines are sound only.
        for (int line = 5; line >= 1; line--)
        {
            if (timeLeft <= line && _fire[line])
            {
                _fire[line] = false;
                _fire0 |= line == 1;
                break;
            }
        }
        SetDialogVariable("capturetime", timeLeft.ToString(CultureInfo.InvariantCulture));
    }
}

/// <summary>A one-shot animation keyed to its start: it draws until the frame that reaches its length, then hides, and a seek back plays it again.</summary>
/// <param name="parent">The viewport.</param>
/// <param name="name">The panel's name.</param>
/// <param name="image">The image.</param>
/// <param name="length">The animation's length.</param>
public abstract class TfControlPointOneShot(VguiPanel parent, string name, string image, float length) : VguiImagePanel(parent, name)
{
    private float _start;
    private float _lastNow;
    private bool _done;

    /// <summary>The seconds since the start this frame.</summary>
    protected float Elapsed { get; private set; }

    /// <summary>Shows the animation for this frame.</summary>
    /// <param name="start">The start, or 0 for none.</param>
    /// <param name="now">`gpGlobals->curtime`.</param>
    /// <param name="freezeCam">`IsInFreezeCam`, which hides it.</param>
    protected void StepAt(float start, float now, bool freezeCam)
    {
        if (MathF.Abs(start - _start) > 1e-4f || now < _lastNow)
        {
            (_start, _done, _lastNow) = (start, false, float.MinValue);
        }

        Evaluate(now, freezeCam);
    }

    /// <summary>
    /// The engine's panel hides itself in its own `PaintBackground`, so it finishes even on a frame its icon is not updated —
    /// a point leaving the round, or the objectives gone. This one advances on its own think for the same reason.
    /// </summary>
    protected override void OnThink()
    {
        if (HudViewport.Of(this) is { } viewport)
        {
            Evaluate(viewport.State.CurTime, viewport.State.ObserverMode == ObserverModes.FreezeCam);
        }
    }

    private void Evaluate(float now, bool freezeCam)
    {
        // Once a frame: a second look at the same moment would hide the last frame before it is drawn.
        if (now <= _lastNow)
        {
            return;
        }

        float start = _start;

        _lastNow = now;
        Elapsed = now - start;
        Visible = start > 0f && !_done && !freezeCam && Elapsed >= 0f;

        // "Once we've finished the swoop, go away", after the frame that reached it.
        _done = _done || (Visible && Elapsed >= length);

        if (Visible && Image is null)
        {
            SetImage(image);
            SetShouldScaleImage(true);
        }
    }
}

/// <summary>`CControlPointIconSwoop` (hud_controlpointicons.h:115): the highlight that sweeps down over a defended point as its capture starts.</summary>
/// <param name="parent">The viewport.</param>
/// <param name="name">The panel's name.</param>
public sealed class TfControlPointSwoop(VguiPanel parent, string name)
    : TfControlPointOneShot(parent, name, "../sprites/obj_icons/capture_highlight", StartLength)
{
    /// <summary>`STARTCAPANIM_SWOOP_LENGTH`.</summary>
    public const float StartLength = 0.4f;

    /// <summary>`StartSwoop` when the capture started, as `UpdateCapImage` calls it.</summary>
    /// <param name="start">The start, or 0 for none.</param>
    /// <param name="now">`gpGlobals->curtime`.</param>
    /// <param name="freezeCam">`IsInFreezeCam`.</param>
    public void Step(float start, float now, bool freezeCam) => StepAt(start, now, freezeCam);

    /// <summary>`RemapValClamped( flElapsedTime, 0, STARTCAPANIM_SWOOP_LENGTH, 0, GetTall() )`, truncated to an int.</summary>
    /// <param name="elapsed">Seconds since the start.</param>
    /// <param name="tall">The panel's height.</param>
    /// <returns>The image's y.</returns>
    public static int ImageY(float elapsed, int tall) => (int)(Math.Clamp(elapsed / StartLength, 0f, 1f) * tall);

    /// <inheritdoc/>
    public override void PaintBackground(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (Image is not { } image)
        {
            return;
        }

        image.Color = (255, 255, 255, 255);
        image.SetSize(Wide, Tall);
        image.SetPos(0, ImageY(Elapsed, Tall));
        image.Paint(surface);
    }
}

/// <summary>`CControlPointIconCapturePulse` (hud_controlpointicons.h:200): the white flash that shrinks onto a point as it is captured.</summary>
/// <param name="parent">The viewport.</param>
/// <param name="name">The panel's name.</param>
public sealed class TfControlPointCapturePulse(VguiPanel parent, string name)
    : TfControlPointOneShot(parent, name, "../sprites/obj_icons/icon_obj_white", CapPulse.FinishDelay)
{
    private int _shrinkSize;

    /// <summary>`StartPulse( flTime, iShrinkSize )` as of this frame.</summary>
    /// <param name="start">`m_flFinishCapAnimStart`, or 0 for none.</param>
    /// <param name="shrinkSize">`m_iShrinkSize`, the icon's width.</param>
    /// <param name="now">`gpGlobals->curtime`.</param>
    /// <param name="freezeCam">`IsInFreezeCam`.</param>
    public void Step(float start, int shrinkSize, float now, bool freezeCam)
    {
        _shrinkSize = shrinkSize;
        StepAt(start, now, freezeCam);

        // `curtime > m_flFinishCapAnimStart`: strictly after.
        Visible = Visible && Elapsed > 0f;
    }

    /// <summary>`RemapValClamped( flElapsedTime, 0, FINISHCAPANIM_SWOOP_LENGTH, GetWide(), m_iShrinkSize )`, truncated.</summary>
    /// <param name="elapsed">Seconds since the capture.</param>
    /// <param name="wide">The panel's width.</param>
    /// <param name="shrinkSize">The size it shrinks to.</param>
    /// <returns>The image's size.</returns>
    public static int ImageSize(float elapsed, int wide, int shrinkSize) =>
        (int)(wide + ((shrinkSize - wide) * Math.Clamp(elapsed / CapPulse.FinishDelay, 0f, 1f)));

    /// <inheritdoc/>
    public override void PaintBackground(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (Image is not { } image)
        {
            return;
        }

        int size = ImageSize(Math.Max(0f, Elapsed), Wide, _shrinkSize);

        image.Color = (255, 255, 255, 255);
        image.SetPos((int)((Wide - size) * 0.5f), (int)((Tall - size) * 0.5f));
        image.SetSize(size, size);
        image.Paint(surface);
    }
}

/// <summary>`CControlPointIconCapArrow` (hud_controlpointicons.cpp:1672): the capturing team's arrow, its texture moved by the capture.</summary>
/// <param name="parent">The icon.</param>
/// <param name="name">The panel's name.</param>
public sealed class TfControlPointCapArrow(VguiPanel parent, string name) : VguiPanel(parent, name)
{
    /// <summary>`m_pArrowMaterial`.</summary>
    public string? Material { get; set; }

    /// <summary>`ShouldSwipeUp`.</summary>
    public bool SwipeUp { get; set; }

    /// <summary>`GetCappingTeam`.</summary>
    public int CappingTeam { get; set; }

    /// <summary>`GetCPCapPercentage`.</summary>
    public float CapPercent { get; set; }

    /// <inheritdoc/>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (Material is null)
        {
            return;
        }

        (float s0, float t0, float s1, float t1) = TextureRange(CapPercent, SwipeUp, CappingTeam);

        surface.DrawSetColor((255, 255, 255, 255));
        surface.DrawSetTexture(Material);
        surface.DrawTexturedQuadSeparateDetail(0f, 0f, Wide, Tall, s0, t0, s1, t1);
    }

    /// <summary>The arrow's texture coordinates at a capture's share (:1684-1731).</summary>
    /// <param name="percent">`flCapPercentage`.</param>
    /// <param name="swipeUp">Whether the swipe runs upward.</param>
    /// <param name="cappingTeam">The capturing team.</param>
    /// <returns>`flXa`, `flYa`, `flXb`, `flYb`.</returns>
    public static (float S0, float T0, float S1, float T1) TextureRange(float percent, bool swipeUp, int cappingTeam)
    {
        const float ArrowHead = 15f;
        const float ArrowBody = 54f;
        const float Box = 33f;

        float imageSize = ArrowHead + ArrowBody;
        float movement = (Box + ArrowHead) / imageSize;
        float arrowSize = ArrowHead / imageSize;

        if (swipeUp)
        {
            const float UpIndent = 0.07f;

            return (
                0f,
                Remap(percent, -movement - UpIndent, arrowSize - UpIndent),
                1f,
                Remap(percent, UpIndent, movement - UpIndent));
        }

        const float Indent = 0.1f;
        float start = 1f - Indent;
        float end = 1f + Indent;
        float moving = Remap(percent, start + movement, end - arrowSize);
        float fixedEnd = Remap(percent, start, 0f);

        return cappingTeam % 2 == 0 ? (moving, 0f, fixedEnd, 1f) : (fixedEnd, 0f, moving, 1f);
    }

    // `RemapVal( val, 0, 1, a, b )`.
    private static float Remap(float value, float a, float b) => a + ((b - a) * value);
}
