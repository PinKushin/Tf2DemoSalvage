using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CHudControlPointIcons` (game/client/hud_controlpointicons.cpp:646): one icon per visible control point in the mini-round.</summary>
/// <remarks>
/// **From state each think, where the engine waits for `controlpoint_*` events**, as a seek or a rewind has no event history to
/// replay. **Not ported:** the swoop, the pulses, the unlock countdown, the point timer, the progress teardrop, the custom
/// layout position, and arena's capture enable time (filed under B709).
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

        (X, Y, Wide, Tall) = ((int)((parent.Wide - widest) * 0.5f), parent.Tall - tall - GetInt("height_offset"), widest, tall);

        for (int line = 0; line < lines.Count; line++)
        {
            int x = (int)((widest - lineWide[line]) * 0.5f) + gapWide;
            int y = (line * gapTall) + (line * tallest) + gapTall;

            foreach (TfControlPointIcon icon in lines[line])
            {
                (icon.X, icon.Y) = (x, y);
                x += icon.Wide + gapWide;

                // "If we have multiple lines, swipe up when capping."
                icon.SwipeUp = lines.Count > 1;
            }
        }
    }

    // `m_pszCapLayoutInHUD`: point indices, a space between two on a line and a comma between lines. Empty: all on one line.
    private List<List<TfControlPointIcon>> Lines()
    {
        List<List<TfControlPointIcon>> lines = [];

        if (_layout.Length == 0)
        {
            if (_shownCount > 0)
            {
                lines.Add(_icons.GetRange(0, _shownCount));
            }

            return lines;
        }

        foreach (string text in _layout.Split(','))
        {
            List<TfControlPointIcon> line = [];

            foreach (string number in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                int slot = int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int point)
                    ? Array.IndexOf(_shown, point, 0, _shownCount)
                    : -1;

                if (slot >= 0)
                {
                    line.Add(_icons[slot]);
                }
            }

            if (line.Count == 0)
            {
                break;
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

    /// <summary>`CControlPointIcon( parent, name, index )`, and the children `ApplySchemeSettings` makes on its first pass (:174).</summary>
    /// <param name="parent">The icons panel.</param>
    /// <param name="name">The panel's name.</param>
    public TfControlPointIcon(VguiPanel parent, string name)
        : base(parent, name)
    {
        _baseImage = new TfControlPointPulseable(this, "BaseImage");
        _baseImage.SetShouldScaleImage(true);
        _capImage = new TfControlPointCapArrow(this, "CapImage") { Visible = false, ZPos = 2 };
    }

    /// <inheritdoc/>
    public override string ClassName => "CControlPointIcon";

    /// <summary>`m_bSwipeUp`.</summary>
    public bool SwipeUp { get; set; }

    /// <summary>The base image last chosen.</summary>
    public VguiImagePanel BaseImage => _baseImage;

    /// <summary>`m_pCapImage`.</summary>
    public TfControlPointCapArrow CapImage => _capImage;

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);
        LoadControlSettings("resource/UI/ControlPointIcon.res", context);

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
            return;
        }

        SceneControlPoint point = objectives.Points[index];

        _baseImage.Visible = true;
        (_baseImage.X, _baseImage.Y, _baseImage.Wide, _baseImage.Tall) = (0, 0, Wide, Tall);

        // Only a change builds a name: this runs for every icon every frame.
        string? material = objectives.IconNames.GetValueOrDefault(point.Icons[point.Owner]);
        bool locked = IsPointLocked(state, objectives, index);

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

        _baseImage.PulseAlpha = TfControlPointPulseable.PulseAlphaAt(point.Pulse, state.CurTime, Math.Clamp(1f - point.LazyCapPercent, 0f, 1f), defending);

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

    /// <summary>`255 * flPulseMod` this frame, or null when not pulsing.</summary>
    public float? PulseAlpha { get; set; }

    /// <summary>`flPulseMod` at a moment (:65-78), times 255; null before the start and after the length.</summary>
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

        if (pulse.Length != 0f && elapsed >= pulse.Length)
        {
            return null;
        }

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
        SetAlpha(OpaqueAlpha);
        base.PaintBackground(surface, context);

        if (PulseAlpha is { } alpha)
        {
            SetAlpha(alpha);
            _pulseImage.SetPos(0, 0);
            _pulseImage.SetSize(Wide, Tall);
            _pulseImage.Paint(surface);
        }
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
